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
    public string id
    {
        get => Node.Id ?? string.Empty;
        set => Node.Attributes["id"] = value;
    }
    public string tagName => Node.TagName.ToUpperInvariant();
    public string localName => Node.TagName.ToLowerInvariant();

    // ---- DOM Core Level 2 ----
    public int nodeType => Node.TagName == "#text" ? 3 : Node.TagName == "#document-fragment" ? 11 : Node.TagName == "#document" ? 9 : 1; // ELEMENT_NODE=1, TEXT_NODE=3, DOCUMENT_FRAGMENT_NODE=11
    public string nodeName => Node.TagName == "#text" ? "#text" : Node.TagName.ToUpperInvariant();
    public string? nodeValue
    {
        get => Node.TagName == "#text" ? Node.DisplayText : null;
        set { if (Node.TagName == "#text") Node.TextOverride = value; }
    }

    /// <summary>CharacterData.data — alias for nodeValue on text nodes.</summary>
    public string? data
    {
        get => Node.TagName == "#text" ? Node.DisplayText : null;
        set { if (Node.TagName == "#text") Node.TextOverride = value; }
    }

    /// <summary>CharacterData.length — length of text data.</summary>
    public int length => Node.TagName == "#text" ? (Node.DisplayText?.Length ?? 0) : Node.Children.Count;

    // ---- Node type constants (exposed on every element, like browsers do) ----
    public int ELEMENT_NODE => 1;
    public int ATTRIBUTE_NODE => 2;
    public int TEXT_NODE => 3;
    public int CDATA_SECTION_NODE => 4;
    public int COMMENT_NODE => 8;
    public int DOCUMENT_NODE => 9;
    public int DOCUMENT_TYPE_NODE => 10;
    public int DOCUMENT_FRAGMENT_NODE => 11;

    // ---- ownerDocument ----
    public JsDocument? ownerDocument =>
        JsEngine.Instance is { } eng ? new JsDocument(eng.RawEngine, GetRootNode()) : null;

    private LayoutNode GetRootNode()
    {
        var n = Node;
        while (n.Parent is not null) n = n.Parent;
        return n;
    }

    // ---- content ----
    public string textContent
    {
        get => GetTextContentRecursive(Node);
        set
        {
            Node.Children.Clear();
            Node.TextOverride = value;
        }
    }

    private static string GetTextContentRecursive(LayoutNode node)
    {
        if (node.TagName == "#text") return node.DisplayText;
        if (node.Children.Count == 0) return node.DisplayText;
        return string.Concat(node.Children.Select(c => GetTextContentRecursive(c)));
    }

    public string innerHTML
    {
        get => Node.DisplayText;      // simplified — no child serialisation
        set => Node.TextOverride = value;
    }

    public string outerHTML => innerHTML; // simplified

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

    public bool disabled
    {
        get => Node.Attributes.ContainsKey("disabled");
        set { if (value) Node.Attributes["disabled"] = ""; else Node.Attributes.Remove("disabled"); }
    }

    // ---- style ----
    public JsStyle style => _style ??= new JsStyle(Node);
    public string className
    {
        get => Node.Attributes.GetValueOrDefault("class", string.Empty);
        set
        {
            Node.Attributes["class"] = value;
            ReapplyCssRulesForClass();
        }
    }

    /// <summary>
    /// Re-evaluates stored CSS rules against the element's current class attribute
    /// and updates StyleOverrides accordingly. This enables dynamic class-based styling
    /// (e.g. Acid3 bucket color changes via className += 'P').
    /// </summary>
    private void ReapplyCssRulesForClass()
    {
        var classes = Node.Attributes.GetValueOrDefault("class", "")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (classes.Length == 0) return;

        foreach (var (selector, props) in Parser.CssRules)
        {
            if (MatchesSimpleSelector(selector, classes))
            {
                foreach (var (prop, val) in props)
                    Node.StyleOverrides[prop] = val;
            }
        }
    }

    /// <summary>
    /// Simple selector matching for class-based rules (.foo, .foo.bar).
    /// Supports: .class, tag.class, .class1.class2
    /// </summary>
    private bool MatchesSimpleSelector(string? selector, string[] classes)
    {
        if (string.IsNullOrEmpty(selector)) return false;
        // Only handle simple class selectors (no combinators, no pseudo-classes)
        if (selector.Contains(' ') || selector.Contains('>') || selector.Contains('+') || selector.Contains('~'))
            return false;
        if (selector.Contains(':') || selector.Contains('['))
            return false;

        // Extract tag and class parts
        var parts = selector.Split('.');
        var tagPart = parts[0]; // may be empty if selector starts with .

        // Check tag if specified
        if (!string.IsNullOrEmpty(tagPart) &&
            !tagPart.Equals(Node.TagName, StringComparison.OrdinalIgnoreCase))
            return false;

        // Check all class parts
        for (int i = 1; i < parts.Length; i++)
        {
            if (!classes.Contains(parts[i]))
                return false;
        }

        return parts.Length > 1; // must have at least one class part
    }

    // ---- attributes ----
    public string? getAttribute(string name) =>
        Node.Attributes.TryGetValue(name, out var v) ? v : null;

    public void setAttribute(string name, string val) =>
        Node.Attributes[name] = val;

    public void removeAttribute(string name) =>
        Node.Attributes.Remove(name);

    public bool hasAttribute(string name) => Node.Attributes.ContainsKey(name);

    // ---- tree navigation ----

    /// <summary>
    /// Ensures text-only elements have their text represented as a child text node,
    /// matching real browser DOM behavior. Called lazily when child access is needed.
    /// </summary>
    private void EnsureTextChildMaterialized()
    {
        if (Node.Children.Count == 0 && !string.IsNullOrEmpty(Node.DisplayText) && Node.TagName != "#text")
        {
            var textNode = new LayoutNode(null, "#text", Node.DisplayText, Node.Style);
            Node.AddChild(textNode);
            // Clear parent's direct text — it's now in the child
            Node.TextOverride = "";
        }
    }

    public JsElement[] children =>
        Node.Children.Where(c => c.TagName != "#text").Select(c => new JsElement(_engine, c)).ToArray();

    public JsElement[] childNodes
    {
        get
        {
            EnsureTextChildMaterialized();
            return Node.Children.Select(c => new JsElement(_engine, c)).ToArray();
        }
    }

    public JsElement? parentElement =>
        Node.Parent is { } p ? new JsElement(_engine, p) : null;

    public JsElement? parentNode => parentElement;

    public JsElement? firstChild
    {
        get
        {
            EnsureTextChildMaterialized();
            return Node.Children.Count > 0 ? new JsElement(_engine, Node.Children[0]) : null;
        }
    }

    public JsElement? lastChild
    {
        get
        {
            EnsureTextChildMaterialized();
            return Node.Children.Count > 0 ? new JsElement(_engine, Node.Children[^1]) : null;
        }
    }

    public JsElement? firstElementChild =>
        Node.Children.FirstOrDefault(c => c.TagName != "#text") is { } n ? new JsElement(_engine, n) : null;

    public JsElement? lastElementChild =>
        Node.Children.LastOrDefault(c => c.TagName != "#text") is { } n ? new JsElement(_engine, n) : null;

    public JsElement? nextSibling
    {
        get
        {
            if (Node.Parent is null) return null;
            var siblings = Node.Parent.Children;
            var idx = siblings.IndexOf(Node);
            return idx >= 0 && idx + 1 < siblings.Count ? new JsElement(_engine, siblings[idx + 1]) : null;
        }
    }

    public JsElement? previousSibling
    {
        get
        {
            if (Node.Parent is null) return null;
            var siblings = Node.Parent.Children;
            var idx = siblings.IndexOf(Node);
            return idx > 0 ? new JsElement(_engine, siblings[idx - 1]) : null;
        }
    }

    public JsElement? nextElementSibling
    {
        get
        {
            if (Node.Parent is null) return null;
            var siblings = Node.Parent.Children;
            var idx = siblings.IndexOf(Node);
            for (int i = idx + 1; i < siblings.Count; i++)
                if (siblings[i].TagName != "#text") return new JsElement(_engine, siblings[i]);
            return null;
        }
    }

    public JsElement? previousElementSibling
    {
        get
        {
            if (Node.Parent is null) return null;
            var siblings = Node.Parent.Children;
            var idx = siblings.IndexOf(Node);
            for (int i = idx - 1; i >= 0; i--)
                if (siblings[i].TagName != "#text") return new JsElement(_engine, siblings[i]);
            return null;
        }
    }

    public int childElementCount => Node.Children.Count(c => c.TagName != "#text");

    // ---- tree mutation ----

    /// <summary>Checks if childNode is an ancestor of parentNode (would create a cycle).</summary>
    private static bool IsAncestor(LayoutNode childNode, LayoutNode parentNode)
    {
        for (var n = parentNode; n != null; n = n.Parent)
            if (n == childNode) return true;
        return false;
    }

    public JsElement appendChild(JsElement child)
    {
        if (IsAncestor(child.Node, Node))
            throw new InvalidOperationException("The new child element contains the parent.");
        // Remove from old parent if attached
        child.Node.Parent?.Children.Remove(child.Node);
        Node.AddChild(child.Node);
        return child;
    }

    public JsElement removeChild(JsElement? child)
    {
        if (child is null) throw new InvalidOperationException("The node to be removed is not a child of this node.");
        Node.Children.Remove(child.Node);
        child.Node.Parent = null;
        return child;
    }

    public JsElement insertBefore(JsElement newNode, JsElement? refNode)
    {
        if (IsAncestor(newNode.Node, Node))
            throw new InvalidOperationException("The new child element contains the parent.");
        newNode.Node.Parent?.Children.Remove(newNode.Node);
        if (refNode is null)
        {
            Node.AddChild(newNode.Node);
        }
        else
        {
            var idx = Node.Children.IndexOf(refNode.Node);
            if (idx < 0) throw new InvalidOperationException("Reference node not found");
            newNode.Node.Parent = Node;
            Node.Children.Insert(idx, newNode.Node);
        }
        return newNode;
    }

    public JsElement replaceChild(JsElement newNode, JsElement oldNode)
    {
        if (IsAncestor(newNode.Node, Node))
            throw new InvalidOperationException("The new child element contains the parent.");
        // Remove newNode from its old parent first (may shift indices)
        newNode.Node.Parent?.Children.Remove(newNode.Node);
        // Re-find idx after potential removal
        var idx = Node.Children.IndexOf(oldNode.Node);
        if (idx < 0) throw new InvalidOperationException("Old node not found");
        Node.Children[idx] = newNode.Node;
        newNode.Node.Parent = Node;
        oldNode.Node.Parent = null;
        return oldNode;
    }

    public JsElement cloneNode(bool deep = false)
    {
        var clone = new LayoutNode(Node.Id, Node.TagName, Node.Text, Node.Style, Node.Href);
        foreach (var kvp in Node.Attributes) clone.Attributes[kvp.Key] = kvp.Value;
        foreach (var kvp in Node.StyleOverrides) clone.StyleOverrides[kvp.Key] = kvp.Value;
        clone.TextOverride = Node.TextOverride;
        if (deep)
        {
            foreach (var child in Node.Children)
            {
                var childEl = new JsElement(_engine, child);
                var childClone = childEl.cloneNode(true);
                clone.AddChild(childClone.Node);
            }
        }
        return new JsElement(_engine, clone);
    }

    public bool contains(JsElement other)
    {
        if (other.Node == Node) return true;
        return Node.Children.Any(c => new JsElement(_engine, c).contains(other));
    }

    public bool hasChildNodes() => Node.Children.Count > 0;

    // ---- class list (minimal) ----
    public JsClassList classList => new(Node);

    // ---- events ----
    public void addEventListener(string type, JsValue handler, JsValue? options = null)
    {
        bool capture = false;
        if (options is not null)
        {
            if (options.IsBoolean()) capture = options.AsBoolean();
            else if (options.IsObject())
            {
                var captureVal = options.AsObject().Get("capture");
                if (captureVal.IsBoolean()) capture = captureVal.AsBoolean();
            }
        }
        Node.EventListeners.Add(new EventListenerEntry(type.ToLowerInvariant(), handler, null, capture));
    }

    public void removeEventListener(string type, JsValue handler, JsValue? options = null)
    {
        bool capture = false;
        if (options is not null)
        {
            if (options.IsBoolean()) capture = options.AsBoolean();
            else if (options.IsObject())
            {
                var captureVal = options.AsObject().Get("capture");
                if (captureVal.IsBoolean()) capture = captureVal.AsBoolean();
            }
        }
        var normalizedType = type.ToLowerInvariant();
        Node.EventListeners.RemoveAll(l =>
            l.EventType == normalizedType && l.Capture == capture && l.Handler == handler);
    }

    // ---- dispatchEvent ----
    public bool dispatchEvent(JsEvent evt)
    {
        evt.target = this;
        if (JsEngine.Instance is { } engine)
            EventDispatcher.DispatchEvent(Node, evt, engine);
        return !evt.DefaultPrevented;
    }

    // ---- click ----
    public void click()
    {
        var evt = new JsEvent();
        evt.initEvent("click", true, true);
        dispatchEvent(evt);
    }

    // ---- querySelector on element ----
    public JsElement? querySelector(string selector) =>
        SelectorEngine.QuerySelector(Node, selector, _engine);

    public JsElement[] querySelectorAll(string selector) =>
        SelectorEngine.QuerySelectorAll(Node, selector, _engine);

    public JsElement[] getElementsByTagName(string tagName)
    {
        var tag = tagName.ToUpperInvariant();
        return FindAll(Node, n => tag == "*" || n.TagName == tag)
            .Select(n => new JsElement(_engine, n)).ToArray();
    }

    public JsElement[] getElementsByClassName(string classNames)
    {
        var classes = classNames.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return FindAll(Node, n =>
        {
            var nodeClasses = n.Attributes.GetValueOrDefault("class", "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return classes.All(c => nodeClasses.Contains(c));
        }).Select(n => new JsElement(_engine, n)).ToArray();
    }

    // ---- canvas ----
    private JsCanvasContext2D? _canvasCtx;
    public object? getContext(string contextType)
    {
        if (Node.TagName != "CANVAS") return null;
        if (contextType == "2d")
        {
            if (_canvasCtx == null)
            {
                var w = int.TryParse(Node.Attributes.GetValueOrDefault("width", "300"), out var wv) ? wv : 300;
                var h = int.TryParse(Node.Attributes.GetValueOrDefault("height", "150"), out var hv) ? hv : 150;
                _canvasCtx = new JsCanvasContext2D(Node, w, h);
            }
            return _canvasCtx;
        }
        return null;
    }

    // ---- geometry (Phase 7) ----
    public JsBoundingClientRect getBoundingClientRect() => new(Node);

    public double offsetWidth => Node.Box.BorderBox.Width;
    public double offsetHeight => Node.Box.BorderBox.Height;
    public double offsetLeft => Node.Box.MarginBox.Left;
    public double offsetTop => Node.Box.MarginBox.Top;
    public double clientWidth => Node.Box.ContentBox.Width;
    public double clientHeight => Node.Box.ContentBox.Height;
    public double scrollTop { get; set; }
    public double scrollLeft { get; set; }
    public double scrollWidth => Node.Box.ContentBox.Width;
    public double scrollHeight => Node.Box.ContentBox.Height;

    // ---- helpers ----
    private static List<LayoutNode> FindAll(LayoutNode node, Func<LayoutNode, bool> predicate)
    {
        var results = new List<LayoutNode>();
        var stack = new Stack<LayoutNode>();
        var visited = new HashSet<LayoutNode>(ReferenceEqualityComparer.Instance);
        for (int i = node.Children.Count - 1; i >= 0; i--)
            stack.Push(node.Children[i]);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (!visited.Add(n)) continue;
            if (predicate(n)) results.Add(n);
            for (int i = n.Children.Count - 1; i >= 0; i--)
                stack.Push(n.Children[i]);
        }
        return results;
    }
}

/// <summary>classList API.</summary>
public class JsClassList
{
    private readonly LayoutNode _node;
    public JsClassList(LayoutNode node) => _node = node;

    private string[] GetClasses() => _node.Attributes.GetValueOrDefault("class", "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
    private void SetClasses(IEnumerable<string> classes) => _node.Attributes["class"] = string.Join(" ", classes);

    public void add(string cls)
    {
        var classes = GetClasses().ToList();
        if (!classes.Contains(cls)) { classes.Add(cls); SetClasses(classes); }
    }
    public void remove(string cls)
    {
        var classes = GetClasses().ToList();
        classes.Remove(cls);
        SetClasses(classes);
    }
    public bool contains(string cls) => GetClasses().Contains(cls);
    public bool toggle(string cls)
    {
        if (contains(cls)) { remove(cls); return false; }
        add(cls);
        return true;
    }
    public int length => GetClasses().Length;
}

/// <summary>DOMRect returned by getBoundingClientRect().</summary>
public class JsBoundingClientRect
{
    public JsBoundingClientRect(LayoutNode node)
    {
        top = node.Box.BorderBox.Top;
        left = node.Box.BorderBox.Left;
        width = node.Box.BorderBox.Width;
        height = node.Box.BorderBox.Height;
        right = left + width;
        bottom = top + height;
        x = left;
        y = top;
    }
    public double x { get; }
    public double y { get; }
    public double top { get; }
    public double left { get; }
    public double width { get; }
    public double height { get; }
    public double right { get; }
    public double bottom { get; }
}
