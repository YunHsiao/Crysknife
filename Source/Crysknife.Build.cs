// SPDX-FileCopyrightText: 2024 Yun Hsiao Wu <yunhsiaow@gmail.com>
// SPDX-License-Identifier: MIT

using UnrealBuildTool;
using System.Collections.Generic;
using System.IO;
using EpicGames.Core;

public class Crysknife : ModuleRules
{
	public Crysknife(ReadOnlyTargetRules Target) : base(Target)
	{
		PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;
		
		PrivateDependencyModuleNames.AddRange(new[]
		{
			"Core"
		});
	}

	public static void FillInConfigVariables(List<string> Definitions, string TargetDirectory, string Prefix)
	{
		var Config = new ConfigFile(new FileReference(Path.Combine(TargetDirectory, "SourcePatch", "Crysknife.ini")));

		if (Config.TryGetSection("Variables", out var Switches))
		{
			foreach (var Switch in Switches.Lines)
			{
				if (Switch.Key.StartsWith(Prefix, System.StringComparison.OrdinalIgnoreCase))
				{
					Definitions.Add($"{Switch.Key}={Switch.Value}");
				}
			}
		}
	}
}
