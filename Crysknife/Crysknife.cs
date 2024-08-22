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

        foreach (var Arg in Args)
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
                Output[Key[2..]] = Value.ToString();
            }
            else
            {
                foreach (var SingleKey in Key[1..^1])
                {
                    Output[SingleKey.ToString()] = string.Empty;
                }

                // If there are values, they belong to the last operand
                Output[Key.Last().ToString()] = Value.ToString();
            }

            Value.Clear();
        }
    }

    private static void Main(string[] Args)
    {
        var Arguments = ParseArguments(Args);

        if (!Arguments.TryGetValue("P", out var PluginName))
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Please specify the plugin name, where the source patches are located.");
            Utils.Abort();
            return;
        }

        var EngineRoot = Directory.GetCurrentDirectory();
        EngineRoot = Path.GetFullPath(Path.Combine(EngineRoot[..(EngineRoot.IndexOf("Crysknife", StringComparison.Ordinal) - 1)], ".."));
        if (Arguments.TryGetValue("E", out var Parameters)) EngineRoot = Path.GetFullPath(Path.Combine(Parameters, "Engine"));

        Injector.Init(EngineRoot);

        var VariableOverrides = string.Empty;
        if (Arguments.TryGetValue("D", out Parameters)) VariableOverrides = Parameters;

        // First letter of each enum name is used as the command flag
        var Options = JobOptions.None;
        foreach (var (Name, Value) in Enum.GetNames<JobOptions>().Zip(Enum.GetValues<JobOptions>()).Where(Pair => Pair.Second != JobOptions.None))
        {
            var FormattedKey = Injector.CamelCaseToSnakeCase(Name[Name.IndexOf(Name.First(char.IsUpper))..]).Replace('_', '-');
            if (Arguments.ContainsKey($"{char.ToLower(Name.First())}") || Arguments.ContainsKey(FormattedKey)) Options |= Value;
        }

        var InjectorInstance = new Injector(PluginName, VariableOverrides, Options);
        var Job = JobType.None;

        if ((Arguments.TryGetValue("n", out Parameters) || Arguments.TryGetValue("incremental", out Parameters)) &&
            int.TryParse(Parameters.Length > 0 ? Parameters : "1", out var Incremental))
        {
            InjectorInstance.IncrementalMode = (IncrementalMode)Incremental;
        }

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
