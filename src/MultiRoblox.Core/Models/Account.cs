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

    /// <summary>User-chosen short label shown next to the username in the account grid.</summary>
    public string Alias { get; set; } = "";

    /// <summary>Raw <c>.ROBLOSECURITY</c> cookie value (no name, no attributes).</summary>
    public string SecurityToken { get; set; } = "";

    /// <summary>Legacy single-category field. Migrated into <see cref="Categories"/> on load; kept for
    /// reading older account files.</summary>
    public string Group { get; set; } = "";

    /// <summary>Categories this account belongs to. An account can be in several at once.</summary>
    public List<string> Categories { get; set; } = new();

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
    public string DisplayLabel => Username;
}
