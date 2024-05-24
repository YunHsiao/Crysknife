// SPDX-FileCopyrightText: 2024 Yun Hsiao Wu <yunhsiaow@gmail.com>
// SPDX-License-Identifier: MIT

using System.Diagnostics;

namespace Crysknife;

internal class ConfigPredicate
{
    public readonly bool CompileTime;

    public readonly string Keyword;
    public readonly Func<string, bool> EvalFunc = _ => true;
    public readonly Func<string, Func<string, bool>> EvalFuncFactory = _ => _ => true;

    private readonly List<string> Conditions = new();
    private bool LogicalAnd;

    public ConfigPredicate(string Keyword, Func<string, Func<string, bool>> EvalFuncFactory)
    {
        CompileTime = false;
        this.Keyword = Keyword;
        this.EvalFuncFactory = EvalFuncFactory;
    }

    public ConfigPredicate(string Keyword, Func<string, bool> EvalFunc)
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

    public bool IsValid()
    {
        return Conditions.Count > 0;
    }

    public void RequireConjunction()
    {
        LogicalAnd = true;
    }

    public void AddRange(IEnumerable<string> Input)
    {
        Conditions.AddRange(Input);
    }

    public override string ToString()
    {
        string LogicOp = LogicalAnd ? "Conjunction|" : "";
        return IsValid() ? $"{Keyword}:{LogicOp}{string.Join('|', Conditions)}" : string.Empty;
    }
}

internal class ConfigPredicates
{
    private readonly List<string> Descriptions = new();

    private ConfigPredicate[] Predicates = Array.Empty<ConfigPredicate>();
    private bool CompileTimeCondition;
    private bool LogicalAnd; // By default all predicates are disjunction

    private bool Eval(bool Result, bool NewResult)
    {
        return LogicalAnd ? Result && NewResult : Result || NewResult;
    }

    private bool Eval(bool Result, ConfigPredicate Predicate, Func<string, bool> Pred)
    {
        if (Result ^ LogicalAnd) return Result; // Early out if possible
        return Eval(Result, Predicate.IsValid() ? Predicate.Eval(Pred) : LogicalAnd);
    }

    public bool Eval(string Target)
    {
        return Predicates.Where(Predicate => !Predicate.CompileTime).Aggregate(CompileTimeCondition, (Current, Predicate) =>
            Eval(Current, Predicate, Predicate.EvalFuncFactory(Target)));
    }

    public void Add(string Desc, ConfigLineAction Action)
    {
        switch (Action)
        {
            case ConfigLineAction.Set:
                Descriptions.Clear();
                goto case ConfigLineAction.Add;
            case ConfigLineAction.Add:
                Descriptions.Add(Desc);
                break;
            case ConfigLineAction.RemoveKey:
                Descriptions.Clear();
                break;
            case ConfigLineAction.RemoveKeyValue:
                Descriptions.Remove(Desc);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(Action), Action, null);
        }
    }

    public void Compile(string RootPath, IDictionary<string, string> Variables)
    {
        Predicates = new[]
        {
            new ConfigPredicate("NameMatches", Target => Cond =>
                Path.GetFileName(Target).Contains(Cond, StringComparison.OrdinalIgnoreCase)),

            new ConfigPredicate("TargetExists", Cond =>
            {
                string TargetPath = Path.Combine(RootPath, Cond);
                return File.Exists(TargetPath) || Directory.Exists(TargetPath);
            }),
            new ConfigPredicate("IsTruthy", Utils.IsTruthyValue),
        };

        foreach (var Rule in Descriptions.SelectMany(Desc => Desc.Split(',', Utils.SplitOptions)))
        {
            if (Rule.StartsWith("Always", StringComparison.OrdinalIgnoreCase))
            {
                CompileTimeCondition = Eval(CompileTimeCondition, true);
            }
            else if (Rule.StartsWith("Never", StringComparison.OrdinalIgnoreCase))
            {
                CompileTimeCondition = Eval(CompileTimeCondition, false);
            }
            else if (Utils.GetContentIfStartsWith(Rule, "Conjunctions:", out var Content))
            {
                var Scopes = Content.Split('|', Utils.SplitOptions).ToList();
                bool AllTrue = Utils.FindAndRemoveString(Scopes, "All");
                bool PredicatesTrue = Utils.FindAndRemoveString(Scopes, "Predicates");
                if (AllTrue || Utils.FindAndRemoveString(Scopes, "Root")) CompileTimeCondition = LogicalAnd = true;

                foreach (var Predicate in Predicates)
                {
                    if (AllTrue || PredicatesTrue || Utils.FindAndRemoveString(Scopes, Predicate.Keyword))
                    {
                        Predicate.RequireConjunction();
                    }
                }

                if (Scopes.Count <= 0) continue;
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Config: Invalid conjunction scope: {0}", string.Join(' ', Scopes));
            }
            else
            {
                var Predicate = Array.Find(Predicates,Predicate => Rule.StartsWith(Predicate.Keyword + ":", StringComparison.OrdinalIgnoreCase));
                if (Predicate == null)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("Config: Invalid predicate name: {0}", Rule);
                    continue;
                }
                Predicate.AddRange(Rule[(Predicate.Keyword.Length + 1)..]
                    .Split('|', Utils.SplitOptions)
                    .Select(Value => Utils.MapVariables(Variables, Value)));
            }
        }

        foreach (var Predicate in Predicates)
        {
            if (Predicate.CompileTime)
            {
                CompileTimeCondition = Eval(CompileTimeCondition, Predicate, Predicate.EvalFunc);
            }
        }
    }

    public override string ToString()
    {
        return (LogicalAnd ? "Conjunction," : "") + string.Join(',', Predicates.Select(Predicate => Predicate.ToString())
            .Where(Predicate => Predicate.Length > 0));
    }
}

internal class ConfigRule
{
    private readonly ConfigPredicates BasePredicates = new();
    private readonly ConfigPredicates UserPredicates = new();
    public readonly string Keyword;

    public ConfigRule(string Keyword)
    {
        this.Keyword = Keyword;
    }

    public void Add(string Desc, ConfigLineAction Action)
    {
        if (Utils.GetContentIfStartsWith(Desc, "BaseDomain", out var Content))
        {
            BasePredicates.Add(Utils.GetContentIfStartsWith(Content, ","), Action);
        }
        else
        {
            UserPredicates.Add(Desc, Action);
        }
    }

    public void Compile(string RootPath, IDictionary<string, string> Variables)
    {
        BasePredicates.Compile(RootPath, Variables);
        UserPredicates.Compile(RootPath, Variables);
    }

    public bool Eval(string Target)
    {
        return BasePredicates.Eval(Target) || UserPredicates.Eval(Target);
    }

    public override string ToString()
    {
        string BaseDump = BasePredicates.ToString();
        if (BaseDump.Length > 0) BaseDump = $"{Keyword}=BaseDomain,{BaseDump}";

        string UserDump = UserPredicates.ToString();
        if (UserDump.Length > 0)
        {
            string Prefix = BaseDump.Length > 0 ? "+" : "";
            UserDump = $"{Prefix}{Keyword}={UserDump}";
        }

        return string.Join('\n', BaseDump, UserDump).Trim();
    }
}

internal enum RemapResult
{
    DoNotAffect,
    AsIs,
    Skipped,
    Remapped,
}

internal class ConfigSection
{
    private readonly string[] TargetNames;

    private readonly string RemapTarget = string.Empty;
    private readonly ConfigRule[] Rules;

    public ConfigSection(ConfigFileSection Section, string SectionName, string RootPath, IDictionary<string, string> Variables)
    {
        TargetNames = GetTargetNames(SectionName).ToArray();

        Rules = new[]
        {
            new ConfigRule("SkipIf"),
            new ConfigRule("FlattenIf"),
            new ConfigRule("RemapIf"),
        };

        foreach (ConfigLine Line in Section.Lines)
        {
            if (Line.Key.Equals("RemapTarget", StringComparison.OrdinalIgnoreCase))
            {
                RemapTarget = Utils.UnifySeparators(Utils.MapVariables(Variables, Line.Value));
                if (Path.IsPathFullyQualified(RemapTarget)) RemapTarget = Path.GetFullPath(RemapTarget);
            }
            else
            {
                var Rule = Array.Find(Rules, Rule => Line.Key.Equals(Rule.Keyword, StringComparison.OrdinalIgnoreCase));
                if (Rule == null)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"Config: Unsupported rule '{Line.Key}'");
                    continue;
                }
                Rule.Add(Line.Value, Line.Action);
            }
        }

        foreach (ConfigRule Rule in Rules)
        {
            Rule.Compile(RootPath, Variables);
        }
    }

    public RemapResult Remap(string Target, out string Result, bool VerboseLogging)
    {
        Result = Target;
        var ControllingDomain = Array.Find(TargetNames, TargetName => Target.StartsWith(TargetName, StringComparison.OrdinalIgnoreCase));
        if (ControllingDomain == null) return RemapResult.DoNotAffect;

        bool ShouldSkip = Rules[0].Eval(Target);
        if (VerboseLogging && ShouldSkip)
        {
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine($"Config: Skipped '{Target}' due to [{GetSectionName()}] skipping conditions");
        }

        if (ShouldSkip) return RemapResult.Skipped;

        bool ShouldFlatten = Rules[1].Eval(Target);
        if (ShouldFlatten && VerboseLogging)
        {
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine($"Config: Flattened '{Target}' due to [{GetSectionName()}] flatten conditions");
        }

        bool ShouldRemap = Rules[2].Eval(Target);
        if (ShouldRemap && VerboseLogging)
        {
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine($"Config: Remapped '{Target}' due to [{GetSectionName()}] remap conditions");
        }

        if (ShouldRemap)
        {
            Result = ShouldFlatten ? Path.Combine(RemapTarget, Path.GetFileName(Target)) :
                ControllingDomain == string.Empty ? Path.Combine(RemapTarget, Target) : Target.Replace(ControllingDomain, RemapTarget);
            return RemapResult.Remapped;
        }

        if (ShouldFlatten)
        {
            Result = Path.Combine(ControllingDomain, Path.GetFileName(Target));
            return RemapResult.Remapped;
        }
        return RemapResult.AsIs;
    }

    private string GetSectionName()
    {
        return string.Join('|', TargetNames.Select(Name => Name.Length > 0 ? Name : "Global"));
    }

    public override string ToString()
    {
        string Predicates = string.Join('\n', Rules.Select(Predicate => Predicate.ToString())
            .Where(Predicate => Predicate.Length > 0));
        return $"[{GetSectionName()}]\n{Predicates}\nRemapTarget={RemapTarget}";
    }

    public IEnumerable<string> GetTargetNames()
    {
        return TargetNames;
    }

    public static IEnumerable<string> GetTargetNames(string SectionName)
    {
        return SectionName.Split('|', Utils.SplitOptions).Select(Name => // Global section affects all targets
            Name.Equals("Global", StringComparison.OrdinalIgnoreCase) ? "" : Utils.UnifySeparators(Name));
    }
}

internal class ConfigSectionHierarchy
{
    private class ConfigFileSectionNode
    {
        public readonly ConfigFileSection Source;
        public readonly List<ConfigFileSection> AppliedParents = new();

        public int LinkedIndex = -1;

        public ConfigFileSectionNode(ConfigFileSection Source)
        {
            this.Source = Source;
        }
    }

    private ConfigFileSectionNode? Section;
    private readonly Dictionary<string, ConfigSectionHierarchy> Children = new ();

    public void InheritancePatch(ConfigFileSection? Parent)
    {
        if (Section != null)
        {
            if (Parent != null && !Section.AppliedParents.Contains(Parent))
            {
                Section.Source.Lines.InsertRange(0, Parent.Lines);
                Section.AppliedParents.Add(Parent);
            }
            Parent = Section.Source;
        }

        foreach (var Pair in Children)
        {
            Pair.Value.InheritancePatch(Parent);
        }
    }

    private static ConfigFileSectionNode? GetNearestNode(ConfigSectionHierarchy Root, string Target)
    {
        var Node = Root;
        var Section = Root.Section;

        foreach (string Folder in Target.Split(Path.DirectorySeparatorChar, Utils.SplitOptions))
        {
            if (!Node.Children.TryGetValue(Folder, out var Child)) break;
            if (Child.Section != null) Section = Child.Section;
            Node = Child;
        }

        return Section;
    }

    public static int? GetNearestSection(ConfigSectionHierarchy Root, string Target)
    {
        return GetNearestNode(Root, Target)?.LinkedIndex;
    }

    public static void Link(ConfigSectionHierarchy Root, List<ConfigSection> Sections)
    {
        for (int Index = 0; Index < Sections.Count; ++Index)
        {
            foreach (string Target in Sections[Index].GetTargetNames())
            {
                var Node = GetNearestNode(Root, Target)!;
                Debug.Assert(Node != null && (Node.LinkedIndex < 0 || Node.LinkedIndex == Index));
                Node.LinkedIndex = Index;
            }
        }
    }

    public static ConfigSectionHierarchy Build(ConfigFile Config, IEnumerable<string> SectionNames)
    {
        var Root = new ConfigSectionHierarchy();

        foreach (string SectionName in SectionNames)
        {
            if (!Config.TryGetSection(SectionName, out var Section)) continue;
            var SectionNode = new ConfigFileSectionNode(Section);

            foreach (string TargetName in ConfigSection.GetTargetNames(SectionName))
            {
                if (TargetName.Length == 0)
                {
                    Root.Section = SectionNode;
                    continue;
                }

                TargetName.Split(Path.DirectorySeparatorChar, Utils.SplitOptions)
                    .Aggregate(Root, (Current, Folder) =>
                    {
                        if (Current.Children.TryGetValue(Folder, out var Child)) return Child;
                        Child = new ConfigSectionHierarchy();
                        Current.Children.Add(Folder, Child);
                        return Child;
                    })
                    .Section = SectionNode;
            }
        }
        return Root;
    }
}

public class Config
{
    private readonly List<ConfigSection> Sections = new();
    private readonly ConfigSectionHierarchy Hierarchy;
    private readonly Dictionary<string, string> Variables = new();

    public Config(string ConfigPath, string RootPath, ConfigFile BaseConfig, string VariableOverrides)
    {
        ConfigFile Config = File.Exists(ConfigPath) ? new ConfigFile(ConfigPath, BaseConfig) : BaseConfig;

        // Override variables
        Config.AppendFromText("Variables", VariableOverrides.Replace("\"", string.Empty));

        // Gather variables
        var SectionNames = Config.SectionNames.ToList();
        var VariableSecIndex = SectionNames.FindIndex(Name => Name.Equals("Variables", StringComparison.OrdinalIgnoreCase));
        if (VariableSecIndex >= 0 && Config.TryGetSection(SectionNames[VariableSecIndex], out var Section))
        {
            foreach (ConfigLine Line in Section.Lines)
            {
                Variables[Line.Key] = Line.Value;
            }
            SectionNames.RemoveAt(VariableSecIndex);
        }

        // Build inheritance chain
        Hierarchy = ConfigSectionHierarchy.Build(Config, SectionNames);
        Hierarchy.InheritancePatch(null);

        // Parse into scoped rules
        foreach (string SectionName in SectionNames)
        {
            if (Config.TryGetSection(SectionName, out Section))
            {
                Sections.Add(new ConfigSection(Section, SectionName, RootPath, Variables));
            }
        }
        ConfigSectionHierarchy.Link(Hierarchy, Sections);
    }

    public bool Remap(string Target, out string Result, bool VerboseLogging = false)
    {
        Result = Target;
        var NearestSectionIndex = ConfigSectionHierarchy.GetNearestSection(Hierarchy, Target);
        if (NearestSectionIndex == null) return true; // As-is if no rule is found

        switch (Sections[NearestSectionIndex.Value].Remap(Target, out var Temp, VerboseLogging))
        {
            case RemapResult.AsIs:
                return true;
            case RemapResult.Skipped:
                return false;
            case RemapResult.Remapped:
                Result = Temp;
                return true;
            case RemapResult.DoNotAffect:
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private string DumpVariables()
    {
        return "[Variables]\n" + Variables.Aggregate("", (Current, Pair) =>
        {
            string Expr = $"{Pair.Key}={Pair.Value}\n";
            return Pair.Key.StartsWith("Crysknife", StringComparison.OrdinalIgnoreCase) ? Expr + Current : Current + Expr;
        });
    }

    public override string ToString()
    {
        return DumpVariables() + '\n' + string.Join("\n\n", Sections);
    }
}
