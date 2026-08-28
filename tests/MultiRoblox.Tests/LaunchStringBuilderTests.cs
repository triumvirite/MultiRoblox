using MultiRoblox.Core.Models;
using MultiRoblox.Core.Services;
using Xunit;

namespace MultiRoblox.Tests;

public class LaunchStringBuilderTests
{
    [Fact]
    public void FollowPlace_produces_RequestGame_url()
    {
        var s = LaunchStringBuilder.Build("TICKET", JoinRequest.Place(123), 42, launchTimeMs: 1000);

        Assert.StartsWith("roblox-player:1+launchmode:play+gameinfo:TICKET+launchtime:1000+placelauncherurl:", s);
        Assert.Contains(Uri.EscapeDataString("PlaceLauncher.ashx?request=RequestGame&placeId=123"), s);
        Assert.Contains("+browsertrackerid:42", s);
        Assert.EndsWith("+robloxLocale:en_us+gameLocale:en_us+channel:", s);
    }

    [Fact]
    public void SpecificServer_produces_RequestGameJob_url()
    {
        var url = LaunchStringBuilder.BuildPlaceLauncherUrl(JoinRequest.Server(55, "job-abc"));
        Assert.Contains("request=RequestGameJob", url);
        Assert.Contains("placeId=55", url);
        Assert.Contains("gameId=job-abc", url);
    }

    [Fact]
    public void PrivateServer_includes_access_and_link_codes()
    {
        var url = LaunchStringBuilder.BuildPlaceLauncherUrl(JoinRequest.Private(7, "acc de", "lnk"));
        Assert.Contains("request=RequestPrivateGame", url);
        Assert.Contains("accessCode=acc%20de", url);
        Assert.Contains("linkCode=lnk", url);
    }

    [Fact]
    public void Missing_ticket_throws()
    {
        Assert.Throws<ArgumentException>(() => LaunchStringBuilder.Build("", JoinRequest.Place(1), 1));
    }

    [Fact]
    public void Missing_placeId_throws()
    {
        Assert.Throws<ArgumentException>(() => LaunchStringBuilder.BuildPlaceLauncherUrl(JoinRequest.Place(0)));
    }

    [Fact]
    public void Default_launchtime_is_recent_and_monotonic()
    {
        var a = LaunchStringBuilder.Build("T", JoinRequest.Place(1), 1);
        Thread.Sleep(2);
        var b = LaunchStringBuilder.Build("T", JoinRequest.Place(1), 1);
        long ta = ExtractLaunchTime(a), tb = ExtractLaunchTime(b);
        Assert.True(tb >= ta);
        Assert.True(Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - tb) < 5000);
    }

    private static long ExtractLaunchTime(string s)
    {
        var part = s.Split("+launchtime:")[1].Split('+')[0];
        return long.Parse(part);
    }
}
