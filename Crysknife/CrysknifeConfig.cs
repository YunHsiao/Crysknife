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
            var Invert = Cond.StartsWith('!');
            return Invert ? !Pred(Cond[1..]) : Pred(Cond);
        };
        return LogicalAnd ? Conditions.All(Wrapper) : Conditions.Any(Wrapper);
    }

    public bool IsValid()
    {
        return Conditions.Count > 0;
    }

    private void RequireConjunction()
    {
        LogicalAnd = true;
    }

    public void AddRange(string[] Input)
    {
        var ConjunctionIndex = Array.FindIndex(Input, Name => Name.Equals("Conjunction", StringComparison.OrdinalIgnoreCase));
        if (ConjunctionIndex >= 0) RequireConjunction();
        Conditions.AddRange(Input.Where((_, Index) => Index != ConjunctionIndex));
    }

    public override string ToString()
    {
        var LogicOp = LogicalAnd ? "Conjunction|" : "";
        return IsValid() ? $"{Keyword}:{LogicOp}{string.Join('|', Conditions)}" : string.Empty;
    }
}

internal class ConfigPredicates
{
    private string[] Descriptions = Array.Empty<string>();
    private ConfigPredicate[] Predicates = Array.Empty<ConfigPredicate>();
    private bool CompileTimeCondition;
    private bool LogicalAnd; // All predicates are disjunction by default

    private bool Eval(bool Result, bool NewResult)
    {
        return LogicalAnd ? Result && NewResult : Result || NewResult;
    }

    private bool Eval(bool Result, ConfigPredicate Predicate, Func<string, bool> Pred)
    {
        if (Result ^ LogicalAnd) return Result; // Early out if possible
        return Eval(Result, Predicate.IsValid() ? Predicate.Eval(Pred) : LogicalAnd);
    }

    public static bool Eval(string Desc, string Target)
    {
        return new ConfigPredicates().Compile(Desc).Eval(Target);
    }

    public bool Eval(string Target)
    {
        return Predicates.Where(Predicate => !Predicate.CompileTime).Aggregate(CompileTimeCondition, (Current, Predicate) =>
            Eval(Current, Predicate, Predicate.EvalFuncFactory(Target)));
    }

    public void SetValue(string Desc)
    {
        Descriptions = Desc.Split(',', Utils.SplitOptions);
    }

    public ConfigPredicates Compile(string? Desc = null)
    {
        if (Desc != null) SetValue(Desc);

        Predicates = new[]
        {
            new ConfigPredicate("NameMatches", Target => Cond => Utils.ContainsRegex(Path.GetFileName(Target), Cond)),

            new ConfigPredicate("TargetExists", Cond =>
            {
                var TargetPath = Path.Combine(Utils.GetSourceDirectory(), Cond);
                return File.Exists(TargetPath) || Directory.Exists(TargetPath);
            }),
            new ConfigPredicate("IsTruthy", Utils.IsTruthyValue),
            new ConfigPredicate("NewerThan", Cond =>
            {
                var TargetVersion = EngineVersion.Create(Cond);
                return Utils.CurrentEngineVersion.NewerThan(TargetVersion);
            }),
        };

        foreach (var Rule in Descriptions)
        {
            if (Rule.StartsWith("Always", StringComparison.OrdinalIgnoreCase))
            {
                CompileTimeCondition = Eval(CompileTimeCondition, true);
            }
            else if (Rule.StartsWith("Never", StringComparison.OrdinalIgnoreCase))
            {
                CompileTimeCondition = Eval(CompileTimeCondition, false);
            }
            else if (Rule.StartsWith("Conjunction", StringComparison.OrdinalIgnoreCase))
            {
                CompileTimeCondition = LogicalAnd = true;
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

                Predicate.AddRange(Rule[(Predicate.Keyword.Length + 1)..].Split('|', Utils.SplitOptions));
            }
        }

        foreach (var Predicate in Predicates)
        {
            if (Predicate.CompileTime)
            {
                CompileTimeCondition = Eval(CompileTimeCondition, Predicate, Predicate.EvalFunc);
            }
        }

        return this;
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
    private readonly string Keyword;

    public ConfigRule(string Keyword)
    {
        this.Keyword = Keyword;
    }

    public bool Matches(string Key)
    {
        return Key.Equals(Keyword, StringComparison.OrdinalIgnoreCase) ||
               Key.Equals($"Base{Keyword}", StringComparison.OrdinalIgnoreCase);
    }

    public void SetValue(string Key, string Desc)
    {
        var IsBaseDomain = Key.StartsWith("Base", StringComparison.OrdinalIgnoreCase);
        (IsBaseDomain ? BasePredicates : UserPredicates).SetValue(Desc);
    }

    public void Compile()
    {
        BasePredicates.Compile();
        UserPredicates.Compile();
    }

    public bool Eval(string Target)
    {
        return BasePredicates.Eval(Target) || UserPredicates.Eval(Target);
    }

    public override string ToString()
    {
        var BaseDump = BasePredicates.ToString();
        if (BaseDump.Length > 0) BaseDump = $"Base{Keyword}={BaseDump}";

        var UserDump = UserPredicates.ToString();
        if (UserDump.Length > 0) UserDump = $"{Keyword}={UserDump}";

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

    private string RemapTarget = string.Empty;
    private readonly ConfigRule[] Rules;

    public ConfigSection(IDictionary<string, string> Section, string SectionName)
    {
        Rules = new[]
        {
            new ConfigRule("SkipIf"),
            new ConfigRule("FlattenIf"),
            new ConfigRule("RemapIf"),
        };

        TargetNames = GetTargetNames(SectionName).ToArray();

        foreach (var Pair in Section)
        {
            if (Pair.Key.Equals("RemapTarget", StringComparison.OrdinalIgnoreCase))
            {
                RemapTarget = Utils.UnifySeparators(Pair.Value);
                if (Path.IsPathFullyQualified(RemapTarget)) RemapTarget = Path.GetFullPath(RemapTarget);
            }
            else
            {
                var Rule = Array.Find(Rules, Rule => Rule.Matches(Pair.Key));
                if (Rule == null)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"Config: Unsupported rule '{Pair.Key}'");
                    continue;
                }
                Rule.SetValue(Pair.Key, Pair.Value);
            }
        }

        foreach (var Rule in Rules)
        {
            Rule.Compile();
        }
    }

    public void MapLocalVariables(IReadOnlyDictionary<string, string> Variables)
    {
        // Only remap targets can have local variable references for now
        RemapTarget = Utils.MapVariables(Variables, RemapTarget, Utils.MapFlag.AllowLocal);
    }

    public RemapResult Remap(string Target, out string Result, bool VerboseLogging)
    {
        Result = Target;
        var ControllingDomain = Array.Find(TargetNames, TargetName => Target.StartsWith(TargetName, StringComparison.OrdinalIgnoreCase));
        if (ControllingDomain == null) return RemapResult.DoNotAffect;

        var ShouldSkip = Rules[0].Eval(Target);
        if (VerboseLogging && ShouldSkip)
        {
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine($"Config: Skipped '{Target}' due to [{GetSectionName()}] skipping conditions");
        }

        if (ShouldSkip) return RemapResult.Skipped;

        var ShouldFlatten = Rules[1].Eval(Target);
        if (ShouldFlatten && VerboseLogging)
        {
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine($"Config: Flattened '{Target}' due to [{GetSectionName()}] flatten conditions");
        }

        var ShouldRemap = Rules[2].Eval(Target);
        if (ShouldRemap && VerboseLogging)
        {
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine($"Config: Remapped '{Target}' due to [{GetSectionName()}] remap conditions");
        }

        if (ShouldRemap)
        {
            Result = Path.Combine(RemapTarget, ShouldFlatten ? Path.GetFileName(Target) : Target);
            // Convert back into relative path to engine source directory to be consistent
            if (Path.IsPathFullyQualified(Result)) Result = Path.GetRelativePath(Utils.GetSourceDirectory(), Result);
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
        var Predicates = string.Join('\n', Rules.Select(Predicate => Predicate.ToString())
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

// Parent directory config applies to all subdirectories
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
    private readonly Dictionary<string, ConfigSectionHierarchy> Children = new();

    public void InheritancePatch(ConfigFileSection? Parent = null)
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

        foreach (var Folder in Target.Split(Path.DirectorySeparatorChar, Utils.SplitOptions))
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
        foreach (var Index in Enumerable.Range(0, Sections.Count))
        {
            foreach (var Target in Sections[Index].GetTargetNames())
            {
                var Node = GetNearestNode(Root, Target)!;
                Debug.Assert(Node != null && (Node.LinkedIndex < 0 || Node.LinkedIndex == Index));
                Node.LinkedIndex = Index;
            }
        }
    }

    public static ConfigSectionHierarchy Build(ConfigFile Config, IDictionary<string, string> SectionNameMap)
    {
        var Root = new ConfigSectionHierarchy();

        foreach (var Pair in SectionNameMap)
        {
            if (!Config.TryGetSection(Pair.Key, out var Section)) continue;
            var SectionNode = new ConfigFileSectionNode(Section);

            foreach (var TargetName in ConfigSection.GetTargetNames(Pair.Value))
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

internal enum RepositoryType
{
    Release,
    Stock,
    Internal
}

internal class ConfigSystem
{
    private readonly Dictionary<string, string> InnerVariables = new();
    private readonly List<ConfigSection> Sections = new();
    private readonly ConfigSectionHierarchy Hierarchy;
    private readonly Dictionary<string, string> DependencyVariables = new();
    private readonly Dictionary<string, ConfigSystem> Dependencies = new();
    private readonly Dictionary<string, string> Children = new();
    private readonly CommentTagFormat Format;

    public readonly InjectionRegexGroup PatchRegex;
    public readonly CommentTagPacker TagPacker;
    public readonly string PluginName;
    public readonly string PatchDirectory;
    public readonly bool OutputCrlf;

    private static ConfigFile BaseConfig = new();
    private static string LocalSuffix = string.Empty;
    private static readonly string LocalConfigTemplate = @"
        [Variables]
        CRYSKNIFE_LOCAL_CONFIG={0}
    ".Replace("    ", string.Empty);

    public static void Init()
    {
        var RootPath = Utils.GetPluginDirectory("Crysknife");
        var ConfigPath = Path.Combine(RootPath, "BaseCrysknife.ini");
        Debug.Assert(File.Exists(ConfigPath), "Base config file missing, check your repo status");
        BaseConfig = new ConfigFile(ConfigPath);

        foreach (var LocalConfigPath in Directory.GetFiles(RootPath, "BaseCrysknife*Local.ini"))
        {
            var LocalConfigFile = new ConfigFile(LocalConfigPath);
            if (!Utils.IsTruthyValue(new ConfigSystem(LocalConfigFile).GetVariable("CRYSKNIFE_LOCAL_CONFIG_PREDICATE"))) continue;

            var CurrentSuffix = Utils.GetLocalConfigSuffix(LocalConfigPath);
            if (LocalSuffix.Length > 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Aborting due to multiple sets of active local configs: {0} & {1}", LocalSuffix, CurrentSuffix);
                Utils.Abort();
            }
            BaseConfig.Merge(LocalConfigFile);
            LocalSuffix = CurrentSuffix;
        }

        if (LocalSuffix.Length > 0)
        {
            var OutputPath = Utils.GetPluginDirectory("CrysknifeCache.ini");
            var TargetContent = string.Format(LocalConfigTemplate, LocalSuffix);
            if (!File.Exists(OutputPath) || File.ReadAllText(OutputPath) != TargetContent)
            {
                Utils.FileAccessGuard(() => File.WriteAllText(OutputPath, TargetContent), OutputPath);
            }
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine("Internal environment detected, operating under '{0}' local config", LocalSuffix);
        }
        ConfigFile.Init(RootPath);
    }

    private static ConfigFile CreateConfigFile(string PluginName, string VariableOverrides)
    {
        var ConfigPath = GetConfigPath(PluginName);
        var Config = File.Exists(ConfigPath) ? new ConfigFile(ConfigPath).Merge(BaseConfig, false) : BaseConfig;

        var LocalConfigPath = GetConfigPath(PluginName, ConfigType.Local);
        if (File.Exists(LocalConfigPath)) Config.Merge(new ConfigFile(LocalConfigPath));

        var FinalOverrides = string.Join(',',
            $"#CRYSKNIFE_ENGINE_ROOT={Utils.GetEngineRoot()}",
            $"CRYSKNIFE_UNITY_ENGINE={(Utils.IsUnity() ? "1" : "0")}",
            $"CRYSKNIFE_SOURCE_DIRECTORY={Path.Combine("${CRYSKNIFE_ENGINE_ROOT}", Utils.GetEngineRelativePath(Utils.GetSourceDirectory()))}",
            $"CRYSKNIFE_PLUGIN_DIRECTORY={Path.Combine("${CRYSKNIFE_ENGINE_ROOT}", Utils.GetEngineRelativePath(Utils.GetPluginDirectory(PluginName)))}",
            VariableOverrides.Replace("\"", string.Empty) // Need to strip quotes if specified from CLI
        );
        Config.AppendFromText("Variables", FinalOverrides);
        return Config;
    }

    private enum ConfigType
    {
        Main,
        Local,
        Cache,
    }
    private static string GetConfigPath(string PluginName, ConfigType Type = ConfigType.Main)
    {
        var Directory = Utils.GetPatchDirectory(PluginName);
        return Type switch
        {
            ConfigType.Local => Path.Combine(Directory, $"Crysknife{LocalSuffix}Local.ini"),
            ConfigType.Cache => Path.Combine(Directory, $"Crysknife{LocalSuffix}Cache.ini"),
            _ => Path.Combine(Directory, "Crysknife.ini"),
        };
    }

    private void RegisterChildren(string Name, string Tag)
    {
        Children.TryAdd(Name, Tag);
        foreach (var Pair in Dependencies)
        {
            Pair.Value.RegisterChildren(Name, Tag);
        }
    }

    private List<string> BuildVariableList(ConfigFile Config)
    {
        var SectionNames = Config.SectionNames.ToList();

        // Gather variables
        var VariableSecIndex = SectionNames.FindIndex(Name => Name.Equals("Variables", StringComparison.OrdinalIgnoreCase));
        if (VariableSecIndex >= 0 && Config.TryGetSection(SectionNames[VariableSecIndex], out var Section))
        {
            Section.ParseLines(InnerVariables, '|');
            SectionNames.RemoveAt(VariableSecIndex);
        }
        // Predicate evaluation
        foreach (var Pair in InnerVariables)
        {
            if (Utils.GetVariablePredicate(Pair.Value, out var Predicate))
            {
                InnerVariables[Pair.Key] = ConfigPredicates.Eval(Predicate, Utils.GetEngineRoot()) ? "1" : "0";
            }
        }
        // Path compression
        foreach (var Pair in InnerVariables)
        {
            InnerVariables[Pair.Key] = Utils.MapVariables(Variables, Pair.Value, Utils.MapFlag.IgnoreFallbacks);
        }

        return SectionNames;
    }

    private ConfigSystem(ConfigFile Config)
    {
        PluginName = PatchDirectory = string.Empty;
        Hierarchy = new ConfigSectionHierarchy();
        Format = new CommentTagFormat(PluginName);
        PatchRegex = new InjectionRegexGroup(new InjectionRegex(Format));
        TagPacker = new CommentTagPacker(PluginName, Format);
        BuildVariableList(Config);
    }

    private ConfigSystem(string PluginName, string VariableOverrides)
    {
        this.PluginName = PluginName;
        PatchDirectory = Utils.GetPatchDirectory(PluginName);

        var Config = CreateConfigFile(PluginName, VariableOverrides);
        var SectionNames = BuildVariableList(Config);

        // Gather dependencies
        var DependencySecIndex = SectionNames.FindIndex(Name => Name.Equals("Dependencies", StringComparison.OrdinalIgnoreCase));
        if (DependencySecIndex >= 0 && Config.TryGetSection(SectionNames[DependencySecIndex], out var Section))
        {
            Section.ParseLines(DependencyVariables, ',', Value => Utils.MapVariables(Variables, Value));
            SectionNames.RemoveAt(DependencySecIndex);
        }

        // Map section names
        var SectionNameMap = new Dictionary<string, string>();
        SectionNames.ForEach(Name => SectionNameMap[Name] = Utils.MapVariables(Variables, Name));

        // Build inheritance chain
        Hierarchy = ConfigSectionHierarchy.Build(Config, SectionNameMap);
        Hierarchy.InheritancePatch();

        // Parse into scoped rules
        var RulesRegistry = new Dictionary<string, string>();
        foreach (var Pair in SectionNameMap)
        {
            if (!Config.TryGetSection(Pair.Key, out Section)) continue;
            Section.ParseLines(RulesRegistry, ',', Value => Utils.MapVariables(Variables, Value));
            Sections.Add(new ConfigSection(RulesRegistry, Pair.Value));
        }
        ConfigSectionHierarchy.Link(Hierarchy, Sections);

        const string Prefix = "CRYSKNIFE_COMMENT_TAG";
        Format = new CommentTagFormat(Path.GetFileName(PluginName));
        Format.Tag = GetVariable(Prefix, Format.Tag);

        Format.PrefixRegex = GetVariable(Prefix + "_PREFIX_RE", Format.PrefixRegex);
        Format.SuffixRegex = GetVariable(Prefix + "_SUFFIX_RE", Format.SuffixRegex);
        Format.BeginRegex = GetVariable(Prefix + "_BEGIN_RE", Format.BeginRegex);
        Format.EndRegex = GetVariable(Prefix + "_END_RE", Format.EndRegex);
        Format.Anastrophe = Utils.IsTruthyValue(GetVariable(Prefix + "_ANASTROPHE", Format.Anastrophe.ToString()));
        Format.TagNewFiles = Utils.IsTruthyValue(GetVariable(Prefix + "_NEW_FILES", Format.TagNewFiles.ToString()));

        Format.PrefixCtor = GetVariable(Prefix + "_PREFIX_CTOR", Format.PrefixRegex);
        Format.SuffixCtor = GetVariable(Prefix + "_SUFFIX_CTOR", Format.SuffixRegex);
        Format.BeginCtor = GetVariable(Prefix + "_BEGIN_CTOR", Format.BeginRegex);
        Format.EndCtor = GetVariable(Prefix + "_END_CTOR", Format.EndRegex);

        PatchRegex = new InjectionRegexGroup(new InjectionRegex(Format));
        TagPacker = new CommentTagPacker(Path.GetFileName(PluginName), Format);
        OutputCrlf = Utils.IsTruthyValue(GetVariable("CRYSKNIFE_OUTPUT_CRLF"));
    }

    // Always create parent dependencies first
    private static ConfigSystem Create(string PluginName, string VariableOverrides, string VariableOverridesFromChild)
    {
        if (!Directory.Exists(Utils.GetPluginDirectory(PluginName)))
        {
            // Installed plugins are located inside Marketplace folder, try there first
            string InstalledPluginPath = Path.Combine("Marketplace", PluginName);
            if (Directory.Exists(Utils.GetPluginDirectory(InstalledPluginPath)))
            {
                PluginName = InstalledPluginPath;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Specified plugin not found: {0}", PluginName);
                Utils.Abort();
            }
        }
        var Config = new ConfigSystem(PluginName, string.Join(',', VariableOverrides, VariableOverridesFromChild));

        foreach (var Pair in Config.DependencyVariables)
        {
            if (Config.Dependencies.ContainsKey(PluginName)) continue;

            var Parent = Create(Pair.Key, VariableOverrides, Pair.Value);
            var ParentConfigCache = new ConfigFile(GetConfigPath(Pair.Key, ConfigType.Cache));
            if (ParentConfigCache.TryGetSection("Children", out var CachedChildren))
            {
                foreach (var Line in CachedChildren.Lines)
                {
                    if (!Parent.Children.ContainsKey(Line.Key))
                    {
                        Parent.RegisterChildren(Line.Key, Line.Value);
                    }
                }
            }
            if (!Parent.Children.ContainsKey(PluginName))
            {
                Parent.RegisterChildren(PluginName, Config.Format.Tag);
            }

            Config.Dependencies.TryAdd(Pair.Key, Parent);
        }
        return Config;
    }

    private string DumpBuiltinSections()
    {
        var BuiltinSections = new List<string>();

        if (Variables.Count != 0) BuiltinSections.Add("[Variables]\n" + Variables.Aggregate("", (Current, Pair) =>
        {
            if (Pair.Key.StartsWith('#')) return Current;
            var Expr = $"{Pair.Key}={Pair.Value}\n";
            return Pair.Key.StartsWith("Crysknife", StringComparison.OrdinalIgnoreCase) ? Expr + Current : Current + Expr;
        }));
        if (DependencyVariables.Count != 0) BuiltinSections.Add("[Dependencies]\n" + DependencyVariables.Aggregate("", (Current, Pair) => Current + $"{Pair.Key}={Pair.Value}\n"));
        if (Children.Count != 0) BuiltinSections.Add("[Children]\n" + Children.Aggregate("", (Current, Pair) => Current + $"{Pair.Key}={Pair.Value}\n"));

        return string.Join('\n', BuiltinSections);
    }

    private string GetVariable(string Name, string Default = "")
    {
        Variables.TryGetValue(Name, out var Result);
        return Result ?? Default;
    }

    public static ConfigSystem Create(string PluginName, string VariableOverrides)
    {
        var Result = Create(PluginName, VariableOverrides, "");
        Result.Dispatch(Config =>
        {
            // Fill in children plugins
            Config.PatchRegex.AddResiduals(Config.Children.Select(Pair => new InjectionRegex(Config.Format, Pair.Value)));
            // Save config cache
            File.WriteAllText(GetConfigPath(Config.PluginName, ConfigType.Cache), Config.ToString());
            // Map local variables
            Config.Sections.ForEach(Section => Section.MapLocalVariables(Config.Variables));
        }, true);
        return Result;
    }

    public RepositoryType RepoType => LocalSuffix.Length > 0 ? RepositoryType.Internal :
        Utils.IsTruthyValue(GetVariable("CRYSKNIFE_BASE_REPO")) ? RepositoryType.Release : RepositoryType.Stock;

    public IReadOnlyDictionary<string, string> Variables => InnerVariables;

    public void Dispatch(Action<ConfigSystem> Action, bool ParentFirst)
    {
        if (ParentFirst)
        {
            foreach (var Pair in Dependencies)
            {
                Pair.Value.Dispatch(Action, ParentFirst);
            }
            Action(this);
        }
        else
        {
            Action(this);
            foreach (var Pair in Dependencies.Reverse())
            {
                Pair.Value.Dispatch(Action, ParentFirst);
            }
        }
    }

    public bool Remap(string Target, out string Result, bool VerboseLogging, string SuffixForRemapping = "")
    {
        Result = Target;
        var NearestSectionIndex = ConfigSectionHierarchy.GetNearestSection(Hierarchy, Target);
        if (NearestSectionIndex == null) return true; // As-is if no rule is found

        switch (Sections[NearestSectionIndex.Value].Remap(Target + SuffixForRemapping, out var Temp, VerboseLogging))
        {
            case RemapResult.AsIs:
                return true;
            case RemapResult.Skipped:
                return false;
            case RemapResult.Remapped:
                Result = Temp[..^SuffixForRemapping.Length];
                return true;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    public override string ToString()
    {
        return '\n' + DumpBuiltinSections() + '\n' + string.Join("\n\n", Sections) + '\n';
    }
}
