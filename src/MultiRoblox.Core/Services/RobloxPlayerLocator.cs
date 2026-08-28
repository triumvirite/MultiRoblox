using Microsoft.Win32;

namespace MultiRoblox.Core.Services;

/// <summary>Finds the RobloxPlayerBeta.exe to launch directly (bypassing the protocol handler).</summary>
public static class RobloxPlayerLocator
{
    /// <param name="overridePath">Explicit path from settings, or empty/null to auto-detect.</param>
    /// <returns>Full path to an existing exe, or null if nothing was found.</returns>
    public static string? Resolve(string? overridePath = null)
    {
        if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath))
            return overridePath;

        foreach (var candidate in EnumerateCandidates())
        {
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }

    private static IEnumerable<string> EnumerateCandidates()
    {
        // 1. The registered protocol command: HKCR\roblox-player\shell\open\command  ->  "...exe" "%1"
        foreach (var root in new[] { Registry.CurrentUser, Registry.ClassesRoot })
        {
            string sub = root == Registry.CurrentUser
                ? @"Software\Classes\roblox-player\shell\open\command"
                : @"roblox-player\shell\open\command";
            using var key = root.OpenSubKey(sub);
            if (key?.GetValue(null) is string cmd)
            {
                var exe = ExtractExe(cmd);
                if (exe is not null) yield return exe;
            }
        }

        // 2. Vanilla install: %LocalAppData%\Roblox\Versions\version-xxxx\RobloxPlayerBeta.exe
        string versions = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Roblox", "Versions");
        if (Directory.Exists(versions))
        {
            foreach (var dir in Directory.EnumerateDirectories(versions)
                         .OrderByDescending(d => Directory.GetLastWriteTimeUtc(d)))
                yield return Path.Combine(dir, "RobloxPlayerBeta.exe");
        }

        // 3. Bloxstrap / Fishstrap
        foreach (var strap in new[] { "Bloxstrap", "Fishstrap" })
        {
            string bversions = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                strap, "Versions");
            if (!Directory.Exists(bversions)) continue;
            foreach (var dir in Directory.EnumerateDirectories(bversions)
                         .OrderByDescending(d => Directory.GetLastWriteTimeUtc(d)))
                yield return Path.Combine(dir, "RobloxPlayerBeta.exe");
        }
    }

    /// <summary>Pulls the exe path out of a shell command string like <c>"C:\..\x.exe" "%1"</c>.</summary>
    internal static string? ExtractExe(string command)
    {
        command = command.Trim();
        if (command.StartsWith('"'))
        {
            int end = command.IndexOf('"', 1);
            if (end > 1) return command[1..end];
        }
        int space = command.IndexOf(' ');
        return space > 0 ? command[..space] : command;
    }
}
