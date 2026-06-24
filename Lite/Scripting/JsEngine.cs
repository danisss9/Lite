using System.Collections.Concurrent;
using Jint;
using Jint.Native;
using Lite.Layout;
using Lite.Models;
using Lite.Scripting.Dom;

namespace Lite.Scripting;

internal class JsEngine
{
    public static JsEngine? Instance { get; private set; }

    private readonly Engine _engine;
    private readonly JsWindow _jsWindow;
    private readonly LayoutNode _root;
    private Viewport? _viewport;

    // ---- navigation state (Phase 2) ----
    /// <summary>The document's current URL. Mutated by location/history without reload for
    /// same-document changes, and tracked across cross-document navigations.</summary>
    public string CurrentUrl { get; private set; }
    public Dom.JsHistory History { get; }
    public Dom.JsLocation Location { get; }

    /// <summary>Set by the host to update the window title bar when document.title changes.</summary>
    internal Action<string>? OnTitleChange { get; set; }

    // ---- event loop ----
    // Macrotasks queued by timers/fetch/etc. They are drained on the UI thread so that
    // Jint (which is not thread-safe) is only ever touched from one thread.
    private readonly ConcurrentQueue<Action> _macrotasks = new();

    /// <summary>Raised (possibly from a background thread) when a task is enqueued, so the
    /// host message loop can wake up and drain it.</summary>
    internal event Action? TaskEnqueued;

    /// <summary>Queues a callback to run on the next event-loop turn (UI thread).</summary>
    internal void EnqueueMacrotask(Action task)
    {
        _macrotasks.Enqueue(task);
        TaskEnqueued?.Invoke();
    }

    internal bool HasPendingTasks => !_macrotasks.IsEmpty;

    /// <summary>
    /// Runs all currently-queued macrotasks on the calling (UI) thread. Each invocation
    /// drains the Jint microtask (Promise) queue automatically. Returns true if any ran.
    /// </summary>
    internal bool DrainTasks()
    {
        bool ran = false;
        // Snapshot the count so tasks enqueued by these tasks run on the next turn, not this one.
        int budget = _macrotasks.Count;
        while (budget-- > 0 && _macrotasks.TryDequeue(out var task))
        {
            try
            {
                task();
                // Per the HTML spec, a microtask checkpoint runs after each task — this is what
                // lets Promise .then() continuations (e.g. from fetch) actually execute.
                _engine.Advanced.ProcessTasks();
                Dom.MutationObserverRegistry.DeliverAll();
            }
            catch (Exception ex) { Console.WriteLine($"[JS task] {ex.Message}"); }
            ran = true;
        }
        return ran;
    }

    /// <summary>Runs any pending Promise continuations (microtask checkpoint). Call after
    /// invoking DOM event handlers so their .then() callbacks run promptly.</summary>
    internal void FlushMicrotasks()
    {
        try
        {
            _engine.Advanced.ProcessTasks();
            Dom.MutationObserverRegistry.DeliverAll();
        }
        catch (Exception ex) { Console.WriteLine($"[JS microtask] {ex.Message}"); }
    }

    /// <summary>Set by the host window to perform a page navigation (e.g. form submission).</summary>
    internal Action<string>? OnNavigate { get; set; }

    /// <summary>Requests a navigation, deferred onto the event loop so it runs after the
    /// current JS call stack unwinds (and on the UI thread).</summary>
    internal void RequestNavigation(string url)
    {
        if (OnNavigate is { } nav)
            EnqueueMacrotask(() => nav(url));
    }

    /// <summary>Resolves a (possibly relative) URL against the current document URL.
    /// Returns null when <paramref name="url"/> is null.</summary>
    internal string? ResolveAgainstCurrent(string? url)
    {
        if (url is null) return null;
        if (Uri.TryCreate(url, UriKind.Absolute, out var abs)) return abs.AbsoluteUri;
        if (Uri.TryCreate(CurrentUrl, UriKind.Absolute, out var baseUri) &&
            Uri.TryCreate(baseUri, url, out var resolved))
            return resolved.AbsoluteUri;
        return url;
    }

    /// <summary>Updates the current URL without firing events or reloading (used by
    /// history.pushState/replaceState).</summary>
    internal void SetCurrentUrl(string url) => CurrentUrl = url;

    /// <summary>Core navigation entry point used by location setters and assign/replace.
    /// Same-document (fragment-only) changes scroll + fire hashchange and push a history
    /// entry; cross-document changes push a history entry and ask the host to load.</summary>
    internal void Navigate(string url, bool replace)
    {
        var resolved = ResolveAgainstCurrent(url) ?? url;
        var oldUrl = CurrentUrl;

        if (DiffersOnlyByFragment(oldUrl, resolved))
        {
            if (replace) History.replaceState(JsValue.Null, null, resolved);
            else History.pushState(JsValue.Null, null, resolved);
            CurrentUrl = resolved;
            if (!string.Equals(oldUrl, resolved, StringComparison.Ordinal))
            {
                FireHashChange(oldUrl, resolved);
                ScrollToFragment(FragmentOf(resolved));
            }
            return;
        }

        // Cross-document: record the entry, then ask the host to load.
        if (replace) History.replaceState(JsValue.Null, null, resolved);
        else History.pushState(JsValue.Null, null, resolved);
        RequestNavigation(resolved);
    }

    /// <summary>Dispatches a popstate event with the given state to window listeners.</summary>
    internal void FirePopState(JsValue state)
    {
        try
        {
            var evt = new Dom.JsEvent { state = state };
            evt.initEvent("popstate");
            _jsWindow.DispatchEvent("popstate", JsValue.FromObject(_engine, evt));
        }
        catch (Exception ex) { Console.WriteLine($"[popstate] {ex.Message}"); }
    }

    /// <summary>Dispatches a hashchange event to window listeners.</summary>
    internal void FireHashChange(string oldUrl, string newUrl)
    {
        try
        {
            var evt = new Dom.JsEvent { oldURL = oldUrl, newURL = newUrl };
            evt.initEvent("hashchange");
            _jsWindow.DispatchEvent("hashchange", JsValue.FromObject(_engine, evt));
        }
        catch (Exception ex) { Console.WriteLine($"[hashchange] {ex.Message}"); }
    }

    /// <summary>Scrolls the viewport to the element whose id (or name) matches the fragment.</summary>
    internal void ScrollToFragment(string fragment)
    {
        if (string.IsNullOrEmpty(fragment) || _viewport is null) return;
        var target = FindByIdOrName(_root, fragment);
        if (target is not null)
            _viewport.ScrollTo(target.Box.BorderBox.Top);
    }

    private static LayoutNode? FindByIdOrName(LayoutNode node, string id)
    {
        if (node.Id == id || node.Attributes.GetValueOrDefault("name") == id) return node;
        foreach (var child in node.Children)
            if (FindByIdOrName(child, id) is { } found) return found;
        return null;
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

    private static string FragmentOf(string url)
    {
        var i = url.IndexOf('#');
        return i < 0 ? "" : url[(i + 1)..];
    }

    /// <summary>Updates the current URL on a host-driven (real) navigation so location/history
    /// reflect the loaded document.</summary>
    internal void NotifyNavigated(string url) => CurrentUrl = url;

    private JsEngine(LayoutNode root, int viewportWidth = 800, int viewportHeight = 600)
    {
        var baseUrl = Parser.BaseUrl ?? "about://lite/";
        _engine = new Engine(opts =>
        {
            opts.CatchClrExceptions();
            opts.EnableModules(new HttpModuleLoader(baseUrl));
        });

        _root = root;
        CurrentUrl = baseUrl;
        History = new Dom.JsHistory(this, baseUrl);
        Location = new Dom.JsLocation(this);

        _jsWindow = new JsWindow(this, viewportWidth, viewportHeight);
        var jsDocument = new JsDocument(_engine, root);

        _engine.SetValue("console", new JsConsole());
        // The CLR window object is exposed under an internal name; the host shim makes the
        // real `window` an alias for globalThis so `window.foo = x` works (CLR objects reject
        // arbitrary expando assignment, which real pages and test harnesses rely on).
        _engine.SetValue("__jsWindow", _jsWindow);
        _engine.SetValue("document", jsDocument);
        _engine.SetValue("location", Location);
        _engine.SetValue("history", History);
        _engine.SetValue("alert", new Action<object?>(msg => _jsWindow.alert(msg)));

        // Window dimensions/scroll exposed as globals so they survive window===globalThis.
        _engine.SetValue("__innerWidth", new Func<int>(() => _jsWindow.innerWidth));
        _engine.SetValue("__innerHeight", new Func<int>(() => _jsWindow.innerHeight));
        _engine.SetValue("__scrollX", new Func<double>(() => _jsWindow.scrollX));
        _engine.SetValue("__scrollY", new Func<double>(() => _jsWindow.scrollY));
        _engine.SetValue("scrollTo", new Action<int, int>((x, y) => _jsWindow.scrollTo(x, y)));
        _engine.SetValue("scrollBy", new Action<int, int>((x, y) => _jsWindow.scrollBy(x, y)));

        // Timers — delay/id come in as JsValue so a missing/undefined arg (e.g. setTimeout(fn))
        // coerces to 0 instead of throwing a CLR conversion error.
        _engine.SetValue("setTimeout", new Func<JsValue, JsValue, int>((fn, delay) => _jsWindow.setTimeout(fn, ToInt(delay))));
        _engine.SetValue("setInterval", new Func<JsValue, JsValue, int>((fn, delay) => _jsWindow.setInterval(fn, ToInt(delay))));
        _engine.SetValue("clearTimeout", new Action<JsValue>(id => _jsWindow.clearTimeout(ToInt(id))));
        _engine.SetValue("clearInterval", new Action<JsValue>(id => _jsWindow.clearInterval(ToInt(id))));

        // requestAnimationFrame / cancelAnimationFrame
        _engine.SetValue("requestAnimationFrame", new Func<JsValue, int>(fn => _jsWindow.requestAnimationFrame(fn)));
        _engine.SetValue("cancelAnimationFrame", new Action<int>(id => _jsWindow.cancelAnimationFrame(id)));

        // getComputedStyle
        _engine.SetValue("getComputedStyle", new Func<JsElement, string?, JsComputedStyle>(
            (el, pseudo) => _jsWindow.getComputedStyle(el, pseudo)));

        // XMLHttpRequest constructor
        _engine.SetValue("XMLHttpRequest", typeof(JsXmlHttpRequest));

        // URL / URLSearchParams constructors
        _engine.SetValue("URL", typeof(JsUrl));
        _engine.SetValue("URLSearchParams", typeof(JsUrlSearchParams));
        _engine.SetValue("FormData", typeof(JsFormData));

        // navigator
        _engine.SetValue("navigator", new JsNavigator());

        // MutationObserver constructor; reset the registry for this fresh page.
        Dom.MutationObserverRegistry.Reset();
        _engine.SetValue("MutationObserver", typeof(Dom.JsMutationObserver));

        // Event / CustomEvent constructors (JsEvent is a superset of both)
        _engine.SetValue("Event", typeof(JsEvent));
        _engine.SetValue("CustomEvent", typeof(JsEvent));
        _engine.SetValue("MouseEvent", typeof(JsEvent));
        _engine.SetValue("KeyboardEvent", typeof(JsEvent));

        // NodeFilter constants
        _engine.SetValue("NodeFilter", new
        {
            FILTER_ACCEPT = 1,
            FILTER_REJECT = 2,
            FILTER_SKIP = 3,
            SHOW_ALL = unchecked((int)0xFFFFFFFF),
            SHOW_ELEMENT = 0x1,
            SHOW_TEXT = 0x4,
            SHOW_DOCUMENT = 0x100,
            SHOW_DOCUMENT_FRAGMENT = 0x400,
        });

        // Node type constants
        _engine.SetValue("Node", new
        {
            ELEMENT_NODE = 1,
            TEXT_NODE = 3,
            DOCUMENT_NODE = 9,
            DOCUMENT_FRAGMENT_NODE = 11,
        });

        InstallHostApis();
    }

    /// <summary>Installs Web Storage, fetch, and microtask APIs onto the global object.</summary>
    private void InstallHostApis()
    {
        // ---- Web Storage ----
        var siteKey = "default";
        try
        {
            if (Parser.BaseUrl is { } b && Uri.TryCreate(b, UriKind.Absolute, out var u))
                siteKey = $"{u.Host}_{u.Port}";
        }
        catch { /* fall back to default site key */ }

        _engine.SetValue("localStorage", JsStorage.CreateLocal(siteKey));
        _engine.SetValue("sessionStorage", new JsStorage());

        // ---- fetch backing function ----
        _engine.SetValue("__nativeFetch", new Action<string, JsValue, JsValue>(
            (url, opts, cb) => JsFetch.Native(this, url, opts, cb)));

        // ---- JS-side shims: fetch() Promise wrapper + queueMicrotask polyfill ----
        _engine.Execute(HostShim);
    }

    private const string HostShim = """
        (function () {
          if (typeof globalThis.queueMicrotask !== 'function') {
            globalThis.queueMicrotask = function (cb) { Promise.resolve().then(cb); };
          }
          // window === self === globalThis so that `window.foo = x` defines a real global
          // (a CLR window object can't take arbitrary expando properties).
          globalThis.window = globalThis;
          globalThis.self = globalThis;
          // Top-level browsing context: parent/top reference this context, so frame-detection
          // (self !== self.parent) resolves to "not framed". (Real nested contexts: iframe phase.)
          if (!('parent' in globalThis)) globalThis.parent = globalThis;
          if (!('top' in globalThis)) globalThis.top = globalThis;
          if (!('frames' in globalThis)) globalThis.frames = globalThis;
          if (!('length' in globalThis)) globalThis.length = 0;
          if (!('name' in globalThis)) globalThis.name = '';
          // Window event surface forwards to the CLR window (which owns listener state).
          globalThis.addEventListener = function (t, fn, o) { return __jsWindow.addEventListener(t, fn, o); };
          globalThis.removeEventListener = function (t, fn, o) { return __jsWindow.removeEventListener(t, fn, o); };
          globalThis.dispatchEvent = function (e) { return __jsWindow.dispatchEvent(e); };
          // Live window dimensions / scroll offsets as global accessors.
          Object.defineProperty(globalThis, 'innerWidth', { get: __innerWidth, configurable: true });
          Object.defineProperty(globalThis, 'innerHeight', { get: __innerHeight, configurable: true });
          Object.defineProperty(globalThis, 'scrollX', { get: __scrollX, configurable: true });
          Object.defineProperty(globalThis, 'scrollY', { get: __scrollY, configurable: true });
          Object.defineProperty(globalThis, 'pageXOffset', { get: __scrollX, configurable: true });
          Object.defineProperty(globalThis, 'pageYOffset', { get: __scrollY, configurable: true });
          globalThis.fetch = function (url, options) {
            return new Promise(function (resolve, reject) {
              __nativeFetch(String(url), options || null, function (r) {
                if (r.error) { reject(new Error(r.error)); return; }
                resolve({
                  ok: r.ok, status: r.status, statusText: r.statusText, url: String(url),
                  text: function () { return Promise.resolve(r.body); },
                  json: function () { return Promise.resolve(JSON.parse(r.body)); }
                });
              });
            });
          };
        })();
        """;

    /// <summary>Raised after an engine is created but before any page scripts execute.
    /// Lets a host (e.g. the conformance harness) install globals like result reporters.</summary>
    internal static event Action<JsEngine>? OnCreated;

    public static JsEngine Create(LayoutNode root, int viewportWidth = 800, int viewportHeight = 600)
    {
        Instance = new JsEngine(root, viewportWidth, viewportHeight);
        OnCreated?.Invoke(Instance);
        return Instance;
    }

    public void Execute(string script)
    {
        if (string.IsNullOrWhiteSpace(script)) return;
        try { _engine.Execute(script); }
        catch (Exception ex) { Console.WriteLine($"[JS Error] {ex.Message}"); }
    }

    /// <summary>Registers an inline module's source under a specifier so it can be imported.</summary>
    internal void AddModule(string specifier, string code) => _engine.Modules.Add(specifier, code);

    /// <summary>Imports (evaluates) a module by specifier. The loader fetches src modules.</summary>
    internal void ImportModule(string specifier)
    {
        try { _engine.Modules.Import(specifier); }
        catch (Exception ex) { Console.WriteLine($"[JS Module] {ex.Message}"); }
    }

    /// <summary>
    /// Runs layout so geometry queries (getBoundingClientRect / offset*/client*) reflect the
    /// current DOM and styles. In a real window the paint loop lays out every frame; in headless
    /// use (tests, the conformance harness) nothing else triggers layout, so script-time geometry
    /// reads would otherwise see zero boxes. Layout is idempotent, so an extra call is safe.
    /// </summary>
    internal void EnsureLayout()
    {
        if (_layingOut) return; // guard against re-entrancy
        _layingOut = true;
        try { Lite.Layout.BoxEngine.Layout(_root, _jsWindow.innerWidth, _jsWindow.innerHeight); }
        catch (Exception ex) { Console.WriteLine($"[layout-on-demand] {ex.Message}"); }
        finally { _layingOut = false; }
    }
    private bool _layingOut;

    /// <summary>Dispatches the window <c>load</c> event to listeners registered via
    /// addEventListener. Fired once after all page scripts have executed.</summary>
    internal void DispatchLoad() => _jsWindow.DispatchEvent("load");

    /// <summary>Dispatches a named window-level event (e.g. hashchange, popstate).</summary>
    internal void DispatchWindowEvent(string type) => _jsWindow.DispatchEvent(type);

    /// <summary>Flushes pending requestAnimationFrame callbacks. Returns true if any were invoked.</summary>
    internal bool FlushRAF(double timestamp)
    {
        if (!_jsWindow.HasPendingRAF) return false;
        _jsWindow.FlushRAF(timestamp);
        return true;
    }

    /// <summary>Returns true if there are pending requestAnimationFrame callbacks.</summary>
    internal bool HasPendingRAF => _jsWindow.HasPendingRAF;

    /// <summary>Updates window.innerWidth/innerHeight.</summary>
    internal void UpdateViewportSize(int width, int height)
    {
        _jsWindow.innerWidth = width;
        _jsWindow.innerHeight = height;
    }

    /// <summary>Binds a viewport for window.scrollTo/scrollBy support.</summary>
    internal void SetViewport(Viewport viewport) => _viewport = viewport;

    internal void ScrollTo(float y) => _viewport?.ScrollTo(y);
    internal void ScrollBy(float delta) => _viewport?.ScrollBy(delta);
    internal float GetScrollY() => _viewport?.ScrollY ?? 0f;

    internal Engine RawEngine => _engine;

    /// <summary>Coerces a JS value to an int, treating null/undefined/NaN as 0 (matches the
    /// way browsers coerce timer delays and ids).</summary>
    private static int ToInt(JsValue v)
    {
        if (v is null || v.IsUndefined() || v.IsNull()) return 0;
        try
        {
            var d = v.IsNumber() ? v.AsNumber() : v.ToObject() is { } o ? Convert.ToDouble(o) : 0;
            return double.IsNaN(d) ? 0 : (int)d;
        }
        catch { return 0; }
    }
}
