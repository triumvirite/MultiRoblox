using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MultiRoblox.Core.Models;
using MultiRoblox.Core.Services;
using MultiRoblox.Core.Storage;

namespace MultiRoblox.WebApi;

/// <summary>
/// In-process localhost control API. Mirrors the endpoints of the original RAM so existing helper
/// scripts port over. Every request must present the configured key (header <c>Key</c> or query
/// <c>?key=</c>). Bound to 127.0.0.1 only. Disabled by default.
/// </summary>
public sealed class ControlApiHost : IAsyncDisposable
{
    private readonly SettingsStore _settings;
    private readonly AccountStore _accounts;
    private readonly GameLauncher _launcher;
    private readonly InstanceManager _instances;
    private readonly RobloxClientPool _pool;
    private readonly ILoggerFactory? _loggerFactory;

    private WebApplication? _app;

    /// <summary>Invoked when the API asks to launch an account; lets the UI own instance registration.</summary>
    public Func<Account, JoinRequest, Task>? LaunchHandler { get; set; }

    public ControlApiHost(SettingsStore settings, AccountStore accounts, GameLauncher launcher,
        InstanceManager instances, RobloxClientPool pool, ILoggerFactory? loggerFactory = null)
    {
        _settings = settings;
        _accounts = accounts;
        _launcher = launcher;
        _instances = instances;
        _pool = pool;
        _loggerFactory = loggerFactory;
    }

    public bool IsRunning => _app is not null;

    public async Task StartAsync()
    {
        if (_app is not null) return;
        var s = _settings.Current;
        if (!s.WebApiEnabled) return;
        if (string.IsNullOrWhiteSpace(s.WebApiKey))
            throw new InvalidOperationException("Set a Web API key before enabling the API.");

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, s.WebApiPort));

        _app = builder.Build();
        _app.Use(async (ctx, next) =>
        {
            string? key = ctx.Request.Headers["Key"].FirstOrDefault() ?? ctx.Request.Query["key"].FirstOrDefault();
            if (key != _settings.Current.WebApiKey)
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await ctx.Response.WriteAsync("bad key");
                return;
            }
            await next();
        });

        MapEndpoints(_app);
        await _app.StartAsync();
    }

    public async Task StopAsync()
    {
        if (_app is null) return;
        await _app.StopAsync();
        await _app.DisposeAsync();
        _app = null;
    }

    private void MapEndpoints(WebApplication app)
    {
        app.MapGet("/GetAccounts", () =>
            Results.Json(_accounts.Accounts.Select(a => new
            {
                a.Username, a.DisplayName, a.UserId, a.Group, a.Note
            })));

        app.MapGet("/GetCookie", (string account) =>
        {
            var a = _accounts.FindByName(account);
            return a is null ? Results.NotFound() : Results.Text(a.SecurityToken);
        });

        app.MapGet("/GetAuthTicket", async (string account) =>
        {
            var a = _accounts.FindByName(account);
            if (a is null) return Results.NotFound();
            try { return Results.Text(await _pool.Get(a).GetAuthTicketAsync()); }
            catch (Exception ex) { return Results.Problem(ex.Message); }
        });

        app.MapPost("/LaunchAccount", async (LaunchBody body) =>
        {
            var a = _accounts.FindByName(body.Account);
            if (a is null) return Results.NotFound($"no account '{body.Account}'");
            if (!long.TryParse(body.PlaceId, out long placeId)) return Results.BadRequest("bad placeId");

            var join = string.IsNullOrWhiteSpace(body.JobId)
                ? JoinRequest.Place(placeId)
                : JoinRequest.Server(placeId, body.JobId!);

            if (LaunchHandler is not null) { await LaunchHandler(a, join); return Results.Ok("launched"); }
            await _launcher.LaunchAsync(a, join);
            return Results.Ok("launched");
        });

        app.MapGet("/GetLaunchAccount", async (string account, string placeId, string? jobId) =>
        {
            var a = _accounts.FindByName(account);
            if (a is null) return Results.NotFound();
            if (!long.TryParse(placeId, out long pid)) return Results.BadRequest();
            var join = string.IsNullOrWhiteSpace(jobId) ? JoinRequest.Place(pid) : JoinRequest.Server(pid, jobId!);
            if (LaunchHandler is not null) { await LaunchHandler(a, join); return Results.Ok("launched"); }
            await _launcher.LaunchAsync(a, join);
            return Results.Ok("launched");
        });

        app.MapGet("/GetInstances", () =>
            Results.Json(_instances.Snapshot().Select(i => new
            {
                i.AccountLabel, i.PlaceId, i.JobId, State = i.State.ToString(), i.ProcessIds
            })));

        app.MapPost("/TerminateAccount", (string account) =>
        {
            foreach (var i in _instances.Snapshot())
            {
                var a = _accounts.FindById(i.AccountId);
                if (a is not null && (a.Username.Equals(account, StringComparison.OrdinalIgnoreCase)
                                      || a.DisplayName.Equals(account, StringComparison.OrdinalIgnoreCase)))
                    _instances.Terminate(i);
            }
            return Results.Ok();
        });
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    private sealed record LaunchBody(string Account, string PlaceId, string? JobId);
}
