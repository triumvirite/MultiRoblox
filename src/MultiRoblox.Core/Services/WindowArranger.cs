using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MultiRoblox.Core.Services;

/// <summary>Cascades each new Roblox client window instead of stacking them all in one spot.</summary>
public static class WindowArranger
{
    private const uint SWP_NOSIZE = 0x0001, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;
    private const int Step = 38;
    private const int Wrap = 6;

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int L, T, R, B; }

    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr h);

    private static readonly object Gate = new();
    private static (int x, int y)? _anchor;

    /// <param name="slot">0 for the first client, 1 for the next, … (wraps).</param>
    public static void Place(int pid, int slot)
    {
        try
        {
            IntPtr h;
            using (var p = Process.GetProcessById(pid))
            {
                p.Refresh();
                h = p.MainWindowHandle;
            }
            if (h == IntPtr.Zero || IsIconic(h) || !GetWindowRect(h, out var r)) return;

            lock (Gate)
            {
                if (slot <= 0 || _anchor is null)
                {
                    _anchor = (r.L, r.T);   // first window stays where Roblox put it; it's the anchor
                    if (slot <= 0) return;
                }
                int n = slot % Wrap;
                int tier = slot / Wrap;
                int x = _anchor.Value.x + n * Step + tier * (Step * Wrap + 20);
                int y = _anchor.Value.y + n * Step;
                SetWindowPos(h, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
            }
        }
        catch { }
    }

    /// <summary>Forget the anchor once nothing is running so the next batch starts fresh.</summary>
    public static void Reset()
    {
        lock (Gate) _anchor = null;
    }
}
