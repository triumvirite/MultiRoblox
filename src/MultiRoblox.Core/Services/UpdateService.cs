using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace MultiRoblox.Core.Services;

public sealed record UpdateInfo(
    Version Current,
    Version Latest,
    string LatestTag,
    string? ExeDownloadUrl,
    string ReleaseUrl,
    string Notes)
{
    public bool UpdateAvailable => Latest > Current && ExeDownloadUrl is not null;
}

/// <summary>
/// Compares the running build to the newest GitHub release and, on request, downloads the new
/// <c>MultiRoblox.exe</c> and swaps it in via a tiny detached script, then relaunches.
/// Public repo — no auth needed.
/// </summary>
public sealed class UpdateService
{
    private const string Owner = "triumvirite";
    private const string Repo = "MultiRoblox";
    private const string ExeAssetName = "MultiRoblox.exe";

    private readonly HttpClient _http;

    public UpdateService()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("MultiRoblox-Updater/1.0");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public static Version CurrentVersion =>
        Assembly.GetEntryAssembly()?.GetName().Version
        ?? Assembly.GetExecutingAssembly().GetName().Version
        ?? new Version(0, 0, 0);

    public async Task<UpdateInfo> CheckAsync(CancellationToken ct = default)
    {
        using var res = await _http.GetAsync(
            $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest", ct);
        res.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;

        string tag = root.GetProperty("tag_name").GetString() ?? "v0.0.0";
        string releaseUrl = root.GetProperty("html_url").GetString() ?? $"https://github.com/{Owner}/{Repo}/releases";
        string notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";

        string? exeUrl = null;
        if (root.TryGetProperty("assets", out var assets))
        {
            foreach (var a in assets.EnumerateArray())
            {
                if (string.Equals(a.GetProperty("name").GetString(), ExeAssetName, StringComparison.OrdinalIgnoreCase))
                {
                    exeUrl = a.GetProperty("browser_download_url").GetString();
                    break;
                }
            }
        }

        return new UpdateInfo(CurrentVersion, ParseTag(tag), tag, exeUrl, releaseUrl, notes);
    }

    /// <summary>
    /// Downloads the new exe next to the current one, spawns a detached script that waits for this
    /// process to exit, swaps the files, and relaunches. Caller should shut the app down right after.
    /// </summary>
    public async Task DownloadAndApplyAsync(UpdateInfo info, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (info.ExeDownloadUrl is null) throw new InvalidOperationException("No downloadable exe in the latest release.");

        string currentExe = Environment.ProcessPath
            ?? Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new InvalidOperationException("Can't locate the running exe.");
        string dir = Path.GetDirectoryName(currentExe)!;
        string newExe = Path.Combine(dir, "MultiRoblox.new.exe");

        // download with progress
        using (var res = await _http.GetAsync(info.ExeDownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            res.EnsureSuccessStatusCode();
            long? total = res.Content.Headers.ContentLength;
            await using var src = await res.Content.ReadAsStreamAsync(ct);
            await using var dst = File.Create(newExe);
            var buf = new byte[81920];
            long read = 0;
            int n;
            while ((n = await src.ReadAsync(buf, ct)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), ct);
                read += n;
                if (total is > 0) progress?.Report((double)read / total.Value);
            }
        }
        progress?.Report(1.0);

        int pid = Environment.ProcessId;
        string script = Path.Combine(Path.GetTempPath(), $"mr-update-{Guid.NewGuid():N}.cmd");
        await File.WriteAllTextAsync(script, $"""
            @echo off
            :wait
            tasklist /fi "PID eq {pid}" 2>nul | find "{pid}" >nul && ( timeout /t 1 /nobreak >nul & goto wait )
            move /y "{currentExe}" "{currentExe}.old" >nul
            move /y "{newExe}" "{currentExe}" >nul
            del "{currentExe}.old" >nul 2>&1
            start "" "{currentExe}"
            del "%~f0"
            """, ct);

        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{script}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden,
        });
    }

    internal static Version ParseTag(string tag)
    {
        tag = tag.TrimStart('v', 'V').Trim();
        return Version.TryParse(tag, out var v) ? Normalize(v) : new Version(0, 0, 0);
    }

    private static Version Normalize(Version v) =>
        new(v.Major, v.Minor < 0 ? 0 : v.Minor, v.Build < 0 ? 0 : v.Build);
}
