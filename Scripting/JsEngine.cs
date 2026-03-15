using Jint;
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
        _engine.SetValue("console",  new JsConsole());
        _engine.SetValue("window",   jsWindow);
        _engine.SetValue("document", new JsDocument(_engine, root));
        _engine.SetValue("alert",    new Action<object?>(msg => jsWindow.alert(msg)));
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
