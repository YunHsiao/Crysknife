/*
 * MIT License
 *
 * Copyright (c) 2024 Yun Hsiao Wu
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 */

using System.Text.RegularExpressions;
using UnrealBuildTool;

namespace UnrealSourceInjector;

internal class ConfigPredicate
{
    public void Parse(string Desc, ConfigLineAction Action)
    {
        
    }

    public void Compile()
    {
        
    }

    public bool Eval(string Target)
    {
        return false;
    }
}

internal class ConfigRules
{
    private readonly string TargetName;
    private readonly ConfigPredicate SkipPredicate = new();
    private readonly ConfigPredicate RemapPredicate = new();
    private readonly string RemapTarget = string.Empty;

    public ConfigRules(string SectionName, ConfigFileSection Section)
    {
        TargetName = SectionName;

        foreach (ConfigLine Line in Section.Lines)
        {
            switch (Line.Key)
            {
                case "SkipIf":
                    SkipPredicate.Parse(Line.Value, Line.Action);
                    break;
                case "RemapIf":
                    RemapPredicate.Parse(Line.Value, Line.Action);
                    break;
                case "RemapTarget":
                    RemapTarget = Line.Value;
                    break;
            }
        }

        SkipPredicate.Compile();
        RemapPredicate.Compile();
    }

    public bool Affects(string Target)
    {
        return Target.StartsWith(TargetName);
    }

    public bool Remap(string Target, out string Result)
    {
        if (SkipPredicate.Eval(Target))
        {
            Result = Target;
            return false;
        }

        Result = RemapPredicate.Eval(Target) ? Target.Replace(TargetName, RemapTarget) : Target;
        return true;
    }
}

public class Config
{
    private readonly List<ConfigRules> RulesList = new();
    private static readonly Regex SeparatorRE = new (@"[\\/]", RegexOptions.Compiled);

    public Config(string ConfigPath)
    {
        ConfigFile Config = File.Exists(ConfigPath) ? new ConfigFile(ConfigPath) : new ConfigFile();

        foreach (string SectionName in Config.SectionNames)
        {
            if (Config.TryGetSection(SectionName, out var Section))
            {
                RulesList.Add(new ConfigRules(SectionName, Section));
            }
        }
    }

    public bool Remap(string Target, out string Result)
    {
        string RulesKey = SeparatorRE.Replace(Target, "/");

        foreach (var Rules in RulesList.Where(Rules => Rules.Affects(RulesKey)))
        {
            return Rules.Remap(Target, out Result);
        }

        Result = Target;
        return true;
    }
}
