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
    private struct PredicateInstance
    {
        public readonly List<string> Conditions = new();
        public bool LogicalAnd = false;
        public PredicateInstance() {}
        public bool Eval(Func<string, bool> Pred)
        {
            return LogicalAnd ? Conditions.All(Pred) : Conditions.Any(Pred);
        }
    }

    private bool Eval(bool Result, bool NewResult)
    {
        return LogicalAnd ? Result && NewResult : Result || NewResult;
    }

    private static bool ContainsString(IEnumerable<string> Values, string Target)
    {
        return Values.Any(Value => Value.Equals(Target, StringComparison.OrdinalIgnoreCase));
    }

    private readonly List<string> FullDesc = new();
    private bool CompileTimePredicate;
    private bool LogicalAnd; // By default all predicates are disjunction
    private PredicateInstance FilenamePredicates = new();

    public void Add(string Desc, ConfigLineAction Action)
    {
        switch (Action)
        {
            case ConfigLineAction.Set:
                FullDesc.Clear();
                FullDesc.Add(Desc);
                break;
            case ConfigLineAction.Add:
                FullDesc.Add(Desc);
                break;
            case ConfigLineAction.RemoveKey:
                FullDesc.Clear();
                break;
            case ConfigLineAction.RemoveKeyValue:
                FullDesc.Remove(Desc);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(Action), Action, null);
        }
    }

    public void Compile(string RootPath)
    {
        var ExistencePredicates = new PredicateInstance();

        foreach (var Rule in FullDesc.SelectMany(Desc => Desc.Split(',')))
        {
            if (Rule.StartsWith("Exist:", StringComparison.OrdinalIgnoreCase))
            {
                ExistencePredicates.Conditions.AddRange(Rule[6..].Split('|'));
            }
            else if (Rule.StartsWith("Filename:", StringComparison.OrdinalIgnoreCase))
            {
                FilenamePredicates.Conditions.AddRange(Rule[9..].Split('|'));
            }
            else if (Rule.StartsWith("Always", StringComparison.OrdinalIgnoreCase))
            {
                CompileTimePredicate = true;
            }
            else if (Rule.StartsWith("Conjunction:", StringComparison.OrdinalIgnoreCase))
            {
                var Scopes = Rule[12..].Split('|');
                bool AlwaysTrue = ContainsString(Scopes, "All");
                if (AlwaysTrue || ContainsString(Scopes, "Global")) LogicalAnd = true;
                else if (AlwaysTrue || ContainsString(Scopes, "Exist")) ExistencePredicates.LogicalAnd = true;
                else if (AlwaysTrue || ContainsString(Scopes, "Filename")) FilenamePredicates.LogicalAnd = true;
            }
        }

        foreach (var TargetPath in ExistencePredicates.Conditions.Select(Cond => Path.Combine(RootPath, Cond)))
        {
            CompileTimePredicate = Eval(CompileTimePredicate, File.Exists(TargetPath) || Directory.Exists(TargetPath));
        }
    }

    public bool Eval(string Target)
    {
        bool Result = CompileTimePredicate;
        Result = Eval(Result, FilenamePredicates.Eval(Cond => Path.GetFileName(Target).Contains(Cond, StringComparison.OrdinalIgnoreCase)));
        return Result;
    }
}

internal class ScopedRules
{
    private readonly string TargetName;
    private readonly string RemapTarget = string.Empty;
    private readonly ConfigPredicate RemapRule = new();
    private readonly ConfigPredicate SkipRule = new();

    public ScopedRules(string SectionName, ConfigFileSection Section, string RootPath)
    {
        // Global section affects all targets
        TargetName = SectionName.Equals("Global", StringComparison.OrdinalIgnoreCase) ? "" : SectionName;
        TargetName = InjectorConfig.SeparatorPatch(TargetName);

        foreach (ConfigLine Line in Section.Lines)
        {
            if (Line.Key.Equals("SkipIf", StringComparison.OrdinalIgnoreCase))
            {
                SkipRule.Add(Line.Value, Line.Action);
            }
            else if (Line.Key.Equals("RemapIf", StringComparison.OrdinalIgnoreCase))
            {
                RemapRule.Add(Line.Value, Line.Action);
            }
            else if (Line.Key.Equals("RemapTarget", StringComparison.OrdinalIgnoreCase))
            {
                RemapTarget = InjectorConfig.SeparatorPatch(Line.Value);
            }
        }

        SkipRule.Compile(RootPath);
        RemapRule.Compile(RootPath);
    }

    public bool Affects(string Target)
    {
        return Target.StartsWith(TargetName);
    }

    public bool Remap(string Target, out string Result)
    {
        Result = Target;
        if (SkipRule.Eval(Target)) return false;

        if (RemapRule.Eval(Target))
        {
            Result = TargetName == string.Empty ? Path.Combine(RemapTarget, Path.GetFileName(Target)) : Target.Replace(TargetName, RemapTarget);
        }
        return true;
    }
}

public class InjectorConfig
{
    private static ConfigFile BaseConfig = new();
    private readonly List<ScopedRules> Scopes = new();

    private static readonly Regex SeparatorRE = new (@"[\\/]", RegexOptions.Compiled);
    public static string SeparatorPatch(string Value)
    {
        return SeparatorRE.Replace(Value, Path.DirectorySeparatorChar.ToString());
    }

    public static void Init(string RootDirectory)
    {
        ConfigFile.Init(RootDirectory);
        string ConfigPath = Path.Combine(RootDirectory, "BaseInjector.ini");
        if (File.Exists(ConfigPath)) BaseConfig = new ConfigFile(ConfigPath);
    }

    public InjectorConfig(string ConfigPath, string RootPath)
    {
        ConfigFile Config = File.Exists(ConfigPath) ? new ConfigFile(ConfigPath) : new ConfigFile();

        // Merge base config sections
        foreach (string SectionName in BaseConfig.SectionNames)
        {
            if (BaseConfig.TryGetSection(SectionName, out var BaseSection))
            {
                Config.FindOrAddSection(SectionName).Lines.AddRange(BaseSection.Lines);
            }
        }

        // Parse into scoped rules
        foreach (string SectionName in Config.SectionNames)
        {
            if (Config.TryGetSection(SectionName, out var Section))
            {
                Scopes.Add(new ScopedRules(SectionName, Section, RootPath));
            }
        }
    }

    public bool Remap(string Target, out string Result)
    {
        Result = Target;

        foreach (var Scope in Scopes.Where(Rules => Rules.Affects(Target)))
        {
            if (!Scope.Remap(Target, out Result)) return false;
        }
        return true;
    }
}
