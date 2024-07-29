// SPDX-FileCopyrightText: 2024 Yun Hsiao Wu <yunhsiaow@gmail.com>
// SPDX-License-Identifier: MIT

using UnrealBuildTool;
using System.Collections.Generic;
using System.IO;

#if UE_5_0_OR_LATER
using EpicGames.Core;
#else
using Tools.DotNETCommon;
#endif

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

	private static bool IsTruthyValue(string Value)
	{
		int Number = 0;
		if (int.TryParse(Value, out Number)) return Number > 0;
		return Value.StartsWith("T", System.StringComparison.OrdinalIgnoreCase) || Value.Equals("On", System.StringComparison.OrdinalIgnoreCase);
	}

	public static void FillInConfigVariables(List<string> Definitions, string TargetDirectory, string Prefix)
	{
		bool NotYetApplied = false;
		string ConfigPath = Path.Combine(TargetDirectory, "SourcePatch", "CrysknifeCache.ini");
		if (!File.Exists(ConfigPath))
		{
			ConfigPath = Path.Combine(TargetDirectory, "SourcePatch", "Crysknife.ini");
			NotYetApplied = true;
		}
		var Config = new ConfigFile(new FileReference(ConfigPath));

		ConfigFileSection Switches;
		if (Config.TryGetSection("Variables", out Switches))
		{
			foreach (var Switch in Switches.Lines)
			{
				if (Switch.Key.StartsWith(Prefix, System.StringComparison.OrdinalIgnoreCase))
				{
					string Value = Switch.Value;
					if (NotYetApplied && IsTruthyValue(Value)) Value = "0";
					Definitions.Add(string.Format("{0}={1}", Switch.Key, Value));
				}
			}
		}
	}
}
