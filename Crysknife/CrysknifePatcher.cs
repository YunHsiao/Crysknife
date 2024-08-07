// SPDX-FileCopyrightText: 2024 Yun Hsiao Wu <yunhsiaow@gmail.com>
// SPDX-License-Identifier: MIT

using System.Collections;
using System.Diagnostics;

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
            MatchThreshold = 0.3f, // Be more strict on matches
            MatchDistance = int.MaxValue, // Line number may vary significantly
        };
        public short PatchContextLength = 250; // ~5 loc
        public InjectionRegex Injection = null!;
        public string CommentTag = string.Empty;
        public string CurrentPatch = string.Empty;

        private void DecoratePatch<T>(ref T Output, T Value, T Expected, string Decorator) where T : IComparable
        {
            if (Output.CompareTo(Expected) != 0 && Output.CompareTo(Value) != 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Conflicting decorator '{0}' in the same patch from '{1}'", Decorator, CurrentPatch);
                Utils.Abort();
            }
            Output = Value;
        }

        private bool GetDecoratorValue(string Key, string Content, out string Value)
        {
            Value = string.Empty;
            int Index = Content.IndexOf('=');
            if (Index < 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("'{0}' declared without a value from '{1}'", Key, CurrentPatch);
                return false;
            }

            Value = Content[(Index + 1)..].Trim();
            return true;
        }

        private PatchBundle HandleDecorators(PatchBundle Patches)
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
                        if (Decorator.StartsWith("IgnoreContext", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!GetDecoratorValue("IgnoreContext", Decorator, out var Target)) continue;
                            if (Enum.TryParse<MatchContext>(Target, out var TargetContext))
                            {
                                Patch.Context &= ~TargetContext;
                            }
                        }
                        else if (Decorator.StartsWith("EngineNewerThan", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!GetDecoratorValue("EngineNewerThan", Decorator, out var Target)) continue;
                            var ShouldSkip = Utils.CurrentEngineVersion.NewerThan(EngineVersion.Create(Target)) ? BooleanOverride.False : BooleanOverride.True;
                            DecoratePatch(ref Patch.Skip, ShouldSkip, BooleanOverride.Unspecified, "EngineNewerThan");
                        }
                        else if (Decorator.StartsWith("EngineOlderThan", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!GetDecoratorValue("EngineOlderThan", Decorator, out var Target)) continue;
                            var ShouldSkip = Utils.CurrentEngineVersion.NewerThan(EngineVersion.Create(Target)) ? BooleanOverride.True : BooleanOverride.False;
                            DecoratePatch(ref Patch.Skip, ShouldSkip, BooleanOverride.Unspecified, "EngineOlderThan");
                        }
                        else if (Decorator.StartsWith("PatchContextLength", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!GetDecoratorValue("PatchContextLength", Decorator, out var Target)) continue;
                            if (int.TryParse(Target, out var Length))
                            {
                                DecoratePatch(ref Patch.ContextLength, Length, -1, "PatchContextLength");
                            }
                        }
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine("Unsupported decorator '{0}' declared in '{1}'", Decorator, CurrentPatch);
                        }
                    }
                }
            }
            return Patches;
        }

        private List<Diff> MakeDiffs(string Before, string After)
        {
            var Diffs = new List<Diff>();
            var Start2 = new List<int>();
            var Sections = new List<Tuple<int, int>>();
            var AllMatches = Injection.Match(After);

            foreach (var MatchVar in AllMatches)
            {
                var Matched = MatchVar;

                while (Matched.Success)
                {
                    int MatchEnd = Matched.Index + Matched.Length;

                    if (Sections.All(S => MatchEnd <= S.Item1 || Matched.Index >= S.Item2))
                    {
                        var Remaining = InjectionRegexForm.Replace(Matched.Groups["Tag"].Value, Matched.Groups["Content"].Value, CommentTag);

                        if (Remaining.Length > 0)
                        {
                            Diffs.Add(new Diff(Operation.Delete, Remaining));
                            Start2.Add(Matched.Index);
                        }
                        Diffs.Add(new Diff(Operation.Insert, Matched.Value));
                        Start2.Add(Matched.Index);

                        Sections.Add(Tuple.Create(Matched.Index, MatchEnd));
                    }
                    Matched = Matched.NextMatch();
                }
            }

            Sections.Sort();
            int CurrentStart = 0;
            foreach (var Section in Sections)
            {
                if (Section.Item1 > CurrentStart)
                {
                    Diffs.Add(new Diff(Operation.Equal, After.Substring(CurrentStart, Section.Item1 - CurrentStart)));
                    Start2.Add(CurrentStart);
                }
                CurrentStart = Section.Item2;
            }

            if (CurrentStart < After.Length)
            {
                Diffs.Add(new Diff(Operation.Equal, After.Substring(CurrentStart, After.Length - CurrentStart)));
                Start2.Add(CurrentStart);
            }

            var Indices = Enumerable.Range(0, Start2.Count).ToList();
            Indices.Sort((A, B) =>
            {
                if (Start2[A] != Start2[B]) return Start2[A] - Start2[B];
                if (Diffs[A].Operation == Operation.Delete) return -1;
                return Diffs[B].Operation == Operation.Delete ? 1 : 0;
            });
            Diffs = Diffs.Select((_, Index) => Diffs[Indices[Index]]).ToList();

            Debug.Assert(DiffMatchPatch.diff_text1(Diffs).Equals(Before, StringComparison.Ordinal));
            Debug.Assert(DiffMatchPatch.diff_text2(Diffs).Equals(After, StringComparison.Ordinal));
            return Diffs;
        }

        public static string Serialize(PatchBundle Patches)
        {
            return DiffMatchPatch.patch_toText(Patches.Patches);
        }

        public PatchBundle Deserialize(string Content)
        {
            return HandleDecorators(new PatchBundle(DiffMatchPatch.patch_fromText(Content)));
        }

        public bool Apply(PatchBundle Patches, string Content, string DumpPath, bool ForceDump, out string Patched)
        {
            var (Output, Success, Indices) = Context.patch_apply(Patches.Patches, Content);

            int FailureCount = 0;
            var Record = new BitArray(Patches.Patches.Count);
            for (int Index = 0; Index < Success.Length; ++Index)
            {
                if (Success[Index]) continue;
                FailureCount++;

                var MappedIndex = Indices[Index];
                if (Record[MappedIndex]) continue;
                Record[MappedIndex] = true;
                var OutputPath = $"{DumpPath}.{MappedIndex}.html";

                Utils.EnsureParentDirectoryExists(OutputPath);
                File.WriteAllText(OutputPath, DiffMatchPatch.diff_prettyHtml(Patches.Patches[MappedIndex].Diffs));
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine("Error: Patch failed: Please merge the relevant changes manually from '{0}'", OutputPath);
            }

            if (ForceDump)
            {
                var OutputPath = $"{DumpPath}.html";
                Utils.EnsureParentDirectoryExists(OutputPath);
                var Diffs = Patches.Patches.Aggregate(new List<Diff>(), (Acc, Cur) =>
                {
                    if (Acc.Count > 0) Acc.Add(new Diff(Operation.Equal, new string('=', 120)));
                    Acc.AddRange(Cur.Diffs);
                    return Acc;
                });
                File.WriteAllText(OutputPath, DiffMatchPatch.diff_prettyHtml(Diffs));
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Error.WriteLine("Dumped: '{0}'", OutputPath);
            }

            Patched = Output;
            return FailureCount < Success.Length; // Success if any patch is applied
        }

        public PatchBundle Diff(string Before, string After)
        {
            // Adjust the margin temporarily to get longer context
            // patch_apply need this to be within MatchMaxBits
            var OldValue = Context.PatchMargin;
            Context.PatchMargin = PatchContextLength;

            // var Diffs = Context.diff_main(Before, After);
            var Diffs = MakeDiffs(Before, After);
            var Patches = Context.patch_make(Before, Diffs);
            var Result = HandleDecorators(new PatchBundle(Patches));

            Context.PatchMargin = OldValue;
            return Result;
        }

        public static PatchBundle Merge(PatchBundle History, PatchBundle New, bool KeepAllHistory)
        {
            if (KeepAllHistory)
            {
                var FilteredHistory = new PatchBundle(History.Patches.Where(Patch => Patch.Skip != BooleanOverride.False).ToList());
                FilteredHistory.Patches.AddRange(New.Patches.Where(Patch => Patch.Skip == BooleanOverride.False));
                FilteredHistory.Patches.Sort((A, B) => A.Start1 - B.Start1);
                return FilteredHistory;
            }

            New.Patches.AddRange(History.Patches.Where(Patch => Patch.Skip == BooleanOverride.True));
            New.Patches.Sort((A, B) => A.Start1 - B.Start1);
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

    public bool Apply(IPatchBundle Patches, string Before, string DumpPath, bool ForceDump, out string Patched)
    {
        return Context.Apply((DMPContext.PatchBundle)Patches, Before, DumpPath, ForceDump, out Patched);
    }

    public IPatchBundle Generate(string Before, string After)
    {
        return Context.Diff(Before, After);
    }

    public IPatchBundle Generate(string Before, string After, bool KeepAllHistory)
    {
        return DMPContext.Merge((DMPContext.PatchBundle)Load(), (DMPContext.PatchBundle)Generate(Before, After), KeepAllHistory);
    }

    public bool Save(IPatchBundle Patches)
    {
        string PatchPath = CurrentPatch + DefaultExtension;

        string Content = DMPContext.Serialize((DMPContext.PatchBundle)Patches);
        if (File.Exists(PatchPath) && File.ReadAllText(PatchPath) == Content) return false;

        File.WriteAllText(PatchPath, Content);
        return true;
    }

    public IPatchBundle Load()
    {
        string PatchPath = CurrentPatch + DefaultExtension;
        if (!File.Exists(PatchPath)) PatchPath = CurrentPatch + Extensions[(int)PatchFileType.Main];
        return Context.Deserialize(File.ReadAllText(PatchPath));
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

    public InjectionRegex Injection
    {
        get => Context.Injection;
        set => Context.Injection = value;
    }
    public string CommentTag
    {
        get => Context.CommentTag;
        set => Context.CommentTag = value;
    }
    public string CurrentPatch
    {
        get => Context.CurrentPatch;
        set => Context.CurrentPatch = value;
    }
}
