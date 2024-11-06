// SPDX-FileCopyrightText: 2024 Yun Hsiao Wu <yunhsiaow@gmail.com>
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

using UnrealBuildTool;
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
		int Number;
		if (int.TryParse(Value, out Number)) return Number > 0;
		return Value.StartsWith("T", StringComparison.OrdinalIgnoreCase) || Value.Equals("On", StringComparison.OrdinalIgnoreCase);
	}

	private static void FillInConfigVariables(IDictionary<string, string> OutVariables, string ConfigPath, bool NotYetApplied = false)
	{
		var Config = new ConfigFile(new FileReference(ConfigPath));

		ConfigFileSection VariableSection;
		if (!Config.TryGetSection("Variables", out VariableSection)) return;

		foreach (var Variable in VariableSection.Lines)
		{
			string Value = Variable.Value;
			if (NotYetApplied && IsTruthyValue(Value)) Value = "0";
			OutVariables[Variable.Key] = Value;
		}
	}

	private static string GetLocalSuffix(string TargetDirectory)
	{
		var RootDirectory = TargetDirectory.Substring(0, TargetDirectory.LastIndexOf("Plugins" + Path.DirectorySeparatorChar, StringComparison.Ordinal) + 7);
		var ConfigPath = Path.Combine(RootDirectory, "CrysknifeCache.ini");
		if (!File.Exists(ConfigPath)) return string.Empty;

		var Config = new ConfigFile(new FileReference(ConfigPath));
		ConfigFileSection VariableSection;
		if (!Config.TryGetSection("Variables", out VariableSection)) return string.Empty;

		foreach (var Variable in VariableSection.Lines.Where(Variable => Variable.Key == "CRYSKNIFE_LOCAL_CONFIG"))
		{
			return Variable.Value;
		}
		return string.Empty;
	}

	public static void FillInConfigVariables(List<string> Definitions, string TargetDirectory, string Prefix)
	{
		var Variables = new Dictionary<string, string>();
		var LocalSuffix = GetLocalSuffix(TargetDirectory);
		var Configs = new []
		{
			new Tuple<string, bool>("Crysknife.ini", true),
			new Tuple<string, bool>(string.Format("Crysknife{0}Local.ini", LocalSuffix), false),
			new Tuple<string, bool>(string.Format("Crysknife{0}Cache.ini", LocalSuffix), false),
		};

		foreach (var Pair in Configs)
		{
			string ConfigPath = Path.Combine(TargetDirectory, "SourcePatch", Pair.Item1);
			if (File.Exists(ConfigPath)) FillInConfigVariables(Variables, ConfigPath, Pair.Item2);
		}

		Definitions.AddRange(from Pair in Variables where Pair.Key.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) select string.Format("{0}={1}", Pair.Key, Pair.Value));
	}
}
