// SPDX-FileCopyrightText: 2024 Yun Hsiao Wu <yunhsiaow@gmail.com>
// SPDX-License-Identifier: MIT

using System.Text.RegularExpressions;

namespace Crysknife;

internal class InjectionRegexForm
{
    private static readonly Regex CommentRE = new (@"^(\s*)//\s*", RegexOptions.Multiline | RegexOptions.Compiled);

    private readonly string ProjectName;
    private readonly Regex RE;

    public InjectionRegexForm(string ProjectName, string Pattern, RegexOptions Options)
    {
        this.ProjectName = ProjectName;
        RE = new Regex(Pattern, Options);
    }

    public string Unpatch(string Content)
    {
        return RE.Replace(Content, Matched => Replace(
            Matched.Groups["Tag"].Value, Matched.Groups["Content"].Value));
    }

    private string Replace(string Tag, string Content)
    {
        if (Tag.StartsWith(ProjectName + '-')) // Restore deletions
        {
            return CommentRE.Replace(Content, ContentMatch => ContentMatch.Groups[1].Value);
        }
        return ""; // Remove injections
    }
}

public class InjectionRegex
{
    private readonly InjectionRegexForm[] Forms;

    public InjectionRegex(string ProjectName)
    {
        string ProjectTag = ProjectName + @"[^\n]*?"; // Allow some comments in between
        Forms = new []
        {
            new InjectionRegexForm(ProjectName, string.Format(@"\s*// (?<Tag>{0}): Begin(?<Content>.*?)// {0}: End\s*?\n", ProjectTag),
                RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture | RegexOptions.Compiled), // Multi-line form
            new InjectionRegexForm(ProjectName, $@"^(?<Content>\s*\S+.*?)[^\S\n]*// (?<Tag>{ProjectTag})\n",
                RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture | RegexOptions.Compiled), // Single-line form
            new InjectionRegexForm(ProjectName, $@"^\s*// (?<Tag>{ProjectTag})\n(?<Content>.*)\n",
                RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture | RegexOptions.Compiled) // Next-line form
        };
    }

    public string Unpatch(string Content)
    {
        return Forms.Aggregate(Content, (Acc, Form) => Form.Unpatch(Acc));
    }
}

public static class RegexUtils
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
}
