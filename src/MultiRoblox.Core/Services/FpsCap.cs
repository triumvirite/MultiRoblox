using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace MultiRoblox.Core.Services;

/// <summary>
/// Controls the Roblox client's frame-rate cap by writing <c>DFIntTaskSchedulerTargetFps</c> into
/// <c>ClientSettings\ClientAppSettings.json</c> next to the RobloxPlayerBeta.exe that will launch.
/// <list type="bullet">
///   <item>disabled — our key is removed, so Roblox falls back to its own default (60)</item>
///   <item>enabled, target 0 — uncapped (a very large value)</item>
///   <item>enabled, target n — capped at n</item>
/// </list>
/// Other keys already in the file are preserved.
/// </summary>
public static class FpsCap
{
    private const string Key = "DFIntTaskSchedulerTargetFps";
    private const int Uncapped = 10000;

    private static readonly JsonSerializerOptions WriteOpts = new() { WriteIndented = true };

    public static void Apply(string robloxPlayerExePath, bool enabled, int target, ILogger? log = null)
    {
        try
        {
            var versionDir = Path.GetDirectoryName(robloxPlayerExePath);
            if (string.IsNullOrEmpty(versionDir)) return;

            var dir = Path.Combine(versionDir, "ClientSettings");
            var file = Path.Combine(dir, "ClientAppSettings.json");

            JsonObject root = new();
            if (File.Exists(file))
            {
                try { root = JsonNode.Parse(File.ReadAllText(file)) as JsonObject ?? new(); }
                catch { root = new(); }
            }

            if (enabled)
            {
                int value = target <= 0 ? Uncapped : target;
                root[Key] = value;
                Directory.CreateDirectory(dir);
                File.WriteAllText(file, root.ToJsonString(WriteOpts));
                log?.LogInformation("FPS cap: {Key}={Value} -> {File}", Key, value, file);
            }
            else if (root.Remove(Key))
            {
                if (root.Count == 0)
                    File.Delete(file);
                else
                    File.WriteAllText(file, root.ToJsonString(WriteOpts));
                log?.LogInformation("FPS cap: removed {Key} from {File}", Key, file);
            }
        }
        catch (Exception ex)
        {
            log?.LogWarning(ex, "FPS cap: could not update ClientAppSettings.json");
        }
    }
}
