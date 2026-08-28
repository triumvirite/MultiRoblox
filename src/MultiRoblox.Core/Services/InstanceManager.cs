using System.Collections.ObjectModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using MultiRoblox.Core.Models;

namespace MultiRoblox.Core.Services;

/// <summary>
/// Tracks the Roblox clients we launched and tears them down cleanly. Roblox re-spawns itself and
/// lingers in the tray on "leave", so we identify our processes by the per-account BrowserTrackerId
/// embedded in their command line and hard-kill the whole tree on request.
/// </summary>
public sealed class InstanceManager : IDisposable
{
    private readonly ILogger<InstanceManager>? _log;
    private readonly ObservableCollection<RobloxInstance> _instances = new();
    private readonly object _gate = new();
    private readonly Timer _poll;
    private readonly WindowLifetimeWatcher _windows;

    /// <summary>Live <see cref="Process"/> handles we've hooked <see cref="Process.Exited"/> on, keyed by pid.</summary>
    private readonly Dictionary<int, Process> _watched = new();

    public InstanceManager(ILogger<InstanceManager>? log = null)
    {
        _log = log;
        _windows = new WindowLifetimeWatcher(log);
        // Event-driven: Process.Exited handles crash/kill/exit; WindowLifetimeWatcher handles
        // "closed to the tray". The poll is now just a slow safety net.
        _poll = new Timer(_ => SafePoll(), null, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(8));
    }

    /// <summary>Subscribe to Process.Exited for a pid so we react the instant it dies (no poll lag).</summary>
    private void Watch(int pid)
    {
        lock (_gate)
        {
            if (_watched.ContainsKey(pid)) return;
            try
            {
                var p = Process.GetProcessById(pid);
                p.EnableRaisingEvents = true;
                p.Exited += (_, _) =>
                {
                    _log?.LogInformation("pid {Pid} exited", pid);
                    SafePoll();   // re-evaluate every instance immediately
                };
                _watched[pid] = p;
                _windows.Watch(pid, () =>
                {
                    _log?.LogInformation("pid {Pid} lost its last visible window", pid);
                    SafePoll();
                });
            }
            catch { /* already gone */ }
        }
    }

    public ReadOnlyObservableCollection<RobloxInstance> Instances => new(_instances);

    /// <summary>Raised (on the polling thread) whenever an instance's state changes.</summary>
    public event EventHandler<RobloxInstance>? InstanceChanged;

    public RobloxInstance Register(Account account, JoinRequest join, Process process, long browserTrackerId)
    {
        var inst = new RobloxInstance
        {
            AccountId = account.Id,
            AccountLabel = account.DisplayLabel,
            BrowserTrackerId = browserTrackerId,
            PlaceId = join.PlaceId,
            JobId = join.JobId,
        };
        if (!process.HasExited) { inst.ProcessIds.Add(process.Id); Watch(process.Id); }

        lock (_gate) _instances.Add(inst);
        _log?.LogInformation("Registered instance {Id} for {Account}", inst.Id, account.Username);
        return inst;
    }

    /// <summary>The "Leave Game" action: kill this instance's process tree and mark it terminated.</summary>
    public void Terminate(RobloxInstance inst)
    {
        CleanUp(inst);
        inst.State = InstanceState.Terminated;
        InstanceChanged?.Invoke(this, inst);
    }

    public void TerminateAll()
    {
        foreach (var i in Snapshot()) Terminate(i);
    }

    public void Forget(RobloxInstance inst)
    {
        lock (_gate) _instances.Remove(inst);
    }

    public IReadOnlyList<RobloxInstance> Snapshot()
    {
        lock (_gate) return _instances.ToArray();
    }

    // --- internals ------------------------------------------------------

    /// <summary>Match live Roblox processes to this instance via the tracker id in their command line.</summary>
    private void AdoptMatchingProcesses(RobloxInstance inst)
    {
        string needle = inst.BrowserTrackerId.ToString();
        foreach (var p in ProcessScanner.Scan())
        {
            if (p.Name == "RobloxCrashHandler") continue;
            bool mine = p.CommandLine.Contains(needle)
                        || p.CommandLine.Contains($"placeId={inst.PlaceId}")
                        || (string.IsNullOrEmpty(p.CommandLine) && p.StartTime >= inst.LaunchedAt.LocalDateTime.AddSeconds(-2));
            if (mine && !inst.ProcessIds.Contains(p.Pid))
            {
                inst.ProcessIds.Add(p.Pid);
                Watch(p.Pid);
            }
        }
    }

    private void SafePoll()
    {
        try { Poll(); }
        catch (Exception ex) { _log?.LogDebug(ex, "poll failed"); }
    }

    private void Poll()
    {
        foreach (var inst in Snapshot())
        {
            if (inst.State is InstanceState.Terminated or InstanceState.Closed) continue;

            AdoptMatchingProcesses(inst);

            // Look at the actual RobloxPlayerBeta processes: is any alive, and does any still have a
            // visible window? Closing via X / Alt+F4 hides the window to the tray while the process
            // lingers, so "had a window, now doesn't" == the user closed it.
            bool anyClientAlive = false, hasWindow = false;
            foreach (int pid in inst.ProcessIds.ToArray())
            {
                try
                {
                    using var p = Process.GetProcessById(pid);
                    if (p.HasExited) continue;
                    if (SafeName(p).Contains("Crash", StringComparison.OrdinalIgnoreCase)) continue;
                    anyClientAlive = true;
                    p.Refresh();
                    if (p.MainWindowHandle != IntPtr.Zero) hasWindow = true;
                }
                catch { }
            }
            if (hasWindow) inst.HadWindow = true;

            bool graceOver = (DateTimeOffset.Now - inst.LaunchedAt).TotalSeconds >= 25;

            // A live client process with no visible window == closed to the tray (unambiguous, act now).
            bool closedToTray = anyClientAlive && inst.HadWindow && !hasWindow;
            // No client process at all — wait out the startup grace first, because the launcher
            // process can briefly exit while handing off to the real client.
            bool processGone = !anyClientAlive && graceOver;

            if (closedToTray || processGone)
            {
                _log?.LogInformation("Instance {Id} closed ({Reason}); cleaning up",
                    inst.Id, closedToTray ? "window closed" : "process exited");
                CleanUp(inst);
                inst.State = InstanceState.Closed;
                InstanceChanged?.Invoke(this, inst);
            }
            else if (anyClientAlive && inst.State != InstanceState.Running)
            {
                inst.State = InstanceState.Running;
                InstanceChanged?.Invoke(this, inst);
            }
        }
    }

    /// <summary>Hard-kill everything belonging to an instance (clears any tray leftover).</summary>
    private void CleanUp(RobloxInstance inst)
    {
        AdoptMatchingProcesses(inst);
        foreach (int pid in inst.ProcessIds.ToArray())
        {
            KillTree(pid);
            Unwatch(pid);
        }
        foreach (var p in ProcessScanner.Scan())
            if (p.CommandLine.Contains(inst.BrowserTrackerId.ToString()))
                KillTree(p.Pid);
    }

    private void Unwatch(int pid)
    {
        lock (_gate)
        {
            if (_watched.Remove(pid, out var p))
                try { p.Dispose(); } catch { }
        }
        _windows.Unwatch(pid);
    }

    private static bool IsAlive(int pid)
    {
        try { return !Process.GetProcessById(pid).HasExited; }
        catch { return false; }
    }

    private void KillTree(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            p.Kill(entireProcessTree: true);
            p.WaitForExit(3000);
            _log?.LogInformation("Killed pid {Pid} ({Name})", pid, SafeName(p));
        }
        catch (ArgumentException) { /* already gone */ }
        catch (Exception ex) { _log?.LogWarning(ex, "Failed to kill pid {Pid}", pid); }
    }

    private static string SafeName(Process p)
    {
        try { return p.ProcessName; } catch { return "?"; }
    }

    public void Dispose()
    {
        _poll.Dispose();
        _windows.Dispose();
        lock (_gate)
        {
            foreach (var p in _watched.Values) try { p.Dispose(); } catch { }
            _watched.Clear();
        }
    }
}
