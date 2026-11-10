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

    // ---- hit testing (CSSOM View §4) ----

    /// <summary>Node.contains over the document: true when the node is connected to this
    /// document's tree (its ancestor chain reaches the document root).</summary>
    public bool contains(JsElement other)
    {
        for (var n = other.Node; n != null; n = n.Parent)
            if (ReferenceEquals(n, _root)) return true;
        return false;
    }

    /// <summary>elementsFromPoint: the elements whose boxes cover the client-space point,
    /// paint order (deepest first). This box model resolves paint order by tree order (last
    /// child on top) — the same approximation the visual hit regions hit-test with.</summary>
    public JsElement[] elementsFromPoint(double x, double y)
    {
        var engine = JsEngine.For(_engine);
        engine?.EnsureLayout();
        var viewport = engine?.Viewport;
        var cx = (float)x + (viewport?.ScrollX ?? 0);
        var cy = (float)y + (viewport?.ScrollY ?? 0);
        var target = NodeAtPoint(_root, cx, cy);
        if (target is null) return [];
        var chain = new List<JsElement>();
        for (var n = target; n != null; n = n.Parent)
            chain.Add(JsElement.For(_engine, n));
        return chain.ToArray();
    }

    public JsElement? elementFromPoint(double x, double y)
    {
        var chain = elementsFromPoint(x, y);
        return chain.Length > 0 ? chain[0] : null;
    }

    /// <summary>Deepest node whose border box contains the content-space point.</summary>
    private static LayoutNode? NodeAtPoint(LayoutNode node, float x, float y)
    {
        for (var i = node.Children.Count - 1; i >= 0; i--)
            if (NodeAtPoint(node.Children[i], x, y) is { } hit) return hit;
        var box = node.Box.BorderBox;
        return box.Width > 0 && box.Height > 0 && box.Contains(x, y) ? node : null;
    }

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
        ? _document.QuerySelector(selector) is { } element && !JsElement.IsOriginSheet(_engine, element)
            ? Wrap(element) : null
        : SelectorEngine.QuerySelector(_root, selector, _engine);

    public JsNodeList querySelectorAll(string selector)
    {
        var snapshot = _document is not null
            ? _document.QuerySelectorAll(selector).Where(e => !JsElement.IsOriginSheet(_engine, e))
                .Select(Wrap).ToArray()
            : SelectorEngine.QuerySelectorAll(_root, selector, _engine);
        return new JsNodeList(() => snapshot);
    }

    public JsHtmlCollection getElementsByTagName(string tagName)
    {
        var tag = tagName.ToUpperInvariant();
        return new JsHtmlCollection(() => _document is not null
            ? _document.QuerySelectorAll("*").Where(e => (tag == "*" || e.TagName.ToUpperInvariant() == tag)
                    && !JsElement.IsOriginSheet(_engine, e))
                .Select(Wrap).ToArray()
            : FindAll(_root, n => tag == "*" || n.TagName == tag)
                .Select(n => JsElement.For(_engine, n)).ToArray());
    }

    public JsHtmlCollection getElementsByTagNameNS(string? namespaceURI, string localName)
    {
        return new JsHtmlCollection(() => _document is not null
            ? _document.QuerySelectorAll("*").Where(e => JsElement.MatchesNamespace(e, namespaceURI)
                    && (localName == "*" || e.LocalName == localName)
                    && !JsElement.IsOriginSheet(_engine, e)).Select(Wrap).ToArray()
            : FindAll(_root, n => !n.TagName.StartsWith('#') && JsElement.MatchesNamespace(n.DomNode, namespaceURI)
                    && (localName == "*" || JsElement.NodeLocalName(n) == localName))
                .Select(n => JsElement.For(_engine, n)).ToArray());
    }

    public JsHtmlCollection getElementsByClassName(string classNames)
    {
        var classes = DomWhitespace.Split(classNames);
        return new JsHtmlCollection(() => _document is not null
            ? _document.QuerySelectorAll("*").Where(e => classes.All(c =>
                DomWhitespace.Split(e.GetAttribute("class") ?? "").Contains(c))
                && !JsElement.IsOriginSheet(_engine, e))
                .Select(Wrap).ToArray()
            : FindAll(_root, n =>
        {
            var nodeClasses = DomWhitespace.Split(n.Attributes.GetValueOrDefault("class", ""));
            return classes.All(c => nodeClasses.Contains(c));
        }).Select(n => JsElement.For(_engine, n)).ToArray());
    }

    /// <summary>Returns all elements with the given name attribute (Document.getElementsByName()).</summary>
    public JsNodeList getElementsByName(string name) => new(() => _document is not null
        ? _document.QuerySelectorAll("*").Where(e => e.GetAttribute("name") == name
                && !JsElement.IsOriginSheet(_engine, e))
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

    /// <summary>document.createRange() — the document's Range factory (DOM4 §5).</summary>
    public JsRange createRange() => new(_engine);

    /// <summary>The document's live selection (one per document, shared by all windows of the
    /// context through the engine).</summary>
    public JsSelection getSelection() => _selection ??= new JsSelection(_engine);

    /// <summary>HTML §7.5.1 execCommand: dispatches an editing command against the document's
    /// selection. Unknown commands return false without throwing (the never-throw contract);
    /// known commands edit the tree through the same mutation paths the DOM methods use.</summary>
    public bool execCommand(string command, bool showUI = false, string? value = null)
    {
        if (!EditingCommands.Supported.Contains(command)) return false;
        return EditingCommands.Execute(this, command, value);
    }

    public bool queryCommandSupported(string command) => EditingCommands.Supported.Contains(command);

    public bool queryCommandEnabled(string command) => EditingCommands.Supported.Contains(command);

    public bool queryCommandState(string command) =>
        command.Equals("stylewithcss", StringComparison.OrdinalIgnoreCase) ? _styleWithCss : false;

    public string queryCommandValue(string command) => "";

    private bool _styleWithCss;

    internal Engine SelectionEngine => _engine;

    private JsSelection? _selection;

    // ---- EventTarget ----
    // The document carries its own listener store: it is the second object of every node
    // dispatch path (window → document → …, DOM §2.9), firing capture listeners during
    // capture and bubble listeners during bubble.
    private readonly List<EventListenerEntry> _listeners = [];

    private static bool CaptureOf(JsValue? options) => options switch
    {
        null => false,
        _ when options.IsBoolean() => options.AsBoolean(),
        _ when options.IsObject() => options.AsObject().Get("capture") is { } c && c.IsBoolean() && c.AsBoolean(),
        _ => false,
    };

    public void addEventListener(string type, JsValue handler, JsValue? options = null)
    {
        if (handler is null || handler.IsUndefined()) return;
        _listeners.Add(new EventListenerEntry(type, handler, null, CaptureOf(options)));
    }

    public void removeEventListener(string type, JsValue handler, JsValue? options = null)
    {
        var capture = CaptureOf(options);
        _listeners.RemoveAll(l => l.EventType == type && Equals(l.Handler, handler) && l.Capture == capture);
    }

    public bool dispatchEvent(JsEvent? evt = null)
    {
        // dispatchEvent(null) raises TypeError (dom/events/EventTarget-dispatchEvent).
        if (evt is null)
            throw JsErrors.Native(_engine, "TypeError",
                "Failed to execute 'dispatchEvent': parameter 1 is not of type 'Event'.");
        // The document as target: the same dispatch contract as element dispatch — an
        // uninitialized createEvent event throws InvalidStateError, an event already on a
        // path cannot be re-dispatched (DOM §2.9).
        if (!evt.Initialized)
            throw JsErrors.Dom(_engine, "InvalidStateError",
                "Failed to execute 'dispatchEvent': The event provided is not initialized.");
        if (evt.Dispatching)
            throw JsErrors.Dom(_engine, "InvalidStateError",
                "Failed to execute 'dispatchEvent': The event is already being dispatched.");
        var handled = false;
        var report = JsEngine.For(_engine);
        foreach (var listener in _listeners.ToList())
        {
            if (listener.EventType != evt.type) continue;
            if (listener.Handler is null) continue;
            try { _engine.Invoke(listener.Handler, JsValue.FromObject(_engine, evt)); }
            catch (Exception ex) { report?.ReportListenerError(ex); }
            handled = true;
        }
        evt.Dispatching = false;
        evt.ResetPropagationFlags();
        return handled;
    }

    /// <summary>The document's slice of a node-targeted dispatch path (DOM §2.9).</summary>
    internal bool InvokePathListeners(string type, JsEvent evt, bool capturePhase)
    {
        var handled = false;
        var report = JsEngine.For(_engine);
        foreach (var listener in _listeners.ToList())
        {
            if (listener.EventType != type) continue;
            if (listener.Capture != capturePhase) continue;
            if (evt.ImmediatePropagationStopped) break;
            if (!_listeners.Contains(listener)) continue;
            try { _engine.Invoke(listener.Handler, JsValue.FromObject(_engine, evt)); }
            catch (Exception ex) { report?.ReportListenerError(ex); }
            handled = true;
        }
        return handled;
    }

    // ---- document.write / open / close ----
    // A frame document's open()/write()/close() stream replaces the document: writes buffer,
    // and close() swaps the owning frame's child page for a fresh parse of the buffer — the
    // html5lib write corpus's open/write/close pattern then behaves like the spec's streamed
    // parse (whole-input and per-character writes serialize identically; scripts in the
    // written markup run through the child-page pipeline). A write() WITHOUT a prior open()
    // keeps the legacy append-to-body behavior for callers that never re-open the stream.

    private string? _writeBuffer;

    public void open() => _writeBuffer = string.Empty;

    public void write(string markup)
    {
        if (_writeBuffer is not null)
        {
            _writeBuffer += markup ?? string.Empty;
            return;
        }
        if (string.IsNullOrEmpty(markup)) return;
        // A mid-parse write cannot splice into the parser's input: the pipeline
        // executes classic scripts after the tree parse by design (layout
        // determinism), so there is no live insertion point — the tokenizer has
        // consumed the stream. The write therefore parses as a fragment and
        // appends; the streaming document.write semantics live on the
        // frame-level write path (open/write/close) instead.
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

    public void close()
    {
        if (_writeBuffer is null) return;
        var markup = _writeBuffer;
        _writeBuffer = null;
        var engine = JsEngine.For(_engine);
        if (engine?.ParentContextEngine is not { } parent ||
            engine.FrameElementWrapper is not JsElement frame) return;
        var frameNode = frame.Node;
        var navigationVersion = ++frameNode.FrameNavigationVersion;
        parent.EnqueueMacrotask(() =>
        {
            if (navigationVersion != frameNode.FrameNavigationVersion) return;
            try
            {
                var width = int.TryParse(frameNode.Attributes.GetValueOrDefault("width"), out var w) && w > 0 ? w : 300;
                var height = int.TryParse(frameNode.Attributes.GetValueOrDefault("height"), out var h) && h > 0 ? h : 150;
                var child = Parser.ParseChildPage(markup, isSrcdoc: true, parent.DocumentBaseUrl,
                    width, height, parent.DocumentState.Session, parent, frameNode, frameNode.ChildPage.SandboxFlags);
                frameNode.ChildPage = child;
                EventDispatcher.DispatchToNode(frameNode, "load", parent);
            }
            catch (Exception ex)
            {
                parent.DocumentState.Session?.Diagnostics.Enqueue($"document.write: {ex.Message}");
                Console.WriteLine($"[document.write] {ex.Message}");
            }
        });
    }

    // ---- document metadata ----
    // The title's authority is the engine tree (HTML §4.2.2: the child text content of the title
    // element) — the getter reads the projected TITLE element, so mutations through the engine
    // (write streams, textContent, re-projection) are visible immediately instead of going stale
    // against a separate AngleSharp document. The AngleSharp write stays for serialization truth
    // (head.innerHTML), and _title serves the synthetic-root facade path where no TITLE exists.

    public string title
    {
        get
        {
            if (FindFirst(_root, n => n.TagName == "TITLE") is not { } titleElement)
                return _title ?? string.Empty;
            return TitleText(titleElement);
        }
        set
        {
            var title = value ?? string.Empty;
            _title = title;
            if (FindFirst(_root, n => n.TagName == "TITLE") is { } titleElement)
            {
                // TITLE parses as RCDATA, so its content is its own folded text; a #text child
                // can only exist through a synthetic path — fold it in on both sides.
                titleElement.Children.Clear();
                titleElement.SetOwnText(title);
            }
            if (_document is { } doc) doc.Title = title;
            JsEngine.For(_engine)?.OnTitleChange?.Invoke(title);
        }
    }

    private static string TitleText(LayoutNode titleElement) => titleElement.Text +
        string.Concat(titleElement.Children.Where(c => c.TagName == "#text").Select(c => c.Text));

    /// <summary>The engine tree's root node, for walkers rooted at the document itself.</summary>
    internal LayoutNode RootNode => _root;

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
        set
        {
            // HTML §6.4 document.domain setter: registering relaxes same-origin. A page with an
            // opaque origin (sandboxed without allow-same-origin) cannot register — the setter
            // raises SecurityError (dom: sandboxed-document_domain). A value that is not a
            // suffix of the current host is rejected the same way.
            var eng = JsEngine.For(_engine) ?? throw JsErrors.Dom(_engine, "SecurityError",
                "Blocked a frame with an opaque origin from setting document.domain.");
            if (!eng.SandboxAllowsSameOrigin || eng.Origin == "null")
                throw JsErrors.Dom(_engine, "SecurityError",
                    "Blocked a frame with an opaque origin from setting document.domain.");
            if (!Uri.TryCreate(eng.Origin, UriKind.Absolute, out var current) ||
                !Uri.TryCreate("http://" + value, UriKind.Absolute, out var target) ||
                current.Host != target.Host && !current.Host.EndsWith("." + target.Host, StringComparison.OrdinalIgnoreCase))
                throw JsErrors.Dom(_engine, "SecurityError",
                    $"Cannot set document.domain to '{value}' from origin '{eng.Origin}'.");
            // Same-host registration: the observable state stays the host; the relaxed-access
            // table this target tracks is a no-op in the single-origin serving environment.
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
    // The root may be any Node — including the document itself (dom/traversal/NodeIterator
    // "createNodeIterator() parameter defaults"). A null/undefined filter normalizes to no-filter,
    // so createTreeWalker(root, bitMask, null) never reads acceptNode off it.

    public JsTreeWalker createTreeWalker(JsElement root, double whatToShow = 4294967295, JsValue? filter = null) =>
        new(_engine, root.Node, (uint)whatToShow, NormalizeTraversalFilter(filter), root);

    public JsTreeWalker createTreeWalker(JsDocument root, double whatToShow = 4294967295, JsValue? filter = null) =>
        new(_engine, root.RootNode, (uint)whatToShow, NormalizeTraversalFilter(filter), root);

    public JsNodeIterator createNodeIterator(JsElement root, double whatToShow = 4294967295, JsValue? filter = null) =>
        new(_engine, root.Node, (uint)whatToShow, NormalizeTraversalFilter(filter), root);

    public JsNodeIterator createNodeIterator(JsDocument root, double whatToShow = 4294967295, JsValue? filter = null) =>
        new(_engine, root.RootNode, (uint)whatToShow, NormalizeTraversalFilter(filter), root);

    internal static JsValue? NormalizeTraversalFilter(JsValue? filter) =>
        filter is null || filter.IsUndefined() || filter.IsNull() ? null : filter;

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
