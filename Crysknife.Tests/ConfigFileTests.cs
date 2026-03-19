namespace Crysknife.Tests;

/// <summary>
/// Tests for ConfigFile / ConfigFileSection INI parsing and manipulation.
/// Uses temp files to exercise the actual file-based parser.
/// </summary>
public class ConfigFileTests : IDisposable
{
    private readonly string _tempDir;

    public ConfigFileTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "CrysknifeTests_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private string WriteIni(string content, string name = "test.ini")
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    // ──────────── Basic Parsing ────────────

    [Fact]
    public void ParsesSectionsAndKeyValues()
    {
        var path = WriteIni(@"
[Variables]
FOO=bar
BAZ=qux
");
        var config = new ConfigFile(path);
        Assert.True(config.TryGetSection("Variables", out var section));

        var dict = new Dictionary<string, string>();
        section!.ParseLines(dict, ',');

        Assert.Equal("bar", dict["FOO"]);
        Assert.Equal("qux", dict["BAZ"]);
    }

    [Fact]
    public void IgnoresCommentLines()
    {
        var path = WriteIni(@"
[Sec]
; this is a comment
// this too
KEY=value
");
        var config = new ConfigFile(path);
        Assert.True(config.TryGetSection("Sec", out var section));

        var dict = new Dictionary<string, string>();
        section!.ParseLines(dict, ',');

        Assert.Single(dict);
        Assert.Equal("value", dict["KEY"]);
    }

    [Fact]
    public void TrimsWhitespace()
    {
        var path = WriteIni(@"
[Sec]
  KEY  =  value  
");
        var config = new ConfigFile(path);
        config.TryGetSection("Sec", out var section);
        var dict = new Dictionary<string, string>();
        section!.ParseLines(dict, ',');

        Assert.Equal("value", dict["KEY"]);
    }

    [Fact]
    public void StripsQuotes()
    {
        var path = WriteIni(@"
[Sec]
KEY=""quoted value""
");
        var config = new ConfigFile(path);
        config.TryGetSection("Sec", out var section);
        var dict = new Dictionary<string, string>();
        section!.ParseLines(dict, ',');

        Assert.Equal("quoted value", dict["KEY"]);
    }

    [Fact]
    public void HandlesEscapedNewlines()
    {
        // A trailing backslash causes the next line to be concatenated
        // The backslash itself is preserved in the joined string
        var path = WriteIni("[Sec]\nKEY=line1\\\nline2\n");
        var config = new ConfigFile(path);
        config.TryGetSection("Sec", out var section);
        var dict = new Dictionary<string, string>();
        section!.ParseLines(dict, ',');

        Assert.Equal("line1\\line2", dict["KEY"]);
    }

    // ──────────── Config Actions ────────────

    [Fact]
    public void SetAction_OverridesPreviousValue()
    {
        var path = WriteIni(@"
[Sec]
KEY=first
KEY=second
");
        var config = new ConfigFile(path);
        config.TryGetSection("Sec", out var section);
        var dict = new Dictionary<string, string>();
        section!.ParseLines(dict, ',');

        Assert.Equal("second", dict["KEY"]);
    }

    [Fact]
    public void AddAction_AppendsWithSeparator()
    {
        var path = WriteIni(@"
[Sec]
KEY=base
+KEY=extra
");
        var config = new ConfigFile(path);
        config.TryGetSection("Sec", out var section);
        var dict = new Dictionary<string, string>();
        section!.ParseLines(dict, ',');

        Assert.Equal("base,extra", dict["KEY"]);
    }

    [Fact]
    public void AddAction_WithPipeSeparator()
    {
        var path = WriteIni(@"
[Sec]
KEY=a
+KEY=b
+KEY=c
");
        var config = new ConfigFile(path);
        config.TryGetSection("Sec", out var section);
        var dict = new Dictionary<string, string>();
        section!.ParseLines(dict, '|');

        Assert.Equal("a|b|c", dict["KEY"]);
    }

    [Fact]
    public void RemoveKeyAction_DeletesKey()
    {
        var path = WriteIni(@"
[Sec]
KEY=value
!KEY
");
        var config = new ConfigFile(path);
        config.TryGetSection("Sec", out var section);
        var dict = new Dictionary<string, string>();
        section!.ParseLines(dict, ',');

        Assert.Empty(dict);
    }

    [Fact]
    public void RemoveKeyValueAction_RemovesSubstring()
    {
        var path = WriteIni(@"
[Sec]
KEY=abc_def_ghi
-KEY=_def
");
        var config = new ConfigFile(path);
        config.TryGetSection("Sec", out var section);
        var dict = new Dictionary<string, string>();
        section!.ParseLines(dict, ',');

        Assert.Equal("abc_ghi", dict["KEY"]);
    }

    // ──────────── Multiple Sections ────────────

    [Fact]
    public void ParsesMultipleSections()
    {
        var path = WriteIni(@"
[Alpha]
A=1

[Beta]
B=2
");
        var config = new ConfigFile(path);
        Assert.True(config.TryGetSection("Alpha", out _));
        Assert.True(config.TryGetSection("Beta", out _));
        Assert.False(config.TryGetSection("Gamma", out _));
    }

    [Fact]
    public void SectionNames_ReturnsAll()
    {
        var path = WriteIni(@"
[A]
x=1
[B]
y=2
[C]
z=3
");
        var config = new ConfigFile(path);
        var names = config.SectionNames.ToList();
        Assert.Contains("A", names);
        Assert.Contains("B", names);
        Assert.Contains("C", names);
        Assert.Equal(3, names.Count);
    }

    // ──────────── Merge ────────────

    [Fact]
    public void Merge_AppendToTail()
    {
        var path1 = WriteIni("[Sec]\nA=1\n", "a.ini");
        var path2 = WriteIni("[Sec]\nA=2\nB=3\n", "b.ini");
        var config1 = new ConfigFile(path1);
        var config2 = new ConfigFile(path2);

        config1.Merge(config2); // Append config2 to tail of config1

        config1.TryGetSection("Sec", out var section);
        var dict = new Dictionary<string, string>();
        section!.ParseLines(dict, ',');

        // config2's A=2 comes after config1's A=1, so A=2 wins
        Assert.Equal("2", dict["A"]);
        Assert.Equal("3", dict["B"]);
    }

    [Fact]
    public void Merge_PrependToHead()
    {
        var path1 = WriteIni("[Sec]\nA=1\n", "a.ini");
        var path2 = WriteIni("[Sec]\nA=2\nB=3\n", "b.ini");
        var config1 = new ConfigFile(path1);
        var config2 = new ConfigFile(path2);

        config1.Merge(config2, false); // Prepend config2 to head of config1

        config1.TryGetSection("Sec", out var section);
        var dict = new Dictionary<string, string>();
        section!.ParseLines(dict, ',');

        // config1's A=1 comes after config2's A=2, so A=1 wins
        Assert.Equal("1", dict["A"]);
        Assert.Equal("3", dict["B"]);
    }

    [Fact]
    public void Merge_CreatesMissingSections()
    {
        var path1 = WriteIni("[Alpha]\nA=1\n", "a.ini");
        var path2 = WriteIni("[Beta]\nB=2\n", "b.ini");
        var config1 = new ConfigFile(path1);
        var config2 = new ConfigFile(path2);

        config1.Merge(config2);

        Assert.True(config1.TryGetSection("Alpha", out _));
        Assert.True(config1.TryGetSection("Beta", out _));
    }

    // ──────────── AppendFromText ────────────

    [Fact]
    public void AppendFromText_ParsesCommaSeparatedSettings()
    {
        var config = new ConfigFile();
        config.AppendFromText("Variables", "FOO=bar,BAZ=qux");

        Assert.True(config.TryGetSection("Variables", out var section));
        var dict = new Dictionary<string, string>();
        section!.ParseLines(dict, ',');

        Assert.Equal("bar", dict["FOO"]);
        Assert.Equal("qux", dict["BAZ"]);
    }

    [Fact]
    public void AppendFromText_WithAddAction()
    {
        var config = new ConfigFile();
        config.AppendFromText("Sec", "KEY=base,+KEY=extra");

        config.TryGetSection("Sec", out var section);
        var dict = new Dictionary<string, string>();
        section!.ParseLines(dict, '|');

        Assert.Equal("base|extra", dict["KEY"]);
    }

    // ──────────── Case Insensitivity ────────────

    [Fact]
    public void SectionLookup_IsCaseInsensitive()
    {
        var path = WriteIni("[Variables]\nFOO=bar\n");
        var config = new ConfigFile(path);
        Assert.True(config.TryGetSection("VARIABLES", out _));
        Assert.True(config.TryGetSection("variables", out _));
    }

    // ──────────── Nonexistent file ────────────

    [Fact]
    public void NonexistentFile_ProducesEmptyConfig()
    {
        var config = new ConfigFile(Path.Combine(_tempDir, "nope.ini"));
        Assert.Empty(config.SectionNames);
    }
}
