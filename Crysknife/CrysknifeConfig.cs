// SPDX-FileCopyrightText: 2024 Yun Hsiao Wu <yunhsiaow@gmail.com>
// SPDX-License-Identifier: MIT

namespace Crysknife;

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

    private readonly List<string> BaseDesc = new();
    private readonly List<string> FullDesc = new();
    private bool CompileTimePredicate;
    private bool LogicalAnd; // By default all predicates are disjunction
    private PredicateInstance FilenamePredicates = new();

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

    public void Compile(string RootPath, Dictionary<string, bool> Switches)
    {
        var ExistencePredicates = new PredicateInstance();
        var SwitchOnPredicates = new PredicateInstance();
        var SwitchOffPredicates = new PredicateInstance();

        foreach (var Rule in BaseDesc.Concat(FullDesc).SelectMany(Desc => Desc.Split(',', StringSplitOptions.TrimEntries)))
        {
            if (Rule.StartsWith("Exist:", StringComparison.OrdinalIgnoreCase))
            {
                ExistencePredicates.Conditions.AddRange(Rule[6..].Split('|', StringSplitOptions.TrimEntries));
            }
            else if (Rule.StartsWith("IsOn:", StringComparison.OrdinalIgnoreCase))
            {
                SwitchOnPredicates.Conditions.AddRange(Rule[5..].Split('|', StringSplitOptions.TrimEntries));
            }
            else if (Rule.StartsWith("IsOff:", StringComparison.OrdinalIgnoreCase))
            {
                SwitchOffPredicates.Conditions.AddRange(Rule[6..].Split('|', StringSplitOptions.TrimEntries));
            }
            else if (Rule.StartsWith("Filename:", StringComparison.OrdinalIgnoreCase))
            {
                FilenamePredicates.Conditions.AddRange(Rule[9..].Split('|', StringSplitOptions.TrimEntries));
            }
            else if (Rule.StartsWith("Always", StringComparison.OrdinalIgnoreCase))
            {
                CompileTimePredicate = Eval(CompileTimePredicate, true);
            }
            else if (Rule.StartsWith("Conjunction:", StringComparison.OrdinalIgnoreCase))
            {
                var Scopes = Rule[12..].Split('|', StringSplitOptions.TrimEntries);
                bool AllTrue = ContainsString(Scopes, "All");
                bool PredicatesTrue = ContainsString(Scopes, "Predicates");
                if (AllTrue || ContainsString(Scopes, "Root")) CompileTimePredicate = LogicalAnd = true;
                else if (AllTrue || PredicatesTrue || ContainsString(Scopes, "Exist")) ExistencePredicates.LogicalAnd = true;
                else if (AllTrue || PredicatesTrue || ContainsString(Scopes, "Filename")) FilenamePredicates.LogicalAnd = true;
            }
        }

        CompileTimePredicate = Eval(CompileTimePredicate, SwitchOnPredicates.Eval(Cond =>
        {
            if (Switches.TryGetValue(Cond, out var Value))
            {
                return Value;
            }
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"IsOn condition invalid: '{Cond}' not found");
            return false;
        }));
        CompileTimePredicate = Eval(CompileTimePredicate, SwitchOffPredicates.Eval(Cond =>
        {
            if (Switches.TryGetValue(Cond, out var Value))
            {
                return !Value;
            }
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"IsOff condition invalid: '{Cond}' not found");
            return true;
        }));
        CompileTimePredicate = Eval(CompileTimePredicate, ExistencePredicates.Eval(Cond =>
        {
            string TargetPath = Path.Combine(RootPath, Cond);
            return File.Exists(TargetPath) || Directory.Exists(TargetPath);
        }));
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
    private readonly bool Flat;
    private readonly string RemapTarget = string.Empty;
    private readonly ConfigPredicate RemapRule = new();
    private readonly ConfigPredicate SkipRule = new();

    public ScopedRules(string SectionName, ConfigFileSection Section, string RootPath, Dictionary<string, bool> Switches)
    {
        // Global section affects all targets
        TargetName = SectionName;
        TargetName = RegexUtils.UnifySeparators(TargetName);

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
                RemapTarget = RegexUtils.UnifySeparators(Line.Value);
            }
            else if (Line.Key.Equals("Flat", StringComparison.OrdinalIgnoreCase))
            {
                Flat = RegexUtils.IsTruthyValue(Line.Value);
            }
        }

        SkipRule.Compile(RootPath, Switches);
        RemapRule.Compile(RootPath, Switches);
    }

    public bool Affects(string Target)
    {
        return Target.StartsWith(TargetName, StringComparison.OrdinalIgnoreCase);
    }

    public bool Remap(string Target, out string Result)
    {
        Result = Target;
        if (SkipRule.Eval(Target)) return false;

        if (RemapRule.Eval(Target))
        {
            Result = Flat ? Path.Combine(RemapTarget, Path.GetFileName(Target)) : 
                TargetName == string.Empty ? Path.Combine(RemapTarget, Target) : Target.Replace(TargetName, RemapTarget);
        }
        return true;
    }
}

public class Config
{
    private static ConfigFile BaseConfig = new();
    private readonly List<ScopedRules> Scopes = new();

    public static void Init(string RootDirectory)
    {
        ConfigFile.Init(RootDirectory);
        string ConfigPath = Path.Combine(RootDirectory, "BaseCrysknife.ini");
        if (File.Exists(ConfigPath)) BaseConfig = new ConfigFile(ConfigPath);
    }

    public Config(string ConfigPath, string RootPath)
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

        var Switches = new Dictionary<string, bool>();
        List<string> SectionNames = Config.SectionNames.ToList();
        string? SwitchSectionName = SectionNames.Find(Name => Name.Equals("Switches", StringComparison.OrdinalIgnoreCase));
        if (SwitchSectionName != null && Config.TryGetSection(SwitchSectionName, out var Section))
        {
            foreach (ConfigLine Line in Section.Lines)
            {
                Switches.Add(Line.Key, RegexUtils.IsTruthyValue(Line.Value));
            }
            SectionNames.Remove(SwitchSectionName);
        }

        // Parse into scoped rules
        foreach (string SectionName in SectionNames)
        {
            if (Config.TryGetSection(SectionName, out Section))
            {
                Scopes.Add(new ScopedRules(
                    SectionName.Equals("Global", StringComparison.OrdinalIgnoreCase) ? "" : SectionName,
                    Section, RootPath, Switches));
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
