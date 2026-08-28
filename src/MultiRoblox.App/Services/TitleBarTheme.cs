using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace MultiRoblox.App.Services;

/// <summary>
/// Best-effort dark title bar via DWM (Windows 10 20H1+ / 11). Keeps the OS caption but recolors it
/// to match the theme. If the OS ignores it, the caption just stays default — no harm.
/// </summary>
public static class TitleBarTheme
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_CAPTION_COLOR = 35;
    private const int DWMWA_TEXT_COLOR = 36;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public static void HookGlobally()
    {
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((s, _) => { if (s is Window w) Apply(w); }));
    }

    public static void ApplyToOpenWindows()
    {
        foreach (Window w in Application.Current.Windows) Apply(w);
    }

    public static void Apply(Window window)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        if (hwnd == IntPtr.Zero) return;

        var bg = Res("BgColor", Color.FromRgb(0x1E, 0x1F, 0x22));
        bool dark = (0.299 * bg.R + 0.587 * bg.G + 0.114 * bg.B) / 255.0 < 0.5;
        int d = dark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref d, sizeof(int));

        int caption = Ref("SurfaceColor", Color.FromRgb(0x2B, 0x2D, 0x31));
        int text = Ref("TextColor", Colors.White);
        DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref caption, sizeof(int));
        DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref text, sizeof(int));
    }

    private static Color Res(string key, Color fallback) =>
        Application.Current.TryFindResource(key) is Color c ? c : fallback;

    private static int Ref(string key, Color fallback)
    {
        var c = Res(key, fallback);
        return c.R | (c.G << 8) | (c.B << 16);
    }
}
