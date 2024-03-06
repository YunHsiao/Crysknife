using System.Text;
using System.Text.RegularExpressions;
using DiffMatchPatch;

namespace UnrealSourceInjector;

public enum JobType
{
    Apply,
    Generate,
    Clear,
    Test
}

[Flags]
public enum JobOptions
{
    None = 0x0,
    Link = 0x1,
    Debug = 0x2,
    Force = 0x4,
}

public class UnrealSourceInjector
{
    private readonly struct InjectionRegex
    {
        public readonly Regex RE;
        public readonly int Tag;
        public readonly int Content;

        public InjectionRegex(Regex InRE, int InTag, int InContent)
        {
            RE = InRE;
            Tag = InTag;
            Content = InContent;
        }
    }

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
            return Create(Versions[0], Versions[1]);
        }

        public static EngineVersion Create(string Major, string Minor)
        {
            return new EngineVersion(int.Parse(Major), int.Parse(Minor));
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
        Abort = 0x8,
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

        return Response switch
        {
            ConsoleKey.Y => ConfirmResult.Yes,
            ConsoleKey.N => ConfirmResult.No,
            ConsoleKey.A => ConfirmResult.Yes | ConfirmResult.ForAll,
            ConsoleKey.Z => ConfirmResult.No | ConfirmResult.ForAll,
            _ => ConfirmResult.Abort
        };
    }

    private static string GetPatchDebugOutputPath(string PatchPath)
    {
        var ParsedPath = new ParsedPath(PatchPath);
        return ParsedPath.PathTrunc + ".ignore" + ParsedPath.Extensions.First();
    }

    private string Unpatch(string Content)
    {
        return InjectionRE.Aggregate(Content, (Acc, RE) => RE.RE.Replace(Acc, Match =>
        {
            if (Match.Groups[RE.Tag].Value.StartsWith(ProjectName + '-')) // Restore deletions
            {
                return CommentRE.Replace(Match.Groups[RE.Content].Value, ContentMatch => ContentMatch.Groups[1].Value);
            }
            return string.Empty; // Remove injections
        }));
    }

    private void ApplyPatch(string TargetPath, string PatchPath)
    {
        string Target = File.ReadAllText(TargetPath);
        string ClearedTarget = Unpatch(Target);

        var DMP = new diff_match_patch { Match_Threshold = MatchContentTolerance, Match_Distance = MatchLineTolerance };
        var Patches = DMP.patch_fromText(File.ReadAllText(PatchPath));
        object[] Result = DMP.patch_apply(Patches, ClearedTarget);
        string Patched = (string)Result[0];
        if (Patched == Target) return;

        if (Target.Length != ClearedTarget.Length)
        {
            // Apply op is potentially dangerous: Confirm before overriding any new contents.
            if (!OverrideConfirm.HasFlag(ConfirmResult.ForAll))
            {
                OverrideConfirm = PromptToConfirm($"Override patched file {TargetPath}?");
            }
            if (OverrideConfirm.HasFlag(ConfirmResult.No)) return;
            if (OverrideConfirm.HasFlag(ConfirmResult.Abort)) Environment.Exit(1);
        }

        File.WriteAllText(TargetPath, Patched);

        bool[] IsSuccess = (bool[])Result[1];
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

    private void GeneratePatch(string TargetPath, string PatchPath)
    {
        string Target = File.ReadAllText(TargetPath);
        string Source = Unpatch(Target);

        if (Options.HasFlag(JobOptions.Debug))
        {
            string DebugOutput = GetPatchDebugOutputPath(PatchPath);
            File.WriteAllText(DebugOutput, Source);
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Unpatched content written to: " + DebugOutput);
        }

        var DMP = new diff_match_patch { Patch_Margin = PatchContextLength };
        var Diffs = DMP.diff_main(Source, Target);
        if (Diffs.Count > 2)
        {
            DMP.diff_cleanupSemantic(Diffs);
            DMP.diff_cleanupEfficiency(Diffs);
        }
        string Patch = DMP.patch_toText(DMP.patch_make(Source, Diffs));

        if (File.Exists(PatchPath))
        {
            string ExistingPatch = File.ReadAllText(PatchPath);
            if (ExistingPatch == Patch) return;
        }

        File.WriteAllText(PatchPath + ".html", DMP.diff_prettyHtml(Diffs));
        File.WriteAllText(PatchPath, Patch);
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Patch updated: " + TargetPath);
    }

    private void ClearPatch(string TargetPath)
    {
        string Target = File.ReadAllText(TargetPath);
        string ClearedTarget = Unpatch(Target);
        if (ClearedTarget.Length == Target.Length) return;

        File.WriteAllText(TargetPath, ClearedTarget);
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("Patch removed from: " + TargetPath);
    }

    private void TestPatch(string RefPath, string PatchPath)
    {
        string TargetPath = GetPatchDebugOutputPath(PatchPath);
        if (!File.Exists(TargetPath)) File.Copy(RefPath, TargetPath);
        ApplyPatch(TargetPath, PatchPath);
    }
    
    private void ProcessFile(JobType Job, string SrcPath, string DstPath)
    {
        bool Exists = File.Exists(DstPath);
        bool IsSymLink = Exists && new FileInfo(DstPath).Attributes.HasFlag(FileAttributes.ReparsePoint);

        if (Job == JobType.Apply)
        {
            bool ShouldBeSymLink = Options.HasFlag(JobOptions.Link);
            if (IsSymLink && ShouldBeSymLink) return;

            if (Exists)
            {
                if (File.ReadAllText(SrcPath) == File.ReadAllText(DstPath)) return;

                // Apply op is potentially dangerous: Confirm before overriding any new contents.
                if (!OverrideConfirm.HasFlag(ConfirmResult.ForAll))
                {
                    OverrideConfirm = PromptToConfirm($"Override existing file {DstPath}?");
                }
                if (OverrideConfirm.HasFlag(ConfirmResult.No)) return;
                if (OverrideConfirm.HasFlag(ConfirmResult.Abort)) Environment.Exit(1);

                File.Delete(DstPath);
            }

            if (ShouldBeSymLink) File.CreateSymbolicLink(DstPath, SrcPath);
            else File.Copy(SrcPath, DstPath);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("{0}: {1} -> {2}", ShouldBeSymLink ? "Linked" : "Copied", SrcPath, DstPath);
        }
        else if (Job == JobType.Generate && Exists && !IsSymLink)
        {
            File.Copy(DstPath, SrcPath);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Copied back: {0} -> {1}", DstPath, SrcPath);
        }
        else if (Job == JobType.Clear && Exists)
        {
            File.Delete(DstPath);
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("{0} removed: {1}", IsSymLink ? "Link" : "File", DstPath);
        }
    }

    private readonly string ProjectName;
    private readonly string SrcDirectory;
    private readonly string DstDirectory;
    private readonly JobOptions Options;

    private string PrivateInclusiveFilter = string.Empty;
    private string PrivateExclusiveFilter = "NonExist";

    private readonly Regex CommentRE = new (@"^(\s*)//\s*", RegexOptions.Multiline | RegexOptions.Compiled);
    private readonly Regex SeparatorRE = new (@"[\\/]", RegexOptions.Compiled);
    private readonly Regex EngienVersionRE = new (@"#define\s+ENGINE_MAJOR_VERSION\s+(\d+)\s*#define\s+ENGINE_MINOR_VERSION\s+(\d+)", RegexOptions.Compiled);

    private readonly InjectionRegex[] InjectionRE;
    private readonly EngineVersion CurrentEngineVersion;
    private ConfirmResult OverrideConfirm;

    ////////////////////////////////////////////////////////////////////////////////////////////////////

    public UnrealSourceInjector(string InProjectName, string InSrcDirectory, string InDstDirectory, JobOptions InOptions)
    {
        ProjectName = InProjectName;
        SrcDirectory = InSrcDirectory;
        DstDirectory = InDstDirectory;
        Options = InOptions;

        string ProjectTag = ProjectName + @"[\w\s:+-]*"; // Allow some comments in between

        InjectionRE = new InjectionRegex[]
        {
            new(new Regex(string.Format(@"\s*// ({0}): Begin(.*?)// {0}: End", ProjectTag),
                RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled), 1, 2), // Multi-line injection
            new(new Regex($@"^(\s*\S+.*?)\s*// ({ProjectTag})$",
                RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled), 2, 1), // Single-line injection
            new(new Regex($@"^\s*// ({ProjectTag}).*\n(.*)",
                RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled), 1, 2) // Next-line injection
        };
        Match VersionMatch = EngienVersionRE.Match(File.ReadAllText(Path.Combine(DstDirectory, "Runtime/Launch/Resources/Version.h")));
        CurrentEngineVersion = EngineVersion.Create(VersionMatch.Groups[1].Value, VersionMatch.Groups[2].Value);
        OverrideConfirm = Options.HasFlag(JobOptions.Force) ? ConfirmResult.Yes | ConfirmResult.ForAll : ConfirmResult.NotDecided;
    }

    public short PatchContextLength = 50;
    public float MatchContentTolerance = 0.5f;
    public int MatchLineTolerance = int.MaxValue; // Line number may vary significantly
    public string InclusiveFilter
    {
        get => PrivateInclusiveFilter;
        set => PrivateInclusiveFilter = SeparatorRE.Replace(value, Path.DirectorySeparatorChar.ToString());
    }
    public string ExclusiveFilter
    {
        get => PrivateExclusiveFilter;
        set => PrivateExclusiveFilter = SeparatorRE.Replace(value, Path.DirectorySeparatorChar.ToString());
    }

    public void CreatePatchFile(IEnumerable<string> InputPaths)
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
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Patch file skipped: No valid patch found in " + PatchedPath);
                continue;
            }

            Directory.GetParent(PatchPath)?.Create();
            File.Create(PatchPath).Close();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Patch file created: " + PatchPath);
        }
    }

    public void RemovePatchFile(IEnumerable<string> InputPaths)
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

            ClearPatch(PatchedPath);
            File.Delete(PatchPath);
            File.Delete(PatchPath + ".html");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Patch file deleted: " + PatchPath);
        }
    }

    public void Process(JobType Job, string SrcDirectoryOverride)
    {
        var Injections = new List<string>();
        var Patches = new Dictionary<string, PatchDescription>();

        foreach (string SrcPath in Directory.GetFiles(SrcDirectoryOverride, "*", new EnumerationOptions { RecurseSubdirectories = true }))
        {
            string RelativePath = Path.GetRelativePath(SrcDirectoryOverride, SrcPath);
            if (!RelativePath.Contains(InclusiveFilter) || RelativePath.Contains(ExclusiveFilter)) continue;
            var ParsedRelativePath = new ParsedPath(RelativePath);

            if (ParsedRelativePath.Extensions.Last() == ".patch") // Patch existing files
            {
                RelativePath = ParsedRelativePath.PathTrunc + ParsedRelativePath.Extensions.First();
                string DstPath = Path.Combine(DstDirectory, RelativePath);
                if (!File.Exists(DstPath)) continue;

                if (!Patches.ContainsKey(RelativePath)) Patches.Add(RelativePath, new PatchDescription());
                Patches[RelativePath].Add(ParsedRelativePath);
            }
            else if (ParsedRelativePath.Extensions.Last() is ".h" or ".cpp" && !ParsedRelativePath.Extensions.Contains(".ignore")) // Add our new files
            {
                Injections.Add(RelativePath);
            }
        }

        foreach (string RelativePath in Injections)
        {
            ProcessFile(Job, Path.Combine(SrcDirectoryOverride, RelativePath), Path.Combine(DstDirectory, RelativePath));
        }

        foreach (var Pair in Patches)
        {
            string SrcPath = Path.Combine(SrcDirectoryOverride, Pair.Key + Pair.Value.Match(CurrentEngineVersion));
            string DstPath = Path.Combine(DstDirectory, Pair.Key);

            if (Job == JobType.Apply) ApplyPatch(DstPath, SrcPath);
            else if (Job == JobType.Generate) GeneratePatch(DstPath, SrcPath);
            else if (Job == JobType.Clear) ClearPatch(DstPath);
            else if (Job == JobType.Test) TestPatch(DstPath, SrcPath);
        }

        Console.ForegroundColor = ConsoleColor.DarkBlue;
        Console.WriteLine("{0} job done: {1} <=> {2}", Job.ToString(), SrcDirectoryOverride, DstDirectory);
    }

    public void Process(JobType Job)
    {
        Process(Job, SrcDirectory);
    }
}

internal static class UnrealSourceInjectorLauncher
{
    private static Dictionary<string, string> ParseArguments(IEnumerable<string> Args)
    {
        var Output = new Dictionary<string, string>();
        var CurrentKey = string.Empty;
        var CurrentValue = new StringBuilder(256);

        foreach (string Arg in Args)
        {
            if (!Arg.StartsWith("-"))
            {
                if (CurrentValue.Length != 0) CurrentValue.Append(' ');
                CurrentValue.Append(Arg);
                continue;
            }

            if (CurrentKey.Length != 0)
            {
                Output.TryAdd(CurrentKey, CurrentValue.ToString());
                CurrentValue.Clear();
            }
            CurrentKey = Arg.StartsWith("--") ? Arg[2..] : Arg[1..];
        }

        if (CurrentKey.Length != 0)
        {
            Output.TryAdd(CurrentKey, CurrentValue.ToString());
        }

        return Output;
    }

    private static void Main(string[] Args)
    {
        var Arguments = ParseArguments(Args);

        string RootDirectory = Directory.GetCurrentDirectory();
        RootDirectory = RootDirectory[..(RootDirectory.IndexOf("UnrealSourceInjector", StringComparison.Ordinal) - 1)];

        string ProjectName = Arguments.TryGetValue("P", out var Parameters) || Arguments.TryGetValue("project", out Parameters) ?
            Parameters : RootDirectory[(RootDirectory.LastIndexOf(Path.DirectorySeparatorChar) + 1)..];
        string SrcDirectory = Arguments.TryGetValue("src", out Parameters) ? Parameters : Path.Combine(RootDirectory, "SourcePatch");
        // Assuming we are inside an engine plugin by default
        string DstDirectory = Arguments.TryGetValue("dst", out Parameters) ? Parameters : Path.GetFullPath(Path.Combine(RootDirectory, "../../Source"));

        var Options = JobOptions.None;
        if (Arguments.ContainsKey("link")) Options |= JobOptions.Link;
        if (Arguments.ContainsKey("debug")) Options |= JobOptions.Debug;
        if (Arguments.ContainsKey("F") || Arguments.ContainsKey("force")) Options |= JobOptions.Force;

        var Injector = new UnrealSourceInjector(ProjectName, SrcDirectory, DstDirectory, Options);
        var Job = JobType.Apply;

        if (Arguments.TryGetValue("I", out Parameters) || Arguments.TryGetValue("inclusive-filter", out Parameters)) Injector.InclusiveFilter = Parameters;
        if (Arguments.TryGetValue("E", out Parameters) || Arguments.TryGetValue("exclusive-filter", out Parameters)) Injector.ExclusiveFilter = Parameters;
        if (Arguments.TryGetValue("pc", out Parameters) || Arguments.TryGetValue("patch-context", out Parameters)) Injector.PatchContextLength = short.Parse(Parameters);
        if (Arguments.TryGetValue("ct", out Parameters) || Arguments.TryGetValue("content-tolerance", out Parameters)) Injector.MatchContentTolerance = float.Parse(Parameters);
        if (Arguments.TryGetValue("lt", out Parameters) || Arguments.TryGetValue("line-tolerance", out Parameters)) Injector.MatchLineTolerance = int.Parse(Parameters);
        if (Arguments.TryGetValue("add", out Parameters)) { Injector.CreatePatchFile(Parameters.Split()); Job = JobType.Generate; }
        if (Arguments.TryGetValue("rm", out Parameters)) { Injector.RemovePatchFile(Parameters.Split()); Job = JobType.Generate; }

        if (Arguments.ContainsKey("A")) Job = JobType.Apply;
        else if (Arguments.ContainsKey("G")) Job = JobType.Generate;
        else if (Arguments.ContainsKey("C")) Job = JobType.Clear;
        else if (Arguments.ContainsKey("T")) Job = JobType.Test;

        if (!Arguments.ContainsKey("nb") && !Arguments.ContainsKey("no-builtin"))
        {
            string BuiltinSourcePatch = Path.Combine(RootDirectory, "UnrealSourceInjector", "SourcePatch");
            Injector.Process(Job, BuiltinSourcePatch);   
        }

        Injector.Process(Job);
        Console.ResetColor();
    }
}
