using System.Text;
using MultiRoblox.Core.Models;

namespace MultiRoblox.Core.Services;

/// <summary>
/// Builds the single command-line argument passed to RobloxPlayerBeta.exe. Pure and unit-tested.
///
/// Shape:
///   roblox-player:1+launchmode:play+gameinfo:&lt;ticket&gt;+launchtime:&lt;unixMs&gt;
///   +placelauncherurl:&lt;url-encoded&gt;+browsertrackerid:&lt;btid&gt;
///   +robloxLocale:en_us+gameLocale:en_us+channel:
/// </summary>
public static class LaunchStringBuilder
{
    private const string PlaceLauncher = "https://assetgame.roblox.com/game/PlaceLauncher.ashx";

    public static string Build(string authTicket, JoinRequest join, long browserTrackerId, long? launchTimeMs = null)
    {
        if (string.IsNullOrWhiteSpace(authTicket))
            throw new ArgumentException("Auth ticket is required.", nameof(authTicket));

        long ts = launchTimeMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string placeLauncherUrl = BuildPlaceLauncherUrl(join);

        var sb = new StringBuilder("roblox-player:1");
        sb.Append("+launchmode:play");
        sb.Append("+gameinfo:").Append(authTicket);
        sb.Append("+launchtime:").Append(ts);
        sb.Append("+placelauncherurl:").Append(Uri.EscapeDataString(placeLauncherUrl));
        sb.Append("+browsertrackerid:").Append(browserTrackerId);
        sb.Append("+robloxLocale:en_us+gameLocale:en_us+channel:");
        return sb.ToString();
    }

    public static string BuildPlaceLauncherUrl(JoinRequest join)
    {
        if (join.PlaceId <= 0)
            throw new ArgumentException("PlaceId is required.", nameof(join));

        return join.Kind switch
        {
            JoinKind.FollowPlace =>
                $"{PlaceLauncher}?request=RequestGame&placeId={join.PlaceId}&isPlayTogetherGame=false",

            JoinKind.SpecificServer when !string.IsNullOrWhiteSpace(join.JobId) =>
                $"{PlaceLauncher}?request=RequestGameJob&placeId={join.PlaceId}&gameId={join.JobId}",

            JoinKind.PrivateServer when !string.IsNullOrWhiteSpace(join.AccessCode) =>
                $"{PlaceLauncher}?request=RequestPrivateGame&placeId={join.PlaceId}"
                + $"&accessCode={Uri.EscapeDataString(join.AccessCode!)}"
                + (string.IsNullOrWhiteSpace(join.LinkCode) ? "" : $"&linkCode={Uri.EscapeDataString(join.LinkCode!)}"),

            JoinKind.FollowUser when join.FollowUserId > 0 =>
                $"{PlaceLauncher}?request=RequestFollowUser&userId={join.FollowUserId}",

            _ => throw new ArgumentException($"Incomplete JoinRequest for kind {join.Kind}.", nameof(join)),
        };
    }
}
