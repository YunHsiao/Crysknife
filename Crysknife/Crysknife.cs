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

            if (CurrentKey.Length != 0) AddPair(CurrentKey, CurrentValue);
            CurrentKey = Arg;
        }

        if (CurrentKey.Length != 0) AddPair(CurrentKey, CurrentValue);
        return Output;

        void AddPair(string Key, StringBuilder Value)
        {
            if (Key.StartsWith("--"))
            {
                Output.TryAdd(Key[2..], Value.ToString());
            }
            else
            {
                foreach (var SingleKey in Key[1..^1])
                {
                    Output.TryAdd(SingleKey.ToString(), string.Empty);
                }

                // If there are values, they belong to the last operand
                Output.TryAdd(Key.Last().ToString(), Value.ToString());
            }

            Value.Clear();
        }
    }

    private static void Main(string[] Args)
    {
        var Arguments = ParseArguments(Args);

        if (!Arguments.TryGetValue("P", out string? PluginName))
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Please specify the plugin name, where the source patches are located.");
            Utils.Abort();
            return;
        }

        string EngineRoot = Directory.GetCurrentDirectory();
        EngineRoot = Path.GetFullPath(Path.Combine(EngineRoot[..(EngineRoot.IndexOf("Crysknife", StringComparison.Ordinal) - 1)], ".."));

        Injector.Init(EngineRoot);

        string VariableOverrides = "";
        if (Arguments.TryGetValue("D", out var Parameters)) VariableOverrides = Parameters;

        var Options = JobOptions.None;
        if (Arguments.ContainsKey("l") || Arguments.ContainsKey("link")) Options |= JobOptions.Link;
        if (Arguments.ContainsKey("f") || Arguments.ContainsKey("force")) Options |= JobOptions.Force;
        if (Arguments.ContainsKey("d") || Arguments.ContainsKey("dry-run")) Options |= JobOptions.DryRun;
        if (Arguments.ContainsKey("v") || Arguments.ContainsKey("verbose")) Options |= JobOptions.Verbose;
        if (Arguments.ContainsKey("t") || Arguments.ContainsKey("treat-patch-as-file")) Options |= JobOptions.TreatPatchAsFile;
        if (Arguments.ContainsKey("g") || Arguments.ContainsKey("generate-diff-html")) Options |= JobOptions.GenerateDiffHtml;
        if (Arguments.ContainsKey("c") || Arguments.ContainsKey("clear-all-history")) Options |= JobOptions.ClearAllHistory;
        if (Arguments.ContainsKey("k") || Arguments.ContainsKey("keep-all-history")) Options |= JobOptions.KeepAllHistory;

        var InjectorInstance = new Injector(PluginName, VariableOverrides, Options);
        var Job = JobType.None;

        if (Arguments.TryGetValue("i", out Parameters)) InjectorInstance.InclusiveFilter = Parameters;
        if (Arguments.TryGetValue("e", out Parameters)) InjectorInstance.ExclusiveFilter = Parameters;
        if (Arguments.TryGetValue("patch-context", out Parameters)) InjectorInstance.PatchContextLength = short.Parse(Parameters);
        if (Arguments.TryGetValue("content-tolerance", out Parameters)) InjectorInstance.MatchContentTolerance = float.Parse(Parameters);
        if (Arguments.TryGetValue("line-tolerance", out Parameters)) InjectorInstance.MatchLineTolerance = int.Parse(Parameters);

        if (Arguments.ContainsKey("S")) { InjectorInstance.GenerateSetupScripts(); }
        if (Arguments.TryGetValue("R", out Parameters)) { InjectorInstance.RegisterSourcePatch(Parameters); Job = JobType.Generate; }
        if (Arguments.TryGetValue("U", out Parameters)) { InjectorInstance.UnregisterSourcePatch(Parameters); Job = JobType.Generate; }

        if (Arguments.ContainsKey("G")) Job |= JobType.Generate;
        if (Arguments.ContainsKey("C")) Job |= JobType.Clear;
        if (Arguments.ContainsKey("A")) Job |= JobType.Apply;
        if (Job == JobType.None) Job = JobType.Apply; // By default do the apply action

        InjectorInstance.Process(Job);
        Console.ResetColor();
    }
}
