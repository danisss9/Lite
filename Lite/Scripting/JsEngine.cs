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
    private Viewport? _viewport;

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
        try { _engine.Advanced.ProcessTasks(); }
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

    private JsEngine(LayoutNode root, int viewportWidth = 800, int viewportHeight = 600)
    {
        var baseUrl = Parser.BaseUrl ?? "about://lite/";
        _engine = new Engine(opts =>
        {
            opts.CatchClrExceptions();
            opts.EnableModules(new HttpModuleLoader(baseUrl));
        });

        _jsWindow = new JsWindow(this, viewportWidth, viewportHeight);
        var jsDocument = new JsDocument(_engine, root);

        _engine.SetValue("console", new JsConsole());
        _engine.SetValue("window", _jsWindow);
        _engine.SetValue("document", jsDocument);
        _engine.SetValue("alert", new Action<object?>(msg => _jsWindow.alert(msg)));

        // Timers
        _engine.SetValue("setTimeout", new Func<JsValue, int, int>((fn, delay) => _jsWindow.setTimeout(fn, delay)));
        _engine.SetValue("setInterval", new Func<JsValue, int, int>((fn, delay) => _jsWindow.setInterval(fn, delay)));
        _engine.SetValue("clearTimeout", new Action<int>(id => _jsWindow.clearTimeout(id)));
        _engine.SetValue("clearInterval", new Action<int>(id => _jsWindow.clearInterval(id)));

        // requestAnimationFrame / cancelAnimationFrame
        _engine.SetValue("requestAnimationFrame", new Func<JsValue, int>(fn => _jsWindow.requestAnimationFrame(fn)));
        _engine.SetValue("cancelAnimationFrame", new Action<int>(id => _jsWindow.cancelAnimationFrame(id)));

        // getComputedStyle
        _engine.SetValue("getComputedStyle", new Func<JsElement, string?, JsComputedStyle>(
            (el, pseudo) => _jsWindow.getComputedStyle(el, pseudo)));

        // XMLHttpRequest constructor
        _engine.SetValue("XMLHttpRequest", typeof(JsXmlHttpRequest));

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

    public static JsEngine Create(LayoutNode root, int viewportWidth = 800, int viewportHeight = 600)
    {
        Instance = new JsEngine(root, viewportWidth, viewportHeight);
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
}
