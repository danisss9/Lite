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
    public JsElement? documentElement => _root.Children.Count > 0 ? JsElement.For(_engine, _root) : null;

    /// <summary>Returns the window object (document.defaultView).</summary>
    public object? defaultView => JsEngine.Instance?.RawEngine.GetValue("window").ToObject();

    public JsElement? body =>
        FindFirst(_root, n => n.TagName == "BODY") is { } b ? JsElement.For(_engine, b) : null;

    public JsElement? head =>
        FindFirst(_root, n => n.TagName == "HEAD") is { } h ? JsElement.For(_engine, h) : null;

    // ---- selectors ----
    public JsElement? getElementById(string id)
    {
        var node = FindById(_root, id);
        return node is null ? null : JsElement.For(_engine, node);
    }

    public JsElement? querySelector(string selector) =>
        SelectorEngine.QuerySelector(_root, selector, _engine);

    public JsElement[] querySelectorAll(string selector) =>
        SelectorEngine.QuerySelectorAll(_root, selector, _engine);

    public JsElement[] getElementsByTagName(string tagName)
    {
        var tag = tagName.ToUpperInvariant();
        return FindAll(_root, n => tag == "*" || n.TagName == tag)
            .Select(n => JsElement.For(_engine, n)).ToArray();
    }

    public JsElement[] getElementsByClassName(string classNames)
    {
        var classes = classNames.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return FindAll(_root, n =>
        {
            var nodeClasses = n.Attributes.GetValueOrDefault("class", "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return classes.All(c => nodeClasses.Contains(c));
        }).Select(n => JsElement.For(_engine, n)).ToArray();
    }

    /// <summary>Returns all elements with the given name attribute (Document.getElementsByName()).</summary>
    public JsElement[] getElementsByName(string name) =>
        FindAll(_root, n => n.Attributes.GetValueOrDefault("name") == name)
            .Select(n => JsElement.For(_engine, n)).ToArray();

    // ---- creation ----
    public JsElement createElement(string tagName)
    {
        var style = _root.Style;
        var node  = new LayoutNode(null, tagName.ToUpperInvariant(), string.Empty, style)
        {
            NeedsStyleResolution = true, // cascade applied when inserted into the live tree
        };
        return JsElement.For(_engine, node);
    }

    public JsElement createElementNS(string ns, string tagName)
    {
        var style = _root.Style;
        var node = new LayoutNode(null, tagName.ToUpperInvariant(), string.Empty, style)
        {
            NeedsStyleResolution = true,
        };
        node.Attributes["xmlns"] = ns;
        return JsElement.For(_engine, node);
    }

    public JsElement createTextNode(string text)
    {
        var style = _root.Style;
        var node = new LayoutNode(null, "#text", text, style);
        return JsElement.For(_engine, node);
    }

    public JsElement createDocumentFragment()
    {
        var style = _root.Style;
        var node = new LayoutNode(null, "#document-fragment", string.Empty, style);
        return JsElement.For(_engine, node);
    }

    /// <summary>document.createAttribute(name) — a detached Attr to attach via setAttributeNode.</summary>
    public JsAttr createAttribute(string name) => new(name);

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
    private string _title = Parser.Document?.Title ?? string.Empty;
    public string title
    {
        get => _title;
        set
        {
            _title = value ?? string.Empty;
            if (Parser.Document is { } doc) doc.Title = _title;
            JsEngine.Instance?.OnTitleChange?.Invoke(_title);
        }
    }

    /// <summary>document.location — same object as window.location.</summary>
    public JsLocation? location => JsEngine.Instance?.Location;

    public string URL => JsEngine.Instance?.CurrentUrl ?? Parser.BaseUrl ?? "";
    public string domain
    {
        get
        {
            try { return Parser.BaseUrl is { } b && Uri.TryCreate(b, UriKind.Absolute, out var u) ? u.Host : ""; }
            catch { return ""; }
        }
    }
    public string compatMode => "CSS1Compat";

    // ---- cookies (single in-memory jar for the current document) ----
    private static readonly Dictionary<string, string> _cookies = new(StringComparer.Ordinal);

    public string cookie
    {
        get => string.Join("; ", _cookies.Select(kv => $"{kv.Key}={kv.Value}"));
        set
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            // Only the first "name=value" segment is the cookie; attributes (path, expires…) are ignored.
            var pair = value.Split(';')[0];
            var eq = pair.IndexOf('=');
            if (eq < 0) return;
            var name = pair[..eq].Trim();
            var val = pair[(eq + 1)..].Trim();
            if (name.Length > 0) _cookies[name] = val;
        }
    }

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
