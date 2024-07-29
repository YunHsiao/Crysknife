// SPDX-FileCopyrightText: 2024 Yun Hsiao Wu <yunhsiaow@gmail.com>
// SPDX-License-Identifier: MIT

namespace Crysknife;

[Flags]
public enum JobType
{
    None = 0x0,
    Generate = 0x1,
    Clear = 0x2,
    Apply = 0x4,
}

[Flags]
public enum JobOptions
{
    None = 0x0,
    Link = 0x1,
    Force = 0x2,
    DryRun = 0x4,
    Verbose = 0x8,
    TreatPatchAsFile = 0x10,
}

public class Injector
{
    private readonly struct InjectionRegexGroup
    {
        private readonly InjectionRegex Injection;
        private readonly List<InjectionRegex> Residuals = new();

        public InjectionRegexGroup(string Parent, IEnumerable<string> Residuals)
        {
            Injection = new InjectionRegex(Parent);

            foreach (var Residual in Residuals)
            {
                this.Residuals.Add(new InjectionRegex(Residual));
            }
        }

        private static string Unpatch(string Content, IEnumerable<InjectionRegex> Regexes)
        {
            return Regexes.Aggregate(Content, (Current, Regex) => Regex.Unpatch(Current));
        }

        public string ClearResiduals(string Content)
        {
            return Unpatch(Content, Residuals);
        }

        public string Unpatch(string Content)
        {
            return Injection.Unpatch(Content);
        }
    }

    private readonly struct SourcePatchInfo
    {
        public readonly string PluginName;
        public readonly string CommentTag;
        public readonly string Directory;
        public readonly InjectionRegexGroup PatchRegex;

        public static string GetDirectory(string PluginName)
        {
            return Path.Combine(EngineRoot, "Plugins", PluginName, "SourcePatch");
        }

        public SourcePatchInfo(ConfigSystem Config)
        {
            PluginName = Config.PluginName;
            Directory = GetDirectory(Config.PluginName);
            CommentTag = Config.GetCommentTag() ?? PluginName;
            PatchRegex = new InjectionRegexGroup(CommentTag, Config.GetChildrenTags());
        }
    }

    private readonly struct ParsedPath
    {
        private readonly string PathTrunc;
        public readonly List<string> Extensions = new();

        public ParsedPath(string InputPath)
        {
            string Extension = Path.GetExtension(InputPath);
            while (Extension != string.Empty)
            {
                Extensions.Insert(0, Extension);
                InputPath = InputPath[..^Extension.Length];
                Extension = Path.GetExtension(InputPath);
            }
            PathTrunc = InputPath;
        }

        public string Trim(int NumExtensions)
        {
            string Result = PathTrunc;
            for (int Index = 0; Index < Extensions.Count - NumExtensions; ++Index)
            {
                Result += Extensions[Index];
            }
            return Result;
        }
    }

    private readonly struct EngineVersion
    {
        private readonly int Major;
        private readonly int Minor;

        public static EngineVersion Create(string Value)
        {
            string[] Versions = Value.Split('_');
            return new EngineVersion(int.Parse(Versions[0]), int.Parse(Versions[1]));
        }

        public static readonly EngineVersion Empty = new(0, 0);

        private EngineVersion(int InMajor, int InMinor)
        {
            Major = InMajor;
            Minor = InMinor;
        }

        public override string ToString()
        {
            return $"{Major}_{Minor}";
        }

        public int Distance(EngineVersion Other)
        {
            return Math.Abs(Major - Other.Major) * 100 + Math.Abs(Minor - Other.Minor);
        }
    }

    private readonly struct PatchDescription
    {
        private readonly List<EngineVersion> Versions = new();
        public PatchDescription() {}

        public void Add(ParsedPath PatchPath)
        {
            foreach (var Extension in PatchPath.Extensions.Where(Extension => Extension.StartsWith(".v")))
            {
                Versions.Add(EngineVersion.Create(Extension[2..]));
            }
        }
        public string Match(EngineVersion TargetVersion)
        {
            int NearestDistance = int.MaxValue;
            EngineVersion NearestVersion = Versions.Aggregate(EngineVersion.Empty, (Acc, Version) =>
            {
                int Distance = Version.Distance(TargetVersion);
                if (Distance >= NearestDistance) return Acc;
                NearestDistance = Distance;
                return Version;
            });
            return MakeExtension(NearestVersion);
        }
        public static string MakeExtension(EngineVersion Version)
        {
            return $".v{Version.ToString()}.patch";
        }
    }

    [Flags]
    private enum ConfirmResult
    {
        NotDecided = 0x0,
        Yes = 0x1,
        No = 0x2,
        ForAll = 0x4,
    }
    private static ConfirmResult PromptToConfirm(string Message)
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

    private readonly struct DMPContext
    {
        private readonly DiffMatchPatch.diff_match_patch GenerationContext;
        private readonly DiffMatchPatch.diff_match_patch ApplyContext;

        public DMPContext(short ContextLength, float ContentTolerance, int LineTolerance)
        {
            GenerationContext = new DiffMatchPatch.diff_match_patch { Patch_Margin = ContextLength };
            ApplyContext = new DiffMatchPatch.diff_match_patch { Match_Threshold = ContentTolerance, Match_Distance = LineTolerance };
        }

        public string Apply(string Content, List<DiffMatchPatch.Patch> Patches, out bool[] IsSuccess)
        {
            object[] Result = ApplyContext.patch_apply(Patches, Content);
            IsSuccess = (bool[])Result[1];
            return (string)Result[0];
        }

        public string Apply(string Content, string PatchPath, out bool[] IsSuccess)
        {
            return Apply(Content, ApplyContext.patch_fromText(File.ReadAllText(PatchPath)), out IsSuccess);
        }

        public List<DiffMatchPatch.Diff> GenerateDiffs(string Source, string Target)
        {
            var Diffs = GenerationContext.diff_main(Source, Target);
            if (Diffs.Count > 2)
            {
                GenerationContext.diff_cleanupSemantic(Diffs);
                GenerationContext.diff_cleanupEfficiency(Diffs);
            }
            return Diffs;
        }

        public List<DiffMatchPatch.Patch> GeneratePatches(string Source, List<DiffMatchPatch.Diff> Diffs)
        {
            return GenerationContext.patch_make(Source, Diffs);
        }

        public string Generate(List<DiffMatchPatch.Patch> Patches)
        {
            return GenerationContext.patch_toText(Patches);
        }

        public string GetHtml(List<DiffMatchPatch.Diff> Diffs)
        {
            return GenerationContext.diff_prettyHtml(Diffs);
        }
    }

    private void ProcessPatch(JobType Job, string PatchPath, string TargetPath, InjectionRegexGroup PatchRegex)
    {
        string TargetContent = PatchRegex.ClearResiduals(File.ReadAllText(TargetPath));
        string ClearedTarget = PatchRegex.Unpatch(TargetContent);
        List<DiffMatchPatch.Patch>? Patches = null;

        if (Job.HasFlag(JobType.Generate))
        {
            var Diffs = PatchTool.GenerateDiffs(ClearedTarget, TargetContent);
            Patches = PatchTool.GeneratePatches(ClearedTarget, Diffs);

            if (Patches.Count == 0)
            {
                if (!AutoClearConfirm.HasFlag(ConfirmResult.ForAll))
                {
                    AutoClearConfirm = PromptToConfirm($"Couldn't find any patch from '{TargetPath}', remove?");
                }
                if (AutoClearConfirm.HasFlag(ConfirmResult.Yes))
                {
                    UnregisterSourcePatch(TargetPath);
                    return;
                }
            }

            string Patch = PatchTool.Generate(Patches);
            if (!File.Exists(PatchPath) || File.ReadAllText(PatchPath) != Patch)
            {
                File.WriteAllText(PatchPath + ".html", PatchTool.GetHtml(Diffs));
                File.WriteAllText(PatchPath, Patch);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("Patch updated: " + TargetPath);
            }
        }

        if (Job.HasFlag(JobType.Clear) && ClearedTarget.Length != TargetContent.Length)
        {
            File.WriteAllText(TargetPath, ClearedTarget);
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Patch removed from: " + TargetPath);
            TargetContent = ClearedTarget;
        }

        if (Job.HasFlag(JobType.Apply))
        {
            string Patched = Patches != null ? PatchTool.Apply(ClearedTarget, Patches, out var IsSuccess)
                : PatchTool.Apply(ClearedTarget, PatchPath, out IsSuccess);
            if (Patched == TargetContent) return;

            if (TargetContent.Length != ClearedTarget.Length)
            {
                // Apply op is potentially dangerous: Confirm before overriding any new contents.
                if (!OverrideConfirm.HasFlag(ConfirmResult.ForAll))
                {
                    OverrideConfirm = PromptToConfirm($"Override already patched file {TargetPath}?");
                }
                if (OverrideConfirm.HasFlag(ConfirmResult.No)) return;
            }

            Utils.FileAccessGuard(() => File.WriteAllText(TargetPath, Patched), TargetPath);

            int SuccessCount = IsSuccess.Count(V => V);
            if (SuccessCount == IsSuccess.Length)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("Patched: " + TargetPath);
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine("Error: Patch failed ({0}/{1}): Please merge the relevant changes manually from {2} to {3}",
                    SuccessCount, IsSuccess.Length, PatchPath + ".html", TargetPath);
            }
        }
    }

    private void ProcessFile(JobType Job, string SrcPath, string DstPath)
    {
        bool Exists = File.Exists(DstPath);
        bool IsSymLink = Exists && new FileInfo(DstPath).Attributes.HasFlag(FileAttributes.ReparsePoint);
        bool UpToDate = Exists && !IsSymLink && File.ReadAllText(SrcPath) == File.ReadAllText(DstPath);

        if (Job.HasFlag(JobType.Generate) && !IsSymLink && !UpToDate)
        {
            if (!Exists)
            {
                if (!AutoClearConfirm.HasFlag(ConfirmResult.ForAll))
                {
                    AutoClearConfirm = PromptToConfirm($"Couldn't find target file '{DstPath}', remove the source patch?");
                }
                if (AutoClearConfirm.HasFlag(ConfirmResult.Yes))
                {
                    File.Delete(SrcPath);
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("Source patch removed: {0}", SrcPath);
                }
            }
            else if (Utils.FileAccessGuard(() => File.Copy(DstPath, SrcPath, true), SrcPath))
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("Copied back: {0} <- {1}", SrcPath, DstPath);
                UpToDate = true;
            }
        }

        if (Job.HasFlag(JobType.Clear) && Exists)
        {
            File.Delete(DstPath);
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("{0} removed: {1}", IsSymLink ? "Link" : "File", DstPath);
            Exists = IsSymLink = UpToDate = false;
        }

        if (Job.HasFlag(JobType.Apply))
        {
            bool ShouldBeSymLink = Options.HasFlag(JobOptions.Link);
            if (UpToDate || IsSymLink && ShouldBeSymLink) return;

            if (Exists)
            {
                // Apply op is potentially dangerous: Confirm before overriding any new contents.
                if (!OverrideConfirm.HasFlag(ConfirmResult.ForAll))
                {
                    OverrideConfirm = PromptToConfirm($"Override existing file {DstPath}?");
                }
                if (OverrideConfirm.HasFlag(ConfirmResult.No)) return;
            }
            else
            {
                Utils.EnsureParentDirectoryExists(DstPath);
            }

            if (ShouldBeSymLink ?
                Utils.FileAccessGuard(() => File.CreateSymbolicLink(DstPath, SrcPath), DstPath) :
                Utils.FileAccessGuard(() => File.Copy(SrcPath, DstPath, true), DstPath))
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("{0}: {1} -> {2}", ShouldBeSymLink ? "Linked" : "Copied", SrcPath, DstPath);
            }
        }
    }

    private void CreatePatchTool()
    {
        PatchTool = new DMPContext(PatchContextLength, MatchContentTolerance, MatchLineTolerance);
    }

    private readonly string SourceDirectory;
    private readonly JobOptions Options;

    private string PrivateInclusiveFilter = string.Empty;
    private string PrivateExclusiveFilter = "NonExist";
    private short PrivatePatchContextLength = 50;
    private float PrivateMatchContentTolerance = 0.5f;
    private int PrivateMatchLineTolerance = int.MaxValue; // Line number may vary significantly

    private readonly ConfigSystem DefaultConfig;
    private readonly EngineVersion CurrentEngineVersion;
    private ConfirmResult OverrideConfirm;
    private ConfirmResult AutoClearConfirm;
    private DMPContext PatchTool;

    ////////////////////////////////////////////////////////////////////////////////////////////////////

    public Injector(string PluginName, string VariableOverrides, JobOptions Options)
    {
        SourceDirectory = Path.Combine(EngineRoot, "Source");
        this.Options = Options;

        CurrentEngineVersion = EngineVersion.Create(Utils.GetCurrentEngineVersion(SourceDirectory));
        OverrideConfirm = Options.HasFlag(JobOptions.Force) ? ConfirmResult.Yes | ConfirmResult.ForAll : ConfirmResult.NotDecided;
        CreatePatchTool();

        string PatchDirectory = SourcePatchInfo.GetDirectory(PluginName);
        string BuiltinVariables = $"CRYSKNIFE_SOURCE_DIRECTORY={SourceDirectory},CRYSKNIFE_PATCH_DIRECTORY={PatchDirectory}";
        if (Options.HasFlag(JobOptions.DryRun)) BuiltinVariables = string.Join(',', BuiltinVariables, "CRYSKNIFE_DRY_RUN=1");
        VariableOverrides = string.Join(',', BuiltinVariables, VariableOverrides);
        DefaultConfig = ConfigSystem.Create(PluginName, VariableOverrides);
    }

    public short PatchContextLength
    {
        get => PrivatePatchContextLength;
        set
        {
            PrivatePatchContextLength = value;
            CreatePatchTool();
        }
    }
    public float MatchContentTolerance
    {
        get => PrivateMatchContentTolerance;
        set
        {
            PrivateMatchContentTolerance = value;
            CreatePatchTool();
        }
    }
    public int MatchLineTolerance
    {
        get => PrivateMatchLineTolerance;
        set
        {
            PrivateMatchLineTolerance = value;
            CreatePatchTool();
        }
    }
    public string InclusiveFilter
    {
        get => PrivateInclusiveFilter;
        set => PrivateInclusiveFilter = Utils.UnifySeparators(value);
    }
    public string ExclusiveFilter
    {
        get => PrivateExclusiveFilter;
        set => PrivateExclusiveFilter = Utils.UnifySeparators(value);
    }

    private void RegisterSourcePatch(SourcePatchInfo SourcePatch, string InputPaths)
    {
        var PatchedPaths = new List<string>();

        foreach (string InputPath in InputPaths.Split())
        {
            if (Path.GetExtension(InputPath) != string.Empty)
            {
                string FilePath = InputPath;
                if (!File.Exists(FilePath)) FilePath = Path.Combine(SourceDirectory, FilePath);
                if (!File.Exists(FilePath)) continue;
                PatchedPaths.Add(FilePath);
            }
            else
            {
                string DirPath = InputPath;
                if (!Directory.Exists(DirPath)) DirPath = Path.Combine(SourceDirectory, DirPath);
                if (!Directory.Exists(DirPath)) continue;
                PatchedPaths.AddRange(Directory.GetFiles(DirPath, "*", new EnumerationOptions
                    { RecurseSubdirectories = true }).Where(Utils.CanBePatched));
            }
        }

        foreach (string PatchedPath in PatchedPaths)
        {
            string RelativePath = Path.GetRelativePath(SourceDirectory, PatchedPath);
            string PatchPath = Path.Combine(SourcePatch.Directory, RelativePath);

            // Register any file contains the project name
            if (!File.Exists(PatchPath) && Path.GetFileName(PatchedPath).Contains(SourcePatch.PluginName))
            {
                goto Register;
            }

            // Or new patched files
            PatchPath += PatchDescription.MakeExtension(CurrentEngineVersion);
            if (!File.Exists(PatchPath) && File.ReadAllText(PatchedPath).Contains($"// {SourcePatch.CommentTag}"))
            {
                goto Register;
            }

            continue;

            Register:
            Directory.GetParent(PatchPath)?.Create();
            File.Create(PatchPath).Close();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("New source patch registered: " + PatchPath);
        }
    }

    public void RegisterSourcePatch(string InputPaths)
    {
        DefaultConfig.Dispatch(Config => RegisterSourcePatch(new SourcePatchInfo(Config), InputPaths), true);
    }

    private void UnregisterSourcePatch(SourcePatchInfo SourcePatch, string InputPaths)
    {
        var PatchedPaths = new List<string>();

        foreach (string InputPath in InputPaths.Split())
        {
            if (Path.GetExtension(InputPath) != string.Empty)
            {
                string FilePath = InputPath;
                if (!File.Exists(FilePath)) FilePath = Path.Combine(SourceDirectory, FilePath);
                if (!File.Exists(FilePath)) continue;
                PatchedPaths.Add(FilePath);
            }
            else
            {
                string DirPath = InputPath;
                if (!Directory.Exists(DirPath)) DirPath = Path.Combine(SourceDirectory, DirPath);
                if (!Directory.Exists(DirPath)) continue;
                PatchedPaths.AddRange(Directory.GetFiles(DirPath, "*", new EnumerationOptions
                    { RecurseSubdirectories = true }).Where(Utils.CanBePatched));
            }
        }

        foreach (string PatchedPath in PatchedPaths)
        {
            string RelativePath = Path.GetRelativePath(SourceDirectory, PatchedPath);
            string PatchPath = Path.Combine(SourcePatch.Directory, RelativePath + PatchDescription.MakeExtension(CurrentEngineVersion));
            if (!File.Exists(PatchPath)) continue;

            ProcessPatch(JobType.Clear, PatchPath, PatchedPath, SourcePatch.PatchRegex);
            File.Delete(PatchPath);
            File.Delete(PatchPath + ".html");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Patch file deleted: " + PatchPath);
        }
    }

    public void UnregisterSourcePatch(string InputPaths)
    {
        DefaultConfig.Dispatch(Config => UnregisterSourcePatch(new SourcePatchInfo(Config), InputPaths), false);
    }

    private static string EngineRoot = string.Empty;
    public static void Init(string RootDirectory)
    {
        EngineRoot = RootDirectory;
        ConfigSystem.Init(RootDirectory);
        ProjectSetup.Init(RootDirectory);
    }

    public void GenerateSetupScripts()
    {
        ProjectSetup.Generate(DefaultConfig.PluginName);
    }

    private void Process(ConfigSystem Config, JobType Job)
    {
        var SourcePatch = new SourcePatchInfo(Config);
        File.WriteAllText(Path.Combine(SourcePatch.Directory, "CrysknifeCache.ini"), Config.ToString());

        bool VerboseLogging = Options.HasFlag(JobOptions.Verbose);
        if (VerboseLogging)
        {
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine($"Processing '{DefaultConfig.PluginName}' Using Config:");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine(Config);
        }

        var Patches = new Dictionary<string, PatchDescription>();
        foreach (string SrcPath in Directory.GetFiles(SourcePatch.Directory, "*", new EnumerationOptions { RecurseSubdirectories = true }))
        {
            if (!SrcPath.Contains(InclusiveFilter) || SrcPath.Contains(ExclusiveFilter)) continue;

            string RelativePath = Path.GetRelativePath(SourcePatch.Directory, SrcPath);
            var ParsedRelativePath = new ParsedPath(RelativePath);
            if (ParsedRelativePath.Extensions.Last() == ".patch") // Patch existing files
            {
                RelativePath = ParsedRelativePath.Trim(2);
                string DstPath = Path.Combine(SourceDirectory, RelativePath);
                if (!File.Exists(DstPath)) continue;

                if (!Patches.ContainsKey(RelativePath)) Patches.Add(RelativePath, new PatchDescription());
                Patches[RelativePath].Add(ParsedRelativePath);
            }
            else if (Config.Remap(RelativePath, out var DstRelativePath, VerboseLogging))
            {
                string OutputPath = Path.Combine(SourceDirectory, DstRelativePath);

                // When dry running, sync with original output path unconditionally
                if (Options.HasFlag(JobOptions.DryRun) && RelativePath != DstRelativePath)
                {
                    Utils.EnsureParentDirectoryExists(OutputPath);
                    string OriginalDstPath = Path.Combine(SourceDirectory, RelativePath);
                    if (File.Exists(OriginalDstPath)) Utils.FileAccessGuard(() => File.Copy(OriginalDstPath, OutputPath, true), OutputPath);
                    else File.Delete(OutputPath);
                }

                ProcessFile(Job, SrcPath, OutputPath);
            }
        }

        foreach (var Pair in Patches)
        {
            string PatchSuffix = Pair.Value.Match(CurrentEngineVersion);
            string RelativePatch = Pair.Key + PatchSuffix;

            if (!Config.Remap(RelativePatch, out var DstRelativePath, VerboseLogging)) continue;

            string PatchPath = Path.Combine(SourcePatch.Directory, RelativePatch);
            string OutputPath = Path.Combine(SourceDirectory, DstRelativePath[..^PatchSuffix.Length]);

            if (Options.HasFlag(JobOptions.TreatPatchAsFile))
            {
                ProcessFile(Job, PatchPath, OutputPath + PatchSuffix);
                continue;
            }

            // The original source file have to exist
            string TargetPath = Path.Combine(SourceDirectory, Pair.Key);
            if (!File.Exists(TargetPath))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Skipped patch: {0} does not exist!", TargetPath);
                continue;
            }

            // When remapping patches, sync from original source if not exist
            if (TargetPath != OutputPath && !File.Exists(OutputPath))
            {
                Utils.EnsureParentDirectoryExists(OutputPath);
                Utils.FileAccessGuard(() => File.Copy(TargetPath, OutputPath), OutputPath);
            }

            // When dry running, sync with original output path unconditionally
            if (Options.HasFlag(JobOptions.DryRun) && TargetPath != OutputPath)
            {
                Utils.FileAccessGuard(() => File.Copy(TargetPath, OutputPath, true), OutputPath);
            }

            ProcessPatch(Job, PatchPath, OutputPath, SourcePatch.PatchRegex);
        }

        Console.ForegroundColor = ConsoleColor.DarkBlue;
        Console.WriteLine("{0} job done: {1} <=> {2}", Job.ToString(), SourcePatch.Directory, SourceDirectory);
    }

    public void Process(JobType Job)
    {
        DefaultConfig.Dispatch(Config => Process(Config, Job), Job != JobType.Clear);
    }
}
