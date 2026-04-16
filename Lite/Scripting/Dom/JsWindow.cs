using Jint.Native;
using Lite.Models;

namespace Lite.Scripting.Dom;

internal class JsWindow
{
    private readonly JsEngine _engine;
    private int _nextTimerId = 1;
    private readonly Dictionary<int, System.Threading.Timer> _timers = [];

    // Viewport dimensions
    public int innerWidth { get; set; }
    public int innerHeight { get; set; }

    // requestAnimationFrame
    private int _nextRafId = 1;
    private readonly List<(int Id, JsValue Fn)> _rafCallbacks = [];
    internal bool HasPendingRAF => _rafCallbacks.Count > 0;

    public JsWindow(JsEngine engine, int viewportWidth = 800, int viewportHeight = 600)
    {
        _engine = engine;
        innerWidth = viewportWidth;
        innerHeight = viewportHeight;
    }

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

    // ---- requestAnimationFrame / cancelAnimationFrame ----
    public int requestAnimationFrame(JsValue fn)
    {
        var id = _nextRafId++;
        _rafCallbacks.Add((id, fn));
        return id;
    }

    public void cancelAnimationFrame(int id)
    {
        _rafCallbacks.RemoveAll(r => r.Id == id);
    }

    // ---- scroll methods ----
    public void scrollTo(int x, int y)
    {
        _engine.ScrollTo(y);
    }

    public void scrollBy(int x, int y)
    {
        _engine.ScrollBy(y);
    }

    public double scrollX => 0;
    public double scrollY => _engine.GetScrollY();
    public double pageXOffset => 0;
    public double pageYOffset => _engine.GetScrollY();

    /// <summary>Invokes all pending RAF callbacks with the given timestamp and clears the queue.</summary>
    internal void FlushRAF(double timestamp)
    {
        if (_rafCallbacks.Count == 0) return;
        var callbacks = _rafCallbacks.ToList();
        _rafCallbacks.Clear();
        foreach (var (_, fn) in callbacks)
        {
            try { _engine.RawEngine.Invoke(fn, timestamp); }
            catch (Exception ex) { Console.WriteLine($"[JS RAF] {ex.Message}"); }
        }
    }
}
