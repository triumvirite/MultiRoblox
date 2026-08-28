using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using MultiRoblox.Core.Interop;

namespace MultiRoblox.Core.Services;

/// <summary>
/// Multi-instance enabler. Roblox refuses to run a second client while it can open its singleton
/// objects with ownership. If <b>we</b> create <c>ROBLOX_singletonEvent</c> and
/// <c>ROBLOX_singletonMutex</c> first and hold them for our whole lifetime, each Roblox client that
/// starts afterwards simply opens the existing objects and never signals the others to quit.
///
/// This must run before the first launch and stay alive. Disposing releases the objects (returning
/// Roblox to normal single-instance behaviour).
/// </summary>
public sealed class SingletonHolder : IDisposable
{
    // Roblox has used both names across versions; hold both.
    private static readonly string[] Names = { "ROBLOX_singletonEvent", "ROBLOX_singletonMutex" };

    private readonly ILogger<SingletonHolder>? _log;
    private readonly List<SafeHandle> _handles = new();

    public SingletonHolder(ILogger<SingletonHolder>? log = null) => _log = log;

    public bool IsHeld { get; private set; }

    /// <summary>
    /// Attempts to create and hold the singleton objects.
    /// Returns false if Roblox (or something) already owns one — in that case multi-instance can't be
    /// guaranteed until those processes exit.
    /// </summary>
    public bool TryHold()
    {
        if (IsHeld) return true;

        bool allFresh = true;
        foreach (var name in Names)
        {
            // Event first (current Roblox), then Mutex (older). Named, initially-unsignalled event / owned mutex.
            var evt = NativeMethods.CreateEventW(IntPtr.Zero, true, false, name);
            int err = Marshal.GetLastPInvokeError();
            if (evt.IsInvalid)
            {
                _log?.LogWarning("CreateEvent({Name}) failed: {Err}", name, err);
                allFresh = false;
                continue;
            }
            if (err == NativeMethods.ERROR_ALREADY_EXISTS)
            {
                _log?.LogInformation("{Name} already existed; another process owns it.", name);
                allFresh = false;
            }
            _handles.Add(evt);
        }

        IsHeld = _handles.Count > 0;
        return allFresh;
    }

    /// <summary>
    /// Make sure a second client can launch <b>right now</b>, even if a Roblox client started outside
    /// MultiRoblox is already running and owns the singleton. Holds the objects ourselves, then closes
    /// any singleton handles still held by running <c>RobloxPlayerBeta.exe</c> processes.
    /// Safe to call before every launch.
    /// </summary>
    public void EnsureMultiInstance()
    {
        TryHold();
        try
        {
            int closed = SingletonInterloper.FreeExistingClients(_log);
            if (closed > 0)
            {
                _log?.LogInformation("freed {Count} singleton handle(s) from pre-existing Roblox client(s)", closed);
                // Re-hold in case closing the last external handle destroyed an object we only opened.
                Release();
                TryHold();
            }
        }
        catch (Exception ex)
        {
            _log?.LogWarning(ex, "SingletonInterloper failed; a pre-existing Roblox client may still block multi-instance");
        }
    }

    public void Release()
    {
        foreach (var h in _handles)
            h.Dispose();
        _handles.Clear();
        IsHeld = false;
    }

    public void Dispose() => Release();
}
