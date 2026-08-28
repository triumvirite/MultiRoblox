using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace MultiRoblox.Core.Services;

/// <summary>
/// Fires a callback the instant a watched process's last visible top-level window goes away — which
/// is what "close to tray" (X / Alt+F4) does without exiting the process. Uses a global
/// <c>SetWinEventHook</c> for HIDE / DESTROY on a dedicated message-pump thread.
/// </summary>
internal sealed class WindowLifetimeWatcher : IDisposable
{
    private const uint EVENT_OBJECT_DESTROY = 0x8001;
    private const uint EVENT_OBJECT_HIDE = 0x8003;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
    private const int OBJID_WINDOW = 0;
    private const uint WM_QUIT = 0x0012;
    private const uint GW_OWNER = 4;

    private delegate void WinEventProc(IntPtr hHook, uint ev, IntPtr hwnd, int idObject, int idChild,
        uint dwEventThread, uint dwmsEventTime);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmod,
        WinEventProc proc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hHook);
    [DllImport("user32.dll")] private static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref MSG msg);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr w, IntPtr l);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc cb, IntPtr param);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr param);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG { public IntPtr hwnd; public uint message; public IntPtr w; public IntPtr l; public uint time; public int x; public int y; }

    private readonly ILogger? _log;
    private readonly ConcurrentDictionary<int, Action> _watched = new();
    private readonly WinEventProc _proc;          // kept alive so the delegate isn't collected
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new(false);
    private IntPtr _hook;
    private uint _threadId;
    private volatile bool _disposed;

    public WindowLifetimeWatcher(ILogger? log = null)
    {
        _log = log;
        _proc = OnWinEvent;
        _thread = new Thread(Run) { IsBackground = true, Name = "MR-WinEventHook" };
        _thread.Start();
        _ready.Wait(2000);
    }

    public void Watch(int pid, Action onLastWindowGone) => _watched[pid] = onLastWindowGone;

    public void Unwatch(int pid) => _watched.TryRemove(pid, out _);

    private void Run()
    {
        _threadId = GetCurrentThreadId();
        _hook = SetWinEventHook(EVENT_OBJECT_DESTROY, EVENT_OBJECT_HIDE, IntPtr.Zero, _proc, 0, 0,
            WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        _ready.Set();

        if (_hook == IntPtr.Zero) { _log?.LogWarning("SetWinEventHook failed; falling back to polling only."); return; }

        while (!_disposed && GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        UnhookWinEvent(_hook);
        _hook = IntPtr.Zero;
    }

    private void OnWinEvent(IntPtr hHook, uint ev, IntPtr hwnd, int idObject, int idChild,
        uint dwEventThread, uint dwmsEventTime)
    {
        if (hwnd == IntPtr.Zero || idObject != OBJID_WINDOW || idChild != 0 || _watched.IsEmpty) return;

        GetWindowThreadProcessId(hwnd, out uint pidU);
        int pid = (int)pidU;
        if (!_watched.ContainsKey(pid) || HasVisibleTopLevelWindow(pid)) return;

        // Debounce: Roblox briefly hides its window during some transitions. Only act if it's
        // still gone ~1.5s later.
        Task.Delay(1500).ContinueWith(_ =>
        {
            if (_disposed) return;
            if (_watched.TryGetValue(pid, out var onGone) && !HasVisibleTopLevelWindow(pid))
            {
                try { onGone(); } catch (Exception e) { _log?.LogDebug(e, "window-gone callback threw"); }
            }
        });
    }

    private static bool HasVisibleTopLevelWindow(int pid)
    {
        bool found = false;
        EnumWindows((h, _) =>
        {
            GetWindowThreadProcessId(h, out uint p);
            if (p == pid && IsWindowVisible(h) && GetWindow(h, GW_OWNER) == IntPtr.Zero)
            {
                found = true;
                return false; // stop
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _watched.Clear();
        if (_threadId != 0) PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(1500);
        _ready.Dispose();
    }
}
