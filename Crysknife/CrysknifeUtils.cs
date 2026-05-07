// SPDX-FileCopyrightText: Yun Hsiao Wu <yunhsiaow@gmail.com>
// SPDX-License-Identifier: MIT

using System.Text.RegularExpressions;

namespace Crysknife;

public enum LogLevel
{
    Verbose,
    Info,
    Action,
    Warning,
    Error,
    Fatal,
}

public enum CrysknifeExitCode
{
    Success = 0,
    PartialFailure = 1, // Apply finished but some patches did not land cleanly.
    UsageError = 2,     // Bad CLI arguments / missing required flags.
    Fatal = 3,          // Unhandled exception or environment error (Utils.Abort default).
}

public interface ILogger
{
    void Log(LogLevel Level, string Format, params object[] Args);
}

public static class Logger
{
    private static ILogger Instance = new ConsoleLogger();
    public static LogLevel MinimumLevel { get; set; } = LogLevel.Info;

    public static void SetLogger(ILogger NewLogger)
    {
        Instance = NewLogger;
    }

    public static bool IsEnabled(LogLevel Level) => Level >= MinimumLevel;

    public static void Verbose(string Format, params object[] Args) { if (IsEnabled(LogLevel.Verbose)) Instance.Log(LogLevel.Verbose, Format, Args); }
    public static void Info   (string Format, params object[] Args) { if (IsEnabled(LogLevel.Info))    Instance.Log(LogLevel.Info,    Format, Args); }
    public static void Action (string Format, params object[] Args) { if (IsEnabled(LogLevel.Action))  Instance.Log(LogLevel.Action,  Format, Args); }
    public static void Warning(string Format, params object[] Args) { if (IsEnabled(LogLevel.Warning)) Instance.Log(LogLevel.Warning, Format, Args); }
    public static void Error  (string Format, params object[] Args) { if (IsEnabled(LogLevel.Error))   Instance.Log(LogLevel.Error,   Format, Args); }
    public static void Fatal  (string Format, params object[] Args) { if (IsEnabled(LogLevel.Fatal))   Instance.Log(LogLevel.Fatal,   Format, Args); }
}

internal class ConsoleLogger(bool UseColor = true) : ILogger
{
    private static readonly Dictionary<LogLevel, (ConsoleColor Color, string Prefix)> LevelStyles = new()
    {
        { LogLevel.Verbose, (ConsoleColor.Gray,     "[VRB]") },
        { LogLevel.Info,    (ConsoleColor.Blue,     "[INF]") },
        { LogLevel.Action,  (ConsoleColor.Green,    "[ACT]") },
        { LogLevel.Warning, (ConsoleColor.Yellow,   "[WRN]") },
        { LogLevel.Error,   (ConsoleColor.Red,      "[ERR]") },
        { LogLevel.Fatal,   (ConsoleColor.DarkRed,  "[FTL]") },
    };

    public void Log(LogLevel Level, string Format, params object[] Args)
    {
        if (Level < Logger.MinimumLevel) return;

        var (Color, Prefix) = LevelStyles[Level];
        var Message = Args.Length > 0 ? string.Format(Format, Args) : Format;

        if (UseColor) Console.ForegroundColor = Color;
        var Output = Level >= LogLevel.Error ? Console.Error : Console.Out;
        Output.WriteLine("{0} {1}", Prefix, Message);
        if (UseColor) Console.ResetColor();
    }
}

internal class InjectionRegexForm(string CommentTag, Regex Pattern)
{
    private static readonly Regex CommentRegex = new (@"^(\s*)//\s*", RegexOptions.Multiline | RegexOptions.Compiled);

    public Match Match(string Content)
    {
        return Pattern.Match(Content);
    }

    public string Unpatch(string Content)
    {
        return Pattern.Replace(Content, Matched => Replace(Matched.Groups["Tag"].Value, Matched.Groups["Content"].Value, CommentTag));
    }

    public static string Replace(string Tag, string Content, string CommentTag)
    {
        if (Tag.StartsWith(CommentTag + '-')) // Restore deletions
        {
            return CommentRegex.Replace(Content, ContentMatch => ContentMatch.Groups[1].Value);
        }
        return ""; // Remove injections
    }
}

internal struct CommentTagFormat(string pluginName)
{
    public string Tag = Path.GetFileName(pluginName);

    public string PrefixRegex = "";
    public string SuffixRegex = "";
    public string BeginRegex = "";
    public string EndRegex = "";
    public bool Anastrophe = false;
    public bool TagNewFiles = false;

    public string PrefixCtor = "";
    public string SuffixCtor = "";
    public string BeginCtor = "";
    public string EndCtor = "";

    public const int MultiLineForm = 0;
    public const int SingleLineForm = 1;
    public const int NextLineForm = 2;

    public readonly Regex[] BuildMatchForms(string? TagOverride = null)
    {
        string ResolvedTag = TagOverride ?? Tag;
        var CommentTag = Utils.EscapeLiteralsForRegex(ResolvedTag) + @"[^\n]*?"; // Allow some comments in between

        var Forms = new Regex[3];
        Forms[MultiLineForm] = new Regex(string.Format(Anastrophe ?
                @"[^\S\n]*//[^\S\n]*{0}{3}(?<Tag>{1}){2}\n(?<Content>.*?)// (?<EndTag>{0}{4}{1}{2})[^\S\n]*\n" :
                @"[^\S\n]*//[^\S\n]*{0}(?<Tag>{1}){2}{3}(?<Content>.*?)// (?<EndTag>{0}{1}{2}{4})[^\S\n]*\n",
            PrefixRegex, CommentTag, SuffixRegex, BeginRegex, EndRegex),
            RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
        Forms[SingleLineForm] = new Regex(
            $@"^(?<Content>[^\S\n]*\S+.*?)[^\S\n]*//[^\S\n]*{PrefixRegex}(?<Tag>{CommentTag}){SuffixRegex}[^\S\n]*\n",
            RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
        Forms[NextLineForm] = new Regex(
            $@"^[^\S\n]*//[^\S\n]*{PrefixRegex}(?<Tag>{CommentTag}){SuffixRegex}[^\S\n]*\n(?<Content>.*)\n",
            RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
        return Forms;
    }
}

internal class InjectionRegex
{
    private readonly InjectionRegexForm[] MatchForms;

    public InjectionRegex(CommentTagFormat Format, string? TagOverride = null)
    {
        string Tag = TagOverride ?? Format.Tag;
        var Patterns = Format.BuildMatchForms(TagOverride);
        MatchForms = Patterns.Select(Pattern => new InjectionRegexForm(Tag, Pattern)).ToArray();
    }

    public List<Match> Match(string Content)
    {
        return MatchForms.Aggregate(new List<Match>(), (Acc, Form) =>
        {
            var Match = Form.Match(Content);
            if (Match.Success) Acc.Add(Match);
            return Acc;
        });
    }

    public string Unpatch(string Content)
    {
        return MatchForms.Aggregate(Content, (Acc, Form) => Form.Unpatch(Acc));
    }
}

internal class InjectionRegexGroup(InjectionRegex injection)
{
    public readonly InjectionRegex Injection = injection;
    private readonly List<InjectionRegex> Residuals = new();

    public void AddResiduals(IEnumerable<InjectionRegex> NewResiduals)
    {
        Residuals.AddRange(NewResiduals);
    }

    private static string Unpatch(string Content, IEnumerable<InjectionRegex> Regexes)
    {
        return Regexes.Aggregate(Content, (Current, Regex) => Regex.Unpatch(Current));
    }

    public string ClearResiduals(string Content)
    {
        return Unpatch(Content, Residuals);
    }

    public string Unpatch(string Content)
    {
        return Injection.Unpatch(Content);
    }
}

internal class CommentTagPacker
{
    public readonly CommentTagFormat Format;
    private readonly string ModuleName;

    private readonly Regex PackRegex;
    private readonly Regex UnpackRegex;
    private readonly Regex[] AuthoritativeForms;

    public CommentTagPacker(string ModuleName, CommentTagFormat Format)
    {
        this.Format = Format;
        this.ModuleName = ModuleName;

        var CommentTag = Utils.EscapeLiteralsForRegex(Format.Tag) + @"[^\n]*?"; // Allow some comments in between
        var PackFormat = string.Format(Format.Anastrophe ? 
                @"//[^\S\n]*{0}(?<Begin>{3})?(?<End>{4})?(?<Tag>{1}){2}[^\S\n]*(?=\n)" :
                @"//[^\S\n]*{0}(?<Tag>{1}){2}(?<Begin>{3})?(?<End>{4})?[^\S\n]*(?=\n)",
            Format.PrefixRegex, CommentTag, Format.SuffixRegex, Format.BeginRegex, Format.EndRegex);
        PackRegex = new Regex(PackFormat, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        UnpackRegex = new Regex($@"@{ModuleName}Tag(\w*)\(([^\n]*)\)\n", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        AuthoritativeForms = Format.BuildMatchForms();
    }

    public string GetDefaultTag(IReadOnlyDictionary<string, string> Variables) { return Unpack($"@{ModuleName}Tag()\n", Variables); }
    public bool HasAnyMatch(string Content) { return CollectAuthoritativeAnchors(Content).Count > 0; }

    private List<(int Start, int End)> CollectAuthoritativeAnchors(string Content)
    {
        var Anchors = new List<(int Start, int End)>();
        var ClaimedSegments = new List<(int Start, int End)>();

        void RecordTagAnchor(Group TagGroup)
        {
            if (!TagGroup.Success || TagGroup.Length == 0) return;
            Anchors.Add((TagGroup.Index, TagGroup.Index + TagGroup.Length));
        }

        bool OverlapsClaimed(int Start, int End)
        {
            return ClaimedSegments.Any(S => Start < S.End && End > S.Start);
        }

        foreach (Match Matched in AuthoritativeForms[CommentTagFormat.MultiLineForm].Matches(Content))
        {
            ClaimedSegments.Add((Matched.Index, Matched.Index + Matched.Length));
            RecordTagAnchor(Matched.Groups["Tag"]);
            RecordTagAnchor(Matched.Groups["EndTag"]);
        }
        foreach (Match Matched in AuthoritativeForms[CommentTagFormat.SingleLineForm].Matches(Content))
        {
            if (OverlapsClaimed(Matched.Index, Matched.Index + Matched.Length)) continue;
            ClaimedSegments.Add((Matched.Index, Matched.Index + Matched.Length));
            RecordTagAnchor(Matched.Groups["Tag"]);
        }
        foreach (Match Matched in AuthoritativeForms[CommentTagFormat.NextLineForm].Matches(Content))
        {
            if (OverlapsClaimed(Matched.Index, Matched.Index + Matched.Length)) continue;
            ClaimedSegments.Add((Matched.Index, Matched.Index + Matched.Length));
            RecordTagAnchor(Matched.Groups["Tag"]);
        }

        return Anchors;
    }

    private static bool IsAnchoredAt(IReadOnlyList<(int Start, int End)> Anchors, int Index, int Length)
    {
        var End = Index + Length;
        return Anchors.Any(Anchor => Anchor.Start == Index && End <= Anchor.End);
    }

    private string Pack(string Content, bool SkipCaptures)
    {
        var Anchors = CollectAuthoritativeAnchors(Content);

        return PackRegex.Replace(Content, Matched =>
        {
            var TagGroup = Matched.Groups["Tag"];
            // Preserve text that merely looks like a tag inside payload.
            if (!IsAnchoredAt(Anchors, TagGroup.Index, TagGroup.Length))
            {
                return Matched.Value;
            }

            string Result = $"// @{ModuleName}Tag";
            if (Matched.Groups["Begin"].Success) Result += "Begin";
            else if (Matched.Groups["End"].Success) Result += "End";
            Result += $"({TagGroup.Value[Format.Tag.Length..]})\n";
            if (SkipCaptures) return Result;

            foreach (var Index in Enumerable.Range(0, 10))
            {
                if (!Matched.Groups.TryGetValue($"Capture{Index}", out var Capture)) break;
                Result += $"@{ModuleName}TagCapture{Index}({Capture.Value})\n";
            }
            return Result;
        });
    }

    private string Unpack(string Content, IReadOnlyDictionary<string, string> Variables)
    {
        var CaptureRecord = new Dictionary<string, string>();

        var Result = UnpackRegex.Replace(Content, Matched =>
        {
            if (Matched.Groups[1].Value.StartsWith("Capture"))
            {
                CaptureRecord.Add(Matched.Groups[1].Value, Matched.Groups[2].Value);
                return string.Empty;
            }

            var ReconstructedPre = Format.Anastrophe ? Format.PrefixCtor : Format.PrefixCtor + Format.Tag + Matched.Groups[2].Value + Format.SuffixCtor;
            var ReconstructedPost = Format.Anastrophe ? Format.Tag + Matched.Groups[2].Value + Format.SuffixCtor : "";

            return Matched.Groups[1].Value switch
            {
                "" => ReconstructedPre + ReconstructedPost,
                "Begin" => ReconstructedPre + Format.BeginCtor + ReconstructedPost,
                "End" => ReconstructedPre + Format.EndCtor + ReconstructedPost,
                _ => throw new ArgumentOutOfRangeException(nameof(Content))
            };
        });

        if (Utils.MapVariables(CaptureRecord, Result, out var Temp, Utils.MapFlag.SkipWarning)) return Temp;
        return Utils.MapVariables(Variables, Result); // Fallback to config variables
    }

    public string Pack(string Content, ref int Increment, bool SkipCaptures)
    {
        var Result = Pack(Content, SkipCaptures);
        Increment += Result.Length - Content.Length;
        return Result;
    }

    public string Unpack(string Content, ref int Increment, IReadOnlyDictionary<string, string> Variables)
    {
        var Result = Unpack(Content, Variables);
        Increment += Result.Length - Content.Length;
        return Result;
    }
}

internal readonly struct EngineVersion
{
    private readonly int Major;
    private readonly int Minor;
    private readonly int Patch;

    public static EngineVersion Create(string Value)
    {
        string[] Versions = Value.Split('.');
        return new EngineVersion(int.Parse(Versions[0]), int.Parse(Versions[1]), Versions.Length > 2 ? int.Parse(Versions[2]) : 0);
    }

    private EngineVersion(int Major, int Minor, int Patch)
    {
        this.Major = Major;
        this.Minor = Minor;
        this.Patch = Patch;
    }

    public override string ToString()
    {
        return $"{Major}.{Minor}.{Patch}";
    }

    public bool NewerThan(EngineVersion Other)
    {
        if (Major != Other.Major) return Major > Other.Major;
        if (Minor != Other.Minor) return Minor > Other.Minor;
        return Patch >= Other.Patch; // Newer than 5.0 should include 5.0.0
    }
}

internal static class Utils
{
    private static readonly Regex EngineVersionRegex = new (@"#define\s+ENGINE_MAJOR_VERSION\s+(\d+)\s*#define\s+ENGINE_MINOR_VERSION\s+(\d+)\s*#define\s+ENGINE_PATCH_VERSION\s+(\d+)", RegexOptions.Compiled);
    private static readonly Regex UnityVersionRegex = new (@"unityVersion\s*=\s*""([\d.]+)", RegexOptions.Compiled);
    private static string GetCurrentEngineVersion(string SourceDirectory)
    {
        if (Unity)
        {
            return UnityVersionRegex.Match(File.ReadAllText(Path.Combine(SourceDirectory, "Configuration/BuildConfig.pm"))).Groups[1].Value;
        }
        var VersionMatch = EngineVersionRegex.Match(File.ReadAllText(Path.Combine(SourceDirectory, "Runtime/Launch/Resources/Version.h")));
        return $"{VersionMatch.Groups[1].Value}.{VersionMatch.Groups[2].Value}.{VersionMatch.Groups[3].Value}";
    }
    public static EngineVersion CurrentEngineVersion;

    private static readonly Regex InjectionDirectiveRegex = new (@"@Crysknife\((.+)\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    public static string GetInjectionDecorators(string Content)
    {
        var Result = "";
        var DirectiveMatch = InjectionDirectiveRegex.Match(Content);
        while (DirectiveMatch.Success)
        {
            Result = string.Join(',', Result, DirectiveMatch.Groups[1].Value);
            DirectiveMatch = DirectiveMatch.NextMatch();
        }
        return Result;
    }

    public static bool ContainsRegex(string Content, string TargetRE)
    {
        return new Regex(TargetRE).IsMatch(Content);
    }

    private static readonly Regex PredicateRegex = new (@"@Predicate\((.+)\)", RegexOptions.Compiled);
    public static bool GetVariablePredicate(string Value, out string Predicate)
    {
        var Matched = PredicateRegex.Match(Value);
        Predicate = Matched.Groups[1].Value;
        return Matched.Success;
    }

    private static readonly Regex LocalConfigRegex = new (@"Crysknife(\w*)Local.ini", RegexOptions.Compiled);
    public static string GetLocalConfigSuffix(string Value)
    {
        return LocalConfigRegex.Match(Value).Groups[1].Value;
    }

    private static readonly Regex SeparatorRegex = new (@"[\\/]", RegexOptions.Compiled);
    public static string UnifySeparators(string Value, string Target)
    {
        return SeparatorRegex.Replace(Value, Target);
    }
    public static string UnifySeparators(string Value)
    {
        return UnifySeparators(Value, Path.DirectorySeparatorChar.ToString());
    }

    private static readonly Regex NonWindowsRE = new (@"(?<!\r)\n|\r(?!\n)", RegexOptions.Compiled);
    private static readonly Regex NonUnixRE = new (@"\r\n|\r(?!\n)", RegexOptions.Compiled);
    public static string UnifyLineEndings(string Content, bool Crlf = false)
    {
        return Crlf ? NonWindowsRE.Replace(Content, "\r\n") : NonUnixRE.Replace(Content, "\n");
    }

    private static readonly Regex TruthyRegex = new ("^(?:T|On)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex BinaryOps = new ("(==|!=|>=|<=|>|<)", RegexOptions.Compiled);
    public static bool IsTruthyValue(string Value)
    {
        var OpsMatch = BinaryOps.Match(Value);
        if (OpsMatch.Success)
        {
            var Left = Value[..OpsMatch.Index];
            var Right = Value[(OpsMatch.Index + OpsMatch.Length)..];
            return OpsMatch.Groups[1].Value switch
            {
                "==" => Left.Equals(Right, StringComparison.OrdinalIgnoreCase),
                "!=" => !Left.Equals(Right, StringComparison.OrdinalIgnoreCase),
                ">" => int.Parse(Left) > int.Parse(Right),
                "<" => int.Parse(Left) < int.Parse(Right),
                ">=" => int.Parse(Left) >= int.Parse(Right),
                "<=" => int.Parse(Left) <= int.Parse(Right),
                _ => throw new ArgumentOutOfRangeException()
            };
        }
        if (int.TryParse(Value, out var Number)) return Number > 0;
        return TruthyRegex.IsMatch(Value);
    }

    private static readonly Regex VariableRegex = new (@"\${([\w|]+)}", RegexOptions.Compiled);

    [Flags]
    public enum MapFlag
    {
        None = 0x0,
        Shallow = 0x1,
        AllowLocal = 0x2,
        IgnoreFallbacks = 0x4,
        SkipWarning = 0x8,
    }
    public static string MapVariables(IReadOnlyDictionary<string, string> Variables, string Input, MapFlag Flags = MapFlag.None)
    {
        MapVariables(Variables, Input, out var Result, Flags);
        return Result;
    }

    public static bool MapVariables(IReadOnlyDictionary<string, string> Variables, string Input, out string Result, MapFlag Flags = MapFlag.None)
    {
        var AllSuccess = true;

        Result = VariableRegex.Replace(Input, Matched =>
        {
            var Names = Matched.Groups[1].Value.Split('|');
            foreach (var Index in Enumerable.Range(0, Names.Length))
            {
                // Skip if ignored
                if (Index > 0 && Flags.HasFlag(MapFlag.IgnoreFallbacks)) return Matched.Value;

                var LocalName = '#' + Names[Index];
                var IsLocal = Variables.ContainsKey(LocalName);
                if (!Flags.HasFlag(MapFlag.AllowLocal) && IsLocal) return Matched.Value;
                if (!Variables.TryGetValue(IsLocal ? LocalName : Names[Index], out var Value)) continue; // Not found

                if (!Flags.HasFlag(MapFlag.Shallow)) MapVariables(Variables, Value, out Value, Flags);
                return Value;
            }

            AllSuccess = false;

            if (!Flags.HasFlag(MapFlag.SkipWarning))
            {
                Logger.Warning($"Invalid variable reference: '{Matched.Groups[1].Value}' not found");
            }

            return Matched.Value;
        });

        return AllSuccess;
    }

    public static void EnsureParentDirectoryExists(string TargetPath)
    {
        var TargetDir = Path.GetDirectoryName(TargetPath);
        if (TargetDir != null && !Directory.Exists(TargetDir)) Directory.CreateDirectory(TargetDir);
    }

    public const StringSplitOptions SplitOptions = StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries;

    public static bool FindAndRemoveString(List<string> Values, string Target)
    {
        return Values.RemoveAll(Value => Value.Equals(Target, StringComparison.OrdinalIgnoreCase)) > 0;
    }

    public static bool FileAccessGuard(Action Action, string Dest)
    {
        try
        {
            if (File.Exists(Dest))
            {
                File.SetAttributes(Dest, File.GetAttributes(Dest) & ~FileAttributes.ReadOnly);
            }
            Action();
        }
        catch (Exception E)
        {
            Logger.Warning($"Failed to access '{Dest}': {E}");
            return false;
        }

        return true;
    }

    [Flags]
    public enum ConfirmResult
    {
        NotDecided = 0x0,
        Yes = 0x1,
        No = 0x2,
        ForAll = 0x4,
    }
    public static ConfirmResult PromptToConfirm(string Message)
    {
        ConsoleKey Response;

        do
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write("{0} [Yes(Y)/No(N)/YesForAll(A)/NoForAll(Z)/Abort(C)] ", Message); // Interactive prompt, keep Console
            if (!Console.IsInputRedirected)
            {
                Response = Console.ReadKey(false).Key; // true is intercept key (don't show), false is show
                if (Response != ConsoleKey.Enter) Console.WriteLine();
            }
            else
            {
                var Key = Console.ReadLine()?.ToUpper()[0].ToString();
                if (!Enum.TryParse(Key, out Response)) Response = ConsoleKey.Enter;
            }
        } while (Response is not (ConsoleKey.Y or ConsoleKey.N or ConsoleKey.A or ConsoleKey.Z or ConsoleKey.C));

        if (Response == ConsoleKey.C)
        {
            Abort();
        }

        return Response switch
        {
            ConsoleKey.Y => ConfirmResult.Yes,
            ConsoleKey.N => ConfirmResult.No,
            ConsoleKey.A => ConfirmResult.Yes | ConfirmResult.ForAll,
            ConsoleKey.Z => ConfirmResult.No | ConfirmResult.ForAll,
            _ => ConfirmResult.No
        };
    }

    private static readonly HashSet<string> PatchableExtensions = new(StringComparer.OrdinalIgnoreCase);

    public static void SetPatchableExtensions(string Extensions)
    {
	    PatchableExtensions.Clear();

	    foreach (var ext in Extensions.Split('|'))
		    PatchableExtensions.Add(ext);
    }

    public static bool CanBePatched(string TargetPath)
    {
        return PatchableExtensions.Contains(Path.GetExtension(TargetPath));
    }

    public static string GetEngineRoot()
    {
        return EngineRoot;
    }

    public static string GetSourceDirectory()
    {
        return Unity ? Path.Combine(EngineRoot) : Path.Combine(EngineRoot, "Source");
    }

    public static string GetPluginFolderName()
    {
        return Unity ? "Modules" : "Plugins";
    }

    public static string GetPluginDirectory(string PluginName)
    {
        return Path.Combine(EngineRoot, GetPluginFolderName(), PluginName);
    }

    public static string GetPatchDirectory(string PluginName)
    {
        return Path.Combine(GetPluginDirectory(PluginName), "SourcePatch");
    }

    public static string GetEngineRelativePath(string TargetPath)
    {
        return Path.GetRelativePath(EngineRoot, TargetPath);
    }

    // Se7en        -> se7en
    // Addr2Line    -> addr_2_line
    // HTMLElement  -> html_element
    // OptionA      -> option_a
    // Route66      -> route_66
    private static readonly Regex CaseRegex = new (@"(?:(?<=[a-zA-Z\d])[A-Z](?=[a-z]|$|\s))|(?:(?<=[a-zA-Z])\d(?=[A-Z\d]|$|\s))", RegexOptions.Compiled);
    public static string CamelCaseToSnakeCase(string Value)
    {
        return CaseRegex.Replace(Value, Match => $"_{Match.Value}").ToLower();
    }

    private static readonly Regex EscapeRegex = new ("\\" + string.Join<char>("|\\", "^$()[].*+?"), RegexOptions.Compiled);
    public static string EscapeLiteralsForRegex(string Value)
    {
        return EscapeRegex.Replace(Value, Matched => $"\\{Matched.Value}");
    }

    public static string ApplyNewFileTag(CommentTagPacker Packer, IReadOnlyDictionary<string, string> Variables, string Content)
    {
        if (!Packer.Format.TagNewFiles) return Content;
        return $"/** {Packer.GetDefaultTag(Variables)} */\n{Content}";
    }

    public static string StripNewFileTag(CommentTagPacker Packer, IReadOnlyDictionary<string, string> Variables, string Content)
    {
        if (!Packer.Format.TagNewFiles) return Content;
        int NewLine = Content.IndexOf('\n');
        return Content[..NewLine].Contains(Packer.GetDefaultTag(Variables)) ? Content[(NewLine + 1)..] : Content;
    }

    public static void Abort(string Message = "", CrysknifeExitCode ExitCode = CrysknifeExitCode.Fatal)
    {
        if (Message.Length > 0) Logger.Fatal(Message);
        Console.ResetColor();
        Environment.Exit((int)ExitCode);
    }

    private static string EngineRoot = string.Empty;
    private static bool Unity;

    public static bool IsUnity()
    {
        return Unity;
    }

    public static void Init(string RootDirectory)
    {
        EngineRoot = RootDirectory;
        Unity = File.Exists(Path.Combine(EngineRoot, "jam"));
        CurrentEngineVersion = EngineVersion.Create(GetCurrentEngineVersion(GetSourceDirectory()));
    }
}
