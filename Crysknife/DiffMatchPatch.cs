// SPDX-FileCopyrightText: 2018 The diff-match-patch Authors <https://github.com/google/diff-match-patch>
// SPDX-License-Identifier: Apache-2.0

/*
 * Diff Match and Patch
 * Copyright 2018 The diff-match-patch Authors.
 * https://github.com/google/diff-match-patch
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

/*
 * The following changes are made for Crysknife usages:
 *   Support constrained match with specified context range
 *   Use 64-bit mask for the bitap matching algorithm
 *   `patch_apply` returns additional insights into the patching process 
 *   Misc format & semantic improvements for .Net 6
 *   Use Preformatted text element in HTML output
 */

// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable FieldCanBeMadeReadOnly.Global

using System.Text;
using System.Text.RegularExpressions;
using System.Web;

// ReSharper disable once CheckNamespace
namespace DiffMatchPatch;

internal static class CompatibilityExtensions
{
    // JScript splice function
    public static List<T> Splice<T>(this List<T> Input, int Start, int Count, params T[] Objects)
    {
        var DeletedRange = Input.GetRange(Start, Count);
        Input.RemoveRange(Start, Count);
        Input.InsertRange(Start, Objects);

        return DeletedRange;
    }

    // Java substring function
    public static string JavaSubstring(this string S, int Begin, int End)
    {
        return S.Substring(Begin, End - Begin);
    }
}

/**-
 * The data structure representing a diff is a List of Diff objects:
 * {Diff(Operation.DELETE, "Hello"), Diff(Operation.INSERT, "Goodbye"),
 *  Diff(Operation.EQUAL, " world.")}
 * which means: delete "Hello", add "Goodbye" and keep " world."
 */
internal enum Operation
{
    Delete,
    Insert,
    Equal
}

[Flags]
internal enum MatchContext
{
    Upper = 0x1,
    Lower = 0x2,
    All = Upper | Lower
}

internal enum BooleanOverride
{
    Unspecified,
    False,
    True
}

/**
 * Class representing one diff operation.
 */
internal class Diff
{
    public Operation Operation;

    // One of: INSERT, DELETE or EQUAL.
    public string Text;
    // The text associated with this diff operation.

    /**
     * Constructor.  Initializes the diff with the provided values.
     * @param operation One of INSERT, DELETE or EQUAL.
     * @param text The text being applied.
     */
    public Diff(Operation Operation, string Text)
    {
        // Construct a diff with the specified operation and text.
        this.Operation = Operation;
        this.Text = Text;
    }

    /**
     * Display a human-readable version of this Diff.
     * @return text version.
     */
    public override string ToString()
    {
        var PrettyText = Text.Replace('\n', '\u00b6');
        return "Diff(" + Operation + ",\"" + PrettyText + "\")";
    }

    /**
     * Is this Diff equivalent to another Diff?
     * @param d Another Diff to compare against.
     * @return true or false.
     */
    public override bool Equals(object? Obj)
    {
        // If parameter cannot be cast to Diff return false.
        if (Obj is not Diff P)
        {
            return false;
        }

        // Return true if the fields match.
        return P.Operation == Operation && P.Text == Text;
    }

    public bool Equals(Diff? Obj)
    {
        // If parameter is null return false.
        if (Obj == null)
        {
            return false;
        }

        // Return true if the fields match.
        return Obj.Operation == Operation && Obj.Text == Text;
    }

    public override int GetHashCode()
    {
        // ReSharper disable NonReadonlyMemberInGetHashCode
        return Text.GetHashCode() ^ Operation.GetHashCode();
        // ReSharper restore NonReadonlyMemberInGetHashCode
    }
}

/**
 * Class representing one patch operation.
 */
internal class Patch
{
    public List<Diff> Diffs = new();
    public int Start1;
    public int Start2;
    public int Length1;
    public int Length2;

    public MatchContext Context = MatchContext.All;
    public BooleanOverride Skip = BooleanOverride.Unspecified;
    public int ContextLength = -1;

    /**
     * Emulate GNU diff's format.
     * Header: @@ -382,8 +481,9 @@
     * Indices are printed as 1-based, not 0-based.
     * @return The GNU diff string.
     */
    public override string ToString()
    {
        var Coords1 = Length1 switch
        {
            0 => Start1 + ",0",
            1 => Convert.ToString(Start1 + 1),
            _ => (Start1 + 1) + "," + Length1
        };

        var Coords2 = Length2 switch
        {
            0 => Start2 + ",0",
            1 => Convert.ToString(Start2 + 1),
            _ => (Start2 + 1) + "," + Length2
        };

        var Text = new StringBuilder();
        Text.Append("@@ -").Append(Coords1).Append(" +").Append(Coords2).Append(" @@\n");
        // Escape the body of the patch with %xx notation.
        foreach (var ADiff in Diffs)
        {
            switch (ADiff.Operation)
            {
                case Operation.Insert:
                    Text.Append('+');
                    break;
                case Operation.Delete:
                    Text.Append('-');
                    break;
                case Operation.Equal:
                    Text.Append(' ');
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            Text.Append(DiffMatchPatch.EncodeUri(ADiff.Text)).Append("\n");
        }

        return Text.ToString();
    }
}

/**
 * Class containing the diff, match and patch methods.
 * Also Contains the behaviour settings.
 */
internal class DiffMatchPatch
{
    // Defaults.
    // Set these on your diff_match_patch instance to override the defaults.

    // Number of seconds to map a diff before giving up (0 for infinity).
    public float DiffTimeout = 1.0f;

    // Cost of an empty edit operation in terms of edit characters.
    public short DiffEditCost = 4;

    // At what point is no match declared (0.0 = perfection, 1.0 = very loose).
    public float MatchThreshold = 0.5f;

    // How far to search for a match (0 = exact location, 1000+ = broad match).
    // A match this many characters away from the expected location will add
    // 1.0 to the score (0.0 is a perfect match).
    public int MatchDistance = 1000;

    // When deleting a large block of text (over ~64 characters), how close
    // do the contents have to be to match the expected contents. (0.0 =
    // perfection, 1.0 = very loose).  Note that MatchThreshold controls
    // how closely the end points of a delete need to match.
    public float PatchDeleteThreshold = 0.5f;

    // Chunk size for context length.
    public short PatchMargin = 4;

    // The number of bits in an int.
    public const short MatchMaxBits = 64;

    //  DIFF FUNCTIONS

    /**
     * Find the differences between two texts.
     * @param text1 Old string to be diffed.
     * @param text2 New string to be diffed.
     * @param checklines Speedup flag.  If false, then don't run a
     *     line-level diff first to identify the changed areas.
     *     If true, then run a faster slightly less optimal diff.
     * @return List of Diff objects.
     */
    public List<Diff> diff_main(string Text1, string Text2, bool Checklines = true)
    {
        // Set a deadline by which time the diff must be complete.
        DateTime Deadline;
        if (DiffTimeout <= 0)
        {
            Deadline = DateTime.MaxValue;
        }
        else
        {
            Deadline = DateTime.Now + new TimeSpan(((long)(DiffTimeout * 1000)) * 10000);
        }

        return diff_main(Text1, Text2, Checklines, Deadline);
    }

    /**
     * Find the differences between two texts.  Simplifies the problem by
     * stripping any common prefix or suffix off the texts before diffing.
     * @param text1 Old string to be diffed.
     * @param text2 New string to be diffed.
     * @param checklines Speedup flag.  If false, then don't run a
     *     line-level diff first to identify the changed areas.
     *     If true, then run a faster slightly less optimal diff.
     * @param deadline Time when the diff should be complete by.  Used
     *     internally for recursive calls.  Users should set DiffTimeout
     *     instead.
     * @return List of Diff objects.
     */
    private List<Diff> diff_main(string Text1, string Text2, bool Checklines, DateTime Deadline)
    {
        // Check for null inputs not needed since null can't be passed in C#.

        // Check for equality (speedup).
        List<Diff> Diffs;
        if (Text1 == Text2)
        {
            Diffs = new List<Diff>();
            if (Text1.Length != 0)
            {
                Diffs.Add(new Diff(Operation.Equal, Text1));
            }

            return Diffs;
        }

        // Trim off common prefix (speedup).
        var Commonlength = diff_commonPrefix(Text1, Text2);
        var Commonprefix = Text1[..Commonlength];
        Text1 = Text1[Commonlength..];
        Text2 = Text2[Commonlength..];

        // Trim off common suffix (speedup).
        Commonlength = diff_commonSuffix(Text1, Text2);
        var Commonsuffix = Text1[^Commonlength..];
        Text1 = Text1[..^Commonlength];
        Text2 = Text2[..^Commonlength];

        // Compute the diff on the middle block.
        Diffs = diff_compute(Text1, Text2, Checklines, Deadline);

        // Restore the prefix and suffix.
        if (Commonprefix.Length != 0)
        {
            Diffs.Insert(0, (new Diff(Operation.Equal, Commonprefix)));
        }

        if (Commonsuffix.Length != 0)
        {
            Diffs.Add(new Diff(Operation.Equal, Commonsuffix));
        }

        diff_cleanupMerge(Diffs);
        return Diffs;
    }

    /**
     * Find the differences between two texts.  Assumes that the texts do not
     * have any common prefix or suffix.
     * @param text1 Old string to be diffed.
     * @param text2 New string to be diffed.
     * @param checklines Speedup flag.  If false, then don't run a
     *     line-level diff first to identify the changed areas.
     *     If true, then run a faster slightly less optimal diff.
     * @param deadline Time when the diff should be complete by.
     * @return List of Diff objects.
     */
    private List<Diff> diff_compute(string Text1, string Text2, bool Checklines, DateTime Deadline)
    {
        var Diffs = new List<Diff>();

        if (Text1.Length == 0)
        {
            // Just add some text (speedup).
            Diffs.Add(new Diff(Operation.Insert, Text2));
            return Diffs;
        }

        if (Text2.Length == 0)
        {
            // Just delete some text (speedup).
            Diffs.Add(new Diff(Operation.Delete, Text1));
            return Diffs;
        }

        var Longtext = Text1.Length > Text2.Length ? Text1 : Text2;
        var Shorttext = Text1.Length > Text2.Length ? Text2 : Text1;
        var I = Longtext.IndexOf(Shorttext, StringComparison.Ordinal);
        if (I != -1)
        {
            // Shorter text is inside the longer text (speedup).
            var Op = (Text1.Length > Text2.Length) ? Operation.Delete : Operation.Insert;
            Diffs.Add(new Diff(Op, Longtext[..I]));
            Diffs.Add(new Diff(Operation.Equal, Shorttext));
            Diffs.Add(new Diff(Op, Longtext[(I + Shorttext.Length)..]));
            return Diffs;
        }

        if (Shorttext.Length == 1)
        {
            // Single character string.
            // After the previous speedup, the character can't be an equality.
            Diffs.Add(new Diff(Operation.Delete, Text1));
            Diffs.Add(new Diff(Operation.Insert, Text2));
            return Diffs;
        }

        // Check to see if the problem can be split in two.
        string[]? Hm = diff_halfMatch(Text1, Text2);
        if (Hm != null)
        {
            // A half-match was found, sort out the return data.
            var Text1A = Hm[0];
            var Text1B = Hm[1];
            var Text2A = Hm[2];
            var Text2B = Hm[3];
            var MidCommon = Hm[4];
            // Send both pairs off for separate processing.
            var DiffsA = diff_main(Text1A, Text2A, Checklines, Deadline);
            var DiffsB = diff_main(Text1B, Text2B, Checklines, Deadline);
            // Merge the results.
            Diffs = DiffsA;
            Diffs.Add(new Diff(Operation.Equal, MidCommon));
            Diffs.AddRange(DiffsB);
            return Diffs;
        }

        if (Checklines && Text1.Length > 100 && Text2.Length > 100)
        {
            return diff_lineMode(Text1, Text2, Deadline);
        }

        return diff_bisect(Text1, Text2, Deadline);
    }

    /**
     * Do a quick line-level diff on both strings, then rediff the parts for
     * greater accuracy.
     * This speedup can produce non-minimal diffs.
     * @param text1 Old string to be diffed.
     * @param text2 New string to be diffed.
     * @param deadline Time when the diff should be complete by.
     * @return List of Diff objects.
     */
    private List<Diff> diff_lineMode(string Text1, string Text2, DateTime Deadline)
    {
        // Scan the text on a line-by-line basis first.
        object[] A = diff_linesToChars(Text1, Text2);
        Text1 = (string)A[0];
        Text2 = (string)A[1];
        var Linearray = (List<string>)A[2];

        var Diffs = diff_main(Text1, Text2, false, Deadline);

        // Convert the diff back to original text.
        diff_charsToLines(Diffs, Linearray);
        // Eliminate freak matches (e.g. blank lines)
        diff_cleanupSemantic(Diffs);

        // Rediff any replacement blocks, this time character-by-character.
        // Add a dummy entry at the end.
        Diffs.Add(new Diff(Operation.Equal, string.Empty));
        var Pointer = 0;
        var CountDelete = 0;
        var CountInsert = 0;
        var TextDelete = string.Empty;
        var TextInsert = string.Empty;
        while (Pointer < Diffs.Count)
        {
            switch (Diffs[Pointer].Operation)
            {
                case Operation.Insert:
                    CountInsert++;
                    TextInsert += Diffs[Pointer].Text;
                    break;
                case Operation.Delete:
                    CountDelete++;
                    TextDelete += Diffs[Pointer].Text;
                    break;
                case Operation.Equal:
                    // Upon reaching an equality, check for prior redundancies.
                    if (CountDelete >= 1 && CountInsert >= 1)
                    {
                        // Delete the offending records and add the merged ones.
                        Diffs.RemoveRange(Pointer - CountDelete - CountInsert, CountDelete + CountInsert);
                        Pointer = Pointer - CountDelete - CountInsert;
                        var SubDiff = diff_main(TextDelete, TextInsert, false, Deadline);
                        Diffs.InsertRange(Pointer, SubDiff);
                        Pointer += SubDiff.Count;
                    }

                    CountInsert = 0;
                    CountDelete = 0;
                    TextDelete = string.Empty;
                    TextInsert = string.Empty;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            Pointer++;
        }

        Diffs.RemoveAt(Diffs.Count - 1); // Remove the dummy entry at the end.

        return Diffs;
    }

    /**
     * Find the 'middle snake' of a diff, split the problem in two
     * and return the recursively constructed diff.
     * See Myers 1986 paper: An O(ND) Difference Algorithm and Its Variations.
     * @param text1 Old string to be diffed.
     * @param text2 New string to be diffed.
     * @param deadline Time at which to bail if not yet complete.
     * @return List of Diff objects.
     */
    protected List<Diff> diff_bisect(string Text1, string Text2, DateTime Deadline)
    {
        // Cache the text lengths to prevent multiple calls.
        var Text1Length = Text1.Length;
        var Text2Length = Text2.Length;
        var MaxD = (Text1Length + Text2Length + 1) / 2;
        var VLength = 2 * MaxD;
        var V1 = new int[VLength];
        var V2 = new int[VLength];
        for (var X = 0; X < VLength; X++)
        {
            V1[X] = -1;
            V2[X] = -1;
        }

        V1[MaxD + 1] = 0;
        V2[MaxD + 1] = 0;
        var Delta = Text1Length - Text2Length;
        // If the total number of characters is odd, then the front path will
        // collide with the reverse path.
        var Front = (Delta % 2 != 0);
        // Offsets for start and end of k loop.
        // Prevents mapping of space beyond the grid.
        var K1Start = 0;
        var K1End = 0;
        var K2Start = 0;
        var K2End = 0;
        for (var D = 0; D < MaxD; D++)
        {
            // Bail out if deadline is reached.
            if (DateTime.Now > Deadline)
            {
                break;
            }

            // Walk the front path one step.
            for (var K1 = -D + K1Start; K1 <= D - K1End; K1 += 2)
            {
                var K1Offset = MaxD + K1;
                int X1;
                if (K1 == -D || K1 != D && V1[K1Offset - 1] < V1[K1Offset + 1])
                {
                    X1 = V1[K1Offset + 1];
                }
                else
                {
                    X1 = V1[K1Offset - 1] + 1;
                }

                var Y1 = X1 - K1;
                while (X1 < Text1Length && Y1 < Text2Length && Text1[X1] == Text2[Y1])
                {
                    X1++;
                    Y1++;
                }

                V1[K1Offset] = X1;
                if (X1 > Text1Length)
                {
                    // Ran off the right of the graph.
                    K1End += 2;
                }
                else if (Y1 > Text2Length)
                {
                    // Ran off the bottom of the graph.
                    K1Start += 2;
                }
                else if (Front)
                {
                    var K2Offset = MaxD + Delta - K1;
                    if (K2Offset >= 0 && K2Offset < VLength && V2[K2Offset] != -1)
                    {
                        // Mirror x2 onto top-left coordinate system.
                        var X2 = Text1Length - V2[K2Offset];
                        if (X1 >= X2)
                        {
                            // Overlap detected.
                            return diff_bisectSplit(Text1, Text2, X1, Y1, Deadline);
                        }
                    }
                }
            }

            // Walk the reverse path one step.
            for (var K2 = -D + K2Start; K2 <= D - K2End; K2 += 2)
            {
                var K2Offset = MaxD + K2;
                int X2;
                if (K2 == -D || K2 != D && V2[K2Offset - 1] < V2[K2Offset + 1])
                {
                    X2 = V2[K2Offset + 1];
                }
                else
                {
                    X2 = V2[K2Offset - 1] + 1;
                }

                var Y2 = X2 - K2;
                while (X2 < Text1Length && Y2 < Text2Length &&
                       Text1[Text1Length - X2 - 1] == Text2[Text2Length - Y2 - 1])
                {
                    X2++;
                    Y2++;
                }

                V2[K2Offset] = X2;
                if (X2 > Text1Length)
                {
                    // Ran off the left of the graph.
                    K2End += 2;
                }
                else if (Y2 > Text2Length)
                {
                    // Ran off the top of the graph.
                    K2Start += 2;
                }
                else if (!Front)
                {
                    var K1Offset = MaxD + Delta - K2;
                    if (K1Offset < 0 || K1Offset >= VLength || V1[K1Offset] == -1) continue;
                    var X1 = V1[K1Offset];
                    var Y1 = MaxD + X1 - K1Offset;
                    // Mirror x2 onto top-left coordinate system.
                    X2 = Text1Length - V2[K2Offset];
                    if (X1 >= X2)
                    {
                        // Overlap detected.
                        return diff_bisectSplit(Text1, Text2, X1, Y1, Deadline);
                    }
                }
            }
        }

        // Diff took too long and hit the deadline or
        // number of diffs equals number of characters, no commonality at all.
        var Diffs = new List<Diff> { new(Operation.Delete, Text1), new(Operation.Insert, Text2) };
        return Diffs;
    }

    /**
     * Given the location of the 'middle snake', split the diff in two parts
     * and recurse.
     * @param text1 Old string to be diffed.
     * @param text2 New string to be diffed.
     * @param x Index of split point in text1.
     * @param y Index of split point in text2.
     * @param deadline Time at which to bail if not yet complete.
     * @return LinkedList of Diff objects.
     */
    private List<Diff> diff_bisectSplit(string Text1, string Text2, int X, int Y, DateTime Deadline)
    {
        var Text1A = Text1[..X];
        var Text2A = Text2[..Y];
        var Text1B = Text1[X..];
        var Text2B = Text2[Y..];

        // Compute both diffs serially.
        var Diffs = diff_main(Text1A, Text2A, false, Deadline);
        var Diffsb = diff_main(Text1B, Text2B, false, Deadline);

        Diffs.AddRange(Diffsb);
        return Diffs;
    }

    /**
     * Split two texts into a list of strings.  Reduce the texts to a string of
     * hashes where each Unicode character represents one line.
     * @param text1 First string.
     * @param text2 Second string.
     * @return Three element Object array, containing the encoded text1, the
     *     encoded text2 and the List of unique strings.  The zeroth element
     *     of the List of unique strings is intentionally blank.
     */
    protected static object[] diff_linesToChars(string Text1, string Text2)
    {
        var LineArray = new List<string>();
        var LineHash = new Dictionary<string, int>();
        // e.g. linearray[4] == "Hello\n"
        // e.g. linehash.get("Hello\n") == 4

        // "\x00" is a valid character, but various debuggers don't like it.
        // So we'll insert a junk entry to avoid generating a null character.
        LineArray.Add(string.Empty);

        // Allocate 2/3rds of the space for text1, the rest for text2.
        var Chars1 = diff_linesToCharsMunge(Text1, LineArray, LineHash, 40000);
        var Chars2 = diff_linesToCharsMunge(Text2, LineArray, LineHash, 65535);
        return new object[] { Chars1, Chars2, LineArray };
    }

    /**
     * Split a text into a list of strings.  Reduce the texts to a string of
     * hashes where each Unicode character represents one line.
     * @param text String to encode.
     * @param lineArray List of unique strings.
     * @param lineHash Map of strings to indices.
     * @param maxLines Maximum length of lineArray.
     * @return Encoded string.
     */
    private static string diff_linesToCharsMunge(string Text, List<string> LineArray,
        Dictionary<string, int> LineHash, int MaxLines)
    {
        var LineStart = 0;
        var LineEnd = -1;
        var Chars = new StringBuilder();
        // Walk the text, pulling out a Substring for each line.
        // text.split('\n') would would temporarily double our memory footprint.
        // Modifying text would create many large strings to garbage collect.
        while (LineEnd < Text.Length - 1)
        {
            LineEnd = Text.IndexOf('\n', LineStart);
            if (LineEnd == -1)
            {
                LineEnd = Text.Length - 1;
            }

            var Line = Text.JavaSubstring(LineStart, LineEnd + 1);

            if (LineHash.TryGetValue(Line, out var Value))
            {
                Chars.Append(((char)Value));
            }
            else
            {
                if (LineArray.Count == MaxLines)
                {
                    // Bail out at 65535 because char 65536 == char 0.
                    Line = Text[LineStart..];
                    LineEnd = Text.Length;
                }

                LineArray.Add(Line);
                LineHash.Add(Line, LineArray.Count - 1);
                Chars.Append(((char)(LineArray.Count - 1)));
            }

            LineStart = LineEnd + 1;
        }

        return Chars.ToString();
    }

    /**
     * Rehydrate the text in a diff from a string of line hashes to real lines
     * of text.
     * @param diffs List of Diff objects.
     * @param lineArray List of unique strings.
     */
    protected void diff_charsToLines(ICollection<Diff> Diffs, IList<string> LineArray)
    {
        foreach (var Diff in Diffs)
        {
            var Text = new StringBuilder();
            foreach (var T in Diff.Text)
            {
                Text.Append(LineArray[T]);
            }

            Diff.Text = Text.ToString();
        }
    }

    /**
     * Determine the common prefix of two strings.
     * @param text1 First string.
     * @param text2 Second string.
     * @return The number of characters common to the start of each string.
     */
    public static int diff_commonPrefix(string Text1, string Text2)
    {
        // Performance analysis: https://neil.fraser.name/news/2007/10/09/
        var N = Math.Min(Text1.Length, Text2.Length);
        for (var I = 0; I < N; I++)
        {
            if (Text1[I] != Text2[I])
            {
                return I;
            }
        }

        return N;
    }

    /**
     * Determine the common suffix of two strings.
     * @param text1 First string.
     * @param text2 Second string.
     * @return The number of characters common to the end of each string.
     */
    public static int diff_commonSuffix(string Text1, string Text2)
    {
        // Performance analysis: https://neil.fraser.name/news/2007/10/09/
        var Text1Length = Text1.Length;
        var Text2Length = Text2.Length;
        var N = Math.Min(Text1.Length, Text2.Length);
        for (var I = 1; I <= N; I++)
        {
            if (Text1[Text1Length - I] != Text2[Text2Length - I])
            {
                return I - 1;
            }
        }

        return N;
    }

    /**
     * Determine if the suffix of one string is the prefix of another.
     * @param text1 First string.
     * @param text2 Second string.
     * @return The number of characters common to the end of the first
     *     string and the start of the second string.
     */
    protected static int diff_commonOverlap(string Text1, string Text2)
    {
        // Cache the text lengths to prevent multiple calls.
        var Text1Length = Text1.Length;
        var Text2Length = Text2.Length;
        // Eliminate the null case.
        if (Text1Length == 0 || Text2Length == 0)
        {
            return 0;
        }

        // Truncate the longer string.
        if (Text1Length > Text2Length)
        {
            Text1 = Text1[(Text1Length - Text2Length)..];
        }
        else if (Text1Length < Text2Length)
        {
            Text2 = Text2[..Text1Length];
        }

        var TextLength = Math.Min(Text1Length, Text2Length);
        // Quick check for the worst case.
        if (Text1 == Text2)
        {
            return TextLength;
        }

        // Start by looking for a single character match
        // and increase length until no match is found.
        // Performance analysis: https://neil.fraser.name/news/2010/11/04/
        var Best = 0;
        var Length = 1;
        while (true)
        {
            var Pattern = Text1[(TextLength - Length)..];
            var Found = Text2.IndexOf(Pattern, StringComparison.Ordinal);
            if (Found == -1)
            {
                return Best;
            }

            Length += Found;
            if (Found == 0 || Text1[(TextLength - Length)..] == Text2[..Length])
            {
                Best = Length;
                Length++;
            }
        }
    }

    /**
     * Do the two texts share a Substring which is at least half the length of
     * the longer text?
     * This speedup can produce non-minimal diffs.
     * @param text1 First string.
     * @param text2 Second string.
     * @return Five element String array, containing the prefix of text1, the
     *     suffix of text1, the prefix of text2, the suffix of text2 and the
     *     common middle.  Or null if there was no match.
     */
    protected string[]? diff_halfMatch(string Text1, string Text2)
    {
        if (DiffTimeout <= 0)
        {
            // Don't risk returning a non-optimal diff if we have unlimited time.
            return null;
        }

        var Longtext = Text1.Length > Text2.Length ? Text1 : Text2;
        var Shorttext = Text1.Length > Text2.Length ? Text2 : Text1;
        if (Longtext.Length < 4 || Shorttext.Length * 2 < Longtext.Length)
        {
            return null; // Pointless.
        }

        // First check if the second quarter is the seed for a half-match.
        string[]? Hm1 = diff_halfMatchI(Longtext, Shorttext, (Longtext.Length + 3) / 4);
        // Check again based on the third quarter.
        string[]? Hm2 = diff_halfMatchI(Longtext, Shorttext, (Longtext.Length + 1) / 2);
        string[] Hm;
        if (Hm1 == null && Hm2 == null)
        {
            return null;
        }

        if (Hm2 == null)
        {
            Hm = Hm1!;
        }
        else if (Hm1 == null)
        {
            Hm = Hm2;
        }
        else
        {
            // Both matched.  Select the longest.
            Hm = Hm1[4].Length > Hm2[4].Length ? Hm1 : Hm2;
        }

        // A half-match was found, sort out the return data.
        return Text1.Length > Text2.Length
            ? Hm
            :
            //return new string[]{hm[0], hm[1], hm[2], hm[3], hm[4]};
            new[] { Hm[2], Hm[3], Hm[0], Hm[1], Hm[4] };
    }

    /**
     * Does a Substring of shorttext exist within longtext such that the
     * Substring is at least half the length of longtext?
     * @param longtext Longer string.
     * @param shorttext Shorter string.
     * @param i Start index of quarter length Substring within longtext.
     * @return Five element string array, containing the prefix of longtext, the
     *     suffix of longtext, the prefix of shorttext, the suffix of shorttext
     *     and the common middle.  Or null if there was no match.
     */
    private static string[]? diff_halfMatchI(string Longtext, string Shorttext, int I)
    {
        // Start with a 1/4 length Substring at position i as a seed.
        var Seed = Longtext.Substring(I, Longtext.Length / 4);
        var J = -1;
        var BestCommon = string.Empty;
        string BestLongtextA = string.Empty, BestLongtextB = string.Empty;
        string BestShorttextA = string.Empty, BestShorttextB = string.Empty;
        while (J < Shorttext.Length && (J = Shorttext.IndexOf(Seed, J + 1, StringComparison.Ordinal)) != -1)
        {
            var PrefixLength = diff_commonPrefix(Longtext[I..], Shorttext[J..]);
            var SuffixLength = diff_commonSuffix(Longtext[..I], Shorttext[..J]);
            if (BestCommon.Length >= SuffixLength + PrefixLength) continue;
            BestCommon = string.Concat(Shorttext.AsSpan(J - SuffixLength, SuffixLength), Shorttext.AsSpan(J, PrefixLength));
            BestLongtextA = Longtext[..(I - SuffixLength)];
            BestLongtextB = Longtext[(I + PrefixLength)..];
            BestShorttextA = Shorttext[..(J - SuffixLength)];
            BestShorttextB = Shorttext[(J + PrefixLength)..];
        }

        return BestCommon.Length * 2 >= Longtext.Length
            ? new[] { BestLongtextA, BestLongtextB, BestShorttextA, BestShorttextB, BestCommon }
            : null;
    }

    /**
     * Reduce the number of edits by eliminating semantically trivial
     * equalities.
     * @param diffs List of Diff objects.
     */
    public void diff_cleanupSemantic(List<Diff> Diffs)
    {
        var Changes = false;
        // Stack of indices where equalities are found.
        var Equalities = new Stack<int>();
        // Always equal to equalities[equalitiesLength-1][1]
        string? LastEquality = null;
        var Pointer = 0; // Index of current position.
        // Number of characters that changed prior to the equality.
        var LengthInsertions1 = 0;
        var LengthDeletions1 = 0;
        // Number of characters that changed after the equality.
        var LengthInsertions2 = 0;
        var LengthDeletions2 = 0;
        while (Pointer < Diffs.Count)
        {
            if (Diffs[Pointer].Operation == Operation.Equal)
            {
                // Equality found.
                Equalities.Push(Pointer);
                LengthInsertions1 = LengthInsertions2;
                LengthDeletions1 = LengthDeletions2;
                LengthInsertions2 = 0;
                LengthDeletions2 = 0;
                LastEquality = Diffs[Pointer].Text;
            }
            else
            {
                // an insertion or deletion
                if (Diffs[Pointer].Operation == Operation.Insert)
                {
                    LengthInsertions2 += Diffs[Pointer].Text.Length;
                }
                else
                {
                    LengthDeletions2 += Diffs[Pointer].Text.Length;
                }

                // Eliminate an equality that is smaller or equal to the edits on both
                // sides of it.
                if (LastEquality != null &&
                    (LastEquality.Length <= Math.Max(LengthInsertions1, LengthDeletions1)) &&
                    (LastEquality.Length <= Math.Max(LengthInsertions2, LengthDeletions2)))
                {
                    // Duplicate record.
                    Diffs.Insert(Equalities.Peek(), new Diff(Operation.Delete, LastEquality));
                    // Change second copy to insert.
                    Diffs[Equalities.Peek() + 1].Operation = Operation.Insert;
                    // Throw away the equality we just deleted.
                    Equalities.Pop();
                    if (Equalities.Count > 0)
                    {
                        Equalities.Pop();
                    }

                    Pointer = Equalities.Count > 0 ? Equalities.Peek() : -1;
                    LengthInsertions1 = 0; // Reset the counters.
                    LengthDeletions1 = 0;
                    LengthInsertions2 = 0;
                    LengthDeletions2 = 0;
                    LastEquality = null;
                    Changes = true;
                }
            }

            Pointer++;
        }

        // Normalize the diff.
        if (Changes)
        {
            diff_cleanupMerge(Diffs);
        }

        diff_cleanupSemanticLossless(Diffs);

        // Find any overlaps between deletions and insertions.
        // e.g: <del>abcxxx</del><ins>xxxdef</ins>
        //   -> <del>abc</del>xxx<ins>def</ins>
        // e.g: <del>xxxabc</del><ins>defxxx</ins>
        //   -> <ins>def</ins>xxx<del>abc</del>
        // Only extract an overlap if it is as big as the edit ahead or behind it.
        Pointer = 1;
        while (Pointer < Diffs.Count)
        {
            if (Diffs[Pointer - 1].Operation == Operation.Delete && Diffs[Pointer].Operation == Operation.Insert)
            {
                var Deletion = Diffs[Pointer - 1].Text;
                var Insertion = Diffs[Pointer].Text;
                var OverlapLength1 = diff_commonOverlap(Deletion, Insertion);
                var OverlapLength2 = diff_commonOverlap(Insertion, Deletion);
                if (OverlapLength1 >= OverlapLength2)
                {
                    if (OverlapLength1 >= Deletion.Length / 2.0 || OverlapLength1 >= Insertion.Length / 2.0)
                    {
                        // Overlap found.
                        // Insert an equality and trim the surrounding edits.
                        Diffs.Insert(Pointer, new Diff(Operation.Equal, Insertion[..OverlapLength1]));
                        Diffs[Pointer - 1].Text = Deletion[..^OverlapLength1];
                        Diffs[Pointer + 1].Text = Insertion[OverlapLength1..];
                        Pointer++;
                    }
                }
                else
                {
                    if (OverlapLength2 >= Deletion.Length / 2.0 || OverlapLength2 >= Insertion.Length / 2.0)
                    {
                        // Reverse overlap found.
                        // Insert an equality and swap and trim the surrounding edits.
                        Diffs.Insert(Pointer, new Diff(Operation.Equal, Deletion[..OverlapLength2]));
                        Diffs[Pointer - 1].Operation = Operation.Insert;
                        Diffs[Pointer - 1].Text = Insertion[..^OverlapLength2];
                        Diffs[Pointer + 1].Operation = Operation.Delete;
                        Diffs[Pointer + 1].Text = Deletion[OverlapLength2..];
                        Pointer++;
                    }
                }

                Pointer++;
            }

            Pointer++;
        }
    }

    /**
     * Look for single edits surrounded on both sides by equalities
     * which can be shifted sideways to align the edit to a word boundary.
     * e.g: The c<ins>at c</ins>ame. -> The <ins>cat </ins>came.
     * @param diffs List of Diff objects.
     */
    public void diff_cleanupSemanticLossless(List<Diff> Diffs)
    {
        var Pointer = 1;
        // Intentionally ignore the first and last element (don't need checking).
        while (Pointer < Diffs.Count - 1)
        {
            if (Diffs[Pointer - 1].Operation == Operation.Equal && Diffs[Pointer + 1].Operation == Operation.Equal)
            {
                // This is a single edit surrounded by equalities.
                var Equality1 = Diffs[Pointer - 1].Text;
                var Edit = Diffs[Pointer].Text;
                var Equality2 = Diffs[Pointer + 1].Text;

                // First, shift the edit as far left as possible.
                var CommonOffset = diff_commonSuffix(Equality1, Edit);
                if (CommonOffset > 0)
                {
                    var CommonString = Edit[^CommonOffset..];
                    Equality1 = Equality1[..^CommonOffset];
                    Edit = CommonString + Edit[..^CommonOffset];
                    Equality2 = CommonString + Equality2;
                }

                // Second, step character by character right,
                // looking for the best fit.
                var BestEquality1 = Equality1;
                var BestEdit = Edit;
                var BestEquality2 = Equality2;
                var BestScore = diff_cleanupSemanticScore(Equality1, Edit) +
                                diff_cleanupSemanticScore(Edit, Equality2);
                while (Edit.Length != 0 && Equality2.Length != 0 && Edit[0] == Equality2[0])
                {
                    Equality1 += Edit[0];
                    Edit = Edit[1..] + Equality2[0];
                    Equality2 = Equality2[1..];
                    var Score = diff_cleanupSemanticScore(Equality1, Edit) +
                                diff_cleanupSemanticScore(Edit, Equality2);
                    // The >= encourages trailing rather than leading whitespace on
                    // edits.
                    if (Score < BestScore) continue;
                    BestScore = Score;
                    BestEquality1 = Equality1;
                    BestEdit = Edit;
                    BestEquality2 = Equality2;
                }

                if (Diffs[Pointer - 1].Text != BestEquality1)
                {
                    // We have an improvement, save it back to the diff.
                    if (BestEquality1.Length != 0)
                    {
                        Diffs[Pointer - 1].Text = BestEquality1;
                    }
                    else
                    {
                        Diffs.RemoveAt(Pointer - 1);
                        Pointer--;
                    }

                    Diffs[Pointer].Text = BestEdit;
                    if (BestEquality2.Length != 0)
                    {
                        Diffs[Pointer + 1].Text = BestEquality2;
                    }
                    else
                    {
                        Diffs.RemoveAt(Pointer + 1);
                        Pointer--;
                    }
                }
            }

            Pointer++;
        }
    }

    /**
     * Given two strings, compute a score representing whether the internal
     * boundary falls on logical boundaries.
     * Scores range from 6 (best) to 0 (worst).
     * @param one First string.
     * @param two Second string.
     * @return The score.
     */
    private int diff_cleanupSemanticScore(string One, string Two)
    {
        if (One.Length == 0 || Two.Length == 0)
        {
            // Edges are the best.
            return 6;
        }

        // Each port of this function behaves slightly differently due to
        // subtle differences in each language's definition of things like
        // 'whitespace'.  Since this function's purpose is largely cosmetic,
        // the choice has been made to use each language's native features
        // rather than force total conformity.
        var Char1 = One[^1];
        var Char2 = Two[0];
        var NonAlphaNumeric1 = !char.IsLetterOrDigit(Char1);
        var NonAlphaNumeric2 = !char.IsLetterOrDigit(Char2);
        var Whitespace1 = NonAlphaNumeric1 && char.IsWhiteSpace(Char1);
        var Whitespace2 = NonAlphaNumeric2 && char.IsWhiteSpace(Char2);
        var LineBreak1 = Whitespace1 && char.IsControl(Char1);
        var LineBreak2 = Whitespace2 && char.IsControl(Char2);
        var BlankLine1 = LineBreak1 && Blanklineend.IsMatch(One);
        var BlankLine2 = LineBreak2 && Blanklinestart.IsMatch(Two);

        if (BlankLine1 || BlankLine2)
        {
            // Five points for blank lines.
            return 5;
        }

        if (LineBreak1 || LineBreak2)
        {
            // Four points for line breaks.
            return 4;
        }

        if (NonAlphaNumeric1 && !Whitespace1 && Whitespace2)
        {
            // Three points for end of sentences.
            return 3;
        }

        if (Whitespace1 || Whitespace2)
        {
            // Two points for whitespace.
            return 2;
        }

        if (NonAlphaNumeric1 || NonAlphaNumeric2)
        {
            // One point for non-alphanumeric.
            return 1;
        }

        return 0;
    }

    // Define some regex patterns for matching boundaries.
    private readonly Regex Blanklineend = new("\\n\\r?\\n\\Z");
    private readonly Regex Blanklinestart = new("\\A\\r?\\n\\r?\\n");

    /**
     * Reduce the number of edits by eliminating operationally trivial
     * equalities.
     * @param diffs List of Diff objects.
     */
    public void diff_cleanupEfficiency(List<Diff> Diffs)
    {
        var Changes = false;
        // Stack of indices where equalities are found.
        var Equalities = new Stack<int>();
        // Always equal to equalities[equalitiesLength-1][1]
        var LastEquality = string.Empty;
        var Pointer = 0; // Index of current position.
        // Is there an insertion operation before the last equality.
        var PreIns = false;
        // Is there a deletion operation before the last equality.
        var PreDel = false;
        // Is there an insertion operation after the last equality.
        var PostIns = false;
        // Is there a deletion operation after the last equality.
        var PostDel = false;
        while (Pointer < Diffs.Count)
        {
            if (Diffs[Pointer].Operation == Operation.Equal)
            {
                // Equality found.
                if (Diffs[Pointer].Text.Length < DiffEditCost && (PostIns || PostDel))
                {
                    // Candidate found.
                    Equalities.Push(Pointer);
                    PreIns = PostIns;
                    PreDel = PostDel;
                    LastEquality = Diffs[Pointer].Text;
                }
                else
                {
                    // Not a candidate, and can never become one.
                    Equalities.Clear();
                    LastEquality = string.Empty;
                }

                PostIns = PostDel = false;
            }
            else
            {
                // An insertion or deletion.
                if (Diffs[Pointer].Operation == Operation.Delete)
                {
                    PostDel = true;
                }
                else
                {
                    PostIns = true;
                }

                /*
                 * Five types to be split:
                 * <ins>A</ins><del>B</del>XY<ins>C</ins><del>D</del>
                 * <ins>A</ins>X<ins>C</ins><del>D</del>
                 * <ins>A</ins><del>B</del>X<ins>C</ins>
                 * <ins>A</del>X<ins>C</ins><del>D</del>
                 * <ins>A</ins><del>B</del>X<del>C</del>
                 */
                if ((LastEquality.Length != 0) && ((PreIns && PreDel && PostIns && PostDel) ||
                                                   ((LastEquality.Length < DiffEditCost / 2) &&
                                                    ((PreIns ? 1 : 0) + (PreDel ? 1 : 0) + (PostIns ? 1 : 0) +
                                                     (PostDel ? 1 : 0)) == 3)))
                {
                    // Duplicate record.
                    Diffs.Insert(Equalities.Peek(), new Diff(Operation.Delete, LastEquality));
                    // Change second copy to insert.
                    Diffs[Equalities.Peek() + 1].Operation = Operation.Insert;
                    Equalities.Pop(); // Throw away the equality we just deleted.
                    LastEquality = string.Empty;
                    if (PreIns && PreDel)
                    {
                        // No changes made which could affect previous entry, keep going.
                        PostIns = PostDel = true;
                        Equalities.Clear();
                    }
                    else
                    {
                        if (Equalities.Count > 0)
                        {
                            Equalities.Pop();
                        }

                        Pointer = Equalities.Count > 0 ? Equalities.Peek() : -1;
                        PostIns = PostDel = false;
                    }

                    Changes = true;
                }
            }

            Pointer++;
        }

        if (Changes)
        {
            diff_cleanupMerge(Diffs);
        }
    }

    /**
     * Reorder and merge like edit sections.  Merge equalities.
     * Any edit section can move as long as it doesn't cross an equality.
     * @param diffs List of Diff objects.
     */
    public static void diff_cleanupMerge(List<Diff> Diffs)
    {
        while (true)
        {
            // Add a dummy entry at the end.
            Diffs.Add(new Diff(Operation.Equal, string.Empty));
            var Pointer = 0;
            var CountDelete = 0;
            var CountInsert = 0;
            var TextDelete = string.Empty;
            var TextInsert = string.Empty;
            while (Pointer < Diffs.Count)
            {
                switch (Diffs[Pointer].Operation)
                {
                    case Operation.Insert:
                        CountInsert++;
                        TextInsert += Diffs[Pointer].Text;
                        Pointer++;
                        break;
                    case Operation.Delete:
                        CountDelete++;
                        TextDelete += Diffs[Pointer].Text;
                        Pointer++;
                        break;
                    case Operation.Equal:
                        // Upon reaching an equality, check for prior redundancies.
                        if (CountDelete + CountInsert > 1)
                        {
                            if (CountDelete != 0 && CountInsert != 0)
                            {
                                // Factor out any common prefixies.
                                var Commonlength = diff_commonPrefix(TextInsert, TextDelete);
                                if (Commonlength != 0)
                                {
                                    if ((Pointer - CountDelete - CountInsert) > 0 &&
                                        Diffs[Pointer - CountDelete - CountInsert - 1].Operation == Operation.Equal)
                                    {
                                        Diffs[Pointer - CountDelete - CountInsert - 1].Text +=
                                            TextInsert[..Commonlength];
                                    }
                                    else
                                    {
                                        Diffs.Insert(0, new Diff(Operation.Equal, TextInsert[..Commonlength]));
                                        Pointer++;
                                    }

                                    TextInsert = TextInsert[Commonlength..];
                                    TextDelete = TextDelete[Commonlength..];
                                }

                                // Factor out any common suffixies.
                                Commonlength = diff_commonSuffix(TextInsert, TextDelete);
                                if (Commonlength != 0)
                                {
                                    Diffs[Pointer].Text = TextInsert[^Commonlength..] + Diffs[Pointer].Text;
                                    TextInsert = TextInsert[..^Commonlength];
                                    TextDelete = TextDelete[..^Commonlength];
                                }
                            }

                            // Delete the offending records and add the merged ones.
                            Pointer -= CountDelete + CountInsert;
                            Diffs.Splice(Pointer, CountDelete + CountInsert);
                            if (TextDelete.Length != 0)
                            {
                                Diffs.Splice(Pointer, 0, new Diff(Operation.Delete, TextDelete));
                                Pointer++;
                            }

                            if (TextInsert.Length != 0)
                            {
                                Diffs.Splice(Pointer, 0, new Diff(Operation.Insert, TextInsert));
                                Pointer++;
                            }

                            Pointer++;
                        }
                        else if (Pointer != 0 && Diffs[Pointer - 1].Operation == Operation.Equal)
                        {
                            // Merge this equality with the previous one.
                            Diffs[Pointer - 1].Text += Diffs[Pointer].Text;
                            Diffs.RemoveAt(Pointer);
                        }
                        else
                        {
                            Pointer++;
                        }

                        CountInsert = 0;
                        CountDelete = 0;
                        TextDelete = string.Empty;
                        TextInsert = string.Empty;
                        break;
                }
            }

            if (Diffs[^1].Text.Length == 0)
            {
                Diffs.RemoveAt(Diffs.Count - 1); // Remove the dummy entry at the end.
            }

            // Second pass: look for single edits surrounded on both sides by
            // equalities which can be shifted sideways to eliminate an equality.
            // e.g: A<ins>BA</ins>C -> <ins>AB</ins>AC
            var Changes = false;
            Pointer = 1;
            // Intentionally ignore the first and last element (don't need checking).
            while (Pointer < (Diffs.Count - 1))
            {
                if (Diffs[Pointer - 1].Operation == Operation.Equal &&
                    Diffs[Pointer + 1].Operation == Operation.Equal)
                {
                    // This is a single edit surrounded by equalities.
                    if (Diffs[Pointer].Text.EndsWith(Diffs[Pointer - 1].Text, StringComparison.Ordinal))
                    {
                        // Shift the edit over the previous equality.
                        Diffs[Pointer].Text = Diffs[Pointer - 1].Text + Diffs[Pointer]
                            .Text[..^Diffs[Pointer - 1].Text.Length];
                        Diffs[Pointer + 1].Text = Diffs[Pointer - 1].Text + Diffs[Pointer + 1].Text;
                        Diffs.Splice(Pointer - 1, 1);
                        Changes = true;
                    }
                    else if (Diffs[Pointer].Text.StartsWith(Diffs[Pointer + 1].Text, StringComparison.Ordinal))
                    {
                        // Shift the edit over the next equality.
                        Diffs[Pointer - 1].Text += Diffs[Pointer + 1].Text;
                        Diffs[Pointer].Text = Diffs[Pointer].Text[Diffs[Pointer + 1].Text.Length..] +
                                              Diffs[Pointer + 1].Text;
                        Diffs.Splice(Pointer + 1, 1);
                        Changes = true;
                    }
                }

                Pointer++;
            }

            // If shifts were made, the diff needs reordering and another shift sweep.
            if (Changes)
            {
                continue;
            }

            break;
        }
    }

    /**
     * loc is a location in text1, compute and return the equivalent location in
     * text2.
     * e.g. "The cat" vs "The big cat", 1->1, 5->8
     * @param diffs List of Diff objects.
     * @param loc Location within text1.
     * @return Location within text2.
     */
    public static int diff_xIndex(List<Diff> Diffs, int Loc)
    {
        var Chars1 = 0;
        var Chars2 = 0;
        var LastChars1 = 0;
        var LastChars2 = 0;
        Diff? LastDiff = null;
        foreach (var ADiff in Diffs)
        {
            if (ADiff.Operation != Operation.Insert)
            {
                // Equality or deletion.
                Chars1 += ADiff.Text.Length;
            }

            if (ADiff.Operation != Operation.Delete)
            {
                // Equality or insertion.
                Chars2 += ADiff.Text.Length;
            }

            if (Chars1 > Loc)
            {
                // Overshot the location.
                LastDiff = ADiff;
                break;
            }

            LastChars1 = Chars1;
            LastChars2 = Chars2;
        }

        if (LastDiff is { Operation: Operation.Delete })
        {
            // The location was deleted.
            return LastChars2;
        }

        // Add the remaining character length.
        return LastChars2 + (Loc - LastChars1);
    }

    /**
     * Convert a Diff list into a pretty HTML report.
     * @param diffs List of Diff objects.
     * @return HTML representation.
     */
    public static string diff_prettyHtml(List<Diff> Diffs)
    {
        var Html = new StringBuilder();
        foreach (var ADiff in Diffs)
        {
            var Text = ADiff.Text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
            switch (ADiff.Operation)
            {
                case Operation.Insert:
                    Html.Append("<pre style=\"background:#ccffcc;\">").Append(Text).Append("</pre>");
                    break;
                case Operation.Delete:
                    Html.Append("<pre style=\"background:#ffcccc;\">").Append(Text).Append("</pre>");
                    break;
                case Operation.Equal:
                    Html.Append("<pre>").Append(Text).Append("</pre>");
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        return Html.ToString();
    }

    /**
     * Compute and return the source text (all equalities and deletions).
     * @param diffs List of Diff objects.
     * @return Source text.
     */
    public static string diff_text1(IEnumerable<Diff> Diffs)
    {
        var Text = new StringBuilder();
        foreach (var ADiff in Diffs.Where(ADiff => ADiff.Operation != Operation.Insert))
        {
            Text.Append(ADiff.Text);
        }

        return Text.ToString();
    }

    /**
     * Compute and return the destination text (all equalities and insertions).
     * @param diffs List of Diff objects.
     * @return Destination text.
     */
    public static string diff_text2(IEnumerable<Diff> Diffs)
    {
        var Text = new StringBuilder();
        foreach (var ADiff in Diffs.Where(ADiff => ADiff.Operation != Operation.Delete))
        {
            Text.Append(ADiff.Text);
        }

        return Text.ToString();
    }

    /**
     * Compute the Levenshtein distance; the number of inserted, deleted or
     * substituted characters.
     * @param diffs List of Diff objects.
     * @return Number of changes.
     */
    public static int diff_levenshtein(IEnumerable<Diff> Diffs)
    {
        var Levenshtein = 0;
        var Insertions = 0;
        var Deletions = 0;
        foreach (var ADiff in Diffs)
        {
            switch (ADiff.Operation)
            {
                case Operation.Insert:
                    Insertions += ADiff.Text.Length;
                    break;
                case Operation.Delete:
                    Deletions += ADiff.Text.Length;
                    break;
                case Operation.Equal:
                    // A deletion and an insertion is one substitution.
                    Levenshtein += Math.Max(Insertions, Deletions);
                    Insertions = 0;
                    Deletions = 0;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        Levenshtein += Math.Max(Insertions, Deletions);
        return Levenshtein;
    }

    /**
     * Crush the diff into an encoded string which describes the operations
     * required to transform text1 into text2.
     * E.g. =3\t-2\t+ing  -> Keep 3 chars, delete 2 chars, insert 'ing'.
     * Operations are tab-separated.  Inserted text is escaped using %xx
     * notation.
     * @param diffs Array of Diff objects.
     * @return Delta text.
     */
    public string diff_toDelta(IEnumerable<Diff> Diffs)
    {
        var Text = new StringBuilder();
        foreach (var ADiff in Diffs)
        {
            switch (ADiff.Operation)
            {
                case Operation.Insert:
                    Text.Append("+").Append(EncodeUri(ADiff.Text)).Append("\t");
                    break;
                case Operation.Delete:
                    Text.Append("-").Append(ADiff.Text.Length).Append("\t");
                    break;
                case Operation.Equal:
                    Text.Append("=").Append(ADiff.Text.Length).Append("\t");
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        var Delta = Text.ToString();
        if (Delta.Length != 0)
        {
            // Strip off trailing tab character.
            Delta = Delta[..^1];
        }

        return Delta;
    }

    /**
     * Given the original text1, and an encoded string which describes the
     * operations required to transform text1 into text2, compute the full diff.
     * @param text1 Source string for the diff.
     * @param delta Delta text.
     * @return Array of Diff objects or null if invalid.
     * @throws ArgumentException If invalid input.
     */
    public List<Diff> diff_fromDelta(string Text1, string Delta)
    {
        var Diffs = new List<Diff>();
        var Pointer = 0; // Cursor in text1
        string[] Tokens = Delta.Split(new[] { "\t" }, StringSplitOptions.None);
        foreach (var Token in Tokens)
        {
            if (Token.Length == 0)
            {
                // Blank tokens are ok (from a trailing \t).
                continue;
            }

            // Each token begins with a one character parameter which specifies the
            // operation of this token (delete, insert, equality).
            var Param = Token[1..];
            switch (Token[0])
            {
                case '+':
                    // decode would change all "+" to " "
                    Param = Param.Replace("+", "%2b");

                    Param = HttpUtility.UrlDecode(Param);
                    //} catch (UnsupportedEncodingException e) {
                    //  // Not likely on modern system.
                    //  throw new Error("This system does not support UTF-8.", e);
                    //} catch (IllegalArgumentException e) {
                    //  // Malformed URI sequence.
                    //  throw new IllegalArgumentException(
                    //      "Illegal escape in diff_fromDelta: " + param, e);
                    //}
                    Diffs.Add(new Diff(Operation.Insert, Param));
                    break;
                case '-':
                // Fall through.
                case '=':
                    int N;
                    try
                    {
                        N = Convert.ToInt32(Param);
                    }
                    catch (FormatException E)
                    {
                        throw new ArgumentException("Invalid number in diff_fromDelta: " + Param, E);
                    }

                    if (N < 0)
                    {
                        throw new ArgumentException("Negative number in diff_fromDelta: " + Param);
                    }

                    string Text;
                    try
                    {
                        Text = Text1.Substring(Pointer, N);
                        Pointer += N;
                    }
                    catch (ArgumentOutOfRangeException E)
                    {
                        throw new ArgumentException(
                            "Delta length (" + Pointer + ") larger than source text length (" + Text1.Length + ").",
                            E);
                    }

                    Diffs.Add(Token[0] == '=' ? new Diff(Operation.Equal, Text) : new Diff(Operation.Delete, Text));

                    break;
                default:
                    // Anything else is an error.
                    throw new ArgumentException("Invalid diff operation in diff_fromDelta: " + Token[0]);
            }
        }

        if (Pointer != Text1.Length)
        {
            throw new ArgumentException("Delta length (" + Pointer + ") smaller than source text length (" +
                                        Text1.Length + ").");
        }

        return Diffs;
    }

    //  MATCH FUNCTIONS

    /**
     * Locate the best instance of 'pattern' in 'text' near 'loc'.
     * Returns -1 if no match found.
     * @param text The text to search.
     * @param pattern The pattern to search for.
     * @param loc The location to search around.
     * @return Best match index or -1.
     */
    public int match_main(string Text, string Pattern, int Loc)
    {
        // Check for null inputs not needed since null can't be passed in C#.

        Loc = Math.Max(0, Math.Min(Loc, Text.Length));
        if (Text == Pattern)
        {
            // Shortcut (potentially not guaranteed by the algorithm)
            return 0;
        }
        if (Text.Length == 0)
        {
            // Nothing to match.
            return -1;
        }
        if (Loc + Pattern.Length <= Text.Length && Text.Substring(Loc, Pattern.Length) == Pattern)
        {
            // Perfect match at the perfect spot!  (Includes case of null pattern)
            return Loc;
        }
        // Do a fuzzy compare.
        return match_bitap(Text, Pattern, Loc);
    }

    /**
     * Locate the best instance of 'pattern' in 'text' near 'loc' using the
     * Bitap algorithm.  Returns -1 if no match found.
     * @param text The text to search.
     * @param pattern The pattern to search for.
     * @param loc The location to search around.
     * @return Best match index or -1.
     */
    protected int match_bitap(string Text, string Pattern, int Loc)
    {
        if (MatchMaxBits != 0 && Pattern.Length > MatchMaxBits)
        {
            throw new ArgumentException("Pattern too long for this application.");
        }

        // Initialise the alphabet.
        var S = match_alphabet(Pattern);

        // Highest score beyond which we give up.
        double ScoreThreshold = MatchThreshold;
        // Is there a nearby exact match? (speedup)
        var BestLoc = Text.IndexOf(Pattern, Loc, StringComparison.Ordinal);
        if (BestLoc != -1)
        {
            ScoreThreshold = Math.Min(match_bitapScore(0, BestLoc, Loc, Pattern), ScoreThreshold);
            // What about in the other direction? (speedup)
            BestLoc = Text.LastIndexOf(Pattern, Math.Min(Loc + Pattern.Length, Text.Length), StringComparison.Ordinal);
            if (BestLoc != -1)
            {
                ScoreThreshold = Math.Min(match_bitapScore(0, BestLoc, Loc, Pattern), ScoreThreshold);
            }
        }

        // Initialise the bit arrays.
        var Matchmask = 1ul << (Pattern.Length - 1);
        BestLoc = -1;

        var BinMax = Pattern.Length + Text.Length;
        // Empty initialization added to appease C# compiler.
        var LastRd = Array.Empty<ulong>();
        for (var D = 0; D < Pattern.Length; D++)
        {
            // Scan for the best match; each iteration allows for one more error.
            // Run a binary search to determine how far from 'loc' we can stray at
            // this error level.
            var BinMin = 0;
            var BinMid = BinMax;
            while (BinMin < BinMid)
            {
                if (match_bitapScore(D, Loc + BinMid, Loc, Pattern) <= ScoreThreshold)
                {
                    BinMin = BinMid;
                }
                else
                {
                    BinMax = BinMid;
                }

                BinMid = (BinMax - BinMin) / 2 + BinMin;
            }

            // Use the result from this iteration as the maximum for the next.
            BinMax = BinMid;
            var Start = Math.Max(1, Loc - BinMid + 1);
            var Finish = Math.Min(Loc + BinMid, Text.Length) + Pattern.Length;

            var Rd = new ulong[Finish + 2];
            Rd[Finish + 1] = (1ul << D) - 1;
            for (var J = Finish; J >= Start; J--)
            {
                ulong CharMatch;
                if (Text.Length <= J - 1 || !S.ContainsKey(Text[J - 1]))
                {
                    // Out of range.
                    CharMatch = 0;
                }
                else
                {
                    CharMatch = S[Text[J - 1]];
                }

                if (D == 0)
                {
                    // First pass: exact match.
                    Rd[J] = ((Rd[J + 1] << 1) | 1ul) & CharMatch;
                }
                else
                {
                    // Subsequent passes: fuzzy match.
                    Rd[J] = ((Rd[J + 1] << 1) | 1) & CharMatch | (((LastRd[J + 1] | LastRd[J]) << 1) | 1) |
                            LastRd[J + 1];
                }

                if ((Rd[J] & Matchmask) != 0)
                {
                    var Score = match_bitapScore(D, J - 1, Loc, Pattern);
                    // This match will almost certainly be better than any existing
                    // match.  But check anyway.
                    if (Score <= ScoreThreshold)
                    {
                        // Told you so.
                        ScoreThreshold = Score;
                        BestLoc = J - 1;
                        if (BestLoc > Loc)
                        {
                            // When passing loc, don't exceed our current distance from loc.
                            Start = Math.Max(1, 2 * Loc - BestLoc);
                        }
                        else
                        {
                            // Already passed loc, downhill from here on in.
                            break;
                        }
                    }
                }
            }

            if (match_bitapScore(D + 1, Loc, Loc, Pattern) > ScoreThreshold)
            {
                // No hope for a (better) match at greater error levels.
                break;
            }

            LastRd = Rd;
        }

        return BestLoc;
    }

    /**
     * Compute and return the score for a match with e errors and x location.
     * @param e Number of errors in match.
     * @param x Location of match.
     * @param loc Expected location of match.
     * @param pattern Pattern being sought.
     * @return Overall score for match (0.0 = good, 1.0 = bad).
     */
    private double match_bitapScore(int E, int X, int Loc, string Pattern)
    {
        var Accuracy = (float)E / Pattern.Length;
        var Proximity = Math.Abs(Loc - X);
        if (MatchDistance == 0)
        {
            // Dodge divide by zero error.
            return Proximity == 0 ? Accuracy : 1.0;
        }

        return Accuracy + (Proximity / (float)MatchDistance);
    }

    /**
     * Initialise the alphabet for the Bitap algorithm.
     * @param pattern The text to encode.
     * @return Hash of character locations.
     */
    protected static Dictionary<char, ulong> match_alphabet(string Pattern)
    {
        var S = new Dictionary<char, ulong>();
        var CharPattern = Pattern.ToCharArray();
        foreach (var C in CharPattern)
        {
            S.TryAdd(C, 0);
        }

        var I = 0;
        foreach (var C in CharPattern)
        {
            var Value = S[C] | (1ul << (Pattern.Length - I - 1));
            S[C] = Value;
            I++;
        }

        return S;
    }

    //  PATCH FUNCTIONS

    /**
     * Increase the context until it is unique,
     * but don't let the pattern expand beyond Match_MaxBits.
     * @param patch The patch to grow.
     * @param text Source text.
     */
    protected void patch_addContext(Patch Patch, string Text)
    {
        if (Text.Length == 0)
        {
            return;
        }

        var Pattern = Text.Substring(Patch.Start2, Patch.Length1);
        var Padding = 0;

        // Look for the first and last matches of pattern in text.  If two
        // different matches are found, increase the pattern length.
        while (Text.IndexOf(Pattern, StringComparison.Ordinal) !=
               Text.LastIndexOf(Pattern, StringComparison.Ordinal) &&
               Pattern.Length < MatchMaxBits - PatchMargin - PatchMargin)
        {
            Padding += PatchMargin;
            Pattern = Text.JavaSubstring(Math.Max(0, Patch.Start2 - Padding),
                Math.Min(Text.Length, Patch.Start2 + Patch.Length1 + Padding));
        }

        // Add one chunk for good luck.
        Padding += PatchMargin;

        // Add the prefix.
        var Prefix = Text.JavaSubstring(Math.Max(0, Patch.Start2 - Padding), Patch.Start2);
        if (Prefix.Length != 0)
        {
            Patch.Diffs.Insert(0, new Diff(Operation.Equal, Prefix));
        }

        // Add the suffix.
        var Suffix = Text.JavaSubstring(Patch.Start2 + Patch.Length1,
            Math.Min(Text.Length, Patch.Start2 + Patch.Length1 + Padding));
        if (Suffix.Length != 0)
        {
            Patch.Diffs.Add(new Diff(Operation.Equal, Suffix));
        }

        // Roll back the start points.
        Patch.Start1 -= Prefix.Length;
        Patch.Start2 -= Prefix.Length;
        // Extend the lengths.
        Patch.Length1 += Prefix.Length + Suffix.Length;
        Patch.Length2 += Prefix.Length + Suffix.Length;
    }

    /**
     * Compute a list of patches to turn text1 into text2.
     * A set of diffs will be computed.
     * @param text1 Old text.
     * @param text2 New text.
     * @return List of Patch objects.
     */
    public List<Patch> patch_make(string Text1, string Text2)
    {
        // Check for null inputs not needed since null can't be passed in C#.
        // No diffs provided, compute our own.
        var Diffs = diff_main(Text1, Text2);
        if (Diffs.Count <= 2) return patch_make(Text1, Diffs);
        diff_cleanupSemantic(Diffs);
        diff_cleanupEfficiency(Diffs);

        return patch_make(Text1, Diffs);
    }

    /**
     * Compute a list of patches to turn text1 into text2.
     * text1 will be derived from the provided diffs.
     * @param diffs Array of Diff objects for text1 to text2.
     * @return List of Patch objects.
     */
    public List<Patch> patch_make(List<Diff> Diffs)
    {
        // Check for null inputs not needed since null can't be passed in C#.
        // No origin string provided, compute our own.
        var Text1 = diff_text1(Diffs);
        return patch_make(Text1, Diffs);
    }

    /**
     * Compute a list of patches to turn text1 into text2.
     * text2 is not provided, diffs are the delta between text1 and text2.
     * @param text1 Old text.
     * @param diffs Array of Diff objects for text1 to text2.
     * @return List of Patch objects.
     */
    public List<Patch> patch_make(string Text1, List<Diff> Diffs, bool SplitOnInsertion = false)
    {
        // Check for null inputs not needed since null can't be passed in C#.
        var Patches = new List<Patch>();
        if (Diffs.Count == 0)
        {
            return Patches; // Get rid of the null case.
        }

        var Patch = new Patch();
        var CharCount1 = 0; // Number of characters into the text1 string.
        var CharCount2 = 0; // Number of characters into the text2 string.
        // Start with text1 (prepatch_text) and apply the diffs until we arrive at
        // text2 (postpatch_text). We recreate the patches one by one to determine
        // context info.
        var PrepatchText = Text1;
        var PostpatchText = Text1;
        var ForceSplit = false;
        foreach (var Index in Enumerable.Range(0, Diffs.Count))
        {
            var ADiff = Diffs[Index];
            if (Patch.Diffs.Count == 0 && ADiff.Operation != Operation.Equal)
            {
                // A new patch starts here.
                Patch.Start1 = CharCount1;
                Patch.Start2 = CharCount2;
            }

            switch (ADiff.Operation)
            {
                case Operation.Insert:
                    Patch.Diffs.Add(ADiff);
                    Patch.Length2 += ADiff.Text.Length;
                    PostpatchText = PostpatchText.Insert(CharCount2, ADiff.Text);
                    ForceSplit = SplitOnInsertion;
                    break;
                case Operation.Delete:
                    Patch.Length1 += ADiff.Text.Length;
                    Patch.Diffs.Add(ADiff);
                    PostpatchText = PostpatchText.Remove(CharCount2, ADiff.Text.Length);
                    break;
                case Operation.Equal:
                    if (ADiff.Text.Length <= 2 * PatchMargin && Patch.Diffs.Count != 0 && (Index != Diffs.Count - 1) && !ForceSplit)
                    {
                        // Small equality inside a patch.
                        Patch.Diffs.Add(ADiff);
                        Patch.Length1 += ADiff.Text.Length;
                        Patch.Length2 += ADiff.Text.Length;
                    }

                    if (ADiff.Text.Length >= 2 * PatchMargin || ForceSplit)
                    {
                        // Time for a new patch.
                        if (Patch.Diffs.Count != 0)
                        {
                            patch_addContext(Patch, PrepatchText);
                            Patches.Add(Patch);
                            Patch = new Patch();
                            // Unlike Unidiff, our patch lists have a rolling context.
                            // https://github.com/google/diff-match-patch/wiki/Unidiff
                            // Update prepatch text & pos to reflect the application of the
                            // just completed patch.
                            PrepatchText = PostpatchText;
                            CharCount1 = CharCount2;
                            ForceSplit = false;
                        }
                    }

                    break;
            }

            // Update the current character count.
            if (ADiff.Operation != Operation.Insert)
            {
                CharCount1 += ADiff.Text.Length;
            }

            if (ADiff.Operation != Operation.Delete)
            {
                CharCount2 += ADiff.Text.Length;
            }
        }

        // Pick up the leftover patch if not empty.
        if (Patch.Diffs.Count == 0) return Patches;

        patch_addContext(Patch, PrepatchText);
        Patches.Add(Patch);
        return Patches;
    }

    /**
     * Given an array of patches, return another array that is identical.
     * @param patches Array of Patch objects.
     * @return Array of Patch objects.
     */
    public static List<Patch> patch_deepCopy(List<Patch> Patches)
    {
        var PatchesCopy = new List<Patch>();
        foreach (var APatch in Patches)
        {
            var PatchCopy = new Patch();
            foreach (var DiffCopy in APatch.Diffs.Select(ADiff => new Diff(ADiff.Operation, ADiff.Text)))
            {
                PatchCopy.Diffs.Add(DiffCopy);
            }

            PatchCopy.Start1 = APatch.Start1;
            PatchCopy.Start2 = APatch.Start2;
            PatchCopy.Length1 = APatch.Length1;
            PatchCopy.Length2 = APatch.Length2;

            PatchCopy.ContextLength = APatch.ContextLength;
            PatchCopy.Context = APatch.Context;
            PatchCopy.Skip = APatch.Skip;
            PatchesCopy.Add(PatchCopy);
        }

        return PatchesCopy;
    }

    // Crysknife customization
    protected static List<Patch> patch_constrain(IEnumerable<Patch> Patches)
    {
        var Result = new List<Patch>();

        foreach (var Patch in Patches)
        {
            if (Patch.Diffs.First().Operation == Operation.Equal)
            {
                var PreLimit = Patch.Context.HasFlag(MatchContext.Upper) ? Patch.ContextLength : 0;
                if (PreLimit >= 0 && PreLimit < Patch.Diffs.First().Text.Length)
                {
                    var Cutout = Patch.Diffs.First().Text.Length - PreLimit;
                    Patch.Start1 += Cutout;
                    Patch.Start2 += Cutout;
                    Patch.Length1 -= Cutout;
                    Patch.Length2 -= Cutout;
                    if (PreLimit == 0) Patch.Diffs.RemoveAt(0);
                    else Patch.Diffs.First().Text = Patch.Diffs.First().Text[Cutout..];
                }
            }

            if (Patch.Diffs.Last().Operation == Operation.Equal)
            {
                var PostLimit = Patch.Context.HasFlag(MatchContext.Lower) ? Patch.ContextLength : 0;
                if (PostLimit >= 0 && PostLimit < Patch.Diffs.Last().Text.Length)
                {
                    var Cutout = Patch.Diffs.Last().Text.Length - PostLimit;
                    Patch.Length1 -= Cutout;
                    Patch.Length2 -= Cutout;
                    if (PostLimit == 0) Patch.Diffs.RemoveAt(Patch.Diffs.Count - 1);
                    else Patch.Diffs.Last().Text = Patch.Diffs.Last().Text[..^Cutout];
                }
            }

            Result.Add(Patch);
        }

        return Result;
    }

    public struct ApplyResult
    {
        public string Text; // The new text
        public List<int> Locations = new(); // < 0 if failed, otherwise the applied starting location
        public List<int> Indices = new(); // Indices into the source patch array
        public List<Patch> Patches = new(); // The splitted patches

        public ApplyResult(string Text)
        {
            this.Text = Text;
        }
    }

    /**
     * Merge a set of patches onto the text.  Return a patched text, as well
     * as an array of true/false values indicating which patches were applied.
     * @param patches Array of Patch objects
     * @param text Old text.
     * @param MatchSequentially Whether to sequentially match the context instead of using expected locations
     * @return The new text and other insights of the patching process
     */
    public ApplyResult patch_apply(List<Patch> Patches, string Text, bool MatchSequentially = false)
    {
        // Deep copy the patches so that no changes are made to originals.
        Patches = patch_deepCopy(Patches);
        Patches = patch_constrain(Patches);
        if (Patches.Count == 0) return new ApplyResult(Text);

        var NullPadding = patch_addPadding(Patches);
        Text = NullPadding + Text + NullPadding;
        Patches = patch_splitMax(Patches, out var Indices);

        var X = 0;
        // delta keeps track of the offset between the expected and actual
        // location of the previous patch.  If there are patches expected at
        // positions 10 and 20, but the first patch was found at 12, delta is 2
        // and the second patch has an effective expected position of 22.
        var Delta = 0;
        var LastEndingLocation = 0;
        var Locations = new List<int>(Patches.Count);

        foreach (var APatch in Patches)
        {
            var ExpectedLoc = APatch.Start2 + Delta;
            if (MatchSequentially) ExpectedLoc = LastEndingLocation;

            var Text1 = diff_text1(APatch.Diffs);
            int StartLoc;
            var EndLoc = -1;
            if (Text1.Length > MatchMaxBits)
            {
                // patch_splitMax will only provide an oversized pattern
                // in the case of a monster delete.
                StartLoc = match_main(Text, Text1[..MatchMaxBits], ExpectedLoc);
                if (StartLoc != -1)
                {
                    EndLoc = match_main(Text, Text1[^MatchMaxBits..], ExpectedLoc + Text1.Length - MatchMaxBits);
                    if (EndLoc == -1 || StartLoc >= EndLoc)
                    {
                        // Can't find valid trailing context.  Drop this patch.
                        StartLoc = -1;
                    }
                }
            }
            else
            {
                StartLoc = match_main(Text, Text1, ExpectedLoc);
            }

            Locations.Add(StartLoc);
            LastEndingLocation = StartLoc + Text1.Length;

            if (StartLoc == -1)
            {
                // No match found.  :(
                // Subtract the delta for this failed patch from subsequent patches.
                Delta -= APatch.Length2 - APatch.Length1;
            }
            else
            {
                // Found a match.  :)
                Delta = StartLoc - ExpectedLoc;
                var Text2 = Text.JavaSubstring(StartLoc,
                    EndLoc == -1
                        ? Math.Min(StartLoc + Text1.Length, Text.Length)
                        : Math.Min(EndLoc + MatchMaxBits, Text.Length));

                if (Text1 == Text2)
                {
                    // Perfect match, just shove the Replacement text in.
                    Text = Text[..StartLoc] + diff_text2(APatch.Diffs) + Text[(StartLoc + Text1.Length)..];
                }
                else
                {
                    // Imperfect match.  Run a diff to get a framework of equivalent indices.
                    var Diffs = diff_main(Text1, Text2, false);
                    if (Text1.Length > MatchMaxBits && diff_levenshtein(Diffs) / (float)Text1.Length > PatchDeleteThreshold)
                    {
                        // The end points match, but the content is unacceptably bad.
                        Locations[X] = -1;
                    }
                    else
                    {
                        diff_cleanupSemanticLossless(Diffs);
                        var Index1 = 0;
                        foreach (var ADiff in APatch.Diffs)
                        {
                            if (ADiff.Operation != Operation.Equal)
                            {
                                var Index2 = diff_xIndex(Diffs, Index1);
                                if (ADiff.Operation == Operation.Insert)
                                {
                                    // Insertion
                                    Text = Text.Insert(StartLoc + Index2, ADiff.Text);
                                }
                                else if (ADiff.Operation == Operation.Delete)
                                {
                                    // Deletion
                                    Text = Text.Remove(StartLoc + Index2, diff_xIndex(Diffs, Index1 + ADiff.Text.Length) - Index2);
                                }
                            }

                            if (ADiff.Operation != Operation.Delete)
                            {
                                Index1 += ADiff.Text.Length;
                            }
                        }
                    }
                }
            }

            X++;
        }

        // Strip the padding off.
        Text = Text.Substring(NullPadding.Length, Text.Length - 2 * NullPadding.Length);
        return new ApplyResult(Text) { Locations = Locations, Indices = Indices, Patches = Patches };
    }

    /**
     * Add some padding on text start and end so that edges can match something.
     * Intended to be called only from within patch_apply.
     * @param patches Array of Patch objects.
     * @return The padding string added to each side.
     */
    public string patch_addPadding(List<Patch> Patches)
    {
        var PaddingLength = PatchMargin;
        var NullPadding = string.Empty;
        for (short X = 1; X <= PaddingLength; X++)
        {
            NullPadding += (char)X;
        }

        // Bump all the patches forward.
        foreach (var APatch in Patches)
        {
            APatch.Start1 += PaddingLength;
            APatch.Start2 += PaddingLength;
        }

        // Add some padding on start of first diff.
        var Patch = Patches.First();
        var Diffs = Patch.Diffs;
        if (Diffs.Count == 0 || Diffs.First().Operation != Operation.Equal)
        {
            // Add nullPadding equality.
            Diffs.Insert(0, new Diff(Operation.Equal, NullPadding));
            Patch.Start1 -= PaddingLength; // Should be 0.
            Patch.Start2 -= PaddingLength; // Should be 0.
            Patch.Length1 += PaddingLength;
            Patch.Length2 += PaddingLength;
        }
        else if (PaddingLength > Diffs.First().Text.Length)
        {
            // Grow first equality.
            var FirstDiff = Diffs.First();
            var ExtraLength = PaddingLength - FirstDiff.Text.Length;
            FirstDiff.Text = NullPadding[FirstDiff.Text.Length..] + FirstDiff.Text;
            Patch.Start1 -= ExtraLength;
            Patch.Start2 -= ExtraLength;
            Patch.Length1 += ExtraLength;
            Patch.Length2 += ExtraLength;
        }

        // Add some padding on end of last diff.
        Patch = Patches.Last();
        Diffs = Patch.Diffs;
        if (Diffs.Count == 0 || Diffs.Last().Operation != Operation.Equal)
        {
            // Add nullPadding equality.
            Diffs.Add(new Diff(Operation.Equal, NullPadding));
            Patch.Length1 += PaddingLength;
            Patch.Length2 += PaddingLength;
        }
        else if (PaddingLength > Diffs.Last().Text.Length)
        {
            // Grow last equality.
            var LastDiff = Diffs.Last();
            var ExtraLength = PaddingLength - LastDiff.Text.Length;
            LastDiff.Text += NullPadding[..ExtraLength];
            Patch.Length1 += ExtraLength;
            Patch.Length2 += ExtraLength;
        }

        return NullPadding;
    }

    /**
     * Look through the patches and break up any which are longer than the
     * maximum limit of the match algorithm.
     * Intended to be called only from within patch_apply.
     * @param patches List of Patch objects.
     */
    public List<Patch> patch_splitMax(IEnumerable<Patch> Patches, out List<int> MappingIndices)
    {
        var Indices = MappingIndices = new List<int>();

        return Patches.SelectMany<Patch, Patch>((BigPatch, Index) =>
        {
            if (BigPatch.Skip == BooleanOverride.True)
            {
                return Array.Empty<Patch>();
            }

            if (BigPatch.Length1 <= MatchMaxBits)
            {
                Indices.Add(Index);
                return new[] { BigPatch };
            }

            var NewPatches = new List<Patch>();
            var Start1 = BigPatch.Start1;
            var Start2 = BigPatch.Start2;
            var PreContext = string.Empty;
            while (BigPatch.Diffs.Count != 0)
            {
                // Create one of several smaller patches.
                var Patch = new Patch();
                var Empty = true;
                Patch.Start1 = Start1 - PreContext.Length;
                Patch.Start2 = Start2 - PreContext.Length;
                if (PreContext.Length != 0)
                {
                    Patch.Length1 = Patch.Length2 = PreContext.Length;
                    Patch.Diffs.Add(new Diff(Operation.Equal, PreContext));
                }

                while (BigPatch.Diffs.Count != 0 && Patch.Length1 < MatchMaxBits - PatchMargin)
                {
                    var DiffType = BigPatch.Diffs[0].Operation;
                    var DiffText = BigPatch.Diffs[0].Text;
                    if (DiffType == Operation.Insert)
                    {
                        // Insertions are harmless.
                        Patch.Length2 += DiffText.Length;
                        Start2 += DiffText.Length;
                        Patch.Diffs.Add(BigPatch.Diffs.First());
                        BigPatch.Diffs.RemoveAt(0);
                        Empty = false;
                    }
                    else if (DiffType == Operation.Delete && Patch.Diffs.Count == 1 &&
                             Patch.Diffs.First().Operation == Operation.Equal && DiffText.Length > 2 * MatchMaxBits)
                    {
                        // This is a large deletion.  Let it pass in one chunk.
                        Patch.Length1 += DiffText.Length;
                        Start1 += DiffText.Length;
                        Empty = false;
                        Patch.Diffs.Add(new Diff(DiffType, DiffText));
                        BigPatch.Diffs.RemoveAt(0);
                    }
                    else
                    {
                        // Deletion or equality.  Only take as much as we can stomach.
                        DiffText = DiffText[..Math.Min(DiffText.Length, MatchMaxBits - Patch.Length1 - PatchMargin)];
                        Patch.Length1 += DiffText.Length;
                        Start1 += DiffText.Length;
                        if (DiffType == Operation.Equal)
                        {
                            Patch.Length2 += DiffText.Length;
                            Start2 += DiffText.Length;
                        }
                        else
                        {
                            Empty = false;
                        }

                        Patch.Diffs.Add(new Diff(DiffType, DiffText));
                        if (DiffText == BigPatch.Diffs[0].Text)
                        {
                            BigPatch.Diffs.RemoveAt(0);
                        }
                        else
                        {
                            BigPatch.Diffs[0].Text = BigPatch.Diffs[0].Text[DiffText.Length..];
                        }
                    }
                }

                // Compute the head context for the next patch.
                PreContext = diff_text2(Patch.Diffs);
                PreContext = PreContext[Math.Max(0, PreContext.Length - PatchMargin)..];

                var Text1 = diff_text1(BigPatch.Diffs);
                // Append the end context for this patch.
                var PostContext = Text1.Length > PatchMargin ? Text1[..PatchMargin] : Text1;

                if (PostContext.Length != 0)
                {
                    Patch.Length1 += PostContext.Length;
                    Patch.Length2 += PostContext.Length;
                    if (Patch.Diffs.Count != 0 && Patch.Diffs[^1].Operation == Operation.Equal)
                    {
                        Patch.Diffs[^1].Text += PostContext;
                    }
                    else
                    {
                        Patch.Diffs.Add(new Diff(Operation.Equal, PostContext));
                    }
                }

                if (Empty) continue;

                Patch.ContextLength = BigPatch.ContextLength;
                Patch.Context = BigPatch.Context;
                Patch.Skip = BigPatch.Skip;
                Indices.Add(Index);

                NewPatches.Add(Patch);
            }

            return NewPatches;
        }).ToList();
    }

    /**
     * Take a list of patches and return a textual representation.
     * @param patches List of Patch objects.
     * @return Text representation of patches.
     */
    public static string patch_toText(List<Patch> Patches)
    {
        var Text = new StringBuilder();
        foreach (var APatch in Patches)
        {
            Text.Append(APatch);
        }

        return Text.ToString();
    }

    /**
     * Parse a textual representation of patches and return a List of Patch
     * objects.
     * @param textline Text representation of patches.
     * @return List of Patch objects.
     * @throws ArgumentException If invalid input.
     */
    public static List<Patch> patch_fromText(string Textline)
    {
        var Patches = new List<Patch>();
        if (Textline.Length == 0)
        {
            return Patches;
        }

        string[] Text = Textline.Split('\n');
        var TextPointer = 0;
        var PatchHeader = new Regex(@"^@@ -(\d+),?(\d*) \+(\d+),?(\d*) @@$");
        while (TextPointer < Text.Length)
        {
            var M = PatchHeader.Match(Text[TextPointer]);
            if (!M.Success)
            {
                throw new ArgumentException("Invalid patch string: " + Text[TextPointer]);
            }

            var Patch = new Patch();
            Patches.Add(Patch);
            Patch.Start1 = Convert.ToInt32(M.Groups[1].Value);
            if (M.Groups[2].Length == 0)
            {
                Patch.Start1--;
                Patch.Length1 = 1;
            }
            else if (M.Groups[2].Value == "0")
            {
                Patch.Length1 = 0;
            }
            else
            {
                Patch.Start1--;
                Patch.Length1 = Convert.ToInt32(M.Groups[2].Value);
            }

            Patch.Start2 = Convert.ToInt32(M.Groups[3].Value);
            if (M.Groups[4].Length == 0)
            {
                Patch.Start2--;
                Patch.Length2 = 1;
            }
            else if (M.Groups[4].Value == "0")
            {
                Patch.Length2 = 0;
            }
            else
            {
                Patch.Start2--;
                Patch.Length2 = Convert.ToInt32(M.Groups[4].Value);
            }

            TextPointer++;

            while (TextPointer < Text.Length)
            {
                char Sign;
                try
                {
                    Sign = Text[TextPointer][0];
                }
                catch (IndexOutOfRangeException)
                {
                    // Blank line?  Whatever.
                    TextPointer++;
                    continue;
                }

                var Line = Text[TextPointer][1..];
                Line = Line.Replace("+", "%2b");
                Line = HttpUtility.UrlDecode(Line);
                if (Sign == '-')
                {
                    // Deletion.
                    Patch.Diffs.Add(new Diff(Operation.Delete, Line));
                }
                else if (Sign == '+')
                {
                    // Insertion.
                    Patch.Diffs.Add(new Diff(Operation.Insert, Line));
                }
                else if (Sign == ' ')
                {
                    // Minor equality.
                    Patch.Diffs.Add(new Diff(Operation.Equal, Line));
                }
                else if (Sign == '@')
                {
                    // Start of next patch.
                    break;
                }
                else
                {
                    // WTF?
                    throw new ArgumentException("Invalid patch mode '" + Sign + "' in: " + Line);
                }

                TextPointer++;
            }
        }

        return Patches;
    }

    /**
     * Encodes a string with URI-style % escaping.
     * Compatible with JavaScript's encodeURI function.
     *
     * @param str The string to encode.
     * @return The encoded string.
     */
    public static string EncodeUri(string Str)
    {
        // C# is overzealous in the replacements.  Walk back on a few.
        return new StringBuilder(HttpUtility.UrlEncode(Str)).Replace('+', ' ')
            .Replace("%20", " ")
            .Replace("%21", "!")
            .Replace("%2a", "*")
            .Replace("%27", "'")
            .Replace("%28", "(")
            .Replace("%29", ")")
            .Replace("%3b", ";")
            .Replace("%2f", "/")
            .Replace("%3f", "?")
            .Replace("%3a", ":")
            .Replace("%40", "@")
            .Replace("%26", "&")
            .Replace("%3d", "=")
            .Replace("%2b", "+")
            .Replace("%24", "$")
            .Replace("%2c", ",")
            .Replace("%23", "#")
            .Replace("%7e", "~")
            .ToString();
    }
}
