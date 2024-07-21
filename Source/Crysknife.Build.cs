// SPDX-FileCopyrightText: 2024 Yun Hsiao Wu <yunhsiaow@gmail.com>
// SPDX-License-Identifier: MIT

using UnrealBuildTool;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
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

	private static readonly Regex TruthyRE = new ("^(T|On)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
	private static bool IsTruthyValue(string Value)
	{
		if (int.TryParse(Value, out var Number)) return Number > 0;
		return TruthyRE.IsMatch(Value);
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

		if (Config.TryGetSection("Variables", out var Switches))
		{
			foreach (var Switch in Switches.Lines)
			{
				if (Switch.Key.StartsWith(Prefix, System.StringComparison.OrdinalIgnoreCase))
				{
					string Value = Switch.Value;
					if (NotYetApplied && IsTruthyValue(Value)) Value = "0";
					Definitions.Add($"{Switch.Key}={Value}");
				}
			}
		}
	}
}
