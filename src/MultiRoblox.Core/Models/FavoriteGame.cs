namespace MultiRoblox.Core.Models;

/// <summary>A game the user pinned locally for quick joining. App-managed, not Roblox favorites.</summary>
public sealed class FavoriteGame
{
    public long PlaceId { get; set; }
    public string Name { get; set; } = "";
}
