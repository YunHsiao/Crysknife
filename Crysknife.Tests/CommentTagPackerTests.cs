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
        Assert.True(packer.HasAnyMatch("// TestPlugin\n"));
        Assert.True(packer.HasAnyMatch("// TestPlugin some comment\n"));
        Assert.False(packer.HasAnyMatch("// OtherPlugin\n"));
        Assert.False(packer.HasAnyMatch("no comment here\n"));
    }

    // ──────────── Pack/Unpack symmetry ────────────

    [Fact]
    public void PackUnpack_SimpleTag_RoundTrips()
    {
        var packer = MakePacker();
        var vars = new Dictionary<string, string>();
        var original = "// TestPlugin\n";

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

        var beginTag = "// TestPlugin Begin\n";
        var endTag = "// TestPlugin End\n";

        int inc = 0;
        var packedBegin = packer.Pack(beginTag, ref inc, false);
        var packedEnd = packer.Pack(endTag, ref inc, false);

        Assert.Contains("@TestPluginTagBegin(", packedBegin);
        Assert.Contains("@TestPluginTagEnd(", packedEnd);

        int unpackInc = 0;
        var unpackedBegin = packer.Unpack(packedBegin, ref unpackInc, vars);
        var unpackedEnd = packer.Unpack(packedEnd, ref unpackInc, vars);

        Assert.Equal(beginTag, unpackedBegin);
        Assert.Equal(endTag, unpackedEnd);
    }

    [Fact]
    public void PackUnpack_TagWithExtraComment_RoundTrips()
    {
        var packer = MakePacker();
        var vars = new Dictionary<string, string>();
        var original = "// TestPlugin extra info\n";

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
        var original = "// TestPlugin\n";

        int inc = 0;
        var packed = packer.Pack(original, ref inc, false);

        Assert.Equal(packed.Length - original.Length, inc);
    }

    [Fact]
    public void Unpack_TracksLengthDifference()
    {
        var packer = MakePacker();
        var vars = new Dictionary<string, string>();
        var original = "// TestPlugin\n";

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
    public void HasAnyMatch_FindsTag()
    {
        var regex = MakeRegex();
        Assert.True(regex.HasAnyMatch("// TestPlugin\n"));
        Assert.False(regex.HasAnyMatch("no tags here\n"));
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
