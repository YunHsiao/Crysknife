// SPDX-FileCopyrightText: 2024 Yun Hsiao Wu <yunhsiaow@gmail.com>
// SPDX-License-Identifier: MIT

namespace Crysknife;
using DiffMatchPatch;

internal interface IPatchBundle
{
    bool IsValid();
}

internal class Patcher
{
    private class DMPContext
    {
        public class PatchBundle : IPatchBundle
        {
            public readonly List<Patch> Patches;
            public PatchBundle(List<Patch> Patches)
            {
                this.Patches = Patches;
            }

            public bool IsValid()
            {
                return Patches.Count > 0;
            }
        }

        private readonly DiffMatchPatch Context = new()
        {
            MatchDistance = int.MaxValue, // Line number may vary significantly
        };
        public short PatchContextLength = 500; // ~10 loc

        private static PatchBundle HandleDecorators(PatchBundle Patches, string CommentTag)
        {
            foreach (var Patch in Patches.Patches)
            {
                foreach (var Diff in Patch.Diffs)
                {
                    if (Diff.Operation != Operation.Insert) continue;
                    string Decorators = Utils.GetInjectionDecorators(Diff.Text, CommentTag);
                    if (Decorators.Length == 0) continue;

                    foreach (var Decorator in Decorators.Split(',', Utils.SplitOptions))
                    {
                        if (Decorator.Equals("UpperContextOnly", StringComparison.OrdinalIgnoreCase))
                        {
                            Patch.Context &= ~MatchContext.Lower;
                        }
                        else if (Decorator.Equals("LowerContextOnly", StringComparison.OrdinalIgnoreCase))
                        {
                            Patch.Context &= ~MatchContext.Upper;
                        }
                    }
                }
            }
            return Patches;
        }

        public static string Serialize(PatchBundle Patches)
        {
            return DiffMatchPatch.patch_toText(Patches.Patches);
        }

        public static PatchBundle Deserialize(string Content)
        {
            return new PatchBundle(DiffMatchPatch.patch_fromText(Content));
        }

        public bool Apply(PatchBundle Patches, string Content, string CommentTag, string DumpPath, out string Patched)
        {
            var (Output, Success, Indices) = Context.patch_apply(HandleDecorators(Patches, CommentTag).Patches, Content);
            Patched = Output;

            for (int Index = 0; Index < Success.Length; ++Index)
            {
                if (Success[Index]) continue;

                var MappedIndex = Indices[Index]; 
                var OutputPath = $"{DumpPath}.{MappedIndex}.html";
                if (File.Exists(OutputPath)) continue;

                Utils.EnsureParentDirectoryExists(OutputPath);
                File.WriteAllText(OutputPath, DiffMatchPatch.diff_prettyHtml(Patches.Patches[MappedIndex].Diffs));
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine("Error: Patch failed: Please merge the relevant changes manually from '{0}'", OutputPath);
            }

            return Success.Any(V => V); // Success if any patch is applied
        }

        public PatchBundle Diff(string Before, string After)
        {
            // Adjust the margin temporarily to get longer context
            // patch_apply need this to be within MatchMaxBits
            var OldValue = Context.PatchMargin;
            Context.PatchMargin = PatchContextLength;

            var Result = new PatchBundle(Context.patch_make(Before, After));

            Context.PatchMargin = OldValue;
            return Result;
        }

        public PatchBundle Merge(PatchBundle _, PatchBundle New)
        {
            return New;
        }

        public float MatchContentTolerance
        {
            get => Context.MatchThreshold;
            set => Context.MatchThreshold = value;
        }
        public int MatchLineTolerance
        {
            get => Context.MatchDistance;
            set => Context.MatchDistance = value;
        }
    }

    private readonly DMPContext Context = new();
    public readonly string DefaultExtension;

    private enum PatchFileType
    {
        Protected,
        Main
    }
    public static readonly string[] Extensions =
    {
        ".protected.patch",
        ".patch",
    };

    public Patcher(ConfigSystem Config)
    {
        DefaultExtension = Config.GetEngineTag().Length > 0 ? Extensions[(int)PatchFileType.Protected] : Extensions[(int)PatchFileType.Main]; // All custom engine patches are protected
    }

    public bool Apply(IPatchBundle Patches, string Before, string CommentTag, string DumpPath, out string Patched)
    {
        return Context.Apply((DMPContext.PatchBundle)Patches, Before, CommentTag, DumpPath, out Patched);
    }

    public IPatchBundle Generate(string Before, string After, string? InputPath)
    {
        var NewPatches = Context.Diff(Before, After);
        if (InputPath == null) return NewPatches;

        string PatchPath = InputPath + DefaultExtension;
        var HistoryPatches = DMPContext.Deserialize(File.Exists(PatchPath) ? File.ReadAllText(PatchPath) : string.Empty);
        return Context.Merge(HistoryPatches, NewPatches);
    }

    public bool Save(IPatchBundle Patches, string OutputPath)
    {
        string PatchPath = OutputPath + DefaultExtension;

        string Content = DMPContext.Serialize((DMPContext.PatchBundle)Patches);
        if (File.Exists(PatchPath) && File.ReadAllText(PatchPath) == Content) return false;

        File.WriteAllText(PatchPath, Content);
        return true;
    }

    public IPatchBundle Load(string InputPath)
    {
        string PatchPath = InputPath + DefaultExtension;
        if (!File.Exists(PatchPath)) PatchPath = InputPath + Extensions[(int)PatchFileType.Main];
        return DMPContext.Deserialize(File.ReadAllText(PatchPath));
    }

    public static string GetSourcePath(string FullPath)
    {
        foreach (string Extension in Extensions)
        {
            if (FullPath.EndsWith(Extension)) return FullPath[..^Extension.Length];
        }
        return FullPath;
    }

    public short PatchContextLength
    {
        get => Context.PatchContextLength;
        set => Context.PatchContextLength = value;
    }
    public float MatchContentTolerance
    {
        get => Context.MatchContentTolerance;
        set => Context.MatchContentTolerance = value;
    }
    public int MatchLineTolerance
    {
        get => Context.MatchLineTolerance;
        set => Context.MatchLineTolerance = value;
    }
}
