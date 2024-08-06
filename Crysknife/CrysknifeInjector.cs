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
    ClearAllHistory = 0x20,
    KeepAllHistory = 0x40,
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

        public SourcePatchInfo(ConfigSystem Config)
        {
            PluginName = Config.PluginName;
            Directory = Utils.GetPatchDirectory(Config.PluginName);
            CommentTag = Config.GetCommentTag();
            PatchRegex = new InjectionRegexGroup(CommentTag, Config.GetChildrenTags());
        }
    }

    private void ProcessPatch(JobType Job, string PatchPath, string TargetPath, SourcePatchInfo SourcePatch)
    {
        string TargetContent = SourcePatch.PatchRegex.ClearResiduals(File.ReadAllText(TargetPath));
        string ClearedTarget = SourcePatch.PatchRegex.Unpatch(TargetContent);
        PatcherInstance.CommentTag = SourcePatch.CommentTag;
        PatcherInstance.CurrentPatch = PatchPath;
        IPatchBundle? Patches = null;

        if (Job.HasFlag(JobType.Generate))
        {
            Patches = Options.HasFlag(JobOptions.ClearAllHistory) ? PatcherInstance.Generate(ClearedTarget, TargetContent)
                : PatcherInstance.Generate(ClearedTarget, TargetContent, Options.HasFlag(JobOptions.KeepAllHistory));

            if (!Patches.IsValid())
            {
                if (!AutoClearConfirm.HasFlag(Utils.ConfirmResult.ForAll))
                {
                    AutoClearConfirm = Utils.PromptToConfirm($"Couldn't find any patch from '{TargetPath}', remove?");
                }
                if (AutoClearConfirm.HasFlag(Utils.ConfirmResult.Yes))
                {
                    UnregisterSourcePatch(TargetPath);
                    return;
                }
            }

            if (PatcherInstance.Save(Patches))
            {
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
            Patches ??= PatcherInstance.Load();

            if (Patches.IsValid())
            {
                string DumpOutput = Path.Combine(Utils.GetPluginDirectory(SourcePatch.PluginName), "Intermediate", "Crysknife", Path.GetRelativePath(Utils.GetSourceDirectory(), TargetPath));
                bool Success = PatcherInstance.Apply(Patches, ClearedTarget, DumpOutput, out var Patched);
                if (Success && !Patched.Equals(TargetContent, StringComparison.Ordinal))
                {
                    if (TargetContent.Length != ClearedTarget.Length)
                    {
                        // Apply op is potentially dangerous: Confirm before overriding any new contents.
                        if (!OverrideConfirm.HasFlag(Utils.ConfirmResult.ForAll))
                        {
                            OverrideConfirm = Utils.PromptToConfirm($"New patches detected for already patched file '{TargetPath}', override?");
                        }
                        if (OverrideConfirm.HasFlag(Utils.ConfirmResult.No)) return;
                    }

                    Utils.FileAccessGuard(() => File.WriteAllText(TargetPath, Patched), TargetPath);
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("Patched: " + TargetPath);
                }
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
                if (!AutoClearConfirm.HasFlag(Utils.ConfirmResult.ForAll))
                {
                    AutoClearConfirm = Utils.PromptToConfirm($"Couldn't find target file '{DstPath}', remove the source patch?");
                }
                if (AutoClearConfirm.HasFlag(Utils.ConfirmResult.Yes))
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
                if (!OverrideConfirm.HasFlag(Utils.ConfirmResult.ForAll))
                {
                    OverrideConfirm = Utils.PromptToConfirm($"Override existing file {DstPath}?");
                }
                if (OverrideConfirm.HasFlag(Utils.ConfirmResult.No)) return;
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

    private void RegisterSourcePatch(SourcePatchInfo SourcePatch, string InputPaths)
    {
        var PatchedPaths = new List<string>();

        foreach (string InputPath in InputPaths.Split())
        {
            if (Path.GetExtension(InputPath) != string.Empty)
            {
                string FilePath = InputPath;
                if (!File.Exists(FilePath)) FilePath = Path.Combine(Utils.GetSourceDirectory(), FilePath);
                if (!File.Exists(FilePath)) continue;
                PatchedPaths.Add(FilePath);
            }
            else
            {
                string DirPath = InputPath;
                if (!Directory.Exists(DirPath)) DirPath = Path.Combine(Utils.GetSourceDirectory(), DirPath);
                if (!Directory.Exists(DirPath)) continue;
                PatchedPaths.AddRange(Directory.GetFiles(DirPath, "*", new EnumerationOptions
                    { RecurseSubdirectories = true }).Where(Utils.CanBePatched));
            }
        }

        foreach (string PatchedPath in PatchedPaths)
        {
            string RelativePath = Path.GetRelativePath(Utils.GetSourceDirectory(), PatchedPath);
            string PatchPath = Path.Combine(SourcePatch.Directory, RelativePath);

            // Register any file contains the project name
            if (!File.Exists(PatchPath) && Path.GetFileName(PatchedPath).Contains(SourcePatch.PluginName))
            {
                goto Register;
            }

            // Or new patched files
            PatchPath += PatcherInstance.DefaultExtension;
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

    private void UnregisterSourcePatch(SourcePatchInfo SourcePatch, string InputPaths)
    {
        var PatchedPaths = new List<string>();

        foreach (string InputPath in InputPaths.Split())
        {
            if (Path.GetExtension(InputPath) != string.Empty)
            {
                string FilePath = InputPath;
                if (!File.Exists(FilePath)) FilePath = Path.Combine(Utils.GetSourceDirectory(), FilePath);
                if (!File.Exists(FilePath)) continue;
                PatchedPaths.Add(FilePath);
            }
            else
            {
                string DirPath = InputPath;
                if (!Directory.Exists(DirPath)) DirPath = Path.Combine(Utils.GetSourceDirectory(), DirPath);
                if (!Directory.Exists(DirPath)) continue;
                PatchedPaths.AddRange(Directory.GetFiles(DirPath, "*", new EnumerationOptions
                    { RecurseSubdirectories = true }).Where(Utils.CanBePatched));
            }
        }

        foreach (string PatchedPath in PatchedPaths)
        {
            string RelativePath = Path.GetRelativePath(Utils.GetSourceDirectory(), PatchedPath);
            string PatchPath = Path.Combine(SourcePatch.Directory, RelativePath + PatcherInstance.DefaultExtension);
            if (!File.Exists(PatchPath)) continue;

            ProcessPatch(JobType.Clear, PatchPath, PatchedPath, SourcePatch);
            File.Delete(PatchPath);
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Patch file deleted: " + PatchPath);
        }
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

        var Patches = new HashSet<string>();
        foreach (string SrcPath in Directory.GetFiles(SourcePatch.Directory, "*", new EnumerationOptions { RecurseSubdirectories = true }))
        {
            if (!SrcPath.Contains(InclusiveFilter) || SrcPath.Contains(ExclusiveFilter)) continue;

            string RelativePath = Path.GetRelativePath(SourcePatch.Directory, SrcPath);
            if (RelativePath.EndsWith(".patch", StringComparison.OrdinalIgnoreCase)) // Patch existing files
            {
                Patches.Add(Patcher.GetSourcePath(RelativePath));
            }
            else if (Config.Remap(RelativePath, out var DstRelativePath, VerboseLogging))
            {
                string OutputPath = Path.Combine(Utils.GetSourceDirectory(), DstRelativePath);

                // When dry running, sync with original output path unconditionally
                if (Options.HasFlag(JobOptions.DryRun) && RelativePath != DstRelativePath)
                {
                    Utils.EnsureParentDirectoryExists(OutputPath);
                    string OriginalDstPath = Path.Combine(Utils.GetSourceDirectory(), RelativePath);
                    if (File.Exists(OriginalDstPath)) Utils.FileAccessGuard(() => File.Copy(OriginalDstPath, OutputPath, true), OutputPath);
                    else File.Delete(OutputPath);
                }

                ProcessFile(Job, SrcPath, OutputPath);
            }
        }

        foreach (var RelativePath in Patches)
        {
            if (!Config.Remap(RelativePath, out var NewRelativePath, VerboseLogging)) continue;

            string PatchPath = Path.Combine(SourcePatch.Directory, RelativePath);
            string SourcePath = Path.Combine(Utils.GetSourceDirectory(), NewRelativePath);

            if (Options.HasFlag(JobOptions.TreatPatchAsFile))
            {
                foreach (string PatchSuffix in Patcher.Extensions)
                {
                    string PatchFilePath = PatchPath + PatchSuffix;
                    if (File.Exists(PatchFilePath)) ProcessFile(Job, PatchFilePath, SourcePath + PatchSuffix);
                }
                continue;
            }

            // The original source file have to exist
            string OriginalSourcePath = Path.Combine(Utils.GetSourceDirectory(), RelativePath);
            if (!File.Exists(SourcePath))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Skipped patch: {0} does not exist!", OriginalSourcePath);
                continue;
            }

            // When remapping patches, sync from original source if not exist
            if (OriginalSourcePath != SourcePath && !File.Exists(SourcePath))
            {
                Utils.EnsureParentDirectoryExists(SourcePath);
                Utils.FileAccessGuard(() => File.Copy(OriginalSourcePath, SourcePath), SourcePath);
            }

            // When dry running, sync with original output path unconditionally
            if (Options.HasFlag(JobOptions.DryRun) && OriginalSourcePath != SourcePath)
            {
                Utils.FileAccessGuard(() => File.Copy(OriginalSourcePath, SourcePath, true), SourcePath);
            }

            ProcessPatch(Job, PatchPath, SourcePath, SourcePatch);
        }

        Console.ForegroundColor = ConsoleColor.DarkBlue;
        Console.WriteLine("{0} job done: {1} <=> {2}", Job.ToString(), SourcePatch.Directory, Utils.GetSourceDirectory());
    }

    private readonly JobOptions Options;
    private readonly ConfigSystem DefaultConfig;
    private readonly Patcher PatcherInstance;

    private string PrivateInclusiveFilter = string.Empty;
    private string PrivateExclusiveFilter = "NonExist";
    private Utils.ConfirmResult OverrideConfirm;
    private Utils.ConfirmResult AutoClearConfirm = Utils.ConfirmResult.NotDecided;

    ////////////////////////////////////////////////////////////////////////////////////////////////////

    public Injector(string PluginName, string VariableOverrides, JobOptions Options)
    {
        this.Options = Options;

        if (Options.HasFlag(JobOptions.DryRun)) VariableOverrides = string.Join(',', "CRYSKNIFE_DRY_RUN=1", VariableOverrides);
        DefaultConfig = ConfigSystem.Create(Utils.UnifySeparators(PluginName), VariableOverrides);

        PatcherInstance = new Patcher(DefaultConfig);

        OverrideConfirm = Options.HasFlag(JobOptions.Force) ? Utils.ConfirmResult.Yes | Utils.ConfirmResult.ForAll : Utils.ConfirmResult.NotDecided;
    }

    public short PatchContextLength
    {
        get => PatcherInstance.PatchContextLength;
        set => PatcherInstance.PatchContextLength = value;
    }
    public float MatchContentTolerance
    {
        get => PatcherInstance.MatchContentTolerance;
        set => PatcherInstance.MatchContentTolerance = value;
    }
    public int MatchLineTolerance
    {
        get => PatcherInstance.MatchLineTolerance;
        set => PatcherInstance.MatchLineTolerance = value;
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

    public void RegisterSourcePatch(string InputPaths)
    {
        DefaultConfig.Dispatch(Config => RegisterSourcePatch(new SourcePatchInfo(Config), InputPaths), true);
    }

    public void UnregisterSourcePatch(string InputPaths)
    {
        DefaultConfig.Dispatch(Config => UnregisterSourcePatch(new SourcePatchInfo(Config), InputPaths), false);
    }

    public static void Init(string RootDirectory)
    {
        Utils.Init(RootDirectory);
        ConfigSystem.Init();
    }

    public void GenerateSetupScripts()
    {
        ProjectSetup.Generate(DefaultConfig.PluginName);
    }

    public void Process(JobType Job)
    {
        DefaultConfig.Dispatch(Config => Process(Config, Job), Job != JobType.Clear);
    }
}
