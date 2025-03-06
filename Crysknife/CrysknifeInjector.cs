// SPDX-FileCopyrightText: 2024 Yun Hsiao Wu <yunhsiaow@gmail.com>
// SPDX-License-Identifier: MIT

namespace Crysknife;

[Flags]
public enum JobType
{
    None = 0x0,
    Clear = 0x1,
    Apply = 0x2,
    Generate = 0x4,
}

[Flags]
public enum JobOptions
{
    None = 0x0,
    Link = 0x1,
    Force = 0x2,
    DryRun = 0x4,
    Verbose = 0x8,
    Protected = 0x10,
    TreatPatchAsFile = 0x20,
}

public enum IncrementalMode
{
    // All version-relevant patches will be updated
    // Default, Recommended for latest engine version
    Disabled,
    // Only changed or version-specific patches will be updated
    // Recommended for any other engine versions
    Enabled,
    // Only chanegd patches will be updated
    // Recommended for in-house engine repos
    Aggressive
}

public class Injector
{
    // PatchPath is from SourcePatch folder
    private void ProcessPatch(JobType Job, string PatchPath, string TargetPath, ConfigSystem Config)
    {
        PatcherInstance.Injection = Config.PatchRegex.Injection;
        PatcherInstance.CommentTag = Config.TagPacker.Format.Tag;
        PatcherInstance.Variables = Config.Variables;
        PatcherInstance.CurrentPatch = PatchPath;

        // The target file have to exist
        if (!File.Exists(TargetPath))
        {
            // We may encounter files that only exists in specific engine versions
            // If so it is perfectly okay
            if (PatcherInstance.Load().HasAnyActivePatch())
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Skipped patch: {0} does not exist!", TargetPath);
            }
            return;
        }

        var CurrentContent = File.ReadAllText(TargetPath);
        var TargetContent = Config.PatchRegex.ClearResiduals(Utils.UnifyLineEndings(CurrentContent));
        var ClearedTarget = Config.PatchRegex.Unpatch(TargetContent);

        if (Job.HasFlag(JobType.Generate) && Job.HasFlag(JobType.Clear)) Generate();
        if (Job.HasFlag(JobType.Clear)) Clear();
        if (Job.HasFlag(JobType.Apply)) Apply();
        if (Job.HasFlag(JobType.Generate) && !Job.HasFlag(JobType.Clear)) Generate();

        void Generate()
        {
            var Patches = PatcherInstance.Generate(ClearedTarget, TargetContent);

            if (!Patches.IsValid())
            {
                if (!AutoClearConfirm.HasFlag(Utils.ConfirmResult.ForAll))
                {
                    AutoClearConfirm = Utils.PromptToConfirm($"Couldn't find any patch from '{TargetPath}', remove?");
                }
                if (AutoClearConfirm.HasFlag(Utils.ConfirmResult.Yes))
                {
                    UnregisterSourcePatch(Config, TargetPath);
                    return;
                }
            }

            if (PatcherInstance.Save(Patches))
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("Patch updated: " + PatchPath);
            }
        }

        void Clear()
        {
            if (ClearedTarget.Length == TargetContent.Length) return;
            Utils.FileAccessGuard(() => File.WriteAllText(TargetPath, Utils.UnifyLineEndings(ClearedTarget, OutputCrlf)), TargetPath);
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Patch removed from: " + TargetPath);
            TargetContent = ClearedTarget;
        }

        void Apply()
        {
            var Patches = PatcherInstance.Load();

            if (Patches.IsValid())
            {
                var DumpPath = Options.HasFlag(JobOptions.DryRun) ? TargetPath
                    : Path.Combine(Utils.GetPluginDirectory(Config.PluginName), "Intermediate", "Crysknife",
                        Path.GetRelativePath(Utils.GetSourceDirectory(), TargetPath));

                var Success = PatcherInstance.Apply(Patches, ClearedTarget, DumpPath, Options.HasFlag(JobOptions.DryRun), out var Patched);
                var FinalContent = Utils.UnifyLineEndings(Patched, OutputCrlf);
                if (Success && (!Patched.Equals(TargetContent, StringComparison.Ordinal) ||
                    (Options.HasFlag(JobOptions.Force) && !FinalContent.Equals(CurrentContent, StringComparison.Ordinal))))
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

                    Utils.FileAccessGuard(() => File.WriteAllText(TargetPath, FinalContent), TargetPath);
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("Patched: " + TargetPath);
                    TargetContent = Patched;
                    ClearedTarget = Config.PatchRegex.Unpatch(TargetContent);
                }
            }
        }
    }

    // SrcPath is from SourcePatch folder
    private void ProcessFile(JobType Job, string SrcPath, string DstPath, ConfigSystem Config)
    {
        var Exists = File.Exists(DstPath);
        var IsSymLink = Exists && new FileInfo(DstPath).Attributes.HasFlag(FileAttributes.ReparsePoint);
        var SourceContent = Utils.ApplyNewFileTag(Config.TagPacker, Config.Variables, File.ReadAllText(SrcPath));
        var TargetContent = Exists ? Utils.UnifyLineEndings(File.ReadAllText(DstPath)) : string.Empty;
        var UpToDate = Exists && !IsSymLink && SourceContent == TargetContent;

        if (Job.HasFlag(JobType.Generate) && !IsSymLink && !UpToDate)
        {
            if (!Exists)
            {
                if (!AutoClearConfirm.HasFlag(Utils.ConfirmResult.ForAll))
                {
                    AutoClearConfirm = Utils.PromptToConfirm($"Couldn't find target '{DstPath}', remove file from source patch?");
                }
                if (AutoClearConfirm.HasFlag(Utils.ConfirmResult.Yes))
                {
                    File.Delete(SrcPath);
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("Source file removed: {0}", SrcPath);
                }
            }
            else if (Utils.FileAccessGuard(() => File.WriteAllText(SrcPath, Utils.StripNewFileTag(Config.TagPacker, Config.Variables, TargetContent)), SrcPath))
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("Copied back: {0} <- {1}", SrcPath, DstPath);
                SourceContent = TargetContent;
                UpToDate = true;
            }
        }

        if (Job.HasFlag(JobType.Clear) && Exists)
        {
            Utils.FileAccessGuard(() => File.Delete(DstPath), DstPath);
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("{0} removed: {1}", IsSymLink ? "Link" : "File", DstPath);
            Exists = IsSymLink = UpToDate = false;
        }

        if (Job.HasFlag(JobType.Apply))
        {
            var ShouldBeSymLink = Options.HasFlag(JobOptions.Link);
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
                Utils.FileAccessGuard(() => File.WriteAllText(DstPath, Utils.UnifyLineEndings(SourceContent, OutputCrlf)), DstPath))
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("{0}: {1} -> {2}", ShouldBeSymLink ? "Linked" : "Copied", SrcPath, DstPath);
            }
        }
    }

    private void RegisterSourcePatch(ConfigSystem Config, string InputPaths)
    {
        var PatchedPaths = new List<string>();

        foreach (var InputPath in InputPaths.Split())
        {
            if (Path.GetExtension(InputPath) != string.Empty)
            {
                var FilePath = InputPath;
                if (!File.Exists(FilePath)) FilePath = Path.Combine(Utils.GetSourceDirectory(), FilePath);
                if (!File.Exists(FilePath)) continue;
                PatchedPaths.Add(FilePath);
            }
            else
            {
                var DirPath = InputPath;
                if (!Directory.Exists(DirPath)) DirPath = Path.Combine(Utils.GetSourceDirectory(), DirPath);
                if (!Directory.Exists(DirPath)) continue;
                PatchedPaths.AddRange(Directory.GetFiles(DirPath, "*", new EnumerationOptions
                    { RecurseSubdirectories = true }).Where(Utils.CanBePatched));
            }
        }

        foreach (var PatchedPath in PatchedPaths)
        {
            var RelativePath = Path.GetRelativePath(Utils.GetSourceDirectory(), PatchedPath);
            var PatchPath = Path.Combine(Utils.GetPatchDirectory(Config.PluginName), RelativePath);

            // Register any file starts with the plugin name
            if (Path.GetFileName(PatchedPath).StartsWith(Path.GetFileName(Config.PluginName)))
            {
                if (File.Exists(PatchPath)) continue;
                goto Register;
            }

            // Or new patched files
            PatchPath += PatcherInstance.DefaultExtension;
            if (!File.Exists(PatchPath) && Config.TagPacker.HasAnyMatch(Utils.UnifyLineEndings(File.ReadAllText(PatchedPath))))
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

    private void UnregisterSourcePatch(ConfigSystem Config, string InputPaths)
    {
        var PatchedPaths = new List<string>();

        foreach (var InputPath in InputPaths.Split())
        {
            if (Path.GetExtension(InputPath) != string.Empty)
            {
                var FilePath = InputPath;
                if (!File.Exists(FilePath)) FilePath = Path.Combine(Utils.GetSourceDirectory(), FilePath);
                if (!File.Exists(FilePath)) continue;
                PatchedPaths.Add(FilePath);
            }
            else
            {
                var DirPath = InputPath;
                if (!Directory.Exists(DirPath)) DirPath = Path.Combine(Utils.GetSourceDirectory(), DirPath);
                if (!Directory.Exists(DirPath)) continue;
                PatchedPaths.AddRange(Directory.GetFiles(DirPath, "*", new EnumerationOptions
                    { RecurseSubdirectories = true }).Where(Utils.CanBePatched));
            }
        }

        foreach (var PatchedPath in PatchedPaths)
        {
            var RelativePath = Path.GetRelativePath(Utils.GetSourceDirectory(), PatchedPath);
            var PatchPath = Path.Combine(Config.PatchDirectory, RelativePath + PatcherInstance.DefaultExtension);
            if (!File.Exists(PatchPath)) continue;

            ProcessPatch(JobType.Clear, PatchPath, PatchedPath, Config);
            File.Delete(PatchPath);
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Patch file deleted: " + PatchPath);
        }
    }

    private void Process(ConfigSystem Config, JobType Job)
    {
        var VerboseLogging = Options.HasFlag(JobOptions.Verbose);
        var Patches = new HashSet<string>();

        foreach (var SrcPath in Directory.GetFiles(Config.PatchDirectory, "*", new EnumerationOptions { RecurseSubdirectories = true }))
        {
            if (!SrcPath.Contains(InclusiveFilter) || SrcPath.Contains(ExclusiveFilter)) continue;

            var RelativePath = Path.GetRelativePath(Config.PatchDirectory, SrcPath);
            if (RelativePath.EndsWith(".patch", StringComparison.OrdinalIgnoreCase)) // Patch existing files
            {
                Patches.Add(Patcher.GetSourcePath(RelativePath));
            }
            else if (Config.Remap(RelativePath, out var DstRelativePath, VerboseLogging))
            {
                var OutputPath = Path.GetFullPath(Path.Combine(Utils.GetSourceDirectory(), DstRelativePath));

                // When dry running, sync with original output path unconditionally
                if (Options.HasFlag(JobOptions.DryRun) && RelativePath != DstRelativePath)
                {
                    Utils.EnsureParentDirectoryExists(OutputPath);
                    var OriginalDstPath = Path.Combine(Utils.GetSourceDirectory(), RelativePath);
                    if (File.Exists(OriginalDstPath)) Utils.FileAccessGuard(() => File.Copy(OriginalDstPath, OutputPath, true), OutputPath);
                    else Utils.FileAccessGuard(() => File.Delete(OutputPath), OutputPath);
                }

                ProcessFile(Job, SrcPath, OutputPath, Config);
            }
        }

        foreach (var RelativePath in Patches)
        {
            if (!Config.Remap(RelativePath, out var NewRelativePath, VerboseLogging, PatcherInstance.DefaultExtension)) continue;

            var PatchPath = Path.Combine(Config.PatchDirectory, RelativePath);
            var SourcePath = Path.GetFullPath(Path.Combine(Utils.GetSourceDirectory(), NewRelativePath));

            if (Options.HasFlag(JobOptions.TreatPatchAsFile))
            {
                foreach (var PatchSuffix in Patcher.Extensions)
                {
                    var PatchFilePath = PatchPath + PatchSuffix;
                    if (File.Exists(PatchFilePath)) ProcessFile(Job, PatchFilePath, SourcePath + PatchSuffix, Config);
                }
                continue;
            }
            var OriginalSourcePath = Path.Combine(Utils.GetSourceDirectory(), RelativePath);

            // When remapping patches, sync from original source if not exist
            if (OriginalSourcePath != SourcePath && File.Exists(OriginalSourcePath))
            {
                Utils.EnsureParentDirectoryExists(SourcePath);
                if (!File.Exists(SourcePath) || Options.HasFlag(JobOptions.DryRun))
                {
                    Utils.FileAccessGuard(() =>
                    {
                        if (File.Exists(SourcePath)) File.Delete(SourcePath);
                        File.Copy(OriginalSourcePath, SourcePath);
                    }, SourcePath);
                }
            }

            ProcessPatch(Job, PatchPath, SourcePath, Config);
        }

        Console.ForegroundColor = ConsoleColor.Blue;
        Console.WriteLine("{0} job done: {1} <=> {2}", Job.ToString(), Config.PatchDirectory, Utils.GetSourceDirectory());
    }

    private IncrementalMode GetDefaultIncrementalMode()
    {
        return DefaultConfig.RepoType switch
        {
            RepositoryType.Release => IncrementalMode.Disabled,
            RepositoryType.Stock => IncrementalMode.Enabled,
            RepositoryType.Internal => IncrementalMode.Aggressive,
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    private readonly JobOptions Options;
    private readonly ConfigSystem DefaultConfig;
    private readonly Patcher PatcherInstance;

    private readonly bool OutputCrlf;
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

        PatcherInstance = new Patcher(Options.HasFlag(JobOptions.Protected), GetDefaultIncrementalMode());
        DefaultConfig.Dispatch(Config => PatcherInstance.Packers.Add(Config.TagPacker), false); // Always pack all dependent plugins

        OutputCrlf = DefaultConfig.OutputCrlf;
        OverrideConfirm = Options.HasFlag(JobOptions.Force) ? Utils.ConfirmResult.Yes | Utils.ConfirmResult.ForAll : Utils.ConfirmResult.NotDecided;
    }

    public IncrementalMode IncrementalMode
    {
        get => PatcherInstance.Incremental;
        set => PatcherInstance.Incremental = value;
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
        DefaultConfig.Dispatch(Config => RegisterSourcePatch(Config, InputPaths), true);
    }

    public void UnregisterSourcePatch(string InputPaths)
    {
        DefaultConfig.Dispatch(Config => UnregisterSourcePatch(Config, InputPaths), false);
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

    public static string CamelCaseToSnakeCase(string Value)
    {
        return Utils.CamelCaseToSnakeCase(Value);
    }

    public void Process(JobType Job)
    {
        DefaultConfig.Dispatch(Config => Process(Config, Job), Job != JobType.Clear);
    }
}
