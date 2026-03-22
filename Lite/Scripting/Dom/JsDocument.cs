using Jint;
using Lite.Models;

namespace Lite.Scripting.Dom;

/// <summary>Full document proxy exposed to JavaScript.</summary>
public class JsDocument
{
    private readonly Engine     _engine;
    private readonly LayoutNode _root;

    public JsDocument(Engine engine, LayoutNode root)
    {
        _engine = engine;
        _root   = root;
    }

    // ---- identity ----
    public string nodeName => "#document";
    public int nodeType => 9;
    public JsElement? documentElement => _root.Children.Count > 0 ? new JsElement(_engine, _root) : null;

    /// <summary>Returns the window object (document.defaultView).</summary>
    public object? defaultView => JsEngine.Instance?.RawEngine.GetValue("window").ToObject();

    public JsElement? body =>
        FindFirst(_root, n => n.TagName == "BODY") is { } b ? new JsElement(_engine, b) : null;

    public JsElement? head =>
        FindFirst(_root, n => n.TagName == "HEAD") is { } h ? new JsElement(_engine, h) : null;

    // ---- selectors ----
    public JsElement? getElementById(string id)
    {
        var node = FindById(_root, id);
        return node is null ? null : new JsElement(_engine, node);
    }

    public JsElement? querySelector(string selector) =>
        SelectorEngine.QuerySelector(_root, selector, _engine);

    public JsElement[] querySelectorAll(string selector) =>
        SelectorEngine.QuerySelectorAll(_root, selector, _engine);

    public JsElement[] getElementsByTagName(string tagName)
    {
        var tag = tagName.ToUpperInvariant();
        return FindAll(_root, n => tag == "*" || n.TagName == tag)
            .Select(n => new JsElement(_engine, n)).ToArray();
    }

    public JsElement[] getElementsByClassName(string classNames)
    {
        var classes = classNames.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return FindAll(_root, n =>
        {
            var nodeClasses = n.Attributes.GetValueOrDefault("class", "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return classes.All(c => nodeClasses.Contains(c));
        }).Select(n => new JsElement(_engine, n)).ToArray();
    }

    // ---- creation ----
    public JsElement createElement(string tagName)
    {
        var style = _root.Style;
        var node  = new LayoutNode(null, tagName.ToUpperInvariant(), string.Empty, style);
        return new JsElement(_engine, node);
    }

    public JsElement createElementNS(string ns, string tagName)
    {
        var style = _root.Style;
        var node = new LayoutNode(null, tagName.ToUpperInvariant(), string.Empty, style);
        node.Attributes["xmlns"] = ns;
        return new JsElement(_engine, node);
    }

    public JsElement createTextNode(string text)
    {
        var style = _root.Style;
        var node = new LayoutNode(null, "#text", text, style);
        return new JsElement(_engine, node);
    }

    public JsElement createDocumentFragment()
    {
        var style = _root.Style;
        var node = new LayoutNode(null, "#document-fragment", string.Empty, style);
        return new JsElement(_engine, node);
    }

    public JsEvent createEvent(string type)
    {
        return new JsEvent();
    }

    // ---- document.write / open / close (minimal stubs) ----
    private string _writeBuffer = "";

    public void open() => _writeBuffer = "";

    public void write(string markup) => _writeBuffer += markup;

    public void writeln(string markup) => _writeBuffer += markup + "\n";

    public void close()
    {
        // Stub — in a full implementation this would reparse the document.
        // For now, just clear the buffer.
        _writeBuffer = "";
    }

    // ---- document metadata ----
    public string title
    {
        get => ""; // simplified
        set { }    // simplified
    }

    public string URL => "";
    public string domain => "";
    public string compatMode => "CSS1Compat";

    // ---- DOM Traversal Level 2 (Phase 9) ----
    public JsTreeWalker createTreeWalker(JsElement root, int whatToShow = -1, object? filter = null)
    {
        return new JsTreeWalker(_engine, root.Node, whatToShow);
    }

    public JsNodeIterator createNodeIterator(JsElement root, int whatToShow = -1, object? filter = null)
    {
        return new JsNodeIterator(_engine, root.Node, whatToShow);
    }

    // ---- tree helpers (iterative to avoid stack overflow from deep/cyclic trees) ----
    private static LayoutNode? FindById(LayoutNode root, string id)
    {
        var stack = new Stack<LayoutNode>();
        stack.Push(root);
        var visited = new HashSet<LayoutNode>(ReferenceEqualityComparer.Instance);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (!visited.Add(node)) continue;
            if (node.Id == id) return node;
            for (int i = node.Children.Count - 1; i >= 0; i--)
                stack.Push(node.Children[i]);
        }
        return null;
    }

    private static LayoutNode? FindFirst(LayoutNode root, Func<LayoutNode, bool> predicate)
    {
        var stack = new Stack<LayoutNode>();
        stack.Push(root);
        var visited = new HashSet<LayoutNode>(ReferenceEqualityComparer.Instance);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (!visited.Add(node)) continue;
            if (predicate(node)) return node;
            for (int i = node.Children.Count - 1; i >= 0; i--)
                stack.Push(node.Children[i]);
        }
        return null;
    }

    private static List<LayoutNode> FindAll(LayoutNode root, Func<LayoutNode, bool> predicate)
    {
        var results = new List<LayoutNode>();
        var stack = new Stack<LayoutNode>();
        stack.Push(root);
        var visited = new HashSet<LayoutNode>(ReferenceEqualityComparer.Instance);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (!visited.Add(node)) continue;
            if (predicate(node)) results.Add(node);
            for (int i = node.Children.Count - 1; i >= 0; i--)
                stack.Push(node.Children[i]);
        }
        return results;
    }
}
