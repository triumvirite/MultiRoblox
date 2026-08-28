using MultiRoblox.Core.Models;
using MultiRoblox.Core.Services;
using Xunit;

namespace MultiRoblox.Tests;

public class GameLinkParserTests
{
    [Theory]
    [InlineData("920587237", 920587237)]
    [InlineData("  920587237 ", 920587237)]
    [InlineData("https://www.roblox.com/games/920587237/Adopt-Me", 920587237)]
    [InlineData("https://www.roblox.com/games/920587237/Adopt-Me?foo=bar", 920587237)]
    [InlineData("roblox.com/games/920587237", 920587237)]
    [InlineData("roblox://placeId=920587237", 920587237)]
    [InlineData("https://www.roblox.com/games/start?placeId=920587237&launchData=x", 920587237)]
    public void Extracts_place_id(string input, long expected)
    {
        Assert.True(GameLinkParser.TryParse(input, out var r));
        Assert.Equal(expected, r.PlaceId);
    }

    [Fact]
    public void Extracts_private_server_link_code()
    {
        Assert.True(GameLinkParser.TryParse(
            "https://www.roblox.com/games/920587237/Adopt-Me?privateServerLinkCode=12345-67890", out var r));
        Assert.Equal(920587237, r.PlaceId);
        Assert.Equal("12345-67890", r.PrivateServerLinkCode);

        var join = r.ToJoinRequest();
        Assert.Equal(JoinKind.PrivateServer, join.Kind);
        Assert.Equal("12345-67890", join.LinkCode);
    }

    [Fact]
    public void Link_code_only_private_join_builds_launcher_url()
    {
        GameLinkParser.TryParse("https://www.roblox.com/games/55/X?privateServerLinkCode=abc", out var r);
        var url = LaunchStringBuilder.BuildPlaceLauncherUrl(r.ToJoinRequest());
        Assert.Contains("request=RequestPrivateGame", url);
        Assert.Contains("linkCode=abc", url);
        Assert.DoesNotContain("accessCode=", url);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a link")]
    [InlineData("https://example.com/games/1")]
    public void Rejects_junk(string input) => Assert.False(GameLinkParser.TryParse(input, out _));
}
