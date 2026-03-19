namespace Crysknife.Tests;

using DiffMatchPatch;

/// <summary>
/// Tests for the Patcher — the core diff/patch engine that drives Crysknife.
/// Exercises: Diff → Serialize → Deserialize → Apply roundtrip,
/// Decorator-based conditional patching, and Merge across IncrementalModes.
/// </summary>
public class PatcherTests : IDisposable
{
    // ──────────── Helpers ────────────

    private const string PluginName = "TestPlugin";

    private static readonly string IntermediateDir = Path.Combine(
        Path.GetDirectoryName(typeof(PatcherTests).Assembly.Location)!,
        "..", "..", "..", "Intermediate");

    private static CommentTagFormat MakeFormat()
    {
        return new CommentTagFormat(PluginName)
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

    private Patcher MakePatcher(bool @protected = false, IncrementalMode mode = IncrementalMode.Disabled)
    {
        var patcher = new Patcher(@protected, mode);
        var format = MakeFormat();
        patcher.Injection = new InjectionRegex(format);
        patcher.CommentTag = format.Tag;
        patcher.Packers.Add(new CommentTagPacker(PluginName, format));
        patcher.Variables = new Dictionary<string, string>();
        // Point CurrentPatch to a temp file so Load() inside Generate() won't fail
        var patchBase = Path.Combine(_tempDir, "default_patch_" + Guid.NewGuid().ToString("N")[..8]);
        File.WriteAllText(patchBase + patcher.DefaultExtension, "");
        patcher.CurrentPatch = patchBase;
        return patcher;
    }

    private readonly string _tempDir;

    public PatcherTests()
    {
        _tempDir = Path.Combine(IntermediateDir, Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
        // Remove Intermediate dir itself if empty
        try { if (Directory.Exists(IntermediateDir) && Directory.GetFileSystemEntries(IntermediateDir).Length == 0) Directory.Delete(IntermediateDir); } catch { }
    }

    /// <summary>
    /// Simulates the full pipeline: original source → inject code → generate patch →
    /// serialize to file → deserialize from file → apply to original → verify.
    /// </summary>
    private string DiffSerializeDeserializeApply(Patcher patcher, string before, string after)
    {
        // Generate needs Load() for history — ensure an empty patch file exists
        var patchBase = GetPatchBase(patcher);
        EnsureEmptyPatchFile(patchBase, patcher.DefaultExtension);

        patcher.CurrentPatch = patchBase;
        var patches = patcher.Generate(before, after);

        // Save (serialize) to file
        patcher.Save(patches);
        var serialized = File.ReadAllText(patchBase + patcher.DefaultExtension);

        // Load (deserialize) from a fresh copy
        var patchBase2 = patchBase + "_rt";
        File.WriteAllText(patchBase2 + patcher.DefaultExtension, serialized);
        patcher.CurrentPatch = patchBase2;
        var deserialized = patcher.Load();

        // Apply to original
        patcher.Apply(deserialized, before, Path.Combine(_tempDir, "dump"), false, out var patched);
        return patched;
    }

    private string GetPatchBase(Patcher patcher)
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        return Path.Combine(_tempDir, $"patch_{id}");
    }

    private static void EnsureEmptyPatchFile(string patchBase, string extension)
    {
        File.WriteAllText(patchBase + extension, "");
    }

    // ──────────── Diff → Apply Roundtrip ────────────

    [Fact]
    public void SingleLineInjection_RoundTrips()
    {
        var patcher = MakePatcher();

        var before = "void Foo() {\n    Original();\n}\n";
        var after = "void Foo() {\n    Original();\n    Added(); // TestPlugin\n}\n";

        var result = DiffSerializeDeserializeApply(patcher, before, after);
        Assert.Equal(after, result);
    }

    [Fact]
    public void MultiLineInjection_RoundTrips()
    {
        var patcher = MakePatcher();

        var before = "void Foo() {\n    Original();\n}\n";
        var after = "void Foo() {\n    Original();\n// TestPlugin Begin\n    Line1();\n    Line2();\n// TestPlugin End\n}\n";

        var result = DiffSerializeDeserializeApply(patcher, before, after);
        Assert.Equal(after, result);
    }

    [Fact]
    public void NextLineInjection_RoundTrips()
    {
        var patcher = MakePatcher();

        var before = "#include \"header.h\"\nvoid Foo() {}\n";
        var after = "// TestPlugin\n#include \"extra.h\"\n#include \"header.h\"\nvoid Foo() {}\n";

        var result = DiffSerializeDeserializeApply(patcher, before, after);
        Assert.Equal(after, result);
    }

    [Fact]
    public void EmptyPatch_NoChanges()
    {
        var patcher = MakePatcher();

        var content = "void Foo() {\n    Original();\n}\n";
        // Same before and after = no patches generated
        // Generate() internally calls Load() then Merge(), but with empty history + no diff
        var patches = patcher.Generate(content, content);
        Assert.False(patches.IsValid());
    }

    [Fact]
    public void LargeFile_MultipleInjections_RoundTrips()
    {
        var patcher = MakePatcher();

        // Build a realistic source file (~200 lines)
        var lines = new List<string> { "#include \"pch.h\"", "" };
        for (int i = 0; i < 50; i++)
        {
            lines.Add($"void Function{i}() {{");
            lines.Add($"    DoSomething({i});");
            lines.Add("}");
            lines.Add("");
        }
        var before = string.Join("\n", lines) + "\n";

        // Inject at multiple locations
        var afterLines = new List<string>(lines);
        afterLines.Insert(6, "    Injected1(); // TestPlugin");
        afterLines.Insert(16, "// TestPlugin Begin");
        afterLines.Insert(17, "    InjectedBlock1();");
        afterLines.Insert(18, "    InjectedBlock2();");
        afterLines.Insert(19, "// TestPlugin End");
        var after = string.Join("\n", afterLines) + "\n";

        var result = DiffSerializeDeserializeApply(patcher, before, after);
        Assert.Equal(after, result);
    }

    // ──────────── Patch File Types ────────────

    [Fact]
    public void ProtectedMode_UsesDifferentExtension()
    {
        var protectedPatcher = MakePatcher(@protected: true);
        var normalPatcher = MakePatcher(@protected: false);

        Assert.Equal(".protected.patch", protectedPatcher.DefaultExtension);
        Assert.Equal(".patch", normalPatcher.DefaultExtension);
    }

    // ──────────── Apply with offset context ────────────

    [Fact]
    public void Apply_ToleratesMinorContextShift()
    {
        var patcher = MakePatcher();

        // Generate patch against original
        var before = "AAA\nBBB\nCCC\nDDD\nEEE\n";
        var after = "AAA\nBBB\nCCC\nINJECTED(); // TestPlugin\nDDD\nEEE\n";

        var patches = patcher.Generate(before, after);
        patcher.Save(patches);

        // Load and apply to a slightly modified version (extra line at top shifts everything)
        var deserialized = patcher.Load();
        var modified = "ZZZ\nAAA\nBBB\nCCC\nDDD\nEEE\n";
        var success = patcher.Apply(deserialized, modified, Path.Combine(_tempDir, "dump"), false, out var patched);

        Assert.True(success);
        Assert.Contains("INJECTED", patched);
        Assert.Contains("ZZZ", patched);
    }

    // ──────────── Decorator: IsTruthy conditional skip ────────────

    [Fact]
    public void Decorator_IsTruthy_SkipsWhenFalse()
    {
        var patcher = MakePatcher();
        patcher.Variables = new Dictionary<string, string> { { "FEATURE_ENABLED", "0" } };

        var before = "void Foo() {\n    Original();\n}\n";
        // The decorator @Crysknife(IsTruthy=FEATURE_ENABLED) makes this patch conditional
        var after = "void Foo() {\n    Original();\n    Conditional(); // TestPlugin @Crysknife(IsTruthy=FEATURE_ENABLED)\n}\n";

        var result = DiffSerializeDeserializeApply(patcher, before, after);

        // With FEATURE_ENABLED=0, the patch should be skipped
        Assert.DoesNotContain("Conditional", result);
    }

    [Fact]
    public void Decorator_IsTruthy_AppliesWhenTrue()
    {
        var patcher = MakePatcher();
        patcher.Variables = new Dictionary<string, string> { { "FEATURE_ENABLED", "1" } };

        var before = "void Foo() {\n    Original();\n}\n";
        var after = "void Foo() {\n    Original();\n    Conditional(); // TestPlugin @Crysknife(IsTruthy=FEATURE_ENABLED)\n}\n";

        var result = DiffSerializeDeserializeApply(patcher, before, after);

        Assert.Contains("Conditional", result);
    }

    [Fact]
    public void Decorator_IsTruthy_NegatedVariable()
    {
        var patcher = MakePatcher();
        patcher.Variables = new Dictionary<string, string> { { "FEATURE_DISABLED", "1" } };

        var before = "void Foo() {\n    Original();\n}\n";
        // !FEATURE_DISABLED means: skip when FEATURE_DISABLED is truthy
        var after = "void Foo() {\n    Original();\n    Conditional(); // TestPlugin @Crysknife(IsTruthy=!FEATURE_DISABLED)\n}\n";

        var result = DiffSerializeDeserializeApply(patcher, before, after);

        // FEATURE_DISABLED=1 is truthy, negated → should skip
        Assert.DoesNotContain("Conditional", result);
    }

    // ──────────── Decorator: NewerThan / OlderThan ────────────

    [Fact]
    public void Decorator_NewerThan_SkipsOnOlderEngine()
    {
        Utils.CurrentEngineVersion = EngineVersion.Create("5.2.0");
        var patcher = MakePatcher();

        var before = "void Foo() {\n    Original();\n}\n";
        var after = "void Foo() {\n    Original();\n    NewApi(); // TestPlugin @Crysknife(NewerThan=5.3)\n}\n";

        var result = DiffSerializeDeserializeApply(patcher, before, after);

        Assert.DoesNotContain("NewApi", result);

        // Cleanup
        Utils.CurrentEngineVersion = EngineVersion.Create("0.0.0");
    }

    [Fact]
    public void Decorator_NewerThan_AppliesOnNewerEngine()
    {
        Utils.CurrentEngineVersion = EngineVersion.Create("5.4.0");
        var patcher = MakePatcher();

        var before = "void Foo() {\n    Original();\n}\n";
        var after = "void Foo() {\n    Original();\n    NewApi(); // TestPlugin @Crysknife(NewerThan=5.3)\n}\n";

        var result = DiffSerializeDeserializeApply(patcher, before, after);

        Assert.Contains("NewApi", result);

        Utils.CurrentEngineVersion = EngineVersion.Create("0.0.0");
    }

    // ──────────── Merge: IncrementalMode ────────────

    [Fact]
    public void Merge_Disabled_ReplacesAllPatches()
    {
        var patcher = MakePatcher(mode: IncrementalMode.Disabled);

        var original = "AAA\nBBB\nCCC\n";
        var patched1 = "AAA\nBBB\nOLD_INJECT(); // TestPlugin\nCCC\n";

        // Generate first set of patches (simulating history) and save
        var history = patcher.Generate(original, patched1);
        patcher.Save(history);

        // Now generate new patches against the same original, with different injection
        // Generate() internally loads the history and merges
        var patched2 = "AAA\nBBB\nNEW_INJECT(); // TestPlugin\nCCC\n";
        var newPatches = patcher.Generate(original, patched2);
        patcher.Save(newPatches);

        // Load and apply — should contain NEW_INJECT (Disabled mode replaces all)
        var deserialized = patcher.Load();
        patcher.Apply(deserialized, original, Path.Combine(_tempDir, "dump"), false, out var result);

        Assert.Contains("NEW_INJECT", result);
    }

    // ──────────── PatchContextLength ────────────

    [Fact]
    public void PatchContextLength_IsConfigurable()
    {
        var patcher = MakePatcher();
        Assert.Equal(250, patcher.PatchContextLength); // Default

        patcher.PatchContextLength = 100;
        Assert.Equal(100, patcher.PatchContextLength);
    }

    // ──────────── MatchContentTolerance ────────────

    [Fact]
    public void MatchContentTolerance_IsConfigurable()
    {
        var patcher = MakePatcher();
        Assert.Equal(0.4f, patcher.MatchContentTolerance);

        patcher.MatchContentTolerance = 0.6f;
        Assert.Equal(0.6f, patcher.MatchContentTolerance);
    }

    // ──────────── GetSourcePath ────────────

    [Theory]
    [InlineData("Runtime/Foo.cpp.patch", "Runtime/Foo.cpp")]
    [InlineData("Runtime/Foo.cpp.protected.patch", "Runtime/Foo.cpp")]
    [InlineData("Runtime/Foo.cpp", "Runtime/Foo.cpp")]  // Not a patch file
    public void GetSourcePath_StripsExtension(string input, string expected)
    {
        Assert.Equal(expected, Patcher.GetSourcePath(input));
    }
}
