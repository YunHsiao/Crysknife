// SPDX-FileCopyrightText: 2024 Yun Hsiao Wu <yunhsiaow@gmail.com>
// SPDX-License-Identifier: MIT

using System.Diagnostics;
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
        // return RE.Replace(Content, Matched => Replace(
        //     Matched.Groups["Tag"].Value, Matched.Groups["Content"].Value));

        string Result = "";
        int CurrentStart = 0;

        foreach (Match Matched in RE.Matches(Content))
        {
            Result += Content.Substring(CurrentStart, Matched.Index - CurrentStart);
            Result += Replace(Matched.Groups["Tag"].Value, Matched.Groups["Content"].Value);
            CurrentStart = Matched.Index + Matched.Length;
        }
        Result += Content.Substring(CurrentStart, Content.Length - CurrentStart);
        return Result;
    }

    protected virtual string Replace(string Tag, string Content)
    {
        if (Tag.StartsWith(ProjectName + '-')) // Restore deletions
        {
            return CommentRE.Replace(Content, ContentMatch => ContentMatch.Groups[1].Value);
        }
        return ""; // Remove injections
    }
}

internal class InjectionRegexMultiLineForm : InjectionRegexForm
{
    private readonly Regex SeparatorRE;

    public InjectionRegexMultiLineForm(string ProjectName, string ProjectTag)
        : base(ProjectName, string.Format(@"\s*// (?<Tag>{0}): Begin(?<Content>.*?)// {0}: End\s*?\n", ProjectTag),
            RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture | RegexOptions.Compiled)
    {
        SeparatorRE = new Regex($"// (?<Tag>{ProjectTag}): (?<State>Begin|End)",
            RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture | RegexOptions.Compiled);
    }

    // Multiple code blocks may be merged together, need to handle them here
    protected override string Replace(string Tag, string Content)
    {
        MatchCollection Matches = SeparatorRE.Matches(Content);
        if (Matches.Count == 0)
        {
            return base.Replace(Tag, Content);
        }

        string Result = "";
        int CurrentStart = 0;
        bool InsideBlock = true;

        foreach (Match Matched in Matches)
        {
            switch (Matched.Groups["State"].Value)
            {
                case "Begin":
                    Result += Content.Substring(CurrentStart, Matched.Index - CurrentStart);
                    Tag = Matched.Groups["Tag"].Value;
                    Debug.Assert(!InsideBlock);
                    InsideBlock = true;
                    break;
                case "End":
                    Result += base.Replace(Tag, Content.Substring(CurrentStart, Matched.Index - CurrentStart));
                    Debug.Assert(InsideBlock);
                    InsideBlock = false;
                    break;
            }
            CurrentStart = Matched.Index + Matched.Length;
        }

        return Result;
    }
}

public class InjectionRegex
{
    private readonly InjectionRegexForm[] Forms;

    public InjectionRegex(string ProjectName)
    {
        string ProjectTag = ProjectName + @"[\w\s:+-]*?"; // Allow some comments in between
        Forms = new []
        {
            new InjectionRegexMultiLineForm(ProjectName, ProjectTag), // Multi-line form
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

public static class RegexEngine
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
}
