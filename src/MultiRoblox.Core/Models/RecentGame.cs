namespace MultiRoblox.Core.Models;

/// <summary>A game launched through MultiRoblox, for the "Recents" join tab.</summary>
public sealed class RecentGame
{
    public long PlaceId { get; set; }
    public string Name { get; set; } = "";
    public DateTimeOffset LastPlayed { get; set; } = DateTimeOffset.Now;
}
