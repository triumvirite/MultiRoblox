using System.Collections.ObjectModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using MultiRoblox.Core.Models;

namespace MultiRoblox.Core.Services;

/// <summary>
/// Tracks the Roblox clients we launched and tears them down reliably. Each instance is a job object
/// (<see cref="RobloxProcessGroup"/>); killing = TerminateJobObject, which takes the whole tree
/// including RobloxCrashHandler and any self-relaunch, so the tray icon actually goes away.
///
/// All teardown runs on background threads — the UI never blocks on process kills. Every operation
/// is defensive: an already-gone / unknown instance is a no-op, not a crash.
/// </summary>
public sealed class InstanceManager : IDisposable
{
    private readonly ILogger<InstanceManager>? _log;
    private readonly ObservableCollection<RobloxInstance> _instances = new();
    private readonly object _gate = new();
    private readonly Timer _poll;
    private readonly WindowLifetimeWatcher _windows;
    private volatile bool _disposed;

    public InstanceManager(ILogger<InstanceManager>? log = null)
    {
        _log = log;
        _windows = new WindowLifetimeWatcher(log);
        _poll = new Timer(_ => SafePoll(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3));
    }

    public ReadOnlyObservableCollection<RobloxInstance> Instances => new(_instances);

    /// <summary>Raised (off the UI thread) whenever an instance's state changes or it's removed.</summary>
    public event EventHandler<RobloxInstance>? InstanceChanged;

    public IReadOnlyList<RobloxInstance> Snapshot()
    {
        lock (_gate) return _instances.ToArray();
    }

    // --- registration -------------------------------------------------

    public RobloxInstance Register(Account account, JoinRequest join,
        Process process, RobloxProcessGroup? group, long browserTrackerId)
    {
        var inst = new RobloxInstance
        {
            AccountId = account.Id,
            AccountLabel = account.DisplayLabel,
            BrowserTrackerId = browserTrackerId,
            PlaceId = join.PlaceId,
            JobId = join.JobId,
            Group = group,
            RootPid = SafePid(process),
        };

        lock (_gate)
        {
            inst.CascadeSlot = _instances.Count;   // 0 for the first, 1 for the next, …
            _instances.Add(inst);
        }

        WatchRoot(inst);
        _log?.LogInformation("Registered instance {Id} ({Account}) root pid {Pid}", inst.Id, account.Username, inst.RootPid);
        return inst;
    }

    private void WatchRoot(RobloxInstance inst)
    {
        if (inst.RootPid <= 0) return;
        try
        {
            var p = Process.GetProcessById(inst.RootPid);
            p.EnableRaisingEvents = true;
            p.Exited += (_, _) => SafePoll();
        }
        catch { }
        _windows.Watch(inst.RootPid, SafePoll);
    }

    // --- explicit teardown -----------------------------------------

    /// <summary>"Leave" — kill this instance's group and drop it. Safe on an already-gone instance.</summary>
    public void Terminate(RobloxInstance? inst)
    {
        if (inst is null) return;
        RobloxInstance? tracked;
        bool empty;
        lock (_gate)
        {
            tracked = _instances.FirstOrDefault(i => i.Id == inst.Id) ?? inst;
            _instances.Remove(tracked);
            empty = _instances.Count == 0;
        }
        if (empty) WindowArranger.Reset();
        if (tracked.State is InstanceState.Terminated) return;
        tracked.State = InstanceState.Terminated;

        _windows.Unwatch(tracked.RootPid);
        Task.Run(() => KillGroup(tracked, "terminate"));
        RaiseChanged(tracked);
    }

    /// <summary>"Close all" — tear down every tracked instance, plus a sweep for anything ours left over.</summary>
    public void TerminateAll()
    {
        foreach (var i in Snapshot()) Terminate(i);
        Task.Run(SweepOrphans);
    }

    // --- kill mechanics (background only) --------------------------

    private void KillGroup(RobloxInstance inst, string reason)
    {
        try
        {
            _log?.LogInformation("Killing instance {Id} ({Reason})", inst.Id, reason);
            inst.Group?.KillAll();

            // Backstop: anything carrying our tracker id / place id that the job missed.
            for (int pass = 0; pass < 2; pass++)
            {
                bool killedAny = false;
                foreach (var p in SafeScan())
                    if (p.CommandLine.Contains(inst.BrowserTrackerId.ToString()) ||
                        p.CommandLine.Contains($"placeId={inst.PlaceId}") ||
                        p.Pid == inst.RootPid)
                        killedAny |= SafeKill(p.Pid);
                if (!killedAny) break;
                Thread.Sleep(700); // let a relaunch appear, then hit it again
            }
        }
        catch (Exception ex) { _log?.LogWarning(ex, "KillGroup failed for {Id}", inst.Id); }
        finally
        {
            try { inst.Group?.Dispose(); } catch { }
            inst.Group = null;
        }
    }

    private void SweepOrphans()
    {
        var ours = Snapshot().Select(i => i.BrowserTrackerId.ToString()).ToHashSet();
        if (ours.Count == 0) return;
        foreach (var p in SafeScan())
            if (ours.Any(t => p.CommandLine.Contains(t)))
                SafeKill(p.Pid);
    }

    // --- passive detection ---------------------------------------

    private void SafePoll()
    {
        if (_disposed) return;
        try { Poll(); }
        catch (Exception ex) { _log?.LogDebug(ex, "poll failed"); }
    }

    private void Poll()
    {
        foreach (var inst in Snapshot())
        {
            if (inst.State is InstanceState.Terminated) continue;

            AdoptStragglers(inst);

            bool alive = GroupAlive(inst, out bool hasWindow, out int windowPid);
            if (hasWindow)
            {
                inst.HadWindow = true;
                inst.WindowGoneSince = null;
                if (!inst.Positioned && windowPid > 0)
                {
                    inst.Positioned = true;
                    int slot = inst.CascadeSlot;
                    Task.Run(() => WindowArranger.Place(windowPid, slot));
                }
            }

            bool graceOver = (DateTimeOffset.Now - inst.LaunchedAt).TotalSeconds >= 25;

            // window gone while a process lives = closed to the tray; debounce transient hides
            bool windowMissing = alive && inst.HadWindow && !hasWindow;
            if (windowMissing) inst.WindowGoneSince ??= DateTimeOffset.Now;
            bool closedToTray = windowMissing && (DateTimeOffset.Now - inst.WindowGoneSince!.Value).TotalSeconds >= 2;

            bool processGone = !alive && graceOver;

            if (closedToTray || processGone)
            {
                bool empty;
                lock (_gate) { _instances.Remove(inst); empty = _instances.Count == 0; }
                if (empty) WindowArranger.Reset();
                inst.State = InstanceState.Closed;
                _windows.Unwatch(inst.RootPid);
                _log?.LogInformation("Instance {Id} gone ({Why})", inst.Id, closedToTray ? "tray-closed" : "exited");
                Task.Run(() => KillGroup(inst, "detected-closed"));
                RaiseChanged(inst);
            }
            else if (alive && hasWindow && inst.State != InstanceState.Running)
            {
                inst.State = InstanceState.Running;
                RaiseChanged(inst);
            }
        }
    }

    /// <summary>Is any client process for this instance still running? (job first, WMI fallback)</summary>
    private bool GroupAlive(RobloxInstance inst, out bool hasWindow, out int windowPid)
    {
        hasWindow = false;
        windowPid = 0;
        bool alive = false;

        IEnumerable<int> candidates = inst.Group?.Pids() ?? Enumerable.Empty<int>();
        if (!candidates.Any())
            candidates = SafeScan()
                .Where(p => p.CommandLine.Contains(inst.BrowserTrackerId.ToString()))
                .Select(p => p.Pid);

        foreach (int pid in candidates.Distinct())
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                if (p.HasExited) continue;
                if (p.ProcessName.Contains("Crash", StringComparison.OrdinalIgnoreCase)) continue;
                alive = true;
                p.Refresh();
                if (p.MainWindowHandle != IntPtr.Zero) { hasWindow = true; windowPid = pid; }
            }
            catch { }
        }
        return alive;
    }

    private void AdoptStragglers(RobloxInstance inst)
    {
        if (inst.Group is not { } g) return;
        foreach (var p in SafeScan())
            if (p.CommandLine.Contains(inst.BrowserTrackerId.ToString()) ||
                (p.Name.Contains("Crash", StringComparison.OrdinalIgnoreCase) && g.Pids().Contains(p.ParentPid)))
                g.Adopt(p.Pid);
    }

    // --- helpers ------------------------------------------------

    private static int SafePid(Process p)
    {
        try { return p.HasExited ? 0 : p.Id; } catch { return 0; }
    }

    private static IReadOnlyList<RobloxProcess> SafeScan()
    {
        try { return ProcessScanner.Scan(); } catch { return Array.Empty<RobloxProcess>(); }
    }

    private bool SafeKill(int pid)
    {
        if (pid <= 0) return false;
        try
        {
            using var p = Process.GetProcessById(pid);
            if (p.HasExited) return false;
            p.Kill(entireProcessTree: true);
            _log?.LogInformation("killed pid {Pid}", pid);
            return true;
        }
        catch (ArgumentException) { return false; }        // already gone
        catch (InvalidOperationException) { return false; } // already gone
        catch (Exception ex) { _log?.LogDebug(ex, "kill pid {Pid} failed", pid); return false; }
    }

    private void RaiseChanged(RobloxInstance inst)
    {
        try { InstanceChanged?.Invoke(this, inst); }
        catch (Exception ex) { _log?.LogDebug(ex, "InstanceChanged handler threw"); }
    }

    public void Dispose()
    {
        _disposed = true;
        _poll.Dispose();
        _windows.Dispose();
        foreach (var i in Snapshot())
            try { i.Group?.Dispose(); } catch { }
    }
}
