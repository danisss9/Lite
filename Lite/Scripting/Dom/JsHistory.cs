using Jint.Native;

namespace Lite.Scripting.Dom;

/// <summary>
/// History API. Maintains an in-memory session history of (url, state) entries. pushState /
/// replaceState mutate the entry list and the current URL without a reload; back/forward/go
/// traverse it and fire a <c>popstate</c> event. Cross-document traversal (entries that point
/// at a different document) is treated as same-document for state purposes — a documented
/// simplification; full reload-on-traversal can be layered on later.
/// </summary>
public class JsHistory
{
    internal sealed record Entry(string Url, JsValue State);

    private readonly JsEngine _engine;
    private readonly List<Entry> _entries = [];
    private int _index;

    internal JsHistory(JsEngine engine, string initialUrl)
    {
        _engine = engine;
        _entries.Add(new Entry(initialUrl, JsValue.Null));
        _index = 0;
    }

    public int length => _entries.Count;

    public string scrollRestoration { get; set; } = "auto";

    public JsValue state => _entries[_index].State;

    /// <summary>Current entry URL (used by JsLocation/JsEngine to stay in sync).</summary>
    internal string CurrentUrl => _entries[_index].Url;

    public void pushState(JsValue state, string? title, string? url = null)
    {
        var resolved = _engine.ResolveAgainstCurrent(url) ?? CurrentUrl;
        // Drop any forward entries, then append and advance.
        if (_index < _entries.Count - 1)
            _entries.RemoveRange(_index + 1, _entries.Count - _index - 1);
        _entries.Add(new Entry(resolved, state));
        _index = _entries.Count - 1;
        _engine.SetCurrentUrl(resolved);
    }

    public void replaceState(JsValue state, string? title, string? url = null)
    {
        var resolved = _engine.ResolveAgainstCurrent(url) ?? CurrentUrl;
        _entries[_index] = new Entry(resolved, state);
        _engine.SetCurrentUrl(resolved);
    }

    public void back() => go(-1);
    public void forward() => go(1);

    public void go(int delta = 0)
    {
        if (delta == 0)
        {
            // history.go(0) reloads the current document.
            _engine.RequestNavigation(CurrentUrl);
            return;
        }

        var target = _index + delta;
        if (target < 0 || target >= _entries.Count) return; // out of range: no-op

        var oldUrl = CurrentUrl;
        _index = target;
        var entry = _entries[_index];
        _engine.SetCurrentUrl(entry.Url);

        // Fire popstate (and hashchange when only the fragment differs).
        _engine.FirePopState(entry.State);
        if (DiffersOnlyByFragment(oldUrl, entry.Url) && !UrlsEqual(oldUrl, entry.Url))
            _engine.FireHashChange(oldUrl, entry.Url);
    }

    private static bool UrlsEqual(string a, string b) => string.Equals(a, b, StringComparison.Ordinal);

    private static bool DiffersOnlyByFragment(string a, string b)
    {
        static string StripFragment(string u)
        {
            var i = u.IndexOf('#');
            return i < 0 ? u : u[..i];
        }
        return StripFragment(a) == StripFragment(b);
    }
}
