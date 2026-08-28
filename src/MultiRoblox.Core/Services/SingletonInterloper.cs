using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using MultiRoblox.Core.Interop;

namespace MultiRoblox.Core.Services;

/// <summary>
/// Handles the case <see cref="SingletonHolder"/> can't: a Roblox client that was started <b>outside</b>
/// MultiRoblox is already running and <b>owns</b> <c>ROBLOX_singletonEvent</c> / <c>ROBLOX_singletonMutex</c>.
/// Our own handle to those objects doesn't stop that client from bouncing new ones, so we reach into
/// each running <c>RobloxPlayerBeta.exe</c>, find the handles it holds to those named objects, and
/// close them remotely (<c>DuplicateHandle</c> + <c>DUPLICATE_CLOSE_SOURCE</c>). Once no client owns
/// the singleton, new clients launch side-by-side.
/// </summary>
public static class SingletonInterloper
{
    private static readonly string[] TargetSuffixes = { "ROBLOX_singletonEvent", "ROBLOX_singletonMutex" };
    private static readonly string[] TargetTypes = { "Event", "Mutant" };

    /// <summary>
    /// Closes singleton handles held by every running RobloxPlayerBeta process.
    /// Returns the number of handles closed. No-op (returns 0) if nothing matched or access was denied.
    /// </summary>
    public static int FreeExistingClients(ILogger? log = null)
    {
        var robloxPids = new HashSet<int>();
        foreach (var p in Process.GetProcessesByName("RobloxPlayerBeta"))
        {
            robloxPids.Add(p.Id);
            p.Dispose();
        }
        if (robloxPids.Count == 0) return 0;

        List<NativeMethods.SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX> handles;
        try { handles = EnumerateHandles(); }
        catch (Exception ex) { log?.LogWarning(ex, "handle enumeration failed"); return 0; }

        int closed = 0;
        var procCache = new Dictionary<int, IntPtr>();
        IntPtr self = NativeMethods.GetCurrentProcess();

        try
        {
            foreach (var h in handles)
            {
                int pid = (int)h.UniqueProcessId;
                if (!robloxPids.Contains(pid)) continue;

                if (!procCache.TryGetValue(pid, out IntPtr src))
                {
                    src = NativeMethods.OpenProcess(NativeMethods.PROCESS_DUP_HANDLE, false, (uint)pid);
                    procCache[pid] = src;
                }
                if (src == IntPtr.Zero) continue;

                // Duplicate into our process so we can inspect type + name.
                if (!NativeMethods.DuplicateHandle(src, h.HandleValue, self, out IntPtr dup, 0, false,
                        NativeMethods.DUPLICATE_SAME_ACCESS))
                    continue;

                try
                {
                    string? type = QueryString(dup, NativeMethods.ObjectTypeInformation);
                    if (type is null || Array.IndexOf(TargetTypes, type) < 0) continue;

                    string? name = QueryString(dup, NativeMethods.ObjectNameInformation);
                    if (name is null || !TargetSuffixes.Any(s => name.EndsWith(s, StringComparison.Ordinal)))
                        continue;

                    // Close it in the Roblox process.
                    if (NativeMethods.DuplicateHandle(src, h.HandleValue, IntPtr.Zero, out _, 0, false,
                            NativeMethods.DUPLICATE_CLOSE_SOURCE))
                    {
                        closed++;
                        log?.LogInformation("closed {Type} handle '{Name}' in RobloxPlayerBeta pid {Pid}", type, name, pid);
                    }
                }
                finally
                {
                    NativeMethods.CloseHandle(dup);
                }
            }
        }
        finally
        {
            foreach (var hp in procCache.Values)
                if (hp != IntPtr.Zero) NativeMethods.CloseHandle(hp);
        }

        return closed;
    }

    internal static List<NativeMethods.SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX> EnumerateHandles()
    {
        int len = 1 << 20;
        IntPtr buf = Marshal.AllocHGlobal(len);
        try
        {
            int status;
            while ((status = NativeMethods.NtQuerySystemInformation(
                       NativeMethods.SystemExtendedHandleInformation, buf, len, out int needed))
                   == NativeMethods.STATUS_INFO_LENGTH_MISMATCH)
            {
                Marshal.FreeHGlobal(buf);
                len = Math.Max(needed, len * 2);
                buf = Marshal.AllocHGlobal(len);
            }
            if (status != 0) throw new InvalidOperationException($"NtQuerySystemInformation 0x{status:X8}");

            var list = new List<NativeMethods.SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX>();
            nint count = Marshal.ReadIntPtr(buf);                       // ULONG_PTR NumberOfHandles
            IntPtr entry = buf + IntPtr.Size * 2;                        // skip NumberOfHandles + Reserved
            int stride = Marshal.SizeOf<NativeMethods.SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX>();
            for (nint i = 0; i < count; i++)
            {
                list.Add(Marshal.PtrToStructure<NativeMethods.SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX>(entry));
                entry += stride;
            }
            return list;
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    /// <summary>NtQueryObject for the name/type UNICODE_STRING; null on failure.</summary>
    private static string? QueryString(IntPtr handle, int infoClass)
    {
        int len = 2048;
        IntPtr buf = Marshal.AllocHGlobal(len);
        try
        {
            int status = NativeMethods.NtQueryObject(handle, infoClass, buf, len, out int needed);
            if (status == NativeMethods.STATUS_INFO_LENGTH_MISMATCH && needed > 0)
            {
                Marshal.FreeHGlobal(buf);
                len = needed;
                buf = Marshal.AllocHGlobal(len);
                status = NativeMethods.NtQueryObject(handle, infoClass, buf, len, out _);
            }
            if (status != 0) return null;

            // ObjectTypeInformation puts a UNICODE_STRING at the top of OBJECT_TYPE_INFORMATION too.
            var us = Marshal.PtrToStructure<NativeMethods.UNICODE_STRING>(buf);
            if (us.Buffer == IntPtr.Zero || us.Length == 0) return "";
            return Marshal.PtrToStringUni(us.Buffer, us.Length / 2);
        }
        catch { return null; }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }
}
