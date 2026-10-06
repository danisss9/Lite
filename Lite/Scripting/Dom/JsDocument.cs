using Lite.Scripting.Runtime;
using Lite.Models;
using AngleSharp.Dom;

namespace Lite.Scripting.Dom;

/// <summary>Full document proxy exposed to JavaScript.</summary>
public class JsDocument
{
    private readonly Engine _engine;
    private readonly LayoutNode _root;
    private readonly AngleSharp.Dom.IDocument? _document;
    // Title store for facades over a synthetic root (no authoritative DOM): the root is not
    // bound to the source document's element, so there is no <title> element to reflect.
    private string? _title;
    internal bool HasAuthoritativeDom => _document is not null;
    private DocumentState? State => JsEngine.For(_engine)?.DocumentState;
    private JsElement Wrap(INode node) => JsElement.For(_engine, State!.ForDomNode(node));
    private readonly JsHtmlAllCollection _all;

    public JsDocument(Engine engine, LayoutNode root)
    {
        _engine = engine;
        _root = root;
        var source = JsEngine.For(engine)?.SourceDocument;
        _document = ReferenceEquals(root.DomNode, source?.DocumentElement) ? source : null;
        _all = new JsHtmlAllCollection(engine, root);
    }

    // ---- identity ----
    public string nodeName => "#document";
    public int nodeType => 9;

    /// <summary>"loading" → "interactive" (DOMContentLoaded) → "complete" (load). The state lives
    /// on the page's JsEngine so every JsDocument facade over this document agrees.</summary>
    public string readyState => JsEngine.For(_engine)?.DocumentReadyState ?? "complete";
    public JsElement? documentElement => _document?.DocumentElement is { } element
        ? Wrap(element) : _root.Children.Count > 0 ? JsElement.For(_engine, _root) : null;
    public JsElement? doctype => _document?.Doctype is { } type ? Wrap(type) : null;
    public JsNodeList childNodes => new(() => _document is null
        ? Array.Empty<JsElement>() : _document.ChildNodes.Select(Wrap).ToArray());
    public JsElement? firstChild => _document?.FirstChild is { } child ? Wrap(child) : null;
    public JsElement? lastChild => _document?.LastChild is { } child ? Wrap(child) : null;

    /// <summary>A stable, live legacy collection with QuickJS [[IsHTMLDDA]] behavior.</summary>
    public JsValue all => _all.Value;

    /// <summary>Returns the window object (document.defaultView). Returned as a live JsValue:
    /// a CLR round-trip (ToObject) would hand the result converter a graph that cycles through
    /// globalThis and throw "Cyclic reference detected".</summary>
    public JsValue? defaultView => _engine.GetValue("window");

    public JsElement? body => _document?.Body is { } body ? Wrap(body) :
        FindFirst(_root, n => n.TagName == "BODY") is { } b ? JsElement.For(_engine, b) : null;

    public JsElement? activeElement =>
        JsEngine.For(_engine)?.DocumentState.ActiveElement is { } active
            ? JsElement.For(_engine, active) : body;

    public JsElement? head => _document?.Head is { } head ? Wrap(head) :
        FindFirst(_root, n => n.TagName == "HEAD") is { } h ? JsElement.For(_engine, h) : null;

    /// <summary>The currently executing script element, or null outside script execution
    /// (timers, microtasks, module code) — HTML §4.11.1.</summary>
    public JsElement? currentScript =>
        JsEngine.For(_engine)?.CurrentScriptNode is { } node ? JsElement.For(_engine, node) : null;

    // ---- selectors ----
    public JsElement? getElementById(string id)
    {
        if (_document is not null)
            return _document.GetElementById(id) is { } element ? Wrap(element) : null;
        var node = FindById(_root, id);
        return node is null ? null : JsElement.For(_engine, node);
    }

    public JsElement? querySelector(string selector) => _document is not null
        ? _document.QuerySelector(selector) is { } element ? Wrap(element) : null
        : SelectorEngine.QuerySelector(_root, selector, _engine);

    public JsNodeList querySelectorAll(string selector)
    {
        var snapshot = _document is not null
            ? _document.QuerySelectorAll(selector).Select(Wrap).ToArray()
            : SelectorEngine.QuerySelectorAll(_root, selector, _engine);
        return new JsNodeList(() => snapshot);
    }

    public JsHtmlCollection getElementsByTagName(string tagName)
    {
        var tag = tagName.ToUpperInvariant();
        return new JsHtmlCollection(() => _document is not null
            ? _document.QuerySelectorAll("*").Where(e => tag == "*" || e.TagName.ToUpperInvariant() == tag)
                .Select(Wrap).ToArray()
            : FindAll(_root, n => tag == "*" || n.TagName == tag)
                .Select(n => JsElement.For(_engine, n)).ToArray());
    }

    public JsHtmlCollection getElementsByClassName(string classNames)
    {
        var classes = DomWhitespace.Split(classNames);
        return new JsHtmlCollection(() => _document is not null
            ? _document.QuerySelectorAll("*").Where(e => classes.All(c =>
                DomWhitespace.Split(e.GetAttribute("class") ?? "").Contains(c)))
                .Select(Wrap).ToArray()
            : FindAll(_root, n =>
        {
            var nodeClasses = DomWhitespace.Split(n.Attributes.GetValueOrDefault("class", ""));
            return classes.All(c => nodeClasses.Contains(c));
        }).Select(n => JsElement.For(_engine, n)).ToArray());
    }

    /// <summary>Returns all elements with the given name attribute (Document.getElementsByName()).</summary>
    public JsNodeList getElementsByName(string name) => new(() => _document is not null
        ? _document.QuerySelectorAll("*").Where(e => e.GetAttribute("name") == name)
            .Select(Wrap).ToArray()
        : FindAll(_root, n => n.Attributes.GetValueOrDefault("name") == name)
            .Select(n => JsElement.For(_engine, n)).ToArray());

    public JsHtmlCollection images => getElementsByTagName("img");
    public JsHtmlCollection forms => getElementsByTagName("form");
    public JsHtmlCollection scripts => getElementsByTagName("script");
    public JsHtmlCollection links => new(() => _document is null ? Array.Empty<JsElement>() :
        _document.QuerySelectorAll("a,area").Where(e => e.HasAttribute("href")).Select(Wrap).ToArray());

    /// <summary>The document's style sheets (CSSOM): rule mutation through a rule's style
    /// refreshes the engine's rule view and recascades the document.</summary>
    public JsStyleSheetList styleSheets => new(_engine, () =>
    {
        // The CSSOM list carries author sheets only — the injected UA sheet (and user sheets)
        // never appear in document.styleSheets, or scripts mutating cssRules[0] would target
        // the wrong origin.
        var ua = Parser.UaStyleSheetOwner;
        var user = Parser.UserStyleSheetOwners;
        var list = _document is null ? null : _document.StyleSheets;
        if (list is null) return Enumerable.Empty<AngleSharp.Css.Dom.ICssStyleSheet>();
        return list.OfType<AngleSharp.Css.Dom.ICssStyleSheet>()
            .Where(s => s.OwnerNode is not IElement owner || (owner != ua && !user.Contains(owner)));
    });

    // ---- creation ----
    public JsElement createElement(string tagName)
    {
        if (_document is not null)
        {
            var projection = State!.ForDomNode(_document.CreateElement(tagName));
            projection.NeedsStyleResolution = true;
            return JsElement.For(_engine, projection);
        }
        var style = _root.Style;
        var node = new LayoutNode(null, tagName.ToUpperInvariant(), string.Empty, style)
        {
            NeedsStyleResolution = true, // cascade applied when inserted into the live tree
        };
        return JsElement.For(_engine, node);
    }

    public JsElement createElementNS(string ns, string tagName)
    {
        if (_document is not null)
        {
            var projection = State!.ForDomNode(_document.CreateElement(ns, tagName));
            projection.NeedsStyleResolution = true;
            return JsElement.For(_engine, projection);
        }
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
        if (_document is not null) return Wrap(_document.CreateTextNode(text));
        var style = _root.Style;
        var node = new LayoutNode(null, "#text", text, style);
        return JsElement.For(_engine, node);
    }

    public JsElement createDocumentFragment()
    {
        if (_document is not null) return Wrap(_document.CreateDocumentFragment());
        var style = _root.Style;
        var node = new LayoutNode(null, "#document-fragment", string.Empty, style);
        return JsElement.For(_engine, node);
    }

    /// <summary>document.createComment(data) — a Comment (CharacterData) node. Never rendered.</summary>
    public JsElement createComment(string data)
    {
        if (_document is not null) return Wrap(_document.CreateComment(data ?? string.Empty));
        var node = new LayoutNode(null, "#comment", data ?? string.Empty, _root.Style);
        node.StyleOverrides["display"] = "none"; // comments produce no box
        return JsElement.For(_engine, node);
    }

    /// <summary>document.createProcessingInstruction(target, data) — a PI (CharacterData) node.</summary>
    public JsElement createProcessingInstruction(string target, string data)
    {
        if (_document is not null) return Wrap(_document.CreateProcessingInstruction(target, data));
        var node = new LayoutNode(null, "#pi", data ?? string.Empty, _root.Style);
        node.StyleOverrides["display"] = "none";
        node.Attributes["_pi_target"] = target ?? string.Empty;
        return JsElement.For(_engine, node);
    }

    /// <summary>document.createAttribute(name) — a detached Attr to attach via setAttributeNode.</summary>
    public JsAttr createAttribute(string name) => new(name);

    /// <summary>The legacy createEvent alias table (DOM §4.5). All aliases map onto the superset
    /// <see cref="JsEvent"/>; matching is ASCII case-insensitive; anything else → NotSupportedError.</summary>
    private static readonly HashSet<string> _createEventAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        "beforeunloadevent", "compositionevent", "customevent", "devicemotionevent",
        "deviceorientationevent", "dragevent", "event", "events", "focusevent", "hashchangeevent",
        "htmlevents", "keyboardevent", "keyevents", "messageevent", "mouseevent", "mouseevents",
        "storageevent", "svgevents", "textevent", "touchevent", "uievent", "uievents",
    };

    public JsEvent createEvent(string type)
    {
        if (!_createEventAliases.Contains(type ?? string.Empty))
            throw JsErrors.Dom("NotSupportedError",
                $"Failed to execute 'createEvent' on 'Document': The provided event type ('{type}') is invalid.");
        // Born uninitialized: dispatching before initEvent() throws InvalidStateError.
        return new JsEvent { Initialized = false };
    }

    // ---- EventTarget ----
    // The document shares the root node's listener list, so document-level listeners
    // participate in the normal capture/bubble path of every dispatch.
    public void addEventListener(string type, JsValue handler, JsValue? options = null) =>
        JsElement.For(_engine, _root).addEventListener(type, handler, options);

    public void removeEventListener(string type, JsValue handler, JsValue? options = null) =>
        JsElement.For(_engine, _root).removeEventListener(type, handler, options);

    public bool dispatchEvent(JsEvent? evt = null) => JsElement.For(_engine, _root).dispatchEvent(evt);

    // ---- document.write / open / close ----
    // Minimal model: each write() parses its markup and appends the result to <body>. Because the
    // document is parsed up front (not incrementally), writes land at the end of the body rather
    // than at the script's position, and markup split across calls is not stitched together.

    public void open() { /* no-op: we never buffer/blank the document */ }

    public void write(string markup)
    {
        if (string.IsNullOrEmpty(markup)) return;
        var target = FindFirst(_root, n => n.TagName == "BODY") ?? _root;
        foreach (var node in Parser.ParseFragment(markup, target.TagName, JsEngine.For(_engine)?.DocumentState))
        {
            // The fragment's nodes carry detached DOM counterparts; moving them under the live
            // body keeps getElementById/querySelector working on written content.
            if (node.DomNode is { } dom && _document?.Body is { } body) body.AppendChild(dom);
            target.AddChild(node);
        }
    }

    public void writeln(string markup) => write((markup ?? "") + "\n");

    public void close() { /* no-op */ }

    // ---- document metadata ----
    public string title
    {
        get => _document?.Title ?? _title ?? string.Empty;
        set
        {
            var title = value ?? string.Empty;
            _title = title;
            if (_document is { } doc) doc.Title = title;
            JsEngine.For(_engine)?.OnTitleChange?.Invoke(title);
        }
    }

    /// <summary>document.location — same object as window.location.</summary>
    public JsLocation? location => JsEngine.For(_engine)?.Location;

    public string URL => JsEngine.For(_engine)?.CurrentUrl ?? Parser.BaseUrl ?? "";
    public string domain
    {
        get
        {
            try { return JsEngine.For(_engine)?.Origin is { } b && Uri.TryCreate(b, UriKind.Absolute, out var u) ? u.Host : ""; }
            catch { return ""; }
        }
    }
    public string compatMode => _document?.CompatMode ?? "CSS1Compat";

    /// <summary>DOM Normalizer on the document: merges adjacent text nodes everywhere.</summary>
    public void normalize() => JsElement.NormalizeNode(_root);

    // ---- cookies (the owning browser window's HTTP jar) ----
    public string cookie
    {
        get => JsEngine.For(_engine) is { } owner
            ? owner.DocumentState.Session?.GetDocumentCookie(owner.CurrentUrl) ?? string.Empty : string.Empty;
        set
        {
            if (JsEngine.For(_engine) is { } owner)
                owner.DocumentState.Session?.SetDocumentCookie(owner.CurrentUrl, value);
        }
    }

    // ---- DOM Traversal Level 2 (Phase 9) ----
    public JsTreeWalker createTreeWalker(JsElement root, double whatToShow = 4294967295, JsValue? filter = null)
    {
        return new JsTreeWalker(_engine, root.Node, (uint)whatToShow, filter);
    }

    public JsNodeIterator createNodeIterator(JsElement root, double whatToShow = 4294967295, JsValue? filter = null)
    {
        return new JsNodeIterator(_engine, root.Node, (uint)whatToShow, filter);
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
