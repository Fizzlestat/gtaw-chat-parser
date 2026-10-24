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

    [Fact]
    public void The_same_release_is_not_downloaded_again()
    {
        Assert.False(AppUpdate.IsNewer("v1.0.1", new Version(1, 0, 1, 0)));
    }

    [Fact]
    public void Installed_release_version_wins_when_the_exe_is_older()
    {
        var chosen = AppUpdate.Choose(new Version(1, 0, 0, 0), new Version(1, 0, 1, 0));
        Assert.Equal(new Version(1, 0, 1, 0), chosen);
        Assert.False(AppUpdate.IsNewer("v1.0.1", chosen));
    }

    [Fact]
    public void Assembly_version_wins_when_it_is_newer_than_the_installed_stamp()
    {
        var chosen = AppUpdate.Choose(new Version(1, 0, 2, 0), new Version(1, 0, 1, 0));
        Assert.Equal(new Version(1, 0, 2, 0), chosen);
    }
}
