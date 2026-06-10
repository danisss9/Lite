using System.Text;
using System.Text.Json;

namespace Lite.Scripting.Dom;

/// <summary>
/// Web Storage API (localStorage / sessionStorage). localStorage persists to disk;
/// sessionStorage is in-memory for the process lifetime.
/// Note: property-style access (localStorage.foo) is not supported — use getItem/setItem,
/// matching the engine's CLR-object binding model.
/// </summary>
public class JsStorage
{
    private readonly Dictionary<string, string> _data = new(StringComparer.Ordinal);
    private readonly string? _persistPath;

    public JsStorage(string? persistPath = null)
    {
        _persistPath = persistPath;
        Load();
    }

    public int length => _data.Count;

    public string? getItem(string key) => _data.TryGetValue(key, out var v) ? v : null;

    public void setItem(string key, object? value)
    {
        _data[key] = value?.ToString() ?? "null";
        Persist();
    }

    public void removeItem(string key)
    {
        if (_data.Remove(key)) Persist();
    }

    public void clear()
    {
        if (_data.Count == 0) return;
        _data.Clear();
        Persist();
    }

    public string? key(int index) =>
        index >= 0 && index < _data.Count ? _data.Keys.ElementAt(index) : null;

    private void Load()
    {
        if (_persistPath is null || !File.Exists(_persistPath)) return;
        try
        {
            var json = File.ReadAllText(_persistPath);
            var loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (loaded is not null)
                foreach (var (k, v) in loaded) _data[k] = v;
        }
        catch { /* corrupt store — start empty */ }
    }

    private void Persist()
    {
        if (_persistPath is null) return;
        try
        {
            var dir = Path.GetDirectoryName(_persistPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(_persistPath, JsonSerializer.Serialize(_data), Encoding.UTF8);
        }
        catch { /* best-effort persistence */ }
    }

    /// <summary>Creates the persistent localStorage for a given origin/site key.</summary>
    internal static JsStorage CreateLocal(string siteKey)
    {
        var baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Lite", "Storage");
        var safe = string.Concat(siteKey.Select(c => char.IsLetterOrDigit(c) ? c : '_'));
        if (safe.Length > 64) safe = safe[..64];
        return new JsStorage(Path.Combine(baseDir, safe + ".json"));
    }
}
