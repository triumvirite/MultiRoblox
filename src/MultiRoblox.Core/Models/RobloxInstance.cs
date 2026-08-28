using MultiRoblox.Core.Services;

namespace MultiRoblox.Core.Models;

public enum InstanceState
{
    Launching,
    Running,
    /// <summary>The client is gone — window closed (X / Alt+F4), process exited, or crashed.</summary>
    Closed,
    /// <summary>We killed it (Leave / Close all).</summary>
    Terminated,
}

/// <summary>A live (or recently live) Roblox client that the app started for an account.</summary>
public sealed class RobloxInstance
{
    public Guid Id { get; } = Guid.NewGuid();
    public required Guid AccountId { get; init; }
    public required string AccountLabel { get; init; }
    public long BrowserTrackerId { get; init; }
    public long PlaceId { get; init; }
    public string? JobId { get; set; }
    public DateTimeOffset LaunchedAt { get; } = DateTimeOffset.Now;

    /// <summary>The job object wrapping this client's whole process tree.</summary>
    public RobloxProcessGroup? Group { get; set; }

    public int RootPid { get; set; }

    public InstanceState State { get; set; } = InstanceState.Launching;

    /// <summary>Set once the client has shown a visible window; tells "closed to tray" from "still starting".</summary>
    public bool HadWindow { get; set; }

    /// <summary>When the client's window was first found missing (debounce for transient hides during loading).</summary>
    public DateTimeOffset? WindowGoneSince { get; set; }
}
