namespace MultiRoblox.Core.Models;

public sealed class AppSettings
{
    /// <summary>Hold the Roblox singleton objects so multiple clients can run at once.</summary>
    public bool AllowMultipleInstances { get; set; } = true;

    /// <summary>Optional explicit path to RobloxPlayerBeta.exe. Empty = auto-detect from registry.</summary>
    public string RobloxPlayerPathOverride { get; set; } = "";

    /// <summary>
    /// Minutes between background cookie-rotation refreshes. 0 = only refresh once when the app opens
    /// (enough for a desktop app you close between sessions).
    /// </summary>
    public int CookieRefreshMinutes { get; set; } = 0;

    /// <summary>Re-launch an instance automatically if it disconnects or crashes.</summary>
    public bool AutoRelaunchOnDisconnect { get; set; } = false;

    public bool CloseToTray { get; set; } = false;

    // Local control API
    public bool WebApiEnabled { get; set; } = false;
    public int WebApiPort { get; set; } = 7963;
    public string WebApiKey { get; set; } = "";

    // Frame-rate cap (written to Roblox's ClientAppSettings.json on launch).
    //   Enabled = false          -> leave Roblox's own cap untouched
    //   Enabled = true, Target 0 -> uncapped
    //   Enabled = true, Target n -> capped at n
    public bool FpsUnlockerEnabled { get; set; } = false;
    public int FpsUnlockerTarget { get; set; } = 240;

    public string ThemeName { get; set; } = "Dark";

    /// <summary>Last-used join tab: "Manual" / "Favorites" / "Recents" / "PlayerFinder".</summary>
    public string JoinMode { get; set; } = "Manual";

    /// <summary>One-click "Quick Join" target. 0 = none set.</summary>
    public long QuickJoinPlaceId { get; set; }
    public string QuickJoinName { get; set; } = "";

    /// <summary>User-created account categories (kept even when no account uses one yet).</summary>
    public List<string> Categories { get; set; } = new();

    /// <summary>Locally-pinned games for the "Favorites" join tab.</summary>
    public List<FavoriteGame> Favorites { get; set; } = new();

    /// <summary>Games launched through the app, newest first (capped).</summary>
    public List<RecentGame> Recents { get; set; } = new();

    /// <summary>Optional extra passphrase layer on top of DPAPI. Empty = DPAPI only.</summary>
    public bool UsePassphrase { get; set; } = false;

    /// <summary>Saved column layout (width / order / sort) per data grid, keyed by a stable grid id.</summary>
    public Dictionary<string, List<GridColumnState>> GridLayouts { get; set; } = new();

    /// <summary>Width of the account sidebar column (draggable splitter). 0 = use the default.</summary>
    public double SidebarWidth { get; set; }
}
