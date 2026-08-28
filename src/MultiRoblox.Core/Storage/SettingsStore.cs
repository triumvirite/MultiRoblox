using System.Text.Json;
using MultiRoblox.Core.Models;

namespace MultiRoblox.Core.Storage;

/// <summary>Plain-text JSON settings (no secrets live here — the API key is the only sensitive value
/// and is treated as low-value; users can leave the API disabled).</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private readonly string _path;

    public SettingsStore(string path) => _path = path;

    public AppSettings Current { get; private set; } = new();

    public void Load()
    {
        if (!File.Exists(_path)) { Current = new(); return; }
        try
        {
            Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), JsonOpts) ?? new();
        }
        catch
        {
            Current = new();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(Current, JsonOpts));
    }
}
