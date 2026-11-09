using Lite.Scripting.Runtime;

namespace Lite.Scripting.Dom;

/// <summary>
/// The History interface: a per-document facade over the browsing context's shared
/// <see cref="SessionHistory"/>. pushState/replaceState mutate the shared list as
/// same-document entries owned by this engine; back/forward/go traverse it — in place (popstate,
/// and hashchange when only the fragment differs) while the target entry's owner is this live
/// engine, otherwise by re-requesting the target URL through the navigation pipeline (the
/// engine holds no bfcache; a dead same-document entry reloads too). An engine created without
/// an injected session history owns a private one, preserving the standalone shape.
/// </summary>
public class JsHistory
{
    private readonly JsEngine _engine;
    private SessionHistory _shared;

    internal JsHistory(JsEngine engine, string initialUrl)
    {
        _engine = engine;
        _shared = new SessionHistory();
        _shared.Seed(initialUrl, engine);
    }

    /// <summary>Adopts the browsing context's shared history: the engine's first read sees the
    /// entry its navigation targeted (the session adopts it in <see cref="SessionHistory.AddDocument"/>).</summary>
    internal void Adopt(SessionHistory shared)
    {
        // Re-seed the shared list onto this document if the navigation was not part of a
        // traversal AddDocument already recorded (a fresh location.assign chain).
        _shared = shared;
    }

    internal SessionHistory Shared => _shared;

    public int length => _shared.Length;

    public string scrollRestoration { get; set; } = "auto";

    public JsValue state => _shared.CurrentState;

    /// <summary>Current entry URL (used by JsLocation/JsEngine to stay in sync).</summary>
    internal string CurrentUrl => _shared.CurrentUrl;

    public void pushState(JsValue state, string? title, string? url = null)
    {
        var resolved = _engine.ResolveAgainstCurrent(url) ?? CurrentUrl;
        _shared.PushState(resolved, state, _engine);
        _engine.SetCurrentUrl(resolved);
    }

    public void replaceState(JsValue state, string? title, string? url = null)
    {
        var resolved = _engine.ResolveAgainstCurrent(url) ?? CurrentUrl;
        _shared.ReplaceState(resolved, state, _engine);
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

        var target = _shared.Peek(delta);
        if (target is null) return; // out of range: no-op

        var oldUrl = CurrentUrl;
        if (target.SameDocument && ReferenceEquals(target.LiveOwner(), _engine))
        {
            // In-place traversal: the target entry is this engine's own same-document state.
            _shared.Position(target);
            _engine.SetCurrentUrl(target.Url);
            _engine.FirePopState(target.State);
            if (DiffersOnlyByFragment(oldUrl, target.Url) && !string.Equals(oldUrl, target.Url, StringComparison.Ordinal))
                _engine.FireHashChange(oldUrl, target.Url);
            return;
        }

        // Cross-document (or dead same-document): re-request the URL — the session repositions
        // when the loaded document is adopted (AddDocument matches the entry by URL).
        _shared.Position(target);
        _engine.RequestNavigation(target.Url);
    }

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
