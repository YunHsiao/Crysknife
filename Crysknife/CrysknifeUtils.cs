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

    public string Pack(string Content, bool SkipCaptures, string ModuleName, string[] Sentinels)
    {
        return Pattern.Replace(Content, Matched =>
        {
            Group CurrentTag = Matched.Groups["FullTag"];
            string Result = Matched.Value[..(CurrentTag.Index - Matched.Index)];
            Result += Matched.Groups.TryGetValue("EndTag", out var EndTag) ? $"@{ModuleName}CTBegin" : $"@{ModuleName}CT";

            string MatchedContent = Matched.Groups["Tag"].Value[CommentTag.Length..];
            // We may encounter partial insertions where only begin or end is in the context
            int SentinelIndex = Array.FindIndex(Sentinels, Sentinel =>
                Sentinel.Equals(MatchedContent, StringComparison.OrdinalIgnoreCase));
            // Skip if we are inside the multi-line form
            // Most likely we are inside the next-line form here
            if (SentinelIndex >= 0 && EndTag == null)
            {
                Result += SentinelIndex % 2 == 0 ? "Begin()\n" : "End()\n";
            }
            else Result += $"({MatchedContent})\n";

            if (!SkipCaptures)
            {
                foreach (var Index in Enumerable.Range(0, 10))
                {
                    if (!Matched.Groups.TryGetValue($"Capture{Index}", out var Capture)) break;
                    Result += $"@{ModuleName}CTCapture{Index}({Capture.Value})\n";
                }
            }

            if (EndTag != null)
            {
                Result += Matched.Value[(CurrentTag.Index - Matched.Index + CurrentTag.Length)..(EndTag.Index - Matched.Index)];
                Result += $"@{ModuleName}CTEnd()\n";
                CurrentTag = EndTag;
            }

            Result += Matched.Value[(CurrentTag.Index - Matched.Index + CurrentTag.Length)..];
            return Result;
        });
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

internal class InjectionReconstructor
{
    private readonly Regex ReconstructorRegex;
    private readonly string Tag;
    private readonly string Prefix;
    private readonly string Suffix;
    private readonly string Begin;
    private readonly string End;

    public InjectionReconstructor(string ModuleName, string Tag, string Prefix, string Suffix, string Begin, string End)
    {
        this.Tag = Tag;
        this.Prefix = Prefix;
        this.Suffix = Suffix;
        this.Begin = Begin;
        this.End = End;
        ReconstructorRegex = new Regex(@$"@{ModuleName}CT(\w*)\(([^\n]*)\)\n", RegexOptions.Compiled);
    }

    public string Unpack(string Content, IReadOnlyDictionary<string, string> Variables)
    {
        var CaptureRecord = new Dictionary<string, string>();

        var Result = ReconstructorRegex.Replace(Content, Matched =>
        {
            if (Matched.Groups[1].Value.StartsWith("Capture"))
            {
                CaptureRecord.Add(Matched.Groups[1].Value, Matched.Groups[2].Value);
                return string.Empty;
            }

            var Reconstructed = Prefix + Tag + Matched.Groups[2].Value + Suffix;

            return Matched.Groups[1].Value switch
            {
                "" => Reconstructed,
                "Begin" => Reconstructed + Begin,
                "End" => Reconstructed + End,
                _ => throw new ArgumentOutOfRangeException(nameof(Content))
            };
        });

        if (Utils.MapVariables(CaptureRecord, Result, true, false, out var Temp)) return Temp;
        return Utils.MapVariables(Variables, Result, true, false); // Fallback to config variables
    }
}

internal class InjectionRegex
{
    private readonly string ModuleName;
    private readonly InjectionRegexForm[] Forms;
    private readonly InjectionReconstructor Reconstructors;
    private readonly string[] Sentinels;

    public InjectionRegex(string ModuleName, string Tag, string Prefix, string Suffix, string Begin, string End,
        string PrefixCtor, string SuffixCtor, string BeginCtor, string EndCtor)
    {
        this.ModuleName = ModuleName;
        Sentinels = new[] { Begin, End };

        var CommentTag = Tag + @"[^\n]*?"; // Allow some comments in between
        Forms = new []
        {
            // Form order matters here, specific -> general
            new InjectionRegexForm(Tag, string.Format(@"[^\S\n]*// (?<FullTag>{1}(?<Tag>{0}){2}{3})(?<Content>.*?)// (?<EndTag>{1}{0}{2}{4})[^\S\n]*\n", CommentTag, Prefix, Suffix, Begin, End),
                RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture | RegexOptions.Compiled), // Multi-line form
            new InjectionRegexForm(Tag, $@"^(?<Content>[^\S\n]*\S+.*?)[^\S\n]*// (?<FullTag>{Prefix}(?<Tag>{CommentTag}){Suffix})\n",
                RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture | RegexOptions.Compiled), // Single-line form
            new InjectionRegexForm(Tag, $@"^[^\S\n]*// (?<FullTag>{Prefix}(?<Tag>{CommentTag}){Suffix})\n(?<Content>.*)\n",
                RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture | RegexOptions.Compiled), // Next-line form
        };
        Reconstructors = new InjectionReconstructor(ModuleName, Tag, PrefixCtor, SuffixCtor, BeginCtor, EndCtor);
    }

    public List<Match> Match(string Content)
    {
        return Forms.Aggregate(new List<Match>(), (Acc, Form) =>
        {
            var Match = Form.Match(Content);
            if (Match.Success) Acc.Add(Match);
            return Acc;
        });
    }

    public string Unpatch(string Content)
    {
        return Forms.Aggregate(Content, (Acc, Form) => Form.Unpatch(Acc));
    }

    public string Pack(string Content, ref int Increment, bool SkipCaptures)
    {
        var Result = Forms.Aggregate(Content, (Acc, Form) => Form.Pack(Acc, SkipCaptures, ModuleName, Sentinels));
        Increment += Result.Length - Content.Length;
        return Result;
    }

    public string Unpack(string Content, ref int Increment, IReadOnlyDictionary<string, string> Variables)
    {
        var Result = Reconstructors.Unpack(Content, Variables);
        Increment += Result.Length - Content.Length;
        return Result;
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

    public static string MapVariables(IReadOnlyDictionary<string, string> Variables, string Input, bool Recurse = true, bool WarnIfFailed = true)
    {
        MapVariables(Variables, Input, Recurse, WarnIfFailed, out var Result);
        return Result;
    }

    public static bool MapVariables(IReadOnlyDictionary<string, string> Variables, string Input, bool Recurse, bool WarnIfFailed, out string Result)
    {
        var AllSuccess = true;

        Result = VariableRegex.Replace(Input, Matched =>
        {
            foreach (var Name in Matched.Groups[1].Value.Split('|'))
            {
                if (!Variables.TryGetValue(Name, out var Value)) continue;
                if (Recurse) MapVariables(Variables, Value, Recurse, WarnIfFailed, out Value);
                return Value;
            }

            AllSuccess = false;
            if (!WarnIfFailed) return Matched.Value;

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Invalid variable reference: '{Matched.Groups[1].Value}' not found");
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

    public static bool GetContentIfStartsWith(string Str, string Prefix, out string Content)
    {
        if (Str.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            Content = Str[Prefix.Length..];
            return true;
        }

        Content = Str;
        return false;
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
