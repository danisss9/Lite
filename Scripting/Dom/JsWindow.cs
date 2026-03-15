using Lite.Scripting;

namespace Lite.Scripting.Dom;

internal class JsWindow
{
    private readonly JsEngine _engine;

    public JsWindow(JsEngine engine) => _engine = engine;

    public void alert(object? message) => Lite.Utils.User32.MessageBox(IntPtr.Zero, message?.ToString() ?? "", "Alert", 0);
    public void setTimeout(Jint.Native.JsValue fn, int delay)
    {
        // Fire immediately (no real async) — good enough for scripting bootstrap code
        try { _engine.RawEngine.Invoke(fn); }
        catch (Exception ex) { Console.WriteLine($"[JS setTimeout] {ex.Message}"); }
    }
}
