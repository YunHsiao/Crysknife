// SPDX-FileCopyrightText: 2024 Yun Hsiao Wu <yunhsiaow@gmail.com>
// SPDX-License-Identifier: MIT

using System.Text.RegularExpressions;

namespace Crysknife;

internal class InjectionRegexForm
{
    private static readonly Regex CommentRE = new (@"^(\s*)//\s*", RegexOptions.Multiline | RegexOptions.Compiled);

    private readonly string PluginName;
    private readonly Regex RE;

    public InjectionRegexForm(string PluginName, string Pattern, RegexOptions Options)
    {
        this.PluginName = PluginName;
        RE = new Regex(Pattern, Options);
    }

    public string Unpatch(string Content)
    {
        return RE.Replace(Content, Matched => Replace(
            Matched.Groups["Tag"].Value, Matched.Groups["Content"].Value));
    }

    private string Replace(string Tag, string Content)
    {
        if (Tag.StartsWith(PluginName + '-')) // Restore deletions
        {
            return CommentRE.Replace(Content, ContentMatch => ContentMatch.Groups[1].Value);
        }
        return ""; // Remove injections
    }
}

internal class InjectionRegex
{
    private readonly InjectionRegexForm[] Forms;

    public InjectionRegex(string PluginName)
    {
        string CommentTag = PluginName + @"[^\n]*?"; // Allow some comments in between
        Forms = new []
        {
            new InjectionRegexForm(PluginName, string.Format(@"\s*// (?<Tag>{0}): Begin(?<Content>.*?)// {0}: End\s*?\n", CommentTag),
                RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture | RegexOptions.Compiled), // Multi-line form
            new InjectionRegexForm(PluginName, $@"^(?<Content>\s*\S+.*?)[^\S\n]*// (?<Tag>{CommentTag})\n",
                RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture | RegexOptions.Compiled), // Single-line form
            new InjectionRegexForm(PluginName, $@"^\s*// (?<Tag>{CommentTag})\n(?<Content>.*)\n",
                RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture | RegexOptions.Compiled) // Next-line form
        };
    }

    public string Unpatch(string Content)
    {
        return Forms.Aggregate(Content, (Acc, Form) => Form.Unpatch(Acc));
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
    private static readonly Regex EngineVersionRE = new (@"#define\s+ENGINE_MAJOR_VERSION\s+(\d+)\s*#define\s+ENGINE_MINOR_VERSION\s+(\d+)\s*#define\s+ENGINE_PATCH_VERSION\s+(\d+)", RegexOptions.Compiled);
    private static string GetCurrentEngineVersion(string SourceDirectory)
    {
        var VersionMatch = EngineVersionRE.Match(File.ReadAllText(Path.Combine(SourceDirectory, "Runtime/Launch/Resources/Version.h")));
        return $"{VersionMatch.Groups[1].Value}.{VersionMatch.Groups[2].Value}.{VersionMatch.Groups[3].Value}";
    }
    public static EngineVersion CurrentEngineVersion;

    private static readonly Regex InjectionDirectiveRE = new (@"\@Crysknife\((.+)\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    public static string GetInjectionDecorators(string Content, string CommentTag)
    {
        string Result = "";
        var CommentMatch = new Regex($"// {CommentTag}(.+)", RegexOptions.IgnoreCase).Match(Content);
        while (CommentMatch.Success)
        {
            var DirectiveMatch = InjectionDirectiveRE.Match(CommentMatch.Groups[1].Value);
            while (DirectiveMatch.Success)
            {
                Result = string.Join(',', Result, DirectiveMatch.Groups[1].Value);
                DirectiveMatch = DirectiveMatch.NextMatch();
            }
            CommentMatch = CommentMatch.NextMatch();
        }
        return Result;
    }

    private static readonly Regex SeparatorRE = new (@"[\\/]", RegexOptions.Compiled);
    public static string UnifySeparators(string Value)
    {
        return SeparatorRE.Replace(Value, Path.DirectorySeparatorChar.ToString());
    }

    private static readonly Regex TruthyRE = new ("^(?:T|On)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
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
        return TruthyRE.IsMatch(Value);
    }

    private static readonly Regex VariableRE = new (@"\${(\w+)}", RegexOptions.Compiled);
    public static string MapVariables(IDictionary<string, string> Variables, string Input)
    {
        return VariableRE.Replace(Input, Matched =>
        {
            string Name = Matched.Groups[1].Value;
            if (Variables.TryGetValue(Name, out var Value))
            {
                return MapVariables(Variables, Value);
            }
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Invalid variable reference: '{Name}' not found");
            return Matched.Value;
        });
    }

    public static void EnsureParentDirectoryExists(string TargetPath)
    {
        string? TargetDir = Path.GetDirectoryName(TargetPath);
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
            Utils.Abort();
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
