namespace Crysknife.Tests;

/// <summary>
/// Tests for CommentTagPacker — the Pack/Unpack round-trip that normalizes
/// comment tags into a portable representation for patch files.
/// </summary>
public class CommentTagPackerTests
{
    private static CommentTagFormat MakeFormat(string pluginName = "TestPlugin")
    {
        return new CommentTagFormat(pluginName)
        {
            PrefixRegex = "",
            SuffixRegex = "",
            BeginRegex = @"\s*Begin",
            EndRegex = @"\s*End",
            PrefixCtor = "",
            SuffixCtor = "",
            BeginCtor = " Begin",
            EndCtor = " End",
        };
    }

    private static CommentTagPacker MakePacker(string pluginName = "TestPlugin")
    {
        return new CommentTagPacker(pluginName, MakeFormat(pluginName));
    }

    // ──────────── HasAnyMatch ────────────

    [Fact]
    public void HasAnyMatch_DetectsCommentTag()
    {
        var packer = MakePacker();
        // HasAnyMatch is only true for *authoritative* comment-tag anchors that
        // InjectionRegex itself would recognise — i.e. a tag that belongs to one
        // of the three injection forms (multi-line Begin/End, trailing single-line,
        // standalone next-line). Lone tag-looking lines without the required
        // structural context are payload, not tags.
        Assert.True(packer.HasAnyMatch("// TestPlugin Begin\npayload();\n// TestPlugin End\n"));
        Assert.True(packer.HasAnyMatch("existing_code(); // TestPlugin\n"));
        Assert.True(packer.HasAnyMatch("// TestPlugin\nfollowing_line();\n"));
        Assert.False(packer.HasAnyMatch("// TestPlugin\n")); // no following line — not a real anchor
        Assert.False(packer.HasAnyMatch("// OtherPlugin\n"));
        Assert.False(packer.HasAnyMatch("no comment here\n"));
    }

    // ──────────── Pack/Unpack symmetry ────────────

    [Fact]
    public void PackUnpack_SimpleTag_RoundTrips()
    {
        var packer = MakePacker();
        var vars = new Dictionary<string, string>();
        // The next-line form requires a content line after the tag — that is
        // what turns it into an authoritative injection anchor in the first place.
        var original = "// TestPlugin\nfollowing_line();\n";

        int packInc = 0;
        var packed = packer.Pack(original, ref packInc, false);

        // Packed form should be normalized: @TestPluginTag(...)
        Assert.Contains("@TestPluginTag(", packed);

        int unpackInc = 0;
        var unpacked = packer.Unpack(packed, ref unpackInc, vars);

        Assert.Equal(original, unpacked);
    }

    [Fact]
    public void PackUnpack_TagWithBeginEnd_RoundTrips()
    {
        var packer = MakePacker();
        var vars = new Dictionary<string, string>();

        // Begin/End markers are only authoritative as a matched pair, which is
        // exactly what the multi-line injection form recognises.
        var block = "// TestPlugin Begin\npayload();\n// TestPlugin End\n";

        int inc = 0;
        var packed = packer.Pack(block, ref inc, false);

        Assert.Contains("@TestPluginTagBegin(", packed);
        Assert.Contains("@TestPluginTagEnd(", packed);

        int unpackInc = 0;
        var unpacked = packer.Unpack(packed, ref unpackInc, vars);

        Assert.Equal(block, unpacked);
    }

    [Fact]
    public void PackUnpack_TagWithExtraComment_RoundTrips()
    {
        var packer = MakePacker();
        var vars = new Dictionary<string, string>();
        // Tag line carries extra commentary; still requires a following content
        // line to qualify as the next-line injection form.
        var original = "// TestPlugin extra info\nfollowing_line();\n";

        int inc = 0;
        var packed = packer.Pack(original, ref inc, false);
        int unpackInc = 0;
        var unpacked = packer.Unpack(packed, ref unpackInc, vars);

        Assert.Equal(original, unpacked);
    }

    // ──────────── Increment tracking ────────────

    [Fact]
    public void Pack_TracksLengthDifference()
    {
        var packer = MakePacker();
        var original = "// TestPlugin\nfollowing_line();\n";

        int inc = 0;
        var packed = packer.Pack(original, ref inc, false);

        Assert.Equal(packed.Length - original.Length, inc);
    }

    [Fact]
    public void Unpack_TracksLengthDifference()
    {
        var packer = MakePacker();
        var vars = new Dictionary<string, string>();
        var original = "// TestPlugin\nfollowing_line();\n";

        int packInc = 0;
        var packed = packer.Pack(original, ref packInc, false);

        int unpackInc = 0;
        var unpacked = packer.Unpack(packed, ref unpackInc, vars);

        Assert.Equal(unpacked.Length - packed.Length, unpackInc);
    }

    // ──────────── No-op on non-matching content ────────────

    [Fact]
    public void Pack_LeavesNonMatchingContentUnchanged()
    {
        var packer = MakePacker();
        var content = "regular code line;\n";

        int inc = 0;
        var packed = packer.Pack(content, ref inc, false);

        Assert.Equal(content, packed);
        Assert.Equal(0, inc);
    }

    // ──────────── Payload must not be mistaken for a tag ────────────

    /// <summary>
    /// Regression for a real bug observed in production: inside a legitimate
    /// <c>// Plugin: Begin … // Plugin: End</c> block, a payload line that
    /// happens to start with <c>// Plugin&lt;space&gt;</c> followed by ordinary
    /// English used to be picked up by the greedy PackRegex and rewritten
    /// into a bogus <c>@PluginTag( ...)</c> entry — which then got
    /// reconstructed into a malformed comment tag on apply.
    ///
    /// Pack must now restrict itself to authoritative anchors found by
    /// InjectionRegex, so the payload line is preserved verbatim.
    /// </summary>
    [Fact]
    public void Pack_DoesNotRewritePayloadLineLookingLikeATag_InsideBeginEndBlock()
    {
        // Mirror the real-world config that triggered the bug: Prefix/Suffix
        // empty, Begin/End are ": Begin" / ": End" (no leading space, just a
        // colon). This is the configuration where the false-positive bites.
        var format = new CommentTagFormat("TestPlugin")
        {
            PrefixRegex = "",
            SuffixRegex = "",
            BeginRegex = ": Begin",
            EndRegex = ": End",
            PrefixCtor = "",
            SuffixCtor = "",
            BeginCtor = ": Begin",
            EndCtor = ": End",
        };
        var packer = new CommentTagPacker("TestPlugin", format);
        var vars = new Dictionary<string, string>();

        // Topology that reproduces the bug: a Begin/End block whose body
        // contains a comment line starting with "// TestPlugin " followed by
        // ordinary words. That line is *not* a tag (no ": Begin"/": End"
        // suffix, no trailing code), but a greedy regex would have grabbed it.
        var original =
            "    int field_a;\n" +
            "    // TestPlugin: Begin\n" +
            "    int field_b;\n" +
            "    int field_c;\n" +
            "    int field_d;\n" +
            "    // TestPlugin auxiliary helpers\n" +
            "    int field_e;\n" +
            "    int field_f;\n" +
            "    int field_g;\n" +
            "    // TestPlugin: End\n";

        int packInc = 0;
        var packed = packer.Pack(original, ref packInc, false);

        // Begin/End markers should be packed…
        Assert.Contains("@TestPluginTagBegin(", packed);
        Assert.Contains("@TestPluginTagEnd(", packed);
        // …but the payload comment line must stay untouched.
        Assert.Contains("// TestPlugin auxiliary helpers\n", packed);
        Assert.DoesNotContain("@TestPluginTag( auxiliary",  packed);
        Assert.DoesNotContain("@TestPluginTag(auxiliary",   packed);

        // Full round-trip: unpacking must reproduce the original verbatim.
        int unpackInc = 0;
        var unpacked = packer.Unpack(packed, ref unpackInc, vars);
        Assert.Equal(original, unpacked);
    }

    /// <summary>
    /// A tag-looking comment line that does not satisfy any of the three
    /// injection forms (no Begin/End partner, no preceding code on the same
    /// line, no following content line) is just payload and must never be
    /// rewritten by Pack.
    /// </summary>
    [Fact]
    public void Pack_DoesNotRewriteIsolatedTagLikeCommentWithoutFollowingContent()
    {
        var packer = MakePacker();
        // End-of-buffer tag-looking line, no next line exists, so it cannot
        // satisfy the next-line injection form and therefore is not an anchor.
        var content = "// TestPlugin is referenced here just as prose.\n";

        int inc = 0;
        var packed = packer.Pack(content, ref inc, false);

        Assert.Equal(content, packed);
        Assert.Equal(0, inc);
    }

    // ──────────── GetDefaultTag ────────────

    [Fact]
    public void GetDefaultTag_ProducesValidTag()
    {
        var packer = MakePacker();
        var vars = new Dictionary<string, string>();
        var tag = packer.GetDefaultTag(vars);

        // Should contain the plugin name
        Assert.Contains("TestPlugin", tag);
    }
}

/// <summary>
/// Tests for InjectionRegex — the pattern matcher that finds injected code blocks.
/// </summary>
public class InjectionRegexTests
{
    private static InjectionRegex MakeRegex(string pluginName = "TestPlugin")
    {
        var format = new CommentTagFormat(pluginName)
        {
            PrefixRegex = "",
            SuffixRegex = "",
            BeginRegex = @"\s*Begin",
            EndRegex = @"\s*End",
        };
        return new InjectionRegex(format);
    }

    [Fact]
    public void Unpatch_RemovesSingleLineInjection()
    {
        var regex = MakeRegex();
        var input = "existing_code(); // TestPlugin\nnormal_line();\n";
        var result = regex.Unpatch(input);

        // Single-line form: the injection line should be removed
        Assert.DoesNotContain("existing_code", result);
        Assert.Contains("normal_line", result);
    }

    [Fact]
    public void Unpatch_RemovesMultiLineInjection()
    {
        var regex = MakeRegex();
        var input = "before();\n// TestPlugin Begin\ninjected_line1();\ninjected_line2();\n// TestPlugin End\nafter();\n";
        var result = regex.Unpatch(input);

        Assert.Contains("before", result);
        Assert.Contains("after", result);
        Assert.DoesNotContain("injected_line1", result);
    }

    [Fact]
    public void Unpatch_RestoresDeletion()
    {
        var regex = MakeRegex();
        // Deletion marker: tag starts with "TestPlugin-" (note the hyphen)
        // Multi-line form: // Tag Begin\n<content>\n// Tag End\n
        // Content lines are commented code that gets uncommented on unpatch
        var input = "before();\n// TestPlugin-Reason Begin\n// deleted_code();\n// TestPlugin-Reason End\nafter();\n";
        var result = regex.Unpatch(input);

        Assert.Contains("before", result);
        Assert.Contains("after", result);
        // Deleted code should be restored (uncommented)
        Assert.Contains("deleted_code", result);
    }
}
