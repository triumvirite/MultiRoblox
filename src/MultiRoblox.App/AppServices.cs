using System.IO;
using Microsoft.Extensions.Logging;
using MultiRoblox.Core;
using MultiRoblox.Core.Models;
using MultiRoblox.Core.Services;
using MultiRoblox.Core.Storage;
using MultiRoblox.WebApi;
using Serilog;
using Serilog.Extensions.Logging;

namespace MultiRoblox.App;

/// <summary>Tiny hand-rolled composition root. One instance lives on <see cref="App"/>.</summary>
public sealed class AppServices : IDisposable
{
    public ILoggerFactory LoggerFactory { get; }
    public SettingsStore Settings { get; }
    public AccountStore Accounts { get; }
    public RobloxClientPool Pool { get; }
    public GameLauncher Launcher { get; }
    public InstanceManager Instances { get; }
    public CookieKeepAlive KeepAlive { get; }
    public ControlApiHost ControlApi { get; }
    public SingletonHolder Singleton { get; }

    public AppServices()
    {
        AppPaths.EnsureCreated();

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(Path.Combine(AppPaths.LogsDir, "multiroblox-.log"),
                rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7,
                shared: true, flushToDiskInterval: TimeSpan.FromMilliseconds(500))
            .CreateLogger();
        LoggerFactory = new SerilogLoggerFactory(Log.Logger);

        Settings = new SettingsStore(AppPaths.SettingsFile);
        Settings.Load();

        var protector = new SecretProtector(); // DPAPI only for v1; passphrase prompt can wrap this later
        Accounts = new AccountStore(AppPaths.AccountsFile, protector)
        {
            Log = msg => Serilog.Log.Information("AccountStore: {Msg}", msg),
        };

        Pool = new RobloxClientPool(Accounts);
        Launcher = new GameLauncher(Settings, Pool, LoggerFactory.CreateLogger<GameLauncher>());
        Instances = new InstanceManager(LoggerFactory.CreateLogger<InstanceManager>());
        KeepAlive = new CookieKeepAlive(Accounts, Pool, Settings, LoggerFactory.CreateLogger<CookieKeepAlive>());
        ControlApi = new ControlApiHost(Settings, Accounts, Launcher, Instances, Pool, LoggerFactory);

        Singleton = new SingletonHolder(LoggerFactory.CreateLogger<SingletonHolder>());
        ApplyMultiInstance();
    }

    /// <summary>Hold or release the Roblox singleton objects to match the current setting.</summary>
    public void ApplyMultiInstance()
    {
        if (Settings.Current.AllowMultipleInstances)
            Singleton.TryHold();
        else
            Singleton.Release();
    }

    public void LoadAccounts()
    {
        try { Accounts.Load(); }
        catch (Exception ex) { Log.Error(ex, "Failed to load accounts"); throw; }
    }

    public void Dispose()
    {
        try { ControlApi.StopAsync().GetAwaiter().GetResult(); } catch { }
        KeepAlive.Dispose();
        Instances.Dispose();
        Singleton.Dispose();
        Pool.Dispose();
        Log.CloseAndFlush();
    }
}
