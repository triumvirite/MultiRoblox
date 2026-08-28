using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using MultiRoblox.Core;

namespace MultiRoblox.App.Services;

/// <summary>Swaps the merged theme dictionary at runtime. Built-ins: "Dark", "Light". Any other name
/// is loaded from %AppData%\MultiRoblox\themes\&lt;name&gt;.json as a flat color map.</summary>
public static class ThemeManager
{
    private static readonly string[] Keys =
    {
        "BgColor","SurfaceColor","Surface2Color","BorderColor","TextColor","SubtleTextColor",
        "AccentColor","AccentTextColor","OkColor","WarnColor","DangerColor",
    };

    public static void Apply(string name)
    {
        var dict = new ResourceDictionary();
        if (name is "Dark" or "Light")
        {
            dict.Source = new Uri($"Themes/{name}.xaml", UriKind.Relative);
        }
        else
        {
            string path = Path.Combine(AppPaths.ThemesDir, name + ".json");
            if (File.Exists(path))
            {
                var map = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? new();
                // start from Dark as a base, then override
                dict.Source = new Uri("Themes/Dark.xaml", UriKind.Relative);
                foreach (var (k, v) in map)
                {
                    if (Keys.Contains(k) && ColorConverter.ConvertFromString(v) is Color c)
                        dict[k] = c;
                }
            }
            else
            {
                dict.Source = new Uri("Themes/Dark.xaml", UriKind.Relative);
            }
        }

        var merged = Application.Current.Resources.MergedDictionaries;
        if (merged.Count > 0) merged[0] = dict;
        else merged.Add(dict);
    }

    public static IEnumerable<string> AvailableThemes()
    {
        yield return "Dark";
        yield return "Light";
        if (Directory.Exists(AppPaths.ThemesDir))
            foreach (var f in Directory.EnumerateFiles(AppPaths.ThemesDir, "*.json"))
                yield return Path.GetFileNameWithoutExtension(f);
    }
}
