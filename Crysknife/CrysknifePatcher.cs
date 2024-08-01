// SPDX-FileCopyrightText: 2024 Yun Hsiao Wu <yunhsiaow@gmail.com>
// SPDX-License-Identifier: MIT

namespace Crysknife;

internal class Patcher
{
    public class PatchList
    {
        private readonly List<DiffMatchPatch.Patch> Patches;

        public PatchList(List<DiffMatchPatch.Patch> Patches)
        {
            this.Patches = Patches;
        }

        public bool IsValid()
        {
            return Patches.Count > 0;
        }

        public string Serialize(DiffMatchPatch.diff_match_patch Context)
        {
            return Context.patch_toText(Patches);
        }

        public string Apply(DiffMatchPatch.diff_match_patch Context, string Content, out bool[] Success)
        {
            object[] Result = Context.patch_apply(Patches, Content);
            Success = (bool[])Result[1];
            return (string)Result[0];
        }
    }

    private readonly struct EngineVersion
    {
        private readonly int Major;
        private readonly int Minor;

        public static EngineVersion Create(string Value)
        {
            string[] Versions = Value.Split('_');
            return new EngineVersion(int.Parse(Versions[0]), int.Parse(Versions[1]));
        }

        private EngineVersion(int InMajor, int InMinor)
        {
            Major = InMajor;
            Minor = InMinor;
        }

        public override string ToString()
        {
            return $"{Major}_{Minor}";
        }

        public int Distance(EngineVersion Other)
        {
            return Math.Abs(Major - Other.Major) * 100 + Math.Abs(Minor - Other.Minor);
        }
    }

    private readonly struct ParsedPath
    {
        private readonly string PathTrunc;
        private readonly List<string> Extensions = new();

        public ParsedPath(string InputPath)
        {
            string Extension = Path.GetExtension(InputPath);
            while (Extension != string.Empty)
            {
                Extensions.Insert(0, Extension);
                InputPath = InputPath[..^Extension.Length];
                Extension = Path.GetExtension(InputPath);
            }
            PathTrunc = InputPath;
        }

        public void Split(int NumExtensions, out string FilePath, out string Extension)
        {
            int Index = 0;

            FilePath = PathTrunc;
            for (; Index < Extensions.Count - NumExtensions; ++Index)
            {
                FilePath += Extensions[Index];
            }

            Extension = string.Empty;
            for (; Index < Extensions.Count; ++Index)
            {
                Extension += Extensions[Index];
            }
        }

        public bool HasExtension(string Target)
        {
            return Extensions.Contains(Target);
        }
    }

    public readonly string DefaultExtension;
    private readonly EngineVersion CurrentEngineVersion;
    private readonly DiffMatchPatch.diff_match_patch GenerationContext = new() { Patch_Margin = 50 };
    private readonly DiffMatchPatch.diff_match_patch ApplyContext = new()
    {
        Match_Threshold = 0.5f,
        Match_Distance = int.MaxValue // Line number may vary significantly
    };

    public Patcher(ConfigSystem Config)
    {
        var EngineTag = Config.GetEngineTag();
        DefaultExtension = EngineTag != null ? $"{EngineTag}.protected.patch" : ".patch"; // All custom engine patches are protected
        CurrentEngineVersion = EngineVersion.Create(Utils.GetCurrentEngineVersion(Utils.GetSourceDirectory()));
    }

    public void Dump(PatchList Patches, string OutputPath)
    {
    }

    public string Apply(string Before, PatchList Patches, out bool[] Success)
    {
        return Patches.Apply(ApplyContext, Before, out Success);
    }

    public PatchList Generate(string Before, string After)
    {
        return new PatchList(GenerationContext.patch_make(Before, After));
    }

    public bool Save(PatchList Patches, string OutputPath)
    {
        string Content = Patches.Serialize(GenerationContext);
        if (File.Exists(OutputPath) && File.ReadAllText(OutputPath) == Content) return false;

        File.WriteAllText(OutputPath, Content);
        return true;
    }

    public PatchList Load(string InputPath)
    {
        return new PatchList(ApplyContext.patch_fromText(File.ReadAllText(InputPath)));
    }

    public static void Parse(string FullPath, out string FilePath, out string Version)
    {
        ParsedPath ParsedFullPath = new ParsedPath(FullPath);
        ParsedFullPath.Split(ParsedFullPath.HasExtension(".protected") ? 3 : 1, out FilePath, out Version);
    }

    public string Match(string FilePath, List<string> Versions)
    {
        if (Versions.Contains(DefaultExtension)) return DefaultExtension;
        if (Versions.Contains(".patch")) return ".patch";

        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("No valid patch file found for {0}", FilePath);
        Utils.Abort();

        return string.Empty;
    }

    public short PatchContextLength
    {
        get => GenerationContext.Patch_Margin;
        set => GenerationContext.Patch_Margin = value;
    }
    public float MatchContentTolerance
    {
        get => ApplyContext.Match_Threshold;
        set => ApplyContext.Match_Threshold = value;
    }
    public int MatchLineTolerance
    {
        get => ApplyContext.Match_Distance;
        set => ApplyContext.Match_Distance = value;
    }
}
