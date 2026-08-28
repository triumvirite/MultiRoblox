namespace MultiRoblox.Core.Models;

public enum InstanceState
{
    Launching,
    Running,
    Disconnected,
    /// <summary>The client window was closed (X / Alt+F4) or the process exited on its own.</summary>
    Closed,
    Terminated,
    Crashed,
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

    /// <summary>PIDs believed to belong to this instance (RobloxPlayerBeta + helpers).</summary>
    public List<int> ProcessIds { get; } = new();

    public InstanceState State { get; set; } = InstanceState.Launching;

    /// <summary>Set once the client has shown a visible window; lets us tell "closed to tray" from "still starting".</summary>
    public bool HadWindow { get; set; }

    /// <summary>Last known log file path for this instance (used by the watchdog).</summary>
    public string? LogFile { get; set; }
}
