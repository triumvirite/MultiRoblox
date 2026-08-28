using System.Diagnostics;
using System.Management;

namespace MultiRoblox.Core.Services;

public sealed record RobloxProcess(int Pid, string Name, string CommandLine, DateTime StartTime);

/// <summary>Enumerates running Roblox client processes and their command lines (via WMI).</summary>
public static class ProcessScanner
{
    private static readonly string[] Names =
        { "RobloxPlayerBeta", "RobloxCrashHandler", "RobloxPlayerBeta_x64" };

    public static IReadOnlyList<RobloxProcess> Scan()
    {
        var result = new List<RobloxProcess>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT ProcessId, Name, CommandLine, CreationDate FROM Win32_Process " +
                "WHERE Name = 'RobloxPlayerBeta.exe' OR Name = 'RobloxCrashHandler.exe'");
            foreach (ManagementObject mo in searcher.Get())
            {
                int pid = Convert.ToInt32(mo["ProcessId"]);
                string name = (mo["Name"] as string ?? "").Replace(".exe", "");
                string cmd = mo["CommandLine"] as string ?? "";
                DateTime start = DateTime.Now;
                if (mo["CreationDate"] is string cd && cd.Length >= 14)
                {
                    try { start = ManagementDateTimeConverter.ToDateTime(cd); } catch { }
                }
                result.Add(new RobloxProcess(pid, name, cmd, start));
            }
        }
        catch
        {
            // WMI unavailable — fall back to a name-only scan without command lines.
            foreach (var n in Names)
            foreach (var p in Process.GetProcessesByName(n))
            {
                try { result.Add(new RobloxProcess(p.Id, n, "", p.StartTime)); }
                catch { }
            }
        }
        return result;
    }
}
