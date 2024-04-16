// SPDX-FileCopyrightText: 2024 Yun Hsiao Wu <yunhsiaow@gmail.com>
// SPDX-License-Identifier: MIT

using System.Text.Json;
using System.Text.Json.Nodes;

namespace Crysknife;

public static class ProjectSetup
{
    private static readonly string WindowsTemplate = @"
        @echo off
        ""%~dp0..\Crysknife\Crysknife.bat"" -p {0} %*
        pause
    ".Replace("    ", string.Empty);

    private static readonly string LinuxTemplate = @"
        DIR=`cd ""$(dirname ""$0"")""; pwd`
        ""$DIR/../Crysknife/Crysknife.sh"" -p {0} ""$@""
    ".Replace("    ", string.Empty).Replace("\r\n", "\n");

    private static void GenerateSetupScripts(string TargetDirectory, string ProjectName)
    {
        File.WriteAllText(Path.Combine(TargetDirectory, "Setup.bat"), string.Format(WindowsTemplate, ProjectName));
        File.WriteAllText(Path.Combine(TargetDirectory, "Setup.sh"), string.Format(LinuxTemplate, ProjectName));
        File.WriteAllText(Path.Combine(TargetDirectory, "Setup.command"), string.Format(LinuxTemplate, ProjectName));
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

    private static void PatchPluginDescription(string TargetDirectory, string ProjectName)
    {
        string PluginDescFile = Path.Combine(TargetDirectory, ProjectName + ".uplugin");
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

    public static void Generate(string RootDirectory, string ProjectName)
    {
        string TargetDirectory = Path.Combine(RootDirectory, ProjectName);
        Directory.CreateDirectory(Path.Combine(TargetDirectory, "SourcePatch"));
        GenerateSetupScripts(TargetDirectory, ProjectName);
        PatchPluginDescription(TargetDirectory, ProjectName);
    }
}
