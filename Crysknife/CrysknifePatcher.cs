// SPDX-FileCopyrightText: 2024 Yun Hsiao Wu <yunhsiaow@gmail.com>
// SPDX-License-Identifier: MIT

using System.Collections;
using System.Diagnostics;

namespace Crysknife;
using DiffMatchPatch;

internal interface IPatchBundle
{
    bool IsValid();
    bool HasAnyActivePatch();
}

internal class Patcher
{
    private class DmpContext
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

            public bool HasAnyActivePatch()
            {
                return Patches.Any(P => P.Skip != BooleanOverride.True);
            }
        }

        private readonly DiffMatchPatch Context = new()
        {
            // Be more strict on matches
            PatchDeleteThreshold = 0.3f,
            MatchThreshold = 0.3f,
            // Line number may vary significantly
            MatchDistance = int.MaxValue
        };
        public short PatchContextLength = 250; // ~5 loc
        public readonly List<CommentTagPacker> Packers = new();

        public InjectionRegex Injection = null!;
        public IReadOnlyDictionary<string, string> Variables = null!;
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
            var Index = Content.IndexOf('=');
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
                foreach (var Diff in Patch.Diffs.Where(Diff => Diff.Operation == Operation.Insert))
                {
                    var Decorators = Utils.GetInjectionDecorators(Diff.Text);
                    if (Decorators.Length == 0) continue;

                    foreach (var Decorator in Decorators.Split(',', Utils.SplitOptions))
                    {
                        if (Decorator.StartsWith("MatchContext", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!GetDecoratorValue("MatchContext", Decorator, out var Target)) continue;
                            if (Enum.TryParse<MatchContext>(Target, out var TargetContext))
                            {
                                Patch.Context &= TargetContext;
                            }
                        }
                        else if (Decorator.StartsWith("MatchLength", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!GetDecoratorValue("MatchLength", Decorator, out var Target)) continue;
                            if (int.TryParse(Target, out var Length))
                            {
                                DecoratePatch(ref Patch.ContextLength, Length, -1, "MatchLength");
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
            if (AllMatches.Count == 0) return Diffs;

            foreach (var MatchVar in AllMatches)
            {
                var Matched = MatchVar;

                while (Matched.Success)
                {
                    var MatchEnd = Matched.Index + Matched.Length;

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
            var CurrentStart = 0;
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

        private PatchBundle Pack(PatchBundle Patches, bool SkipCaptures)
        {
            var Results = new PatchBundle(DiffMatchPatch.patch_deepCopy(Patches.Patches));
            var TotalIncrement = 0;

            foreach (var Patch in Results.Patches)
            {
                var Increment = 0;

                foreach (var Diff in Patch.Diffs)
                {
                    Diff.Text = Packers.Aggregate(Diff.Text, (Current, Packer) => Packer.Pack(Current, ref Increment, SkipCaptures));
                }

                Patch.Start2 += TotalIncrement;
                Patch.Length2 += Increment;
                TotalIncrement += Increment;
            }

            return Results;
        }

        private PatchBundle Unpack(PatchBundle Patches)
        {
            var TotalIncrement = 0;

            foreach (var Patch in Patches.Patches)
            {
                var Increment = 0;

                foreach (var Diff in Patch.Diffs)
                {
                    Diff.Text = Packers.Aggregate(Diff.Text, (Current, Packer) => Packer.Unpack(Current, ref Increment, Variables));
                }

                Patch.Start2 += TotalIncrement;
                Patch.Length2 += Increment;
                TotalIncrement += Increment;
            }

            return Patches;
        }

        public string Serialize(PatchBundle Patches, bool SkipCapture)
        {
            return DiffMatchPatch.patch_toText(Pack(Patches, SkipCapture).Patches);
        }

        public PatchBundle Deserialize(string Content)
        {
            var Patches = new PatchBundle(DiffMatchPatch.patch_fromText(Content));
            return HandleDecorators(Unpack(Patches));
        }

        public bool Apply(PatchBundle Patches, string Content, string DumpPath, bool ForceDump, out string Patched)
        {
            var Result = Context.patch_apply(Patches.Patches, Content, true);

            var FailureCount = 0;
            var Record = new BitArray(Patches.Patches.Count);
            foreach (var Index in Enumerable.Range(0, Result.Locations.Count).Where(Index => Result.Locations[Index] < 0))
            {
                FailureCount++;

                var MappedIndex = Result.Indices[Index];
                if (Record[MappedIndex]) continue;
                Record[MappedIndex] = true;
                var OutputPath = $"{DumpPath}.{MappedIndex}.html";

                Utils.EnsureParentDirectoryExists(OutputPath);
                var Diffs = new List<Diff>(Result.Patches[Index].Diffs)
                {
                    new (Operation.Equal, new string('=', 50) + " ↓↓↓ COMPLETE CONTEXT ↓↓↓ " + new string('=', 50))
                };
                Diffs.AddRange(Patches.Patches[MappedIndex].Diffs);

                File.WriteAllText(OutputPath, DiffMatchPatch.diff_prettyHtml(Diffs));
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine("Error: Patch failed: Please merge the relevant changes manually from '{0}'", OutputPath);
            }

            if (ForceDump || FailureCount > 0)
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
            }

            Patched = Result.Text;
            return FailureCount < Result.Locations.Count; // Success if any patch is applied
        }

        public PatchBundle Diff(string Before, string After)
        {
            // var Diffs = Context.diff_main(Before, After);
            var Diffs = MakeDiffs(Before, After);

            // Only adjust the margin temporarily to get a longer context
            // patch_apply still need this to be within MatchMaxBits
            var OldValue = Context.PatchMargin;
            Context.PatchMargin = PatchContextLength;
            // Force split patches on every insertion so the decorators could apply accordingly
            var Patches = Context.patch_make(Before, Diffs, true);
            Context.PatchMargin = OldValue;

            var Result = HandleDecorators(new PatchBundle(Patches));
            return Result;
        }

        public PatchBundle Merge(PatchBundle History, PatchBundle New, string Text, IncrementalMode Incremental)
        {
            if (Incremental == IncrementalMode.Disabled)
            {
                New.Patches.AddRange(History.Patches.Where(Patch => Patch.Skip == BooleanOverride.True));
                // Techniquely we shouldn't sort at all because of DMP's rolling context
                // But for our purposes this should be fine
                New.Patches.Sort((A, B) => A.Start1 - B.Start1);
                return New;
            }

            // Discard the new one if associated history is found
            var Preserved = new HashSet<int>();
            var DiscardedNew = new HashSet<int>();

            // Should always compare the packed distance, without captures
            var PackedNew = Pack(New, true);
            var PackedHistory = Pack(History, true);
            var Result = Context.patch_apply(History.Patches, Text, true);
            var HistoryRecord = new Dictionary<int, List<int>>();
            var NewRecord = new Dictionary<int, List<int>>();
            foreach (var HistoryIndex in Enumerable.Range(0, PackedHistory.Patches.Count))
            {
                var HistoryPatch = PackedHistory.Patches[HistoryIndex];

                if (HistoryPatch.Skip != BooleanOverride.Unspecified)
                {
                    // Always preserve for different engine versions
                    if (HistoryPatch.Skip == BooleanOverride.True)
                    {
                        Preserved.Add(HistoryIndex);
                        continue;
                    }
                    // Discard for this engine version if needed
                    if (Incremental == IncrementalMode.Enabled) continue;
                }

                // Always discard if invalid
                if (Result.Locations.Where((_, Index) => Result.Indices[Index] == HistoryIndex)
                    .Any(Loc => Loc < 0)) continue;

                var RelevantIndices = GetRelevantPatches(HistoryIndex);
                HistoryRecord.Add(HistoryIndex, RelevantIndices);
                foreach (var Index in RelevantIndices)
                {
                    if (!NewRecord.ContainsKey(Index)) NewRecord.Add(Index, new List<int>());
                    NewRecord[Index].Add(HistoryIndex);
                }
            }

            foreach (var Pair in HistoryRecord)
            {
                var Patch = PackedHistory.Patches[Pair.Key];
                if (!InsertionEquals(Patch, PackedNew.Patches, Pair.Value)) continue;
                Preserved.Add(Pair.Key);
            }

            foreach (var Pair in NewRecord)
            {
                var Patch = PackedNew.Patches[Pair.Key];
                if (!InsertionEquals(Patch, PackedHistory.Patches, Pair.Value)) continue;
                DiscardedNew.Add(Pair.Key);
            }

            var FilteredHistory = new PatchBundle(History.Patches.Where((_, Index) => Preserved.Contains(Index)).ToList());
            FilteredHistory.Patches.AddRange(New.Patches.Where((_, Index) => !DiscardedNew.Contains(Index)));
            FilteredHistory.Patches.Sort((A, B) => A.Start1 - B.Start1);
            return FilteredHistory;

            List<int> GetRelevantPatches(int HistoryIndex)
            {
                var RelevantPatches = new List<int>();

                foreach (var NewIndex in Enumerable.Range(0, New.Patches.Count))
                {
                    var NewPatch = New.Patches[NewIndex];
                    var ValidStart = NewPatch.Start2 + NewPatch.Diffs.First().Text.Length - DiffMatchPatch.MatchMaxBits;
                    var ValidEnd = NewPatch.Start2 + NewPatch.Length2 - NewPatch.Diffs.Last().Text.Length + DiffMatchPatch.MatchMaxBits;

                    if (Enumerable.Range(0, Result.Indices.Count)
                        .Where(Index => Result.Indices[Index] == HistoryIndex)
                        .All(ResultIndex =>
                        {
                            var Location = Result.Locations[ResultIndex];
                            return Location >= ValidStart && Location < ValidEnd;
                        }))
                    {
                        RelevantPatches.Add(NewIndex);
                    }
                }

                return RelevantPatches;
            }

            bool InsertionEquals(Patch Patch, IReadOnlyList<Patch> AllPatches, IReadOnlyList<int> RelevantIndices)
            {
                var Record = new HashSet<int>();
                return Patch.Diffs
                    .Where(Diff => Diff.Operation == Operation.Insert)
                    .All(Target => RelevantIndices.Any(PatchIndex =>
                    {
                        var Diffs = AllPatches[PatchIndex].Diffs;
                        return Enumerable.Range(0, Diffs.Count)
                            .Where(DiffIndex => Diffs[DiffIndex].Operation == Operation.Insert)
                            .Any(DiffIndex =>
                            {
                                int Hash = (PatchIndex << 16) | DiffIndex;
                                if (Record.Contains(Hash)) return false;

                                var LocalDiffs = Context.diff_main(Target.Text.Trim(), Diffs[DiffIndex].Text.Trim());
                                var Distance = DiffMatchPatch.diff_levenshtein(LocalDiffs);
                                if (Distance >= 3) return false;

                                Record.Add(Hash);
                                return true;
                            });
                    }));
            }
        }

        public float MatchContentTolerance
        {
            get => Context.MatchThreshold;
            set => Context.PatchDeleteThreshold = Context.MatchThreshold = value;
        }
        public int MatchLineTolerance
        {
            get => Context.MatchDistance;
            set => Context.MatchDistance = value;
        }
    }

    private readonly DmpContext Context = new();
    public readonly string DefaultExtension;
    public IncrementalMode Incremental = IncrementalMode.Disabled;

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

    public Patcher(bool Protected)
    {
        DefaultExtension = Protected ? Extensions[(int)PatchFileType.Protected] : Extensions[(int)PatchFileType.Main]; // All custom engine patches are protected
    }

    public bool Apply(IPatchBundle Patches, string Before, string DumpPath, bool ForceDump, out string Patched)
    {
        return Context.Apply((DmpContext.PatchBundle)Patches, Before, DumpPath, ForceDump, out Patched);
    }

    public IPatchBundle Generate(string Before, string After)
    {
        return Context.Merge((DmpContext.PatchBundle)Load(), Context.Diff(Before, After), Before, Incremental);
    }

    public bool Save(IPatchBundle Patches, bool ShouldSave = true)
    {
        var PatchPath = CurrentPatch + DefaultExtension;

        var Content = Context.Serialize((DmpContext.PatchBundle)Patches, DefaultExtension == Extensions[(int)PatchFileType.Main]);

        var Differs = !File.Exists(PatchPath) || File.ReadAllText(PatchPath) != Content;
        if (Differs && ShouldSave) File.WriteAllText(PatchPath, Content);
        return Differs;
    }

    public IPatchBundle Load()
    {
        var PatchPath = CurrentPatch + DefaultExtension;
        if (!File.Exists(PatchPath)) PatchPath = CurrentPatch + Extensions[(int)PatchFileType.Main];
        return Context.Deserialize(File.ReadAllText(PatchPath));
    }

    public static string GetSourcePath(string FullPath)
    {
        foreach (var Extension in Extensions)
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

    public List<CommentTagPacker> Packers => Context.Packers;

    public IReadOnlyDictionary<string, string> Variables
    {
        get => Context.Variables;
        set => Context.Variables = value;
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
