using Jint.Native;
using Lite.Models;

namespace Lite.Scripting.Dom;

internal class JsWindow
{
    private readonly JsEngine _engine;
    private int _nextTimerId = 1;
    private readonly Dictionary<int, System.Threading.Timer> _timers = [];

    public JsWindow(JsEngine engine) => _engine = engine;

    public void alert(object? message) => Lite.Utils.User32.MessageBox(IntPtr.Zero, message?.ToString() ?? "", "Alert", 0);

    public int setTimeout(JsValue fn, int delay = 0)
    {
        var id = _nextTimerId++;
        if (delay <= 0)
        {
            // Fire immediately for backwards compatibility with bootstrap scripts
            try { _engine.RawEngine.Invoke(fn); }
            catch (Exception ex) { Console.WriteLine($"[JS setTimeout] {ex.Message}"); }
            return id;
        }
        var timer = new System.Threading.Timer(_ =>
        {
            try { _engine.RawEngine.Invoke(fn); }
            catch (Exception ex) { Console.WriteLine($"[JS setTimeout] {ex.Message}"); }
            lock (_timers) { _timers.Remove(id); }
        }, null, delay, Timeout.Infinite);
        lock (_timers) { _timers[id] = timer; }
        return id;
    }

    public int setInterval(JsValue fn, int delay = 0)
    {
        var id = _nextTimerId++;
        if (delay <= 0) delay = 1;
        var timer = new System.Threading.Timer(_ =>
        {
            try { _engine.RawEngine.Invoke(fn); }
            catch (Exception ex) { Console.WriteLine($"[JS setInterval] {ex.Message}"); }
        }, null, delay, delay);
        lock (_timers) { _timers[id] = timer; }
        return id;
    }

    public void clearTimeout(int id)
    {
        lock (_timers)
        {
            if (_timers.Remove(id, out var timer))
                timer.Dispose();
        }
    }

    public void clearInterval(int id) => clearTimeout(id);

    // ---- getComputedStyle (Phase 7) ----
    public JsComputedStyle getComputedStyle(JsElement element, string? pseudoElement = null)
    {
        return new JsComputedStyle(element.Node);
    }
}
