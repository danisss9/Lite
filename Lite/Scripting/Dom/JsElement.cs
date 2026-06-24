using Jint;
using Jint.Native;
using Lite.Interaction;
using Lite.Layout;
using Lite.Models;
using Lite.Rendering;

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
        Node = node;
    }

    // Wrapper identity: a LayoutNode belongs to exactly one engine, so a node-keyed weak cache
    // guarantees the same node always yields the SAME JsElement. This makes JS === and WPT
    // assert_equals(node, node) work (e.g. getElementById('x') === getElementById('x')) and keeps
    // per-element state (style, listeners) stable across accessor calls. Keys are weak, so wrappers
    // for GC'd nodes are collected automatically.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<LayoutNode, JsElement> _wrappers = new();

    /// <summary>Returns the canonical JsElement wrapper for a node (cached per node).</summary>
    public static JsElement For(Engine engine, LayoutNode node)
        => _wrappers.GetValue(node, n => new JsElement(engine, n));

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
        get => HtmlSerializer.SerializeChildren(Node);
        set
        {
            Node.Children.Clear();
            Node.TextOverride = string.Empty;
            foreach (var child in Parser.ParseFragment(value ?? string.Empty, Node.TagName))
                Node.AddChild(child);
        }
    }

    public string outerHTML
    {
        get => HtmlSerializer.SerializeOuter(Node);
        set => ReplaceSelfWithFragment(value ?? string.Empty);
    }

    /// <summary>
    /// Parses <paramref name="html"/> and inserts the resulting nodes relative to this
    /// element. position is one of beforebegin, afterbegin, beforeend, afterend.
    /// </summary>
    public void insertAdjacentHTML(string position, string html)
    {
        var nodes = Parser.ParseFragment(html ?? string.Empty, Node.Parent?.TagName ?? Node.TagName);
        switch (position?.ToLowerInvariant())
        {
            case "beforebegin":
                InsertNodesBefore(nodes, Node);
                break;
            case "afterbegin":
                for (int i = nodes.Count - 1; i >= 0; i--)
                {
                    nodes[i].Parent = Node;
                    Node.Children.Insert(0, nodes[i]);
                }
                break;
            case "beforeend":
                foreach (var n in nodes) Node.AddChild(n);
                break;
            case "afterend":
                InsertNodesAfter(nodes, Node);
                break;
        }
    }

    /// <summary>Inserts plain text at the given position relative to this element.</summary>
    public void insertAdjacentText(string position, string text)
    {
        var textNode = new LayoutNode(null, "#text", text ?? string.Empty, Node.Style);
        InsertAdjacentNode(position, textNode);
    }

    /// <summary>Inserts an element at the given position relative to this element.</summary>
    public JsElement? insertAdjacentElement(string position, JsElement element)
    {
        element.Node.Parent?.Children.Remove(element.Node);
        InsertAdjacentNode(position, element.Node);
        StyleResolver.ApplyTree(element.Node);
        return element;
    }

    private void InsertAdjacentNode(string? position, LayoutNode node)
    {
        switch (position?.ToLowerInvariant())
        {
            case "beforebegin": InsertNodesBefore([node], Node); break;
            case "afterbegin":
                node.Parent = Node;
                Node.Children.Insert(0, node);
                break;
            case "beforeend": Node.AddChild(node); break;
            case "afterend": InsertNodesAfter([node], Node); break;
        }
    }

    private void ReplaceSelfWithFragment(string html)
    {
        if (Node.Parent is null) return;
        var nodes = Parser.ParseFragment(html, Node.Parent.TagName);
        InsertNodesBefore(nodes, Node);
        Node.Parent.Children.Remove(Node);
        Node.Parent = null;
    }

    private static void InsertNodesBefore(List<LayoutNode> nodes, LayoutNode reference)
    {
        var parent = reference.Parent;
        if (parent is null) return;
        var idx = parent.Children.IndexOf(reference);
        if (idx < 0) idx = parent.Children.Count;
        foreach (var n in nodes)
        {
            n.Parent = parent;
            parent.Children.Insert(idx++, n);
        }
    }

    private static void InsertNodesAfter(List<LayoutNode> nodes, LayoutNode reference)
    {
        var parent = reference.Parent;
        if (parent is null) return;
        var idx = parent.Children.IndexOf(reference);
        if (idx < 0) idx = parent.Children.Count - 1;
        idx++;
        foreach (var n in nodes)
        {
            n.Parent = parent;
            parent.Children.Insert(idx++, n);
        }
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
            else FormState.CheckedBoxes.Remove(Node.NodeKey);
        }
    }

    public bool disabled
    {
        get => Node.Attributes.ContainsKey("disabled");
        set { if (value) Node.Attributes["disabled"] = ""; else Node.Attributes.Remove("disabled"); }
    }

    /// <summary>HTMLDetailsElement/HTMLDialogElement.open — reflects the <c>open</c> attribute.
    /// Layout collapses a closed &lt;details&gt;'s non-summary content on the next reflow.</summary>
    public bool open
    {
        get => Node.Attributes.ContainsKey("open");
        set
        {
            var was = Node.Attributes.ContainsKey("open");
            if (value) Node.Attributes["open"] = ""; else Node.Attributes.Remove("open");
            // HTMLDetailsElement fires a non-bubbling 'toggle' event asynchronously on change.
            if (was != value && Node.TagName == "DETAILS" && JsEngine.Instance is { } eng)
                eng.EnqueueMacrotask(() =>
                {
                    var evt = new JsEvent();
                    evt.initEvent("toggle", false, false);
                    evt.target = For(eng.RawEngine, Node);
                    EventDispatcher.DispatchEvent(Node, evt, eng);
                });
        }
    }

    public string type
    {
        get => Node.Attributes.GetValueOrDefault("type", Node.TagName == "INPUT" ? "text" : string.Empty);
        set => Node.Attributes["type"] = value;
    }

    public string name
    {
        get => Node.Attributes.GetValueOrDefault("name", string.Empty);
        set => Node.Attributes["name"] = value;
    }

    // ---- form association & constraint validation ----

    /// <summary>The containing &lt;form&gt;, or null.</summary>
    public JsElement? form
    {
        get
        {
            for (var p = Node.Parent; p != null; p = p.Parent)
                if (p.TagName == "FORM") return JsElement.For(_engine, p);
            return null;
        }
    }

    public bool willValidate => FormValidation.IsCandidate(Node);
    public ValidityState validity => FormValidation.GetValidity(Node);
    public bool checkValidity() => !willValidate || validity.valid;
    public bool reportValidity() => checkValidity();

    /// <summary>Submits the form (HTMLFormElement.submit). No-op on non-form elements.</summary>
    public void submit()
    {
        if (Node.TagName != "FORM") return;
        JsEngine.Instance?.RequestNavigation(FormSubmitter.BuildActionUrl(Node, Parser.BaseUrl));
    }

    /// <summary>Resets the form's controls to their defaults (HTMLFormElement.reset).</summary>
    public void reset()
    {
        if (Node.TagName == "FORM") FormSubmitter.Reset(Node);
    }

    // ---- style ----
    public JsStyle style => _style ??= new JsStyle(Node);
    public string className
    {
        get => Node.Attributes.GetValueOrDefault("class", string.Empty);
        set
        {
            var old = Node.Attributes.GetValueOrDefault("class");
            Node.Attributes["class"] = value;
            // Re-run the full, idempotent cascade so rules that no longer match are retracted
            // and higher-specificity rules win (enables dynamic class-based styling).
            StyleResolver.Apply(Node);
            MutationObserverRegistry.NotifyAttribute(_engine, Node, "class", old);
        }
    }

    // ---- attributes ----
    public string? getAttribute(string name) =>
        Node.Attributes.TryGetValue(name, out var v) ? v : null;

    public void setAttribute(string name, string val)
    {
        var old = Node.Attributes.TryGetValue(name, out var o) ? o : null;
        Node.Attributes[name] = val;
        MutationObserverRegistry.NotifyAttribute(_engine, Node, name, old);
    }

    public void removeAttribute(string name)
    {
        var old = Node.Attributes.TryGetValue(name, out var o) ? o : null;
        Node.Attributes.Remove(name);
        MutationObserverRegistry.NotifyAttribute(_engine, Node, name, old);
    }

    public bool hasAttribute(string name) => Node.Attributes.ContainsKey(name);

    /// <summary>NamedNodeMap of this element's attributes (Element.attributes).</summary>
    public JsNamedNodeMap attributes => new(Node);

    /// <summary>True if the element has any attributes (excludes engine-internal keys).</summary>
    public bool hasAttributes() => Node.Attributes.Keys.Any(k => !k.StartsWith('_'));

    /// <summary>The element's attribute names (Element.getAttributeNames()).</summary>
    public string[] getAttributeNames() =>
        Node.Attributes.Keys.Where(k => !k.StartsWith('_')).ToArray();

    /// <summary>Returns the Attr node for the given name, or null.</summary>
    public JsAttr? getAttributeNode(string name) =>
        Node.Attributes.ContainsKey(name) ? new JsAttr(Node, name) : null;

    /// <summary>Toggles an attribute. With <paramref name="force"/>, sets/removes per the flag;
    /// otherwise flips presence. Returns whether the attribute is present afterwards.</summary>
    public bool toggleAttribute(string name, bool? force = null)
    {
        var present = Node.Attributes.ContainsKey(name);
        var shouldHave = force ?? !present;
        if (shouldHave && !present)
        {
            Node.Attributes[name] = "";
            MutationObserverRegistry.NotifyAttribute(_engine, Node, name, null);
        }
        else if (!shouldHave && present)
        {
            var old = Node.Attributes[name];
            Node.Attributes.Remove(name);
            MutationObserverRegistry.NotifyAttribute(_engine, Node, name, old);
        }
        return shouldHave;
    }

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
        Node.Children.Where(c => c.TagName != "#text").Select(c => JsElement.For(_engine, c)).ToArray();

    public JsElement[] childNodes
    {
        get
        {
            EnsureTextChildMaterialized();
            return Node.Children.Select(c => JsElement.For(_engine, c)).ToArray();
        }
    }

    public JsElement? parentElement =>
        Node.Parent is { } p ? JsElement.For(_engine, p) : null;

    public JsElement? parentNode => parentElement;

    public JsElement? firstChild
    {
        get
        {
            EnsureTextChildMaterialized();
            return Node.Children.Count > 0 ? JsElement.For(_engine, Node.Children[0]) : null;
        }
    }

    public JsElement? lastChild
    {
        get
        {
            EnsureTextChildMaterialized();
            return Node.Children.Count > 0 ? JsElement.For(_engine, Node.Children[^1]) : null;
        }
    }

    public JsElement? firstElementChild =>
        Node.Children.FirstOrDefault(c => c.TagName != "#text") is { } n ? JsElement.For(_engine, n) : null;

    public JsElement? lastElementChild =>
        Node.Children.LastOrDefault(c => c.TagName != "#text") is { } n ? JsElement.For(_engine, n) : null;

    public JsElement? nextSibling
    {
        get
        {
            if (Node.Parent is null) return null;
            var siblings = Node.Parent.Children;
            var idx = siblings.IndexOf(Node);
            return idx >= 0 && idx + 1 < siblings.Count ? JsElement.For(_engine, siblings[idx + 1]) : null;
        }
    }

    public JsElement? previousSibling
    {
        get
        {
            if (Node.Parent is null) return null;
            var siblings = Node.Parent.Children;
            var idx = siblings.IndexOf(Node);
            return idx > 0 ? JsElement.For(_engine, siblings[idx - 1]) : null;
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
                if (siblings[i].TagName != "#text") return JsElement.For(_engine, siblings[i]);
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
                if (siblings[i].TagName != "#text") return JsElement.For(_engine, siblings[i]);
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
        StyleResolver.ApplyTree(child.Node);
        var prev = Node.Children.Count >= 2 ? Node.Children[^2] : null;
        MutationObserverRegistry.NotifyChildList(_engine, Node, [child.Node], null, prev, null);
        return child;
    }

    public JsElement removeChild(JsElement? child)
    {
        if (child is null) throw new InvalidOperationException("The node to be removed is not a child of this node.");
        var idx = Node.Children.IndexOf(child.Node);
        var prev = idx > 0 ? Node.Children[idx - 1] : null;
        var next = idx >= 0 && idx + 1 < Node.Children.Count ? Node.Children[idx + 1] : null;
        Node.Children.Remove(child.Node);
        child.Node.Parent = null;
        MutationObserverRegistry.NotifyChildList(_engine, Node, null, [child.Node], prev, next);
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
        StyleResolver.ApplyTree(newNode.Node);
        MutationObserverRegistry.NotifyChildList(_engine, Node, [newNode.Node], null, null, refNode?.Node);
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
        StyleResolver.ApplyTree(newNode.Node);
        return oldNode;
    }

    // ---- modern mutation convenience methods (DOM Living Standard) ----

    /// <summary>Appends nodes/strings as the last children of this element.</summary>
    public void append(params JsValue[] items)
    {
        foreach (var node in ToNodes(items))
        {
            node.Parent?.Children.Remove(node);
            Node.AddChild(node);
            StyleResolver.ApplyTree(node);
        }
    }

    /// <summary>Inserts nodes/strings as the first children of this element.</summary>
    public void prepend(params JsValue[] items)
    {
        var nodes = ToNodes(items);
        for (int i = nodes.Count - 1; i >= 0; i--)
        {
            nodes[i].Parent?.Children.Remove(nodes[i]);
            nodes[i].Parent = Node;
            Node.Children.Insert(0, nodes[i]);
            StyleResolver.ApplyTree(nodes[i]);
        }
    }

    /// <summary>Inserts nodes/strings into this element's parent, just before it.</summary>
    public void before(params JsValue[] items)
    {
        var nodes = ToNodes(items);
        foreach (var n in nodes) n.Parent?.Children.Remove(n);
        InsertNodesBefore(nodes, Node);
        foreach (var n in nodes) StyleResolver.ApplyTree(n);
    }

    /// <summary>Inserts nodes/strings into this element's parent, just after it.</summary>
    public void after(params JsValue[] items)
    {
        var nodes = ToNodes(items);
        foreach (var n in nodes) n.Parent?.Children.Remove(n);
        InsertNodesAfter(nodes, Node);
        foreach (var n in nodes) StyleResolver.ApplyTree(n);
    }

    /// <summary>Replaces this element with the given nodes/strings.</summary>
    public void replaceWith(params JsValue[] items)
    {
        if (Node.Parent is null) return;
        var nodes = ToNodes(items);
        foreach (var n in nodes) n.Parent?.Children.Remove(n);
        InsertNodesBefore(nodes, Node);
        Node.Parent.Children.Remove(Node);
        Node.Parent = null;
        foreach (var n in nodes) StyleResolver.ApplyTree(n);
    }

    /// <summary>Removes this element from its parent.</summary>
    public void remove()
    {
        Node.Parent?.Children.Remove(Node);
        Node.Parent = null;
    }

    /// <summary>Coerces append/before/after arguments (JsElement or string) into LayoutNodes.</summary>
    private List<LayoutNode> ToNodes(JsValue[] items)
    {
        var result = new List<LayoutNode>();
        foreach (var item in items)
        {
            if (item.IsString())
            {
                var textNode = new LayoutNode(null, "#text", item.AsString(), Node.Style);
                textNode.StyleOverrides["display"] = "inline";
                result.Add(textNode);
            }
            else if (item.ToObject() is JsElement el)
            {
                result.Add(el.Node);
            }
        }
        return result;
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
                var childEl = JsElement.For(_engine, child);
                var childClone = childEl.cloneNode(true);
                clone.AddChild(childClone.Node);
            }
        }
        return JsElement.For(_engine, clone);
    }

    public bool contains(JsElement other)
    {
        if (other.Node == Node) return true;
        return Node.Children.Any(c => JsElement.For(_engine, c).contains(other));
    }

    public bool hasChildNodes() => Node.Children.Count > 0;

    // ---- class list (minimal) ----
    public JsClassList classList => new(Node);

    // ---- dataset (data-* attributes) ----
    public JsDataset dataset => new(Node);

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
            .Select(n => JsElement.For(_engine, n)).ToArray();
    }

    public JsElement[] getElementsByClassName(string classNames)
    {
        var classes = classNames.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return FindAll(Node, n =>
        {
            var nodeClasses = n.Attributes.GetValueOrDefault("class", "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return classes.All(c => nodeClasses.Contains(c));
        }).Select(n => JsElement.For(_engine, n)).ToArray();
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
    // Querying geometry forces layout so the boxes reflect the current DOM/styles
    // (matches browser forced-reflow; required for headless geometry reads).
    private static void EnsureLayout() => JsEngine.Instance?.EnsureLayout();

    public JsBoundingClientRect getBoundingClientRect() { EnsureLayout(); return new(Node); }

    public double offsetWidth { get { EnsureLayout(); return Node.Box.BorderBox.Width; } }
    public double offsetHeight { get { EnsureLayout(); return Node.Box.BorderBox.Height; } }
    public double offsetLeft { get { EnsureLayout(); return Node.Box.MarginBox.Left; } }
    public double offsetTop { get { EnsureLayout(); return Node.Box.MarginBox.Top; } }
    public double clientWidth { get { EnsureLayout(); return Node.Box.ContentBox.Width; } }
    public double clientHeight { get { EnsureLayout(); return Node.Box.ContentBox.Height; } }
    public double scrollTop { get; set; }
    public double scrollLeft { get; set; }
    public double scrollWidth { get { EnsureLayout(); return Node.Box.ContentBox.Width; } }
    public double scrollHeight { get { EnsureLayout(); return Node.Box.ContentBox.Height; } }

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
    private void SetClasses(IEnumerable<string> classes)
    {
        _node.Attributes["class"] = string.Join(" ", classes);
        // Re-run the cascade so class-dependent rules apply/retract immediately.
        Lite.Layout.StyleResolver.Apply(_node);
    }

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

/// <summary>Proxy for element.dataset — maps data-* attributes to camelCase properties.</summary>
public class JsDataset
{
    private readonly LayoutNode _node;
    public JsDataset(LayoutNode node) => _node = node;

    /// <summary>Gets a data-* attribute value by camelCase key.</summary>
    public string? get(string key)
    {
        var attrName = "data-" + CamelToKebab(key);
        return _node.Attributes.GetValueOrDefault(attrName);
    }

    /// <summary>Sets a data-* attribute value by camelCase key.</summary>
    public void set(string key, string value)
    {
        var attrName = "data-" + CamelToKebab(key);
        _node.Attributes[attrName] = value;
    }

    private static string CamelToKebab(string s)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var c in s)
        {
            if (char.IsUpper(c)) { sb.Append('-'); sb.Append(char.ToLowerInvariant(c)); }
            else sb.Append(c);
        }
        return sb.ToString();
    }
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
