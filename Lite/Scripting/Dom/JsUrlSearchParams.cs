using System.Text;
using Jint;
using Jint.Native;

namespace Lite.Scripting.Dom;

/// <summary>
/// WHATWG URLSearchParams. Preserves insertion order and duplicate keys. When created
/// by a <see cref="JsUrl"/>, mutations are written back to the owning URL via
/// <see cref="OnChange"/> so <c>url.search</c> stays in sync (the spec requires it to be live).
/// </summary>
public class JsUrlSearchParams
{
    private readonly List<KeyValuePair<string, string>> _pairs = [];

    /// <summary>Invoked after any mutation so an owning URL can refresh its query string.</summary>
    internal Action<JsUrlSearchParams>? OnChange { get; set; }

    public JsUrlSearchParams() { }

    public JsUrlSearchParams(string? init) => Parse(init);

    /// <summary>Replaces all pairs from a query string (with or without leading '?').</summary>
    internal void Parse(string? init)
    {
        _pairs.Clear();
        if (string.IsNullOrEmpty(init)) return;
        var s = init.StartsWith('?') ? init[1..] : init;
        if (s.Length == 0) return;
        foreach (var part in s.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            string key, val;
            if (eq < 0) { key = part; val = ""; }
            else { key = part[..eq]; val = part[(eq + 1)..]; }
            _pairs.Add(new(Decode(key), Decode(val)));
        }
    }

    public void append(string name, string value)
    {
        _pairs.Add(new(name, value));
        OnChange?.Invoke(this);
    }

    public void delete(string name)
    {
        _pairs.RemoveAll(p => p.Key == name);
        OnChange?.Invoke(this);
    }

    public string? get(string name)
    {
        foreach (var p in _pairs) if (p.Key == name) return p.Value;
        return null;
    }

    public string[] getAll(string name) =>
        _pairs.Where(p => p.Key == name).Select(p => p.Value).ToArray();

    public bool has(string name) => _pairs.Any(p => p.Key == name);

    /// <summary>Sets the first occurrence and removes the rest (WHATWG set()).</summary>
    public void set(string name, string value)
    {
        bool replaced = false;
        for (int i = _pairs.Count - 1; i >= 0; i--)
        {
            if (_pairs[i].Key != name) continue;
            if (replaced) { _pairs.RemoveAt(i); }
            else { _pairs[i] = new(name, value); replaced = true; }
        }
        if (!replaced) _pairs.Add(new(name, value));
        OnChange?.Invoke(this);
    }

    public void sort()
    {
        _pairs.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
        OnChange?.Invoke(this);
    }

    public void forEach(JsValue callback)
    {
        var engine = JsEngine.Instance?.RawEngine;
        if (engine is null) return;
        foreach (var p in _pairs.ToList())
            engine.Invoke(callback, p.Value, p.Key, this);
    }

    public string[] keys() => _pairs.Select(p => p.Key).ToArray();
    public string[] values() => _pairs.Select(p => p.Value).ToArray();
    public string[][] entries() => _pairs.Select(p => new[] { p.Key, p.Value }).ToArray();

    public int size => _pairs.Count;

    public override string ToString()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < _pairs.Count; i++)
        {
            if (i > 0) sb.Append('&');
            sb.Append(Encode(_pairs[i].Key)).Append('=').Append(Encode(_pairs[i].Value));
        }
        return sb.ToString();
    }

    /// <summary>JS-visible alias (URLSearchParams has no toString-only contract in Jint interop).</summary>
    public string toString() => ToString();

    private static string Decode(string s) => Uri.UnescapeDataString(s.Replace('+', ' '));
    private static string Encode(string s) => Uri.EscapeDataString(s);
}
