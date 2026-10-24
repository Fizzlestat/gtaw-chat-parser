using Xunit;

namespace GTAWChatParser.Tests;

public class AppUpdateTests
{
    private static readonly Version Current = new(1, 0, 0, 0);

    [Theory]
    [InlineData("v1.0.1", true)]
    [InlineData("1.0.1", true)]
    [InlineData("V2", true)]
    [InlineData("v1.0.0", false)]
    [InlineData("1.0.0", false)]
    [InlineData("v1.0", false)]
    [InlineData("v0.9.9", false)]
    [InlineData("v1.0.1-beta", true)]
    [InlineData("", false)]
    [InlineData("release", false)]
    public void Release_tags_compare_against_the_running_version(string tag, bool newer)
    {
        Assert.Equal(newer, AppUpdate.IsNewer(tag, Current));
    }
}
