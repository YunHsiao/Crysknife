// SPDX-FileCopyrightText: 2024 Yun Hsiao Wu <yunhsiaow@gmail.com>
// SPDX-License-Identifier: MIT

using System.Text;

namespace Crysknife;

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

        if (!Arguments.TryGetValue("p", out var Parameters) && !Arguments.TryGetValue("project", out Parameters))
        {
            Console.Write("Please specify the source project name, where the patches are located.");
            return;
        }

        string RootFolderName = "Crysknife";
        string RootDirectory = Directory.GetCurrentDirectory();
        RootDirectory = RootDirectory[..(RootDirectory.IndexOf(RootFolderName, StringComparison.Ordinal) - 1)];

        Config.Init(Path.Combine(RootDirectory, RootFolderName));

        string ProjectName = Parameters;
        string SrcDirectory = Arguments.TryGetValue("s", out Parameters) || Arguments.TryGetValue("src", out Parameters) ? Parameters : Path.Combine(RootDirectory, ProjectName, "SourcePatch");
        // Assuming we are an engine plugin by default
        string DstDirectory = Arguments.TryGetValue("d", out Parameters) || Arguments.TryGetValue("dst", out Parameters) ? Parameters : Path.GetFullPath(Path.Combine(RootDirectory, "../Source"));

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

        if (Arguments.ContainsKey("setup-scripts")) { SetupScripts.Generate(RootDirectory, ProjectName); }

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
