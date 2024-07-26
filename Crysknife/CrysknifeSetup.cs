// SPDX-FileCopyrightText: 2024 Yun Hsiao Wu <yunhsiaow@gmail.com>
// SPDX-License-Identifier: MIT

using System.Text.Json;
using System.Text.Json.Nodes;

namespace Crysknife;

public static class ProjectSetup
{
    private static readonly string WindowsTemplate = @"
        @echo off
        ""%~dp0..\Crysknife\Crysknife.bat"" -P {0} %*
        pause
    ".Replace("    ", string.Empty);

    private static readonly string LinuxTemplate = @"
        DIR=`cd ""$(dirname ""$0"")""; pwd`
        ""$DIR/../Crysknife/Crysknife.sh"" -P {0} ""$@""
    ".Replace("    ", string.Empty).Replace("\r\n", "\n");

    private static void GenerateSetupScripts(string TargetDirectory, string PluginName)
    {
        File.WriteAllText(Path.Combine(TargetDirectory, "Setup.bat"), string.Format(WindowsTemplate, PluginName));
        File.WriteAllText(Path.Combine(TargetDirectory, "Setup.sh"), string.Format(LinuxTemplate, PluginName));
        File.WriteAllText(Path.Combine(TargetDirectory, "Setup.command"), string.Format(LinuxTemplate, PluginName));
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Setup scripts created: " + Path.Combine(TargetDirectory, "Setup"));
    }

    private static readonly JsonObject PluginReference = new(
        new[]
        {
            KeyValuePair.Create<string, JsonNode?>("Name", "Crysknife"),
            KeyValuePair.Create<string, JsonNode?>("Enabled", true)
        }
    );

    private static void PatchPluginDescription(string TargetDirectory, string PluginName)
    {
        string PluginDescFile = Path.Combine(TargetDirectory, PluginName + ".uplugin");
        if (!File.Exists(PluginDescFile))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine("Error: Couldn't find plugin description at {0}", PluginDescFile);
            Utils.Abort();
        }
        var PluginDesc = JsonNode.Parse(File.ReadAllText(PluginDescFile));
        if (PluginDesc == null) return;

        PluginDesc.AsObject().TryGetPropertyValue("Plugins", out var DependentPlugins);
        if (DependentPlugins == null)
        {
            PluginDesc.AsObject().Add("Plugins", new JsonArray(PluginReference));
        }
        else
        {
            foreach (var DependentPlugin in DependentPlugins.AsArray())
            {
                if (DependentPlugin == null || !DependentPlugin.AsObject().TryGetPropertyValue("Name", out var Name)) continue;
                // Skip if already there
                if (Name != null && Name.ToString() == "Crysknife") return;
            }
            DependentPlugins.AsArray().Add(PluginReference);
        }

        File.WriteAllText(PluginDescFile, PluginDesc.ToJsonString(new JsonSerializerOptions{ WriteIndented = true }));
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Plugin description patched: " + PluginDescFile);
    }

    private static readonly string ConfigTemplate = @"
        [Variables]
        ; Enable patching engine editor source code
        {0}_EDITOR=1
        ; Enable patching engine runtime source code
        {0}_RUNTIME=1

        [Dependencies]
        Crysknife=CRYSKNIFE_RUNTIME=${{{0}_RUNTIME}}
        +Crysknife=CRYSKNIFE_EDITOR=${{{0}_EDITOR}}

        [Runtime|Developer]
        ; All predicates should be satisfied to skip
        SkipIf=Conjunctions:Root
        ; If the switch is off...
        +SkipIf=IsTruthy:!${{{0}_RUNTIME}}
        ; And the input is a patch...
        +SkipIf=NameMatches:.patch
        ; Or those that require the patches to compile

        [Editor|Programs]
        ; All predicates should be satisfied to skip
        SkipIf=Conjunctions:Root
        ; If the switch is off...
        +SkipIf=IsTruthy:!${{{0}_EDITOR}}
        ; And the input is a patch...
        +SkipIf=NameMatches:.patch
        ; Or those that require the patches to compile
    ".Replace("    ", string.Empty);

    private static void WriteDefaultConfig(string SourcePatchDirectory, string PluginName)
    {
        string ConfigPath = Path.Combine(SourcePatchDirectory, "Crysknife.ini");
        if (File.Exists(ConfigPath)) return;
        File.WriteAllText(ConfigPath, string.Format(ConfigTemplate, PluginName.ToUpper()));
    }

    private static string EngineRoot = string.Empty;
    public static void Init(string RootDirectory)
    {
        EngineRoot = RootDirectory;
    }

    public static void Generate(string PluginName)
    {
        string TargetDirectory = Path.Combine(EngineRoot, "Plugins", PluginName);
        PatchPluginDescription(TargetDirectory, PluginName);
        GenerateSetupScripts(TargetDirectory, PluginName);
        string SourcePatchDirectory = Path.Combine(TargetDirectory, "SourcePatch");
        Utils.EnsureParentDirectoryExists(SourcePatchDirectory);
        WriteDefaultConfig(SourcePatchDirectory, PluginName);
    }
}
