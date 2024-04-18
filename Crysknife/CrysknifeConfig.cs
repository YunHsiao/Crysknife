// SPDX-FileCopyrightText: 2024 Yun Hsiao Wu <yunhsiaow@gmail.com>
// SPDX-License-Identifier: MIT

namespace Crysknife;

internal struct ConfigPredicateInstance
{
    public readonly bool CompileTime = false;

    public readonly string Keyword;
    public readonly Func<string, bool> EvalFunc = _ => true;
    public readonly Func<string, Func<string, bool>> EvalFuncFactory = _ => _ => true;

    public readonly List<string> Conditions = new();
    public bool LogicalAnd = false;

    public ConfigPredicateInstance(string Keyword, Func<string, Func<string, bool>> EvalFuncFactory)
    {
        CompileTime = false;
        this.Keyword = Keyword;
        this.EvalFuncFactory = EvalFuncFactory;
    }

    public ConfigPredicateInstance(string Keyword, Func<string, bool> EvalFunc)
    {
        CompileTime = true;
        this.Keyword = Keyword;
        this.EvalFunc = EvalFunc;
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

internal class ConfigPredicate
{
    private ConfigPredicateInstance[] Predicates = Array.Empty<ConfigPredicateInstance>();

    private readonly List<string> BaseDesc = new();
    private readonly List<string> FullDesc = new();

    private bool CompileTimePredicate;
    private bool LogicalAnd; // By default all predicates are disjunction

    private bool Eval(bool Result, bool NewResult)
    {
        return LogicalAnd ? Result && NewResult : Result || NewResult;
    }

    private bool Eval(bool Result, ConfigPredicateInstance Instance, Func<string, bool> Pred)
    {
        if (Result ^ LogicalAnd) return Result; // Early out if possible
        return Eval(Result, Instance.Conditions.Count > 0 ? Instance.Eval(Pred) : LogicalAnd);
    }

    private static bool FindAndRemoveString(List<string> Values, string Target)
    {
        return Values.RemoveAll(Value => Value.Equals(Target, StringComparison.OrdinalIgnoreCase)) > 0;
    }

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
        const StringSplitOptions SplitOptions = StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries;

        foreach (var Rule in BaseDesc.Concat(FullDesc).SelectMany(Desc => Desc.Split(',', SplitOptions)))
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
                var Scopes = Rule[13..].Split('|', SplitOptions).ToList();
                bool AllTrue = FindAndRemoveString(Scopes, "All");
                bool PredicatesTrue = FindAndRemoveString(Scopes, "Predicates");
                if (AllTrue || FindAndRemoveString(Scopes, "Root")) CompileTimePredicate = LogicalAnd = true;

                for (int Index = 0; Index < Predicates.Length; ++Index)
                {
                    if (AllTrue || PredicatesTrue || FindAndRemoveString(Scopes, Predicates[Index].Keyword))
                    {
                        Predicates[Index].LogicalAnd = true;
                    }
                }

                if (Scopes.Count <= 0) continue;
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Invalid conjunction scope:{0}", Scopes.Aggregate("", (Acc, Cur) => $"{Acc} {Cur}"));
            }
            else
            {
                int Index = Array.FindIndex(Predicates, Instance => Rule.StartsWith(Instance.Keyword + ":", StringComparison.OrdinalIgnoreCase));
                if (Index >= 0) Predicates[Index].Conditions.AddRange(Rule[(Predicates[Index].Keyword.Length + 1)..]
                    .Split('|', SplitOptions)
                    .Select(Value => Utils.MapVariables(Variables, Value)));
            }
        }
    }

    public void Compile(string RootPath, IDictionary<string, string> Variables)
    {
        Predicates = new []
        {
            new ConfigPredicateInstance("NameMatches", Target =>
                Cond => Path.GetFileName(Target).Contains(Cond, StringComparison.OrdinalIgnoreCase)),

            new ConfigPredicateInstance("TargetExists", Cond =>
            {
                string TargetPath = Path.Combine(RootPath, Cond);
                return File.Exists(TargetPath) || Directory.Exists(TargetPath);
            }),
            new ConfigPredicateInstance("IsTruthy", Utils.IsTruthyValue),
        };

        ParsePredicateInstances(Variables);

        foreach (var Instance in Predicates)
        {
            if (Instance.CompileTime)
            {
                CompileTimePredicate = Eval(CompileTimePredicate, Instance, Instance.EvalFunc);
            }
        }
    }

    public bool Eval(string Target)
    {
        return Predicates.Where(Instance => !Instance.CompileTime).Aggregate(CompileTimePredicate, (Current, Instance) => 
            Eval(Current, Instance, Instance.EvalFuncFactory(Target)));
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
            if (Line.Key.Equals("RemapTarget", StringComparison.OrdinalIgnoreCase))
            {
                RemapTarget = Utils.UnifySeparators(Utils.MapVariables(Variables, Line.Value));
            }
            else if (Line.Key.Equals("RemapIf", StringComparison.OrdinalIgnoreCase))
            {
                RemapRule.Add(Line.Value, Line.Action);
            }
            else if (Line.Key.Equals("SkipIf", StringComparison.OrdinalIgnoreCase))
            {
                SkipRule.Add(Line.Value, Line.Action);
            }
            else if (Line.Key.Equals("FlattenIf", StringComparison.OrdinalIgnoreCase))
            {
                FlattenRule.Add(Line.Value, Line.Action);
            }
        }

        RemapRule.Compile(RootPath, Variables);
        SkipRule.Compile(RootPath, Variables);
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
