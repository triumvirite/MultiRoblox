using System.Text.Json.Serialization;

namespace MultiRoblox.Core.Models;

/// <summary>
/// A single stored Roblox account. Persisted (encrypted) by <see cref="Storage.AccountStore"/>.
/// The <see cref="SecurityToken"/> is the raw value of the <c>.ROBLOSECURITY</c> cookie.
/// </summary>
public sealed class Account
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Roblox username. May be stale until <see cref="Services.RobloxClient.ValidateAsync"/> runs.</summary>
    public string Username { get; set; } = "";

    public long UserId { get; set; }

    public string DisplayName { get; set; } = "";

    /// <summary>Raw <c>.ROBLOSECURITY</c> cookie value (no name, no attributes).</summary>
    public string SecurityToken { get; set; } = "";

    /// <summary>User-defined grouping label shown as a sidebar section. Empty = "Default".</summary>
    public string Group { get; set; } = "";

    /// <summary>Sort order within the whole list (drag-to-reorder).</summary>
    public int Order { get; set; }

    public string Note { get; set; } = "";

    public DateTimeOffset? LastUsed { get; set; }

    /// <summary>Last place id the user typed for this account (convenience).</summary>
    public string SavedPlaceId { get; set; } = "";

    public string SavedJobId { get; set; } = "";

    /// <summary>
    /// Stable per-account BrowserTrackerId used in launch strings so instances can be told apart.
    /// Generated once on add.
    /// </summary>
    public long BrowserTrackerId { get; set; }

    [JsonIgnore]
    public string EffectiveGroup => string.IsNullOrWhiteSpace(Group) ? "Default" : Group.Trim();

    [JsonIgnore]
    public string DisplayLabel => Username;
}
