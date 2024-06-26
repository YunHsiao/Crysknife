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
    private readonly struct ParsedPath
    {
        public readonly string PathTrunc;
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

    private void ProcessPatch(JobType Job, string PatchPath, string TargetPath)
    {
        string TargetContent = File.ReadAllText(TargetPath);
        string ClearedTarget = InjectionRE.Unpatch(TargetContent);
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
                    RemovePatchFile(TargetPath);
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

            File.WriteAllText(TargetPath, Patched);

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

        if (Job.HasFlag(JobType.Generate) && Exists && !IsSymLink && !UpToDate)
        {
            if (Utils.FileAccessGuard(() => File.Copy(DstPath, SrcPath, true), SrcPath))
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

    private static ConfigFile BaseConfig = new();

    private readonly string ProjectName;
    private readonly string SrcDirectory;
    private readonly string DstDirectory;
    private readonly JobOptions Options;

    private string PrivateInclusiveFilter = string.Empty;
    private string PrivateExclusiveFilter = "NonExist";
    private short PrivatePatchContextLength = 50;
    private float PrivateMatchContentTolerance = 0.5f;
    private int PrivateMatchLineTolerance = int.MaxValue; // Line number may vary significantly

    private readonly InjectionRegex InjectionRE;
    private readonly EngineVersion CurrentEngineVersion;
    private ConfirmResult OverrideConfirm;
    private ConfirmResult AutoClearConfirm;
    private DMPContext PatchTool;

    ////////////////////////////////////////////////////////////////////////////////////////////////////

    public Injector(string InProjectName, string InSrcDirectory, string InDstDirectory, JobOptions InOptions)
    {
        ProjectName = InProjectName;
        SrcDirectory = InSrcDirectory;
        DstDirectory = InDstDirectory;
        Options = InOptions;

        InjectionRE = new InjectionRegex(ProjectName);
        CurrentEngineVersion = EngineVersion.Create(Utils.GetCurrentEngineVersion(DstDirectory));
        OverrideConfirm = Options.HasFlag(JobOptions.Force) ? ConfirmResult.Yes | ConfirmResult.ForAll : ConfirmResult.NotDecided;
        CreatePatchTool();
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

    public void CreatePatchFile(params string[] InputPaths)
    {
        var PatchedPaths = new List<string>();

        foreach (string InputPath in InputPaths)
        {
            if (Path.GetExtension(InputPath) != string.Empty)
            {
                string FilePath = InputPath;
                if (!File.Exists(FilePath)) FilePath = Path.Combine(DstDirectory, FilePath);
                if (!File.Exists(FilePath)) continue;
                PatchedPaths.Add(FilePath);
            }
            else
            {
                string DirPath = InputPath;
                if (!Directory.Exists(DirPath)) DirPath = Path.Combine(DstDirectory, DirPath);
                if (!Directory.Exists(DirPath)) continue;
                PatchedPaths.AddRange(Directory.GetFiles(DirPath, "*", new EnumerationOptions { RecurseSubdirectories = true })
                    .Where(PatchedPath => Path.GetExtension(PatchedPath) is ".cpp" or ".h"));
            }
        }

        foreach (string PatchedPath in PatchedPaths)
        {
            string RelativePath = Path.GetRelativePath(DstDirectory, PatchedPath);
            string PatchPath = Path.Combine(SrcDirectory, RelativePath + PatchDescription.MakeExtension(CurrentEngineVersion));
            if (File.Exists(PatchPath)) continue;
            if (!File.ReadAllText(PatchedPath).Contains($"// {ProjectName}"))
            {
                continue;
            }

            Directory.GetParent(PatchPath)?.Create();
            File.Create(PatchPath).Close();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Patch file created: " + PatchPath);
        }
    }

    public void RemovePatchFile(params string[] InputPaths)
    {
        var PatchedPaths = new List<string>();

        foreach (string InputPath in InputPaths)
        {
            if (Path.GetExtension(InputPath) != string.Empty)
            {
                string FilePath = InputPath;
                if (!File.Exists(FilePath)) FilePath = Path.Combine(DstDirectory, FilePath);
                if (!File.Exists(FilePath)) continue;
                PatchedPaths.Add(FilePath);
            }
            else
            {
                string DirPath = InputPath;
                if (!Directory.Exists(DirPath)) DirPath = Path.Combine(DstDirectory, DirPath);
                if (!Directory.Exists(DirPath)) continue;
                PatchedPaths.AddRange(Directory.GetFiles(DirPath, "*", new EnumerationOptions { RecurseSubdirectories = true })
                    .Where(PatchedPath => Path.GetExtension(PatchedPath) is ".cpp" or ".h"));
            }
        }

        foreach (string PatchedPath in PatchedPaths)
        {
            string RelativePath = Path.GetRelativePath(DstDirectory, PatchedPath);
            string PatchPath = Path.Combine(SrcDirectory, RelativePath + PatchDescription.MakeExtension(CurrentEngineVersion));
            if (!File.Exists(PatchPath)) continue;

            ProcessPatch(JobType.Clear, PatchPath, PatchedPath);
            File.Delete(PatchPath);
            File.Delete(PatchPath + ".html");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Patch file deleted: " + PatchPath);
        }
    }

    public static void Init(string RootDirectory)
    {
        ConfigFile.Init(RootDirectory);
        string ConfigPath = Path.Combine(RootDirectory, "BaseCrysknife.ini");
        if (File.Exists(ConfigPath)) BaseConfig = new ConfigFile(ConfigPath);
    }

    public void Process(JobType Job, string SrcDirectoryOverride, string VariableOverrides)
    {
        string BuiltinVariables = $"CRYSKNIFE_OUTPUT_DIRECTORY={DstDirectory},CRYSKNIFE_INPUT_DIRECTORY={SrcDirectoryOverride}";

        if (Options.HasFlag(JobOptions.DryRun))
        {
            BuiltinVariables = string.Join(',', BuiltinVariables, "CRYSKNIFE_DRY_RUN=1");
        }

        VariableOverrides = string.Join(',', BuiltinVariables, VariableOverrides);

        var Patches = new Dictionary<string, PatchDescription>();
        var Config = new Config(Path.Combine(SrcDirectoryOverride, "Crysknife.ini"), DstDirectory, BaseConfig, VariableOverrides);
        File.WriteAllText(Path.Combine(SrcDirectoryOverride, "CrysknifeCache.ini"), Config.ToString());

        bool VerboseLogging = Options.HasFlag(JobOptions.Verbose);
        if (VerboseLogging)
        {
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine($"Processing '{SrcDirectoryOverride}' Using Config:");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine(Config);
        }

        foreach (string SrcPath in Directory.GetFiles(SrcDirectoryOverride, "*", new EnumerationOptions { RecurseSubdirectories = true }))
        {
            if (!SrcPath.Contains(InclusiveFilter) || SrcPath.Contains(ExclusiveFilter)) continue;

            string RelativePath = Path.GetRelativePath(SrcDirectoryOverride, SrcPath);
            var ParsedRelativePath = new ParsedPath(RelativePath);
            if (ParsedRelativePath.Extensions.Last() == ".patch") // Patch existing files
            {
                RelativePath = ParsedRelativePath.PathTrunc + ParsedRelativePath.Extensions.First();
                string DstPath = Path.Combine(DstDirectory, RelativePath);
                if (!File.Exists(DstPath)) continue;

                if (!Patches.ContainsKey(RelativePath)) Patches.Add(RelativePath, new PatchDescription());
                Patches[RelativePath].Add(ParsedRelativePath);
            }
            else if (Config.Remap(RelativePath, out var DstRelativePath, VerboseLogging))
            {
                string OutputPath = Path.Combine(DstDirectory, DstRelativePath);

                // When dry running, sync with original output path unconditionally
                if (Options.HasFlag(JobOptions.DryRun) && RelativePath != DstRelativePath)
                {
                    Utils.EnsureParentDirectoryExists(OutputPath);
                    string OriginalDstPath = Path.Combine(DstDirectory, RelativePath);
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

            string PatchPath = Path.Combine(SrcDirectoryOverride, RelativePatch);
            string OutputPath = Path.Combine(DstDirectory, DstRelativePath[..^PatchSuffix.Length]);

            if (Options.HasFlag(JobOptions.TreatPatchAsFile))
            {
                ProcessFile(Job, PatchPath, OutputPath + PatchSuffix);
                continue;
            }

            // The original source file have to exist
            string TargetPath = Path.Combine(DstDirectory, Pair.Key);
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

            ProcessPatch(Job, PatchPath, OutputPath);
        }

        Console.ForegroundColor = ConsoleColor.DarkBlue;
        Console.WriteLine("{0} job done: {1} <=> {2}", Job.ToString(), SrcDirectoryOverride, DstDirectory);
    }

    public void Process(JobType Job, string VariableOverrides = "")
    {
        Process(Job, SrcDirectory, VariableOverrides);
    }
}
