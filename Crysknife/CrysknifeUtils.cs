// SPDX-FileCopyrightText: 2024 Yun Hsiao Wu <yunhsiaow@gmail.com>
// SPDX-License-Identifier: MIT

using System.Text.RegularExpressions;

namespace Crysknife;

internal class InjectionRegexForm
{
    private static readonly Regex CommentRegex = new (@"^(\s*)//\s*", RegexOptions.Multiline | RegexOptions.Compiled);
    private readonly string CommentTag;
    private readonly Regex Pattern;

    public InjectionRegexForm(string CommentTag, string Pattern, RegexOptions Options)
    {
        this.CommentTag = CommentTag;
        this.Pattern = new Regex(Pattern, Options);
    }

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

internal struct CommentTagFormat
{
    public string PrefixRegex;
    public string SuffixRegex;
    public string BeginRegex;
    public string EndRegex;

    public string PrefixCtor;
    public string SuffixCtor;
    public string BeginCtor;
    public string EndCtor;
}

internal class InjectionRegex
{
    private readonly InjectionRegexForm[] MatchForms;

    public InjectionRegex(string Tag, CommentTagFormat Format)
    {
        var CommentTag = Utils.EscapeForRegex(Tag) + @"[^\n]*?"; // Allow some comments in between

        MatchForms = new []
        {
            // Form order matters here, specific -> general
            new InjectionRegexForm(Tag, string.Format(@"[^\S\n]*//[^\S\n]*{0}(?<Tag>{1}){2}{3}(?<Content>.*?)// (?<EndTag>{0}{1}{2}{4})[^\S\n]*\n",
                Format.PrefixRegex, CommentTag, Format.SuffixRegex, Format.BeginRegex, Format.EndRegex),
                RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled), // Multi-line form
            new InjectionRegexForm(Tag, $@"^(?<Content>[^\S\n]*\S+.*?)[^\S\n]*//[^\S\n]*{Format.PrefixRegex}(?<Tag>{CommentTag}){Format.SuffixRegex}[^\S\n]*\n",
                RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled), // Single-line form
            new InjectionRegexForm(Tag, $@"^[^\S\n]*//[^\S\n]*{Format.PrefixRegex}(?<Tag>{CommentTag}){Format.SuffixRegex}[^\S\n]*\n(?<Content>.*)\n",
                RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled) // Next-line form
        };
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

internal class InjectionRegexGroup
{
    public readonly InjectionRegex Injection;
    private readonly List<InjectionRegex> Residuals = new();

    public InjectionRegexGroup(InjectionRegex Injection)
    {
        this.Injection = Injection;
    }

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
    private readonly string Tag;
    private readonly CommentTagFormat Format;
    private readonly string ModuleName;

    private readonly Regex PackRegex;
    private readonly Regex UnpackRegex;

    public CommentTagPacker(string ModuleName, string Tag, CommentTagFormat Format)
    {
        this.Tag = Tag;
        this.Format = Format;
        this.ModuleName = ModuleName;

        var CommentTag = Utils.EscapeForRegex(Tag) + @"[^\n]*?"; // Allow some comments in between
        var PackFormat = $@"//[^\S\n]*{Format.PrefixRegex}(?<Tag>{CommentTag}){Format.SuffixRegex}(?<Begin>{Format.BeginRegex})?(?<End>{Format.EndRegex})?[^\S\n]*(?=\n)";
        PackRegex = new Regex(PackFormat, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        UnpackRegex = new Regex($@"@{ModuleName}CT(\w*)\(([^\n]*)\)\n", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }

    private string Pack(string Content, bool SkipCaptures)
    {
        return PackRegex.Replace(Content, Matched =>
        {
            string Result = $"// @{ModuleName}CT";
            if (Matched.Groups["Begin"].Success) Result += "Begin";
            else if (Matched.Groups["End"].Success) Result += "End";
            Result += $"({Matched.Groups["Tag"].Value[Tag.Length..]})\n";
            if (SkipCaptures) return Result;

            foreach (var Index in Enumerable.Range(0, 10))
            {
                if (!Matched.Groups.TryGetValue($"Capture{Index}", out var Capture)) break;
                Result += $"@{ModuleName}CTCapture{Index}({Capture.Value})\n";
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

            var Reconstructed = Format.PrefixCtor + Tag + Matched.Groups[2].Value + Format.SuffixCtor;

            return Matched.Groups[1].Value switch
            {
                "" => Reconstructed,
                "Begin" => Reconstructed + Format.BeginCtor,
                "End" => Reconstructed + Format.EndCtor,
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
    private static string GetCurrentEngineVersion(string SourceDirectory)
    {
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

    private static readonly Regex PredicateRegex = new (@"@Predicate\((.+)\)", RegexOptions.Compiled);
    public static bool GetVariablePredicate(string Value, out string Predicate)
    {
        var Matched = PredicateRegex.Match(Value);
        Predicate = Matched.Groups[1].Value;
        return Matched.Success;
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

    private static readonly Regex TruthyRegex = new ("^(?:T|On)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex BinaryOps = new ("(==|!=|>|<|>=|<=)", RegexOptions.Compiled);
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
                "<" => int.Parse(Left) > int.Parse(Right),
                ">=" => int.Parse(Left) > int.Parse(Right),
                "<=" => int.Parse(Left) > int.Parse(Right),
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

                var LocalName = '@' + Names[Index];
                var IsLocal = Variables.ContainsKey(LocalName);
                if (!Flags.HasFlag(MapFlag.AllowLocal) && IsLocal) return Matched.Value;
                if (!Variables.TryGetValue(IsLocal ? LocalName : Names[Index], out var Value)) continue; // Not found

                if (!Flags.HasFlag(MapFlag.Shallow)) MapVariables(Variables, Value, out Value, Flags);
                return Value;
            }

            AllSuccess = false;

            if (!Flags.HasFlag(MapFlag.SkipWarning))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"Invalid variable reference: '{Matched.Groups[1].Value}' not found");
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
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Failed to access '{0}': {1}", Dest, E);
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
            Console.Write("{0} [Yes(Y)/No(N)/YesForAll(A)/NoForAll(Z)/Abort(C)] ", Message);
            Response = Console.ReadKey(false).Key;   // true is intercept key (dont show), false is show
            if (Response != ConsoleKey.Enter) Console.WriteLine();

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

    public static bool CanBePatched(string TargetPath)
    {
        return Path.GetExtension(TargetPath) is ".cpp" or ".h" or ".cs" or ".inl";
    }

    public static string GetEngineRoot()
    {
        return EngineRoot;
    }

    public static string GetSourceDirectory()
    {
        return Path.Combine(EngineRoot, "Source");
    }

    public static string GetPluginDirectory(string PluginName)
    {
        return Path.Combine(EngineRoot, "Plugins", PluginName);
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

    private static readonly Regex EscapeRegex = new("\\" + string.Join<char>("|\\", "^$()[].*+?"), RegexOptions.Compiled);
    public static string EscapeForRegex(string Value)
    {
        return EscapeRegex.Replace(Value, Matched => $"\\{Matched.Value}");
    }

    public static void Abort()
    {
        Console.ResetColor();
        Environment.Exit(1);
    }

    private static string EngineRoot = string.Empty;
    public static void Init(string RootDirectory)
    {
        EngineRoot = RootDirectory;
        CurrentEngineVersion = EngineVersion.Create(GetCurrentEngineVersion(GetSourceDirectory()));
    }
}
