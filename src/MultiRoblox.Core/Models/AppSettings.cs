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

    // FPS unlocker
    public bool FpsUnlockerEnabled { get; set; } = false;
    public int FpsUnlockerTarget { get; set; } = 240;

    public string ThemeName { get; set; } = "Dark";

    /// <summary>User-created account categories (kept even when no account uses one yet).</summary>
    public List<string> Categories { get; set; } = new();

    /// <summary>Optional extra passphrase layer on top of DPAPI. Empty = DPAPI only.</summary>
    public bool UsePassphrase { get; set; } = false;
}
