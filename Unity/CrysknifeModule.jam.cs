// SPDX-FileCopyrightText: 2024 Yun Hsiao Wu <yunhsiaow@gmail.com>
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Linq;
using Bee.Core;
using NiceIO;
using Unity.BuildSystem;

namespace Modules.Crysknife
{
    sealed class CrysknifeModule : Module
    {
        // Ignore everything by default
        protected override NPath[] PathsToIgnoreWhileGlobbing => new [] { Directory };

        public CrysknifeModule()
        {
            Cpp.Add(Directory.Combine("Unity").Files("*.cpp"));
        }

        private static bool SuffixCached;
        private static string CachedSuffix;

        public static string GetLocalSuffix()
        {
            if (SuffixCached)
                return CachedSuffix;

            var ConfigPath = new NPath("Modules/CrysknifeCache.ini");
            if (!ConfigPath.Exists()) return string.Empty;

            var Config = new ConfigFile(ConfigPath);
            if (!Config.TryGetSection("Variables", out var VariableSection)) return string.Empty;

            foreach (var Variable in VariableSection.Lines.Where(Variable => Variable.Key == "CRYSKNIFE_LOCAL_CONFIG"))
            {
                CachedSuffix = Variable.Value;
            }

            SuffixCached = true;
            return CachedSuffix;
        }

        private static bool IsTruthyValue(string Value)
        {
            if (int.TryParse(Value, out var Number)) return Number > 0;
            return Value!.StartsWith("T", StringComparison.OrdinalIgnoreCase) || Value.Equals("On", StringComparison.OrdinalIgnoreCase);
        }

        private static void FillInConfigVariables(IDictionary<string, string> OutVariables, NPath ConfigPath, bool NotYetApplied = false)
        {
            var Config = new ConfigFile(ConfigPath);

            if (!Config.TryGetSection("Variables", out var VariableSection)) return;

            foreach (var Variable in VariableSection.Lines)
            {
                string Value = Variable.Value;
                if (NotYetApplied && IsTruthyValue(Value)) Value = "0";
                OutVariables[Variable.Key] = Value;
            }
        }

        public static void FillInConfigVariables(CollectionWithConditions<string, UnityPlayerConfiguration> Definitions, NPath TargetPath, string Prefix)
        {
            var LocalSuffix = GetLocalSuffix();
            var Configs = new []
            {
                new Tuple<string, bool>("Crysknife.ini", true),
                new Tuple<string, bool>($"Crysknife{LocalSuffix}Local.ini", false),
                new Tuple<string, bool>($"Crysknife{LocalSuffix}Cache.ini", false),
            };

            var Variables = new Dictionary<string, string>();
            foreach (var Pair in Configs)
            {
                var ConfigPath = TargetPath.Combine("SourcePatch", Pair.Item1);
                if (ConfigPath.Exists()) FillInConfigVariables(Variables, ConfigPath, Pair.Item2);
            }

            Definitions.Add(from Pair in Variables where Pair.Key.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) select $"{Pair.Key}={Pair.Value}");
        }
    }
}
