/*
 * MIT License
 *
 * Copyright (c) 2024 Yun Hsiao Wu
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 */

using System.Text;
using System.Text.RegularExpressions;

namespace UnrealSourceInjector;

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
    DryRun = 0x2,
    Force = 0x4,
}

public class Injector
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

    private void ProcessPatch(JobType Job, string PatchPath, string TargetPath)
    {
        string Target = File.ReadAllText(TargetPath);
        string ClearedTarget = Unpatch(Target);
        List<DiffMatchPatch.Patch>? Patches = null;

        if (Job.HasFlag(JobType.Generate))
        {
            var Diffs = PatchTool.GenerateDiffs(ClearedTarget, Target);
            Patches = PatchTool.GeneratePatches(ClearedTarget, Diffs);
            string Patch = PatchTool.Generate(Patches);

            if (!File.Exists(PatchPath) || File.ReadAllText(PatchPath) != Patch)
            {
                File.WriteAllText(PatchPath + ".html", PatchTool.GetHtml(Diffs));
                File.WriteAllText(PatchPath, Patch);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("Patch updated: " + TargetPath);
            }
        }

        if (Job.HasFlag(JobType.Clear) && ClearedTarget.Length != Target.Length)
        {
            if (Options.HasFlag(JobOptions.DryRun))
            {
                TargetPath = GetPatchDebugOutputPath(PatchPath);
            }

            File.WriteAllText(TargetPath, ClearedTarget);
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Patch removed from: " + TargetPath);
        }

        if (Job.HasFlag(JobType.Apply))
        {
            string Patched = Patches != null ? PatchTool.Apply(ClearedTarget, Patches, out var IsSuccess)
                : PatchTool.Apply(ClearedTarget, PatchPath, out IsSuccess);
            if (Patched == Target) return;

            if (Options.HasFlag(JobOptions.DryRun))
            {
                TargetPath = GetPatchDebugOutputPath(PatchPath);
            }
            else if (Target.Length != ClearedTarget.Length)
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
            File.Delete(SrcPath);
            File.Copy(DstPath, SrcPath);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Copied back: {0} -> {1}", DstPath, SrcPath);
            UpToDate = true;
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
                if (OverrideConfirm.HasFlag(ConfirmResult.Abort)) Environment.Exit(1);

                File.Delete(DstPath);
            }
            else // Create directory if not exist
            {
                string? TargetDir = Path.GetDirectoryName(DstPath);
                if (TargetDir != null && !Directory.Exists(TargetDir)) Directory.CreateDirectory(TargetDir);
            }

            if (ShouldBeSymLink) File.CreateSymbolicLink(DstPath, SrcPath);
            else File.Copy(SrcPath, DstPath);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("{0}: {1} -> {2}", ShouldBeSymLink ? "Linked" : "Copied", SrcPath, DstPath);
        }
    }

    private void CreatePatchTool()
    {
        PatchTool = new DMPContext(PatchContextLength, MatchContentTolerance, MatchLineTolerance);
    }

    private readonly string ProjectName;
    private readonly string SrcDirectory;
    private readonly string DstDirectory;
    private readonly JobOptions Options;

    private string PrivateInclusiveFilter = string.Empty;
    private string PrivateExclusiveFilter = "NonExist";
    private short PrivatePatchContextLength = 50;
    private float PrivateMatchContentTolerance = 0.5f;
    private int PrivateMatchLineTolerance = int.MaxValue; // Line number may vary significantly

    private static readonly Regex CommentRE = new (@"^(\s*)//\s*", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex EngienVersionRE = new (@"#define\s+ENGINE_MAJOR_VERSION\s+(\d+)\s*#define\s+ENGINE_MINOR_VERSION\s+(\d+)", RegexOptions.Compiled);

    private readonly InjectionRegex[] InjectionRE;
    private readonly EngineVersion CurrentEngineVersion;
    private ConfirmResult OverrideConfirm;
    private DMPContext PatchTool;

    ////////////////////////////////////////////////////////////////////////////////////////////////////

    public Injector(string InProjectName, string InSrcDirectory, string InDstDirectory, JobOptions InOptions)
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
        set => PrivateInclusiveFilter = InjectorConfig.SeparatorPatch(value);
    }
    public string ExclusiveFilter
    {
        get => PrivateExclusiveFilter;
        set => PrivateExclusiveFilter = InjectorConfig.SeparatorPatch(value);
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

            ProcessPatch(JobType.Clear, PatchPath, PatchedPath);
            File.Delete(PatchPath);
            File.Delete(PatchPath + ".html");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Patch file deleted: " + PatchPath);
        }
    }

    public void Process(JobType Job, string SrcDirectoryOverride)
    {
        var Patches = new Dictionary<string, PatchDescription>();
        var Config = new InjectorConfig(Path.Combine(SrcDirectoryOverride, "Injector.ini"), DstDirectory);

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
            else if (Config.Remap(RelativePath, out var DstRelativePath))
            {
                ProcessFile(Job, Path.Combine(SrcDirectoryOverride, RelativePath), Path.Combine(DstDirectory, DstRelativePath));
            }
        }

        foreach (var Pair in Patches)
        {
            if (!Config.Remap(Pair.Key, out var DstRelativePath)) continue;

            string SrcPath = Path.Combine(SrcDirectoryOverride, Pair.Key + Pair.Value.Match(CurrentEngineVersion));
            string DstPath = Path.Combine(DstDirectory, DstRelativePath);

            if (!File.Exists(DstPath))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Skipped patch: {0} does not exist!", DstPath);
                continue;
            }

            ProcessPatch(Job, SrcPath, DstPath);
        }

        Console.ForegroundColor = ConsoleColor.DarkBlue;
        Console.WriteLine("{0} job done: {1} <=> {2}", Job.ToString(), SrcDirectoryOverride, DstDirectory);
    }

    public void Process(JobType Job)
    {
        Process(Job, SrcDirectory);
    }
}

internal static class Launcher
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

        string RootFolderName = "UnrealSourceInjector";
        string RootDirectory = Directory.GetCurrentDirectory();
        RootDirectory = RootDirectory[..(RootDirectory.IndexOf(RootFolderName, StringComparison.Ordinal) - 1)];

        InjectorConfig.Init(Path.Combine(RootDirectory, RootFolderName));

        string ProjectName = Arguments.TryGetValue("p", out var Parameters) || Arguments.TryGetValue("project", out Parameters) ?
            Parameters : RootDirectory[(RootDirectory.LastIndexOf(Path.DirectorySeparatorChar) + 1)..];
        string SrcDirectory = Arguments.TryGetValue("s", out Parameters) || Arguments.TryGetValue("src", out Parameters) ? Parameters : Path.Combine(RootDirectory, "SourcePatch");
        // Assuming we are inside an engine plugin by default
        string DstDirectory = Arguments.TryGetValue("d", out Parameters) || Arguments.TryGetValue("dst", out Parameters) ? Parameters : Path.GetFullPath(Path.Combine(RootDirectory, "../../Source"));

        bool IsSrcDirValid = Directory.Exists(SrcDirectory);
        bool IsDstDirValid = Directory.Exists(DstDirectory);
        if (!IsSrcDirValid || !IsDstDirValid)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            if (!IsSrcDirValid) Console.WriteLine("Invalid src patch directory {0}, abort!", SrcDirectory);
            else if (!IsDstDirValid) Console.WriteLine("Invalid dst patch directory {0}, abort!", DstDirectory);
            Console.ResetColor();
            Environment.Exit(1);
        }

        var Options = JobOptions.None;
        if (Arguments.ContainsKey("l") || Arguments.ContainsKey("link")) Options |= JobOptions.Link;
        if (Arguments.ContainsKey("t") || Arguments.ContainsKey("dry-run")) Options |= JobOptions.DryRun;
        if (Arguments.ContainsKey("f") || Arguments.ContainsKey("force")) Options |= JobOptions.Force;

        var Injector = new Injector(ProjectName, SrcDirectory, DstDirectory, Options);
        var Job = JobType.None;

        if (Arguments.TryGetValue("if", out Parameters) || Arguments.TryGetValue("inclusive-filter", out Parameters)) Injector.InclusiveFilter = Parameters;
        if (Arguments.TryGetValue("ef", out Parameters) || Arguments.TryGetValue("exclusive-filter", out Parameters)) Injector.ExclusiveFilter = Parameters;
        if (Arguments.TryGetValue("pc", out Parameters) || Arguments.TryGetValue("patch-context", out Parameters)) Injector.PatchContextLength = short.Parse(Parameters);
        if (Arguments.TryGetValue("ct", out Parameters) || Arguments.TryGetValue("content-tolerance", out Parameters)) Injector.MatchContentTolerance = float.Parse(Parameters);
        if (Arguments.TryGetValue("lt", out Parameters) || Arguments.TryGetValue("line-tolerance", out Parameters)) Injector.MatchLineTolerance = int.Parse(Parameters);

        if (Arguments.TryGetValue("R", out Parameters)) { Injector.CreatePatchFile(Parameters.Split()); Job = JobType.Generate; }
        if (Arguments.TryGetValue("U", out Parameters)) { Injector.RemovePatchFile(Parameters.Split()); Job = JobType.Generate; }

        if (Arguments.ContainsKey("G")) Job |= JobType.Generate;
        if (Arguments.ContainsKey("C")) Job |= JobType.Clear;
        if (Arguments.ContainsKey("A")) Job |= JobType.Apply;
        if (Job == JobType.None) Job = JobType.Apply; // By default do the apply action

        if (!Arguments.ContainsKey("b") && !Arguments.ContainsKey("no-builtin"))
        {
            string BuiltinSourcePatch = Path.Combine(RootDirectory, RootFolderName, "SourcePatch");
            Injector.Process(Job, BuiltinSourcePatch);   
        }

        Injector.Process(Job);
        Console.ResetColor();
    }
}
