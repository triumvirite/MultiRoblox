using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using static MultiRoblox.Core.Interop.NativeMethods;

namespace MultiRoblox.Core.Services;

/// <summary>
/// A Windows Job Object wrapping one launched Roblox client and everything it spawns
/// (RobloxCrashHandler, any self-relaunch). Killing the job terminates the whole group atomically,
/// which is the only reliable way to make Roblox let go of its system-tray icon.
/// </summary>
public sealed class RobloxProcessGroup : IDisposable
{
    private readonly ILogger? _log;
    private IntPtr _job;
    private bool _disposed;

    public int RootPid { get; }

    private RobloxProcessGroup(IntPtr job, int rootPid, ILogger? log)
    {
        _job = job;
        RootPid = rootPid;
        _log = log;
    }

    public static RobloxProcessGroup? Create(Process rootProcess, ILogger? log = null)
    {
        IntPtr job = CreateJobObjectW(IntPtr.Zero, null);
        if (job == IntPtr.Zero)
        {
            log?.LogWarning("CreateJobObject failed ({Err})", Marshal.GetLastPInvokeError());
            return null;
        }
        try
        {
            AttachProcess(job, rootProcess.Id, rootProcess.SafeHandle.DangerousGetHandle(), log);
        }
        catch (Exception ex)
        {
            log?.LogWarning(ex, "assigning root process to job failed");
        }
        return new RobloxProcessGroup(job, rootProcess.Id, log);
    }

    /// <summary>Pull a straggler (adopted by pid) into the job too. Safe to call repeatedly.</summary>
    public void Adopt(int pid)
    {
        if (_disposed || _job == IntPtr.Zero) return;
        IntPtr h = OpenProcess(PROCESS_SET_QUOTA | PROCESS_TERMINATE | PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);
        if (h == IntPtr.Zero) return;
        try { AttachProcess(_job, pid, h, _log); }
        catch { }
        finally { CloseHandle(h); }
    }

    private static void AttachProcess(IntPtr job, int pid, IntPtr handle, ILogger? log)
    {
        if (!AssignProcessToJobObject(job, handle))
        {
            int err = Marshal.GetLastPInvokeError();
            // 5 = access denied (already in a job we can't nest under) — not fatal, we still sweep by pid
            if (err is not 0 and not 5)
                log?.LogDebug("AssignProcessToJobObject pid {Pid} failed ({Err})", pid, err);
        }
    }

    /// <summary>PIDs currently in the job (kernel-authoritative, no WMI).</summary>
    public int[] Pids()
    {
        if (_disposed || _job == IntPtr.Zero) return Array.Empty<int>();
        const int cap = 512;
        int size = Marshal.SizeOf<JOBOBJECT_BASIC_PROCESS_ID_LIST>() + cap * IntPtr.Size;
        IntPtr buf = Marshal.AllocHGlobal(size);
        try
        {
            if (!QueryInformationJobObject(_job, JobObjectInfoType.BasicProcessIdList, buf, (uint)size, IntPtr.Zero))
                return Array.Empty<int>();

            uint count = (uint)Marshal.ReadInt32(buf, sizeof(uint)); // NumberOfProcessIdsInList
            var list = new int[Math.Min(count, (uint)cap)];
            IntPtr arr = buf + Marshal.SizeOf<JOBOBJECT_BASIC_PROCESS_ID_LIST>();
            for (int i = 0; i < list.Length; i++)
                list[i] = (int)(IntPtr.Size == 8 ? Marshal.ReadInt64(arr, i * 8) : Marshal.ReadInt32(arr, i * 4));
            return list;
        }
        catch { return Array.Empty<int>(); }
        finally { Marshal.FreeHGlobal(buf); }
    }

    public bool AnyAlive()
    {
        foreach (int pid in Pids())
        {
            try { if (!Process.GetProcessById(pid).HasExited) return true; }
            catch { }
        }
        return false;
    }

    /// <summary>True if any process in the job currently has a visible top-level window.</summary>
    public bool HasVisibleWindow()
    {
        foreach (int pid in Pids())
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                if (p.HasExited) continue;
                if (p.ProcessName.Contains("Crash", StringComparison.OrdinalIgnoreCase)) continue;
                p.Refresh();
                if (p.MainWindowHandle != IntPtr.Zero) return true;
            }
            catch { }
        }
        return false;
    }

    /// <summary>Terminate every process in the group. Idempotent.</summary>
    public void KillAll()
    {
        if (_job == IntPtr.Zero) return;
        try { TerminateJobObject(_job, 0); }
        catch (Exception ex) { _log?.LogDebug(ex, "TerminateJobObject failed"); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_job != IntPtr.Zero) { CloseHandle(_job); _job = IntPtr.Zero; }
    }
}
