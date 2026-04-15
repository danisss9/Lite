using Jint;
using Jint.Native;
using Lite.Models;
using Lite.Scripting.Dom;

namespace Lite.Scripting;

internal class JsEngine
{
    public static JsEngine? Instance { get; private set; }

    private readonly Engine _engine;
    private readonly JsWindow _jsWindow;

    private JsEngine(LayoutNode root, int viewportWidth = 800, int viewportHeight = 600)
    {
        _engine = new Engine(opts => opts.CatchClrExceptions());

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

        // Event constructor
        _engine.SetValue("Event", typeof(JsEvent));

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
    }

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

    internal Engine RawEngine => _engine;
}
