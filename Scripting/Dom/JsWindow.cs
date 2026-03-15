using Lite.Scripting;

namespace Lite.Scripting.Dom;

internal class JsWindow
{
    private readonly JsEngine _engine;

    public JsWindow(JsEngine engine) => _engine = engine;

    public void alert(object? message) => Console.WriteLine($"[alert] {message}");
    public void setTimeout(Jint.Native.JsValue fn, int delay)
    {
        // Fire immediately (no real async) — good enough for scripting bootstrap code
        try { _engine.RawEngine.Invoke(fn); }
        catch (Exception ex) { Console.WriteLine($"[JS setTimeout] {ex.Message}"); }
    }
}
