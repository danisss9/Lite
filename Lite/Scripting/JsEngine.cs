using Jint;
using Jint.Native;
using Lite.Models;
using Lite.Scripting.Dom;

namespace Lite.Scripting;

internal class JsEngine
{
    public static JsEngine? Instance { get; private set; }

    private readonly Engine _engine;

    private JsEngine(LayoutNode root)
    {
        _engine = new Engine(opts => opts.CatchClrExceptions());

        var jsWindow = new JsWindow(this);
        var jsDocument = new JsDocument(_engine, root);

        _engine.SetValue("console",  new JsConsole());
        _engine.SetValue("window",   jsWindow);
        _engine.SetValue("document", jsDocument);
        _engine.SetValue("alert",    new Action<object?>(msg => jsWindow.alert(msg)));

        // Timers
        _engine.SetValue("setTimeout",    new Func<JsValue, int, int>((fn, delay) => jsWindow.setTimeout(fn, delay)));
        _engine.SetValue("setInterval",   new Func<JsValue, int, int>((fn, delay) => jsWindow.setInterval(fn, delay)));
        _engine.SetValue("clearTimeout",  new Action<int>(id => jsWindow.clearTimeout(id)));
        _engine.SetValue("clearInterval", new Action<int>(id => jsWindow.clearInterval(id)));

        // getComputedStyle
        _engine.SetValue("getComputedStyle", new Func<JsElement, string?, JsComputedStyle>(
            (el, pseudo) => jsWindow.getComputedStyle(el, pseudo)));

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

    public static JsEngine Create(LayoutNode root)
    {
        Instance = new JsEngine(root);
        return Instance;
    }

    public void Execute(string script)
    {
        if (string.IsNullOrWhiteSpace(script)) return;
        try { _engine.Execute(script); }
        catch (Exception ex) { Console.WriteLine($"[JS Error] {ex.Message}"); }
    }

    internal Engine RawEngine => _engine;
}
