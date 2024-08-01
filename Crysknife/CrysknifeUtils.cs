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

internal static class Utils
{
    private static readonly Regex EngineVersionRE = new (@"#define\s+ENGINE_MAJOR_VERSION\s+(\d+)\s*#define\s+ENGINE_MINOR_VERSION\s+(\d+)", RegexOptions.Compiled);
    public static string GetCurrentEngineVersion(string SourceDirectory)
    {
        Match VersionMatch = EngineVersionRE.Match(File.ReadAllText(Path.Combine(SourceDirectory, "Runtime/Launch/Resources/Version.h")));
        return $"{VersionMatch.Groups[1].Value}_{VersionMatch.Groups[2].Value}";
    }

    private static readonly Regex SeparatorRE = new (@"[\\/]", RegexOptions.Compiled);
    public static string UnifySeparators(string Value)
    {
        return SeparatorRE.Replace(Value, Path.DirectorySeparatorChar.ToString());
    }

    private static readonly Regex TruthyRE = new ("^(T|On)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    public static bool IsTruthyValue(string Value)
    {
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
            File.SetAttributes(Dest, File.GetAttributes(Dest) & ~FileAttributes.ReadOnly);
            Action();
        }
        catch
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Failed to access '{0}'", Dest);
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
    }
}
