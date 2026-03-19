namespace Crysknife.Tests;

/// <summary>
/// Tests for ConfigPredicates — the rule evaluation engine used in INI config sections.
/// Uses the static Eval(desc, target) overload which compiles a fresh predicates instance.
/// Note: IsTruthy and NewerThan are compile-time predicates (don't depend on Target),
/// while NameMatches is a runtime predicate.
/// TargetExists is also compile-time but depends on filesystem — tested separately.
/// </summary>
public class ConfigPredicatesTests : IDisposable
{
    public ConfigPredicatesTests()
    {
        // Some predicates need engine version; set a known value
        Utils.CurrentEngineVersion = EngineVersion.Create("5.3.0");
    }

    public void Dispose()
    {
        // Reset to avoid polluting other tests
        Utils.CurrentEngineVersion = EngineVersion.Create("0.0.0");
    }

    // ──────────── Simple predicates ────────────

    [Fact]
    public void Always_ReturnsTrue()
    {
        Assert.True(ConfigPredicates.Eval("Always", "anything"));
    }

    [Fact]
    public void Never_ReturnsFalse()
    {
        Assert.False(ConfigPredicates.Eval("Never", "anything"));
    }

    // ──────────── NameMatches ────────────

    [Fact]
    public void NameMatches_MatchesRegex()
    {
        // NameMatches applies regex to Path.GetFileName(target)
        Assert.True(ConfigPredicates.Eval(@"NameMatches:\.patch", "some/path/file.patch"));
    }

    [Fact]
    public void NameMatches_NoMatch()
    {
        Assert.False(ConfigPredicates.Eval(@"NameMatches:\.patch", "some/path/file.cpp"));
    }

    [Fact]
    public void NameMatches_MultipleConditions_Disjunction()
    {
        // Multiple conditions after ':' separated by '|' are OR-ed
        Assert.True(ConfigPredicates.Eval(@"NameMatches:\.cpp|\.h", "file.h"));
        Assert.True(ConfigPredicates.Eval(@"NameMatches:\.cpp|\.h", "file.cpp"));
        Assert.False(ConfigPredicates.Eval(@"NameMatches:\.cpp|\.h", "file.cs"));
    }

    // ──────────── IsTruthy (compile-time) ────────────

    [Fact]
    public void IsTruthy_WithTruthyValue()
    {
        Assert.True(ConfigPredicates.Eval("IsTruthy:1", "anything"));
    }

    [Fact]
    public void IsTruthy_WithFalsyValue()
    {
        Assert.False(ConfigPredicates.Eval("IsTruthy:0", "anything"));
    }

    // ──────────── NewerThan (compile-time, uses CurrentEngineVersion = 5.3.0) ────────────

    [Fact]
    public void NewerThan_TrueWhenNewer()
    {
        Assert.True(ConfigPredicates.Eval("NewerThan:5.2", "anything"));
    }

    [Fact]
    public void NewerThan_FalseWhenOlder()
    {
        Assert.False(ConfigPredicates.Eval("NewerThan:5.4", "anything"));
    }

    [Fact]
    public void NewerThan_TrueWhenEqual()
    {
        // NewerThan includes the exact version (>= semantics for patch)
        Assert.True(ConfigPredicates.Eval("NewerThan:5.3", "anything"));
    }

    // ──────────── Conjunction (AND logic) ────────────

    [Fact]
    public void Conjunction_AllMustMatch()
    {
        // With Conjunction, all predicates must be true
        // IsTruthy:1 is true, NameMatches:\.cpp should match
        Assert.True(ConfigPredicates.Eval(@"Conjunction,IsTruthy:1,NameMatches:\.cpp", "file.cpp"));
    }

    [Fact]
    public void Conjunction_FailsIfAnyFails()
    {
        // IsTruthy:0 is false → entire conjunction fails
        Assert.False(ConfigPredicates.Eval(@"Conjunction,IsTruthy:0,NameMatches:\.cpp", "file.cpp"));
    }

    [Fact]
    public void Disjunction_PassesIfAnyPasses()
    {
        // Default is disjunction (OR): IsTruthy:0 fails, but NameMatches:\.cpp passes
        Assert.True(ConfigPredicates.Eval(@"IsTruthy:0,NameMatches:\.cpp", "file.cpp"));
    }

    // ──────────── Negation ────────────

    [Fact]
    public void NameMatches_Negation()
    {
        // '!' prefix inverts the condition
        Assert.True(ConfigPredicates.Eval(@"NameMatches:!\.cpp", "file.h"));
        Assert.False(ConfigPredicates.Eval(@"NameMatches:!\.h", "file.h"));
    }
}
