using Jint;
using Jint.Native;
using Lite.Interaction;
using Lite.Models;

namespace Lite.Scripting.Dom;

/// <summary>Lightweight DOM element proxy exposed to JavaScript.</summary>
public class JsElement
{
    private readonly Engine _engine;
    internal readonly LayoutNode Node;
    private JsStyle? _style;

    public JsElement(Engine engine, LayoutNode node)
    {
        _engine = engine;
        Node    = node;
    }

    // ---- identity ----
    public string id      => Node.Id ?? string.Empty;
    public string tagName => Node.TagName.ToLowerInvariant();

    // ---- content ----
    public string textContent
    {
        get => Node.DisplayText;
        set => Node.TextOverride = value;
    }

    public string innerHTML
    {
        get => Node.DisplayText;      // simplified — no child serialisation
        set => Node.TextOverride = value;
    }

    // ---- form value / checked ----
    public string value
    {
        get
        {
            Node.Attributes.TryGetValue("value", out var defaultVal);
            return FormState.GetTextValue(Node.NodeKey, defaultVal);
        }
        set => FormState.TextInputValues[Node.NodeKey] = value;
    }

    public bool @checked
    {
        get => FormState.IsChecked(Node.NodeKey, Node.Attributes.ContainsKey("checked"));
        set
        {
            if (value) FormState.CheckedBoxes.Add(Node.NodeKey);
            else       FormState.CheckedBoxes.Remove(Node.NodeKey);
        }
    }

    // ---- style ----
    public JsStyle style => _style ??= new JsStyle(Node);

    // ---- attributes ----
    public string? getAttribute(string name) =>
        Node.Attributes.TryGetValue(name, out var v) ? v : null;

    public void setAttribute(string name, string value) =>
        Node.Attributes[name] = value;

    public bool hasAttribute(string name) => Node.Attributes.ContainsKey(name);

    // ---- tree ----
    public JsElement[] children =>
        Node.Children.Select(c => new JsElement(_engine, c)).ToArray();

    public JsElement? parentElement =>
        Node.Parent is { } p ? new JsElement(_engine, p) : null;

    public JsElement appendChild(JsElement child)
    {
        Node.AddChild(child.Node);
        return child;
    }

    public JsElement removeChild(JsElement child)
    {
        Node.Children.Remove(child.Node);
        child.Node.Parent = null;
        return child;
    }

    // ---- class list (minimal) ----
    private readonly HashSet<string> _classes = [];
    public void classList_add(string cls)    => _classes.Add(cls);
    public void classList_remove(string cls) => _classes.Remove(cls);
    public bool classList_contains(string cls) => _classes.Contains(cls);
    public void classList_toggle(string cls)
    {
        if (!_classes.Remove(cls)) _classes.Add(cls);
    }

    // ---- events ----
    public void addEventListener(string type, JsValue handler)
    {
        var capturedEngine  = _engine;
        var capturedHandler = handler;
        Node.EventListeners.Add((type.ToLowerInvariant(), () =>
        {
            try { capturedEngine.Invoke(capturedHandler); }
            catch (Exception ex) { Console.WriteLine($"[JS EventListener] {ex.Message}"); }
        }));
    }

    public void removeEventListener(string type, JsValue _handler)
    {
        // Simplified: remove all listeners of this type
        Node.EventListeners.RemoveAll(l => l.EventType == type.ToLowerInvariant());
    }

    // ---- click ----
    public void click() => DispatchEvent("click");

    internal void DispatchEvent(string type)
    {
        foreach (var (evtType, handler) in Node.EventListeners)
            if (evtType == type) handler();
    }
}
