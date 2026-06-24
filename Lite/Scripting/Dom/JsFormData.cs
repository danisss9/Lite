using Jint.Native;
using Lite.Interaction;

namespace Lite.Scripting.Dom;

/// <summary>
/// XMLHttpRequest/fetch FormData. Holds string entries (file entries are out of scope until file
/// input lands), preserving insertion order and duplicate keys like the WHATWG spec.
/// </summary>
public class JsFormData
{
    private readonly List<KeyValuePair<string, string>> _entries = [];

    public JsFormData() { }

    /// <summary>new FormData(form) — pre-populates from a &lt;form&gt;'s successful controls.</summary>
    public JsFormData(JsElement? form)
    {
        if (form?.Node is not { } node) return;
        var query = FormSubmitter.BuildQuery(node);
        if (string.IsNullOrEmpty(query)) return;
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            var key = eq < 0 ? part : part[..eq];
            var val = eq < 0 ? "" : part[(eq + 1)..];
            _entries.Add(new(Decode(key), Decode(val)));
        }
    }

    public void append(string name, string value) => _entries.Add(new(name, value));

    public void delete(string name) => _entries.RemoveAll(p => p.Key == name);

    public string? get(string name)
    {
        foreach (var p in _entries) if (p.Key == name) return p.Value;
        return null;
    }

    public string[] getAll(string name) =>
        _entries.Where(p => p.Key == name).Select(p => p.Value).ToArray();

    public bool has(string name) => _entries.Any(p => p.Key == name);

    /// <summary>Sets the first occurrence and removes the rest (WHATWG set()).</summary>
    public void set(string name, string value)
    {
        bool replaced = false;
        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            if (_entries[i].Key != name) continue;
            if (replaced) _entries.RemoveAt(i);
            else { _entries[i] = new(name, value); replaced = true; }
        }
        if (!replaced) _entries.Add(new(name, value));
    }

    public void forEach(JsValue callback)
    {
        var engine = JsEngine.Instance?.RawEngine;
        if (engine is null) return;
        foreach (var p in _entries.ToList())
            engine.Invoke(callback, p.Value, p.Key, this);
    }

    public string[] keys() => _entries.Select(p => p.Key).ToArray();
    public string[] values() => _entries.Select(p => p.Value).ToArray();
    public string[][] entries() => _entries.Select(p => new[] { p.Key, p.Value }).ToArray();

    private static string Decode(string s) => Uri.UnescapeDataString(s.Replace('+', ' '));
}
