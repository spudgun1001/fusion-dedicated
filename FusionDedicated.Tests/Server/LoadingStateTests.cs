using FusionDedicated.Server;

namespace FusionDedicated.Tests.Server;

/// <summary>What a player's own Loading key says, read the same way FinishedLoading reads it.</summary>
public class LoadingStateTests
{
    [Theory]
    [InlineData("Loading", "False", false)]
    [InlineData("loading", "true", true)]
    [InlineData("LOADING", "False", false)]
    public void The_loading_key_says_whether_they_are_loading(string key, string value, bool expected)
        => Assert.Equal(expected, WorldCatchup.LoadingState(key, value));

    [Theory]
    [InlineData("Nickname", "False")]
    [InlineData("Loading", "soon")]
    public void Anything_else_says_nothing(string key, string value)
        => Assert.Null(WorldCatchup.LoadingState(key, value));

    [Fact]
    public void Finished_loading_is_still_loading_false()
    {
        Assert.True(WorldCatchup.FinishedLoading("Loading", "False"));
        Assert.False(WorldCatchup.FinishedLoading("Loading", "True"));
        Assert.False(WorldCatchup.FinishedLoading("Nickname", "False"));
    }
}
