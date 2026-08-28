using System.Threading;
using System.Windows;

namespace MultiRoblox.App.Services;

/// <summary>
/// Enforces one running MultiRoblox per Windows user. Two instances share the same
/// <c>%AppData%\MultiRoblox\</c> data files, and <see cref="AppServices"/> only reads them at
/// startup, so a second copy would show a stale account list and could overwrite the first
/// copy's changes on save.
///
/// Set the <c>MULTIROBLOX_ALLOW_MULTIPLE</c> environment variable to bypass this (dev only —
/// e.g. running a test build alongside an installed one).
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\MultiRoblox.SingleInstance";
    private const string SignalName = @"Local\MultiRoblox.Activate";

    private readonly Mutex? _mutex;
    private readonly EventWaitHandle? _signal;
    private Thread? _listener;
    private volatile bool _running;

    private SingleInstance(Mutex? mutex, EventWaitHandle? signal)
    {
        _mutex = mutex;
        _signal = signal;
    }

    /// <summary>Owner of the single-instance lock; null when this process is the primary.</summary>
    public bool IsPrimary { get; private init; }

    /// <summary>
    /// Returns null if another instance is already running (after nudging it to the foreground) —
    /// the caller should exit. Otherwise returns the lock to hold for the app's lifetime.
    /// </summary>
    public static SingleInstance? Acquire()
    {
        if (Environment.GetEnvironmentVariable("MULTIROBLOX_ALLOW_MULTIPLE") is { Length: > 0 })
            return new SingleInstance(null, null) { IsPrimary = true };

        var mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        var signal = new EventWaitHandle(false, EventResetMode.AutoReset, SignalName);

        if (!createdNew)
        {
            mutex.Dispose();
            signal.Set();          // ask the primary instance to show itself
            signal.Dispose();
            return null;
        }

        return new SingleInstance(mutex, signal) { IsPrimary = true };
    }

    /// <summary>Start listening for a second launch and bring <paramref name="onActivate"/>'s window forward.</summary>
    public void ListenForActivation(Action onActivate)
    {
        if (_signal is null) return;
        _running = true;
        _listener = new Thread(() =>
        {
            while (_running)
            {
                try
                {
                    if (!_signal.WaitOne(1000)) continue;
                    if (!_running) break;
                    Application.Current?.Dispatcher.BeginInvoke(onActivate);
                }
                catch { break; }
            }
        })
        {
            IsBackground = true,
            Name = "SingleInstance.Activate",
        };
        _listener.Start();
    }

    public void Dispose()
    {
        _running = false;
        try { _signal?.Set(); } catch { }
        _listener?.Join(1500);
        try { _mutex?.ReleaseMutex(); } catch { }
        _mutex?.Dispose();
        _signal?.Dispose();
    }
}
