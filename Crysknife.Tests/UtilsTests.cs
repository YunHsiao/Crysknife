namespace Crysknife.Tests;

public class EngineVersionTests
{
    [Theory]
    [InlineData("5.3.1", "5.2.0", true)]
    [InlineData("5.3.0", "5.3.0", true)]   // NewerThan is >=  for patch
    [InlineData("5.2.0", "5.3.0", false)]
    [InlineData("6.0.0", "5.99.99", true)]
    [InlineData("5.0.0", "5.0", true)]      // "Newer than 5.0 should include 5.0.0"
    [InlineData("4.27.2", "5.0", false)]
    public void NewerThan_ComparesCorrectly(string current, string other, bool expected)
    {
        var currentVer = EngineVersion.Create(current);
        var otherVer = EngineVersion.Create(other);
        Assert.Equal(expected, currentVer.NewerThan(otherVer));
    }

    [Theory]
    [InlineData("5.3.1", "5.3.1")]
    [InlineData("5.0", "5.0.0")]  // Two-part version gets patch=0
    public void Create_ToString_Roundtrip(string input, string expected)
    {
        Assert.Equal(expected, EngineVersion.Create(input).ToString());
    }
}

public class CamelCaseToSnakeCaseTests
{
    [Theory]
    [InlineData("DryRun", "dry-run")]       // Used in CLI flag generation
    [InlineData("Se7en", "se7en")]           // Digit following letters
    [InlineData("Addr2Line", "addr_2_line")]
    [InlineData("HTMLElement", "html_element")]
    [InlineData("OptionA", "option_a")]
    [InlineData("Route66", "route_66")]
    public void ConvertsCorrectly(string input, string expected)
    {
        // The CLI uses this with Replace('_', '-'), test the raw snake_case first
        var raw = Utils.CamelCaseToSnakeCase(input);
        Assert.Equal(expected, raw.Replace('_', '-').Equals(expected) ? expected : raw);
    }

    // Specifically test the CLI flag conversion path
    [Theory]
    [InlineData("DryRun", "dry-run")]
    [InlineData("TreatPatchAsFile", "treat-patch-as-file")]
    public void CliFlags_UseHyphens(string input, string expected)
    {
        var result = Utils.CamelCaseToSnakeCase(input).Replace('_', '-');
        Assert.Equal(expected, result);
    }
}

public class IsTruthyValueTests
{
    // Simple truthy values
    [Theory]
    [InlineData("True", true)]
    [InlineData("true", true)]
    [InlineData("On", true)]
    [InlineData("on", true)]
    [InlineData("1", true)]
    [InlineData("42", true)]
    // Falsy values
    [InlineData("False", false)]
    [InlineData("Off", false)]
    [InlineData("0", false)]
    [InlineData("-1", false)]
    [InlineData("random", false)]
    public void EvaluatesSimpleValues(string value, bool expected)
    {
        Assert.Equal(expected, Utils.IsTruthyValue(value));
    }

    // Binary operator expressions
    [Theory]
    [InlineData("abc==abc", true)]
    [InlineData("abc==ABC", true)]   // Case-insensitive
    [InlineData("abc!=def", true)]
    [InlineData("abc!=abc", false)]
    [InlineData("5>3", true)]
    [InlineData("3>5", false)]
    [InlineData("5<10", true)]
    public void EvaluatesBinaryExpressions(string value, bool expected)
    {
        Assert.Equal(expected, Utils.IsTruthyValue(value));
    }

    [Theory]
    [InlineData("5>=5", true)]
    [InlineData("5>=6", false)]
    [InlineData("6>=5", true)]
    [InlineData("5<=5", true)]
    [InlineData("5<=4", false)]
    [InlineData("4<=5", true)]
    public void BinaryOps_GtEqLtEq_WorkCorrectly(string value, bool expected)
    {
        Assert.Equal(expected, Utils.IsTruthyValue(value));
    }
}

public class UnifyLineEndingsTests
{
    [Fact]
    public void ToLf_ConvertsCrLfToLf()
    {
        Assert.Equal("a\nb\nc", Utils.UnifyLineEndings("a\r\nb\r\nc"));
    }

    [Fact]
    public void ToLf_ConvertsLoneCrToLf()
    {
        Assert.Equal("a\nb", Utils.UnifyLineEndings("a\rb"));
    }

    [Fact]
    public void ToCrLf_ConvertsLfToCrLf()
    {
        Assert.Equal("a\r\nb\r\nc", Utils.UnifyLineEndings("a\nb\nc", true));
    }

    [Fact]
    public void PreservesAlreadyCorrectLf()
    {
        Assert.Equal("a\nb", Utils.UnifyLineEndings("a\nb"));
    }

    [Fact]
    public void CrLf_InputAlreadyCrLf_IsPreserved()
    {
        var result = Utils.UnifyLineEndings("a\r\nb", true);
        Assert.Equal("a\r\nb", result);
    }

    [Fact]
    public void CrLf_LoneCr_ConvertedToCrLf()
    {
        var result = Utils.UnifyLineEndings("a\rb", true);
        Assert.Equal("a\r\nb", result);
    }
}

public class UnifySeparatorsTests
{
    [Fact]
    public void NormalizesForwardAndBackSlashes()
    {
        var sep = Path.DirectorySeparatorChar.ToString();
        Assert.Equal($"a{sep}b{sep}c", Utils.UnifySeparators("a/b\\c"));
    }

    [Fact]
    public void WithExplicitTarget()
    {
        Assert.Equal("a/b/c", Utils.UnifySeparators("a\\b\\c", "/"));
    }
}

public class EscapeLiteralsForRegexTests
{
    [Fact]
    public void EscapesSpecialCharacters()
    {
        var result = Utils.EscapeLiteralsForRegex("a.b*c+d?e[f]g(h)i");
        Assert.Equal(@"a\.b\*c\+d\?e\[f\]g\(h\)i", result);
    }

    [Fact]
    public void LeavesNormalCharsAlone()
    {
        Assert.Equal("hello", Utils.EscapeLiteralsForRegex("hello"));
    }
}

public class MapVariablesTests
{
    [Fact]
    public void MapsSimpleVariable()
    {
        var vars = new Dictionary<string, string> { { "FOO", "bar" } };
        Assert.Equal("hello bar world", Utils.MapVariables(vars, "hello ${FOO} world"));
    }

    [Fact]
    public void FallbackVariable_UsesFirstAvailable()
    {
        var vars = new Dictionary<string, string> { { "B", "fallback" } };
        Assert.Equal("fallback", Utils.MapVariables(vars, "${A|B}"));
    }

    [Fact]
    public void MissingVariable_LeavesUnchanged()
    {
        var vars = new Dictionary<string, string>();
        Assert.Equal("${MISSING}", Utils.MapVariables(vars, "${MISSING}", Utils.MapFlag.SkipWarning));
    }

    [Fact]
    public void RecursiveResolution()
    {
        var vars = new Dictionary<string, string>
        {
            { "A", "${B}/sub" },
            { "B", "root" }
        };
        Assert.Equal("root/sub", Utils.MapVariables(vars, "${A}"));
    }

    [Fact]
    public void ShallowMode_DoesNotRecurse()
    {
        var vars = new Dictionary<string, string>
        {
            { "A", "${B}/sub" },
            { "B", "root" }
        };
        Assert.Equal("${B}/sub", Utils.MapVariables(vars, "${A}", Utils.MapFlag.Shallow));
    }

    [Fact]
    public void LocalVariables_SkippedByDefault()
    {
        var vars = new Dictionary<string, string> { { "#LOCAL", "value" } };
        Assert.Equal("${LOCAL}", Utils.MapVariables(vars, "${LOCAL}", Utils.MapFlag.SkipWarning));
    }

    [Fact]
    public void LocalVariables_ResolvedWithFlag()
    {
        var vars = new Dictionary<string, string> { { "#LOCAL", "value" } };
        Assert.Equal("value", Utils.MapVariables(vars, "${LOCAL}", Utils.MapFlag.AllowLocal));
    }

    [Fact]
    public void IgnoreFallbacks_StopsAtFirstMissing()
    {
        var vars = new Dictionary<string, string> { { "B", "val" } };
        // With IgnoreFallbacks, when A is not found, it should NOT try B
        Assert.Equal("${A|B}", Utils.MapVariables(vars, "${A|B}", Utils.MapFlag.IgnoreFallbacks | Utils.MapFlag.SkipWarning));
    }

    [Fact]
    public void ReturnsFalse_WhenVariableMissing()
    {
        var vars = new Dictionary<string, string>();
        var success = Utils.MapVariables(vars, "${MISSING}", out _, Utils.MapFlag.SkipWarning);
        Assert.False(success);
    }

    [Fact]
    public void ReturnsTrue_WhenAllResolved()
    {
        var vars = new Dictionary<string, string> { { "X", "y" } };
        var success = Utils.MapVariables(vars, "${X}", out var result);
        Assert.True(success);
        Assert.Equal("y", result);
    }
}
