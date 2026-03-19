namespace Crysknife.Tests;

public class SmokeTest
{
    [Fact]
    public void InternalsAreAccessible()
    {
        // Verify InternalsVisibleTo works: we can instantiate an internal type
        var version = EngineVersion.Create("5.3.1");
        Assert.True(version.NewerThan(EngineVersion.Create("5.2.0")));
    }
}
