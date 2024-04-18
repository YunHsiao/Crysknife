// SPDX-FileCopyrightText: 2024 Yun Hsiao Wu <yunhsiaow@gmail.com>
// SPDX-License-Identifier: MIT

namespace Crysknife;

internal class ConfigPredicate
{
    private struct PredicateInstance
    {
        public readonly List<string> Conditions = new();
        public readonly string Keyword;
        public bool LogicalAnd = false;
        public PredicateInstance(string Keyword)
        {
            this.Keyword = Keyword;
        }

        public bool Eval(Func<string, bool> Pred)
        {
            var Wrapper = (string Cond) =>
            {
                bool Invert = Cond.StartsWith('!');
                return Invert ? !Pred(Cond[1..]) : Pred(Cond);
            };
            return LogicalAnd ? Conditions.All(Wrapper) : Conditions.Any(Wrapper);
        }
    }

    private bool Eval(bool Result, bool NewResult)
    {
        return LogicalAnd ? Result && NewResult : Result || NewResult;
    }

    private bool Eval(bool Result, PredicateInstance Instance, Func<string, bool> Predicate)
    {
        if (Result ^ LogicalAnd) return Result; // Early out if possible
        return Eval(Result, Instance.Conditions.Count > 0 ? Instance.Eval(Predicate) : LogicalAnd);
    }

    private static bool ContainsString(IEnumerable<string> Values, string Target)
    {
        return Values.Any(Value => Value.Equals(Target, StringComparison.OrdinalIgnoreCase));
    }

    private readonly List<string> BaseDesc = new();
    private readonly List<string> FullDesc = new();
    private PredicateInstance[] Predicates = Array.Empty<PredicateInstance>();
    private bool CompileTimePredicate;
    private bool LogicalAnd; // By default all predicates are disjunction

    private static void Add(string Desc, ConfigLineAction Action, ICollection<string> Target)
    {
        switch (Action)
        {
            case ConfigLineAction.Set:
                Target.Clear();
                Target.Add(Desc);
                break;
            case ConfigLineAction.Add:
                Target.Add(Desc);
                break;
            case ConfigLineAction.RemoveKey:
                Target.Clear();
                break;
            case ConfigLineAction.RemoveKeyValue:
                Target.Remove(Desc);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(Action), Action, null);
        }
    }

    public void Add(string Desc, ConfigLineAction Action)
    {
        Add(Desc, Action, Desc.StartsWith("BaseDomain", StringComparison.OrdinalIgnoreCase) ? BaseDesc : FullDesc);
    }

    private void ParsePredicateInstances(IDictionary<string, string> Variables)
    {
        foreach (var Rule in BaseDesc.Concat(FullDesc).SelectMany(Desc => Desc.Split(',', StringSplitOptions.TrimEntries)))
        {
            if (Rule.StartsWith("Always", StringComparison.OrdinalIgnoreCase))
            {
                CompileTimePredicate = Eval(CompileTimePredicate, true);
            }
            else if (Rule.StartsWith("Never", StringComparison.OrdinalIgnoreCase))
            {
                CompileTimePredicate = Eval(CompileTimePredicate, false);
            }
            else if (Rule.StartsWith("Conjunctions:", StringComparison.OrdinalIgnoreCase))
            {
                var Scopes = Rule[13..].Split('|', StringSplitOptions.TrimEntries);
                bool AllTrue = ContainsString(Scopes, "All");
                bool PredicatesTrue = ContainsString(Scopes, "Predicates");
                if (AllTrue || ContainsString(Scopes, "Root")) CompileTimePredicate = LogicalAnd = true;

                for (int Index = 0; Index < Predicates.Length; ++Index)
                {
                    if (AllTrue || PredicatesTrue || ContainsString(Scopes, Predicates[Index].Keyword))
                    {
                        Predicates[Index].LogicalAnd = true;
                    }
                }
            }
            else
            {
                int Index = Array.FindIndex(Predicates, Instance => Rule.StartsWith(Instance.Keyword + ":", StringComparison.OrdinalIgnoreCase));
                if (Index >= 0) Predicates[Index].Conditions.AddRange(Rule[(Predicates[Index].Keyword.Length + 1)..]
                    .Split('|', StringSplitOptions.TrimEntries)
                    .Select(Value => Utils.MapVariables(Variables, Value)));
            }
        }
    }

    public void Compile(string RootPath, IDictionary<string, string> Variables)
    {
        Predicates = new []
        {
            new PredicateInstance("NameMatches"),
            new PredicateInstance("TargetExists"),
            new PredicateInstance("IsTruthy"),
        };

        ParsePredicateInstances(Variables);

        CompileTimePredicate = Eval(CompileTimePredicate, Predicates[1], Cond =>
        {
            string TargetPath = Path.Combine(RootPath, Cond);
            return File.Exists(TargetPath) || Directory.Exists(TargetPath);
        });
        CompileTimePredicate = Eval(CompileTimePredicate, Predicates[2], Utils.IsTruthyValue);
    }

    public bool Eval(string Target)
    {
        bool Result = CompileTimePredicate;
        Result = Eval(Result, Predicates[0], Cond => Path.GetFileName(Target).Contains(Cond, StringComparison.OrdinalIgnoreCase));
        return Result;
    }
}

internal enum RemapResult
{
    None,
    Skipped,
    Remapped,
}

internal class ScopedRules
{
    private readonly string TargetName;
    private readonly string RemapTarget = string.Empty;
    private readonly ConfigPredicate RemapRule = new();
    private readonly ConfigPredicate SkipRule = new();
    private readonly ConfigPredicate FlattenRule = new();

    public ScopedRules(string SectionName, ConfigFileSection Section, string RootPath, IDictionary<string, string> Variables)
    {
        // Global section affects all targets
        TargetName = SectionName;
        TargetName = Utils.UnifySeparators(TargetName);

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
                RemapTarget = Utils.UnifySeparators(Utils.MapVariables(Variables, Line.Value));
            }
            else if (Line.Key.Equals("FlattenIf", StringComparison.OrdinalIgnoreCase))
            {
                FlattenRule.Add(Line.Value, Line.Action);
            }
        }

        SkipRule.Compile(RootPath, Variables);
        RemapRule.Compile(RootPath, Variables);
        FlattenRule.Compile(RootPath, Variables);
    }

    public bool Affects(string Target)
    {
        return Target.StartsWith(TargetName, StringComparison.OrdinalIgnoreCase);
    }

    public RemapResult Remap(string Target, out string Result)
    {
        Result = Target;
        if (SkipRule.Eval(Target)) return RemapResult.Skipped;
        bool ShouldFlatten = FlattenRule.Eval(Target);

        if (RemapRule.Eval(Target))
        {
            Result = ShouldFlatten ? Path.Combine(RemapTarget, Path.GetFileName(Target)) : 
                TargetName == string.Empty ? Path.Combine(RemapTarget, Target) : Target.Replace(TargetName, RemapTarget);
            return RemapResult.Remapped;
        }

        if (ShouldFlatten)
        {
            Result = Path.Combine(TargetName, Path.GetFileName(Target));
            return RemapResult.Remapped;
        }
        return RemapResult.None;
    }
}

public class Config
{
    private readonly List<ScopedRules> Scopes = new();

    public Config(string ConfigPath, string RootPath, ConfigFile BaseConfig, string VariableOverrides)
    {
        ConfigFile Config = File.Exists(ConfigPath) ? new ConfigFile(ConfigPath) : new ConfigFile();

        // Merge base config sections
        foreach (string SectionName in BaseConfig.SectionNames)
        {
            if (BaseConfig.TryGetSection(SectionName, out var BaseSection))
            {
                Config.FindOrAddSection(SectionName).Lines.InsertRange(0, BaseSection.Lines);
            }
        }

        // Override variables
        Config.AppendFromText("Variables", VariableOverrides.Replace("\"", string.Empty));

        var Variables = new Dictionary<string, string>();
        var SectionNames = Config.SectionNames.ToList();
        var VariableSectionName = SectionNames.Find(Name => Name.Equals("Variables", StringComparison.OrdinalIgnoreCase));
        if (VariableSectionName != null && Config.TryGetSection(VariableSectionName, out var Section))
        {
            foreach (ConfigLine Line in Section.Lines)
            {
                Variables[Line.Key] = Line.Value;
            }
            SectionNames.Remove(VariableSectionName);
        }

        // Parse into scoped rules
        foreach (string SectionName in SectionNames)
        {
            if (Config.TryGetSection(SectionName, out Section))
            {
                Scopes.Add(new ScopedRules(
                    SectionName.Equals("Global", StringComparison.OrdinalIgnoreCase) ? "" : SectionName,
                    Section, RootPath, Variables));
            }
        }
    }

    public bool Remap(string Target, out string Result)
    {
        Result = Target;

        foreach (var Scope in Scopes.Where(Rules => Rules.Affects(Target)))
        {
            switch (Scope.Remap(Target, out var Temp))
            {
                case RemapResult.Skipped:
                    return false;
                case RemapResult.Remapped:
                    Result = Temp;
                    break;
                case RemapResult.None:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
        return true;
    }
}
