using Lite.Scripting.Runtime;

namespace Lite.Scripting.Dom;

/// <summary>The browsing context's session history (HTML §7.2): the ordered list of document
/// entries every History facade in the context shares. Entries record whether they were
/// created by a same-document mutation (pushState/replaceState, owned by the live engine that
/// made them) or by a cross-document navigation (reloadable by URL — the engine holds no
/// bfcache, so traversing to a dead entry re-requests its URL through the navigation
/// pipeline). The owning session (BrowserWindow) adopts each newly loaded engine onto the
/// entry its navigation targeted.</summary>
internal sealed class SessionHistory
{
    internal sealed class Entry
    {
        internal string Url { get; set; }
        internal JsValue State { get; set; }
        internal bool SameDocument { get; set; }
        internal WeakReference<JsEngine>? Owner { get; set; }

        internal Entry(string url, JsValue state, bool sameDocument, JsEngine? owner)
        {
            Url = url;
            State = state;
            SameDocument = sameDocument;
            Owner = owner is null ? null : new WeakReference<JsEngine>(owner);
        }

        /// <summary>The entry's owning engine, when still live (a disposed engine's entry
        /// reads as ownerless and traversal falls back to a URL reload).</summary>
        internal JsEngine? LiveOwner()
            => Owner is { } r && r.TryGetTarget(out var e) && !e.IsDisposed ? e : null;
    }

    private readonly List<Entry> _entries = [];
    private int _index;

    internal int Length => _entries.Count;
    internal int Index => _index;
    internal string CurrentUrl => _entries.Count > 0 ? _entries[_index].Url : "about:blank";
    internal JsValue CurrentState => _entries.Count > 0 ? _entries[_index].State : JsValue.Null;

    /// <summary>The parse-time adoption: binds <paramref name="engine"/> to this shared list
    /// (its History facade reads it from creation) and seeds/positions the entry for the
    /// document being loaded.</summary>
    internal void AdoptFor(JsEngine engine, string url)
    {
        engine.AdoptSessionHistoryRaw(this);
        AddDocument(url, engine);
    }

    /// <summary>Seeds or adopts: when the URL matches an entry at or after the current index
    /// (a navigation a traversal requested), the context repositions onto that entry and it
    /// adopts the new engine; otherwise the forward entries truncate and a new cross-document
    /// entry appends (a fresh navigation). Returns the entry index.</summary>
    internal int AddDocument(string url, JsEngine engine)
    {
        for (var i = _index; i < _entries.Count; i++)
        {
            if (!string.Equals(_entries[i].Url, url, StringComparison.Ordinal)) continue;
            _index = i;
            _entries[i].Owner = new WeakReference<JsEngine>(engine);
            return i;
        }
        if (_index < _entries.Count - 1)
            _entries.RemoveRange(_index + 1, _entries.Count - _index - 1);
        _entries.Add(new Entry(url, JsValue.Null, sameDocument: false, engine));
        _index = _entries.Count - 1;
        return _index;
    }

    /// <summary>Initial seeding for a context's first document.</summary>
    internal void Seed(string url, JsEngine engine)
    {
        _entries.Clear();
        _entries.Add(new Entry(url, JsValue.Null, sameDocument: false, engine));
        _index = 0;
    }

    /// <summary>pushState: drop the forward entries, append a same-document entry owned by
    /// <paramref name="engine"/>, advance.</summary>
    internal void PushState(string url, JsValue state, JsEngine engine)
    {
        if (_index < _entries.Count - 1)
            _entries.RemoveRange(_index + 1, _entries.Count - _index - 1);
        _entries.Add(new Entry(url, state, sameDocument: true, engine));
        _index = _entries.Count - 1;
    }

    /// <summary>replaceState: rewrite the current entry (same-document, owned by the caller —
    /// an entry a navigation created flips to same-document, matching the spec's URL rewrite).</summary>
    internal void ReplaceState(string url, JsValue state, JsEngine engine)
    {
        _entries[_index] = new Entry(url, state, sameDocument: true, engine);
    }

    /// <summary>A cross-document navigation rewrote the current entry without a push (replace).</summary>
    internal void ReplaceCrossDocument(string url)
    {
        _entries[_index] = new Entry(url, JsValue.Null, sameDocument: false, null);
    }

    /// <summary>A cross-document navigation appended the current entry (push): the entry the
    /// facade created as same-document-owned flips to a reloadable cross-document entry.</summary>
    internal void MarkCurrentCrossDocument()
    {
        var e = _entries[_index];
        e.SameDocument = false;
        e.Owner = null;
    }

    /// <summary>The entry <paramref name="delta"/> steps away from the current one, or null
    /// when the target is out of range (a no-op traversal).</summary>
    internal Entry? Peek(int delta)
    {
        var target = _index + delta;
        if (delta == 0 || target < 0 || target >= _entries.Count) return null;
        return _entries[target];
    }

    /// <summary>Repositions onto <paramref name="entry"/> for a traversal the caller acts on
    /// (a reload for a cross-document or dead same-document entry, popstate for a live one).</summary>
    internal void Position(Entry entry)
    {
        var i = _entries.IndexOf(entry);
        if (i >= 0) _index = i;
    }

    /// <summary>The shared entry list position after the caller repositioned — history.length
    /// and history.state read through here.</summary>
    internal Entry? Current => _entries.Count > 0 ? _entries[_index] : null;
}
