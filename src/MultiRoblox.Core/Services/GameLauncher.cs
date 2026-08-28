using System.Diagnostics;
using Microsoft.Extensions.Logging;
using MultiRoblox.Core.Models;
using MultiRoblox.Core.Storage;

namespace MultiRoblox.Core.Services;

/// <summary>
/// Turns an <see cref="Account"/> + <see cref="JoinRequest"/> into a running Roblox client, launched
/// straight from RobloxPlayerBeta.exe (not the protocol) so instances don't collide.
/// </summary>
public sealed class GameLauncher
{
    private readonly SettingsStore _settings;
    private readonly RobloxClientPool _pool;
    private readonly ILogger<GameLauncher>? _log;

    public GameLauncher(SettingsStore settings, RobloxClientPool pool, ILogger<GameLauncher>? log = null)
    {
        _settings = settings;
        _pool = pool;
        _log = log;
    }

    public sealed record LaunchResult(Process Process, string LaunchString, long BrowserTrackerId);

    public async Task<LaunchResult> LaunchAsync(Account account, JoinRequest join, CancellationToken ct = default)
    {
        string? exe = RobloxPlayerLocator.Resolve(_settings.Current.RobloxPlayerPathOverride);
        if (exe is null)
            throw new InvalidOperationException(
                "Could not find RobloxPlayerBeta.exe. Install Roblox, or set an explicit path in Settings.");

        var client = _pool.Get(account);
        string ticket = await client.GetAuthTicketAsync(ct);

        long btid = account.BrowserTrackerId != 0
            ? account.BrowserTrackerId
            : Random.Shared.NextInt64(100_000_000_000, 175_000_000_000);

        string launchString = LaunchStringBuilder.Build(ticket, join, btid);
        _log?.LogInformation("Launching {Account} -> place {Place} job {Job}", account.Username, join.PlaceId, join.JobId);

        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(exe)!,
        };
        psi.ArgumentList.Add(launchString);

        var proc = Process.Start(psi)
                   ?? throw new InvalidOperationException("Process.Start returned null for RobloxPlayerBeta.exe.");

        return new LaunchResult(proc, launchString, btid);
    }
}
