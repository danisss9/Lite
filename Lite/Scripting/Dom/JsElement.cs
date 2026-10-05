using Lite.Scripting.Runtime;
using Lite.Interaction;
using Lite.Layout;
using Lite.Models;
using Lite.Rendering;
using AngleSharp.Dom;

namespace Lite.Scripting.Dom;

/// <summary>Lightweight DOM element proxy exposed to JavaScript.</summary>
public class JsElement
{
    private readonly Engine _engine;
    internal readonly LayoutNode Node;
    private INode? DomNode => JsEngine.For(_engine)?.DocumentFacade?.HasAuthoritativeDom == true
        ? Node.DomNode : null;
    private DocumentState? State => JsEngine.For(_engine)?.DocumentState;
    private JsElement Wrap(INode node) => For(_engine, State!.ForDomNode(node));
    private JsStyle? _style;
    private JsHtmlCollection? _childrenCollection;
    private JsNodeList? _childNodeList;

    public JsElement(Engine engine, LayoutNode node)
    {
        _engine = engine;
        Node = node;
        Node.DocumentState ??= JsEngine.For(engine)?.DocumentState;
    }

    // Node identity is stable within each realm, including when another realm wraps the node.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Engine,
        System.Runtime.CompilerServices.ConditionalWeakTable<LayoutNode, JsElement>> _wrappers = new();

    /// <summary>Returns the canonical JsElement wrapper for a node in this realm.</summary>
    public static JsElement For(Engine engine, LayoutNode node)
        => _wrappers.GetValue(engine, _ => new()).GetValue(node, n => new JsElement(engine, n));

    // ---- identity ----
    public string id
    {
        get => (DomNode as IElement)?.Id ?? Node.Id ?? string.Empty;
        set => setAttribute("id", value);
    }
    public string tagName => (DomNode as IElement)?.TagName ?? Node.TagName.ToUpperInvariant();
    public string localName => (DomNode as IElement)?.LocalName ?? Node.TagName.ToLowerInvariant();
    public string? namespaceURI => (DomNode as IElement)?.NamespaceUri;
    public bool isConnected => DomNode is { } dom
        ? ReferenceEquals(GetDomRoot(dom), State?.Document)
        : GetRootNode() == JsEngine.For(_engine)?.DocumentFacade.documentElement?.Node;

    private static INode GetDomRoot(INode node)
    {
        while (node.Parent is { } parent) node = parent;
        return node;
    }

    // ---- DOM Core Level 2 ----
    /// <summary>True for the CharacterData node kinds: Text, Comment, ProcessingInstruction.</summary>
    private bool IsCharacterData => Node.TagName is "#text" or "#comment" or "#pi";

    public int nodeType => DomNode is { } dom ? (int)dom.NodeType : Node.TagName switch
    {
        "#text" => 3,            // TEXT_NODE
        "#pi" => 7,              // PROCESSING_INSTRUCTION_NODE
        "#comment" => 8,         // COMMENT_NODE
        "#document" => 9,        // DOCUMENT_NODE
        "#document-fragment" => 11, // DOCUMENT_FRAGMENT_NODE
        _ => 1,                  // ELEMENT_NODE
    };
    public string nodeName => DomNode?.NodeName ?? (Node.TagName switch
    {
        "#text" => "#text",
        "#comment" => "#comment",
        "#pi" => Node.Attributes.GetValueOrDefault("_pi_target", ""),
        _ => Node.TagName.ToUpperInvariant(),
    });

    /// <summary>ProcessingInstruction.target.</summary>
    public string? target => DomNode is IProcessingInstruction pi ? pi.Target :
        Node.TagName == "#pi" ? Node.Attributes.GetValueOrDefault("_pi_target", "") : null;

    public JsValue nodeValue
    {
        get => IsCharacterData ? (JsValue)(DomNode?.TextContent ?? Node.DisplayText ?? "") : JsValue.Null;
        // nodeValue is [LegacyNullToEmptyString]: null → "".
        set { if (IsCharacterData) SetCharacterData(CoerceLegacyNull(value)); }
    }

    /// <summary>CharacterData.data — the text of a Text/Comment/PI node.</summary>
    public JsValue data
    {
        get => IsCharacterData ? (JsValue)(DomNode?.TextContent ?? Node.DisplayText ?? "") : JsValue.Undefined;
        // data is [LegacyNullToEmptyString]: null → "".
        set { if (IsCharacterData) SetCharacterData(CoerceLegacyNull(value)); }
    }

    /// <summary>CharacterData.length — code-unit length of the data (else child count for elements).</summary>
    public int length => IsCharacterData ? (DomNode?.TextContent?.Length ?? Node.DisplayText?.Length ?? 0) :
        DomNode?.ChildNodes.Length ?? Node.Children.Count;

    private void SetCharacterData(string data)
    {
        var old = DomNode?.TextContent ?? Node.DisplayText;
        if (DomNode is not null) DomNode.TextContent = data;
        Node.TextOverride = data;
        MutationObserverRegistry.NotifyCharacterData(_engine, Node, old);
    }

    // ---- CharacterData mutation API (§ DOM CharacterData) ----
    // offset/count are WebIDL unsigned long (ToUint32): JS strings, doubles, and negatives all
    // coerce — e.g. -1 → 4294967295, -0x100000000+2 → 2 — so the bounds checks match the spec.
    // The trailing params array lets WebIDL "extra arguments are ignored" calls (e.g.
    // substringData(0, 1, 2)) bind — the host binding may not otherwise match a call with surplus args.
    public string substringData(JsValue offset, JsValue count, params JsValue[] _)
    {
        var s = DomNode?.TextContent ?? Node.DisplayText ?? "";
        long o = ToU32(offset);
        if (o > s.Length) ThrowDom("IndexSizeError", "offset is greater than length");
        long c = Math.Min(ToU32(count), s.Length - o);
        return s.Substring((int)o, (int)c);
    }

    public void appendData(JsValue data, params JsValue[] _) =>
        SetCharacterData((DomNode?.TextContent ?? Node.DisplayText ?? "") + CoerceString(data));

    public void insertData(JsValue offset, JsValue data, params JsValue[] _) => replaceData(offset, JsNumber.Create(0), data);

    public void deleteData(JsValue offset, JsValue count, params JsValue[] _) => replaceData(offset, count, (JsValue)"");

    public void replaceData(JsValue offset, JsValue count, JsValue data, params JsValue[] _)
    {
        var s = DomNode?.TextContent ?? Node.DisplayText ?? "";
        long o = ToU32(offset);
        if (o > s.Length) ThrowDom("IndexSizeError", "offset is greater than length");
        long c = Math.Min(ToU32(count), s.Length - o);
        SetCharacterData(string.Concat(s.AsSpan(0, (int)o), CoerceString(data), s.AsSpan((int)(o + c))));
    }

    /// <summary>WebIDL DOMString coercion (null → "null", undefined → "undefined", else ToString).</summary>
    private static string CoerceString(JsValue v) => TypeConverter.ToString(v);

    /// <summary>WebIDL [LegacyNullToEmptyString] DOMString coercion: null → "".</summary>
    private static string CoerceLegacyNull(JsValue v) => v.IsNull() ? "" : TypeConverter.ToString(v);

    /// <summary>WebIDL unsigned-long (ToUint32) coercion: truncate, modulo 2^32, into [0, 2^32).</summary>
    private static long ToU32(JsValue v)
    {
        var n = TypeConverter.ToNumber(v);
        if (double.IsNaN(n) || double.IsInfinity(n)) return 0;
        var m = Math.Truncate(n) % 4294967296.0;
        if (m < 0) m += 4294967296.0;
        return (long)m;
    }

    private static readonly Dictionary<string, int> DomCodes = new()
    {
        ["IndexSizeError"] = 1,
        ["HierarchyRequestError"] = 3,
        ["WrongDocumentError"] = 4,
        ["InvalidCharacterError"] = 5,
        ["NoModificationAllowedError"] = 7,
        ["NotFoundError"] = 8,
        ["NotSupportedError"] = 9,
        ["InvalidStateError"] = 11,
        ["SyntaxError"] = 12,
        ["NamespaceError"] = 14,
        ["InvalidNodeTypeError"] = 24,
    };

    /// <summary>Raises a JS-catchable DOMException (e.name / e.code / e.message, and
    /// e.constructor === DOMException so assert_throws_dom's same-global check passes). Builds the
    /// object directly via the shim's DOMException.prototype — re-entering the engine (Invoke/
    /// Evaluate/Construct) from inside a host method cannot safely re-enter the engine — and throws it via
    /// JavaScriptException, which propagates intact now that CatchClrExceptions skips it.</summary>
    private void ThrowDom(string name, string message)
    {
        throw JsErrors.Dom(name, message);
    }

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
    public JsDocument? ownerDocument => JsEngine.For(_engine)?.DocumentFacade;

    private LayoutNode GetRootNode()
    {
        var n = Node;
        while (n.Parent is not null) n = n.Parent;
        return n;
    }

    // ---- content ----
    public string textContent
    {
        get => DomNode?.TextContent ?? GetTextContentRecursive(Node);
        set
        {
            if (DomNode is not null) DomNode.TextContent = value;
            Node.Children.Clear();
            Node.TextOverride = value;
        }
    }

    private static string GetTextContentRecursive(LayoutNode node)
    {
        // A node's OWN textContent: CharacterData returns its data; an element concatenates the
        // textContent of its descendants — but Comment/PI data does NOT contribute to an ancestor's.
        if (node.TagName is "#text" or "#comment" or "#pi") return node.DisplayText;
        if (node.Children.Count == 0) return node.DisplayText;
        return string.Concat(node.Children
            .Where(c => c.TagName is not ("#comment" or "#pi"))
            .Select(GetTextContentRecursive));
    }

    /// <summary>HTMLScriptElement.text (and the legacy alias other elements rarely use): the
    /// element's text content. Loader scripts built with createElement('script') set .text
    /// before insertion; the insertion hook reads it back through the same storage.</summary>
    public string text
    {
        get => textContent;
        set => textContent = value;
    }

    public string innerHTML
    {
        get => DomNode is IElement element ? element.InnerHtml : HtmlSerializer.SerializeChildren(Node);
        set
        {
            if (DomNode is IElement element) element.InnerHtml = value ?? string.Empty;
            Node.Children.Clear();
            Node.TextOverride = string.Empty;
            foreach (var child in DomNode is IElement source && State is { } owner
                ? Parser.ProjectChildren(source, owner)
                : Parser.ParseFragment(value ?? string.Empty, Node.TagName, Node.OwningDocument))
                Node.AddChild(child);
        }
    }

    public string outerHTML
    {
        get => DomNode is IElement element ? element.OuterHtml : HtmlSerializer.SerializeOuter(Node);
        set => ReplaceSelfWithFragment(value ?? string.Empty);
    }

    /// <summary>
    /// Parses <paramref name="html"/> and inserts the resulting nodes relative to this
    /// element. position is one of beforebegin, afterbegin, beforeend, afterend.
    /// </summary>
    public void insertAdjacentHTML(string position, string html)
    {
        var context = position?.ToLowerInvariant() is "afterbegin" or "beforeend" ? Node : Node.Parent ?? Node;
        var nodes = Parser.ParseFragment(html ?? string.Empty, context.TagName, Node.OwningDocument);
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
        var nodes = Parser.ParseFragment(html, Node.Parent.TagName, Node.OwningDocument);
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
    /// <summary>Form-control value. Most controls expose a string; HTMLProgressElement and
    /// HTMLMeterElement expose a numeric value (clamped reflection of the <c>value</c> attribute);
    /// HTMLOutputElement's value is its text content.</summary>
    public object value
    {
        get => Node.TagName switch
        {
            "PROGRESS" => ProgressValue(),
            "METER" => MeterValue(),
            "OUTPUT" => textContent,
            _ => FormValue(),
        };
        set
        {
            switch (Node.TagName)
            {
                case "PROGRESS":
                case "METER":
                    Node.Attributes["value"] = ToNumStr(value);
                    break;
                case "OUTPUT":
                    textContent = ToStr(value);
                    break;
                default:
                    FormState.TextInputValues[Node.NodeKey] = ToStr(value);
                    break;
            }
        }
    }

    private string FormValue()
    {
        Node.Attributes.TryGetValue("value", out var defaultVal);
        return FormState.GetTextValue(Node.NodeKey, defaultVal);
    }

    // ---- progress / meter (HTMLProgressElement, HTMLMeterElement) ----

    private double NumAttr(string name, double fallback) =>
        Node.Attributes.TryGetValue(name, out var s) &&
        double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)
            ? v : fallback;

    private double ProgressMax() { var m = NumAttr("max", 1); return m <= 0 ? 1 : m; }
    private double ProgressValue() => Math.Clamp(NumAttr("value", 0), 0, ProgressMax());
    private double MeterMin() => NumAttr("min", 0);
    private double MeterMax() { var min = MeterMin(); var m = NumAttr("max", 1); return m < min ? min : m; }
    private double MeterValue() => Math.Clamp(NumAttr("value", 0), MeterMin(), MeterMax());

    /// <summary>HTMLProgressElement.max / HTMLMeterElement.max (reflected, defaulting to 1).</summary>
    public double max => Node.TagName == "PROGRESS" ? ProgressMax() : MeterMax();

    /// <summary>HTMLMeterElement.min (reflected, defaulting to 0).</summary>
    public double min => MeterMin();

    /// <summary>HTMLProgressElement.position — value/max for a determinate bar, or -1 when
    /// the element is indeterminate (no <c>value</c> attribute).</summary>
    public double position => Node.Attributes.ContainsKey("value") ? ProgressValue() / ProgressMax() : -1;

    /// <summary>HTMLMeterElement.low (reflected; clamped to [min,max], defaulting to min).</summary>
    public double low { get { var lo = NumAttr("low", MeterMin()); return Math.Clamp(lo, MeterMin(), MeterMax()); } }

    /// <summary>HTMLMeterElement.high (reflected; clamped to [low,max], defaulting to max).</summary>
    public double high { get { var hi = NumAttr("high", MeterMax()); return Math.Clamp(hi, low, MeterMax()); } }

    /// <summary>HTMLMeterElement.optimum (reflected; clamped to [min,max], default midpoint).</summary>
    public double optimum { get { var min = MeterMin(); var max = MeterMax(); return Math.Clamp(NumAttr("optimum", (min + max) / 2), min, max); } }

    private static string ToStr(object? v)
    {
        if (v is JsValue jv) v = jv.ToObject();
        return v switch
        {
            null => string.Empty,
            string s => s,
            double d => d.ToString(System.Globalization.CultureInfo.InvariantCulture),
            bool b => b ? "true" : "false",
            _ => Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
        };
    }

    private static string ToNumStr(object? v)
    {
        if (v is JsValue jv) v = jv.ToObject();
        if (v is double d) return d.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (v is string s && double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var p))
            return p.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return ToStr(v);
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
    /// Layout collapses a closed &lt;details&gt;'s non-summary content / a closed &lt;dialog&gt;
    /// entirely on the next reflow.</summary>
    public bool open
    {
        get => Node.Attributes.ContainsKey("open");
        set
        {
            if (value) setAttribute("open", ""); else removeAttribute("open");
        }
    }

    /// <summary>HTMLDialogElement.returnValue (backed by an internal attribute on the node).</summary>
    public string returnValue
    {
        get => Node.Attributes.GetValueOrDefault("_returnValue", string.Empty);
        set => Node.Attributes["_returnValue"] = value;
    }

    /// <summary>HTMLDialogElement.show() — opens the dialog (non-modal).</summary>
    public void show() => Node.Attributes["open"] = "";

    /// <summary>HTMLDialogElement.showModal() — opens the dialog as modal (top-layer/backdrop are
    /// approximated; the open state + layout reveal are the conformance-visible behaviour).</summary>
    public void showModal() => Node.Attributes["open"] = "";

    /// <summary>HTMLDialogElement.close([returnValue]) — closes the dialog and fires a 'close' event.</summary>
    public void close(string? returnValue = null)
    {
        if (returnValue != null) Node.Attributes["_returnValue"] = returnValue;
        if (Node.Attributes.Remove("open")) FireSimpleEvent("close");
    }

    /// <summary>Queues a non-bubbling event of <paramref name="type"/> on this node (macrotask).</summary>
    private void FireSimpleEvent(string type)
    {
        if (JsEngine.For(_engine) is not { } eng) return;
        eng.EnqueueMacrotask(() =>
        {
            var evt = new JsEvent();
            evt.Init(type, false, false);
            evt.target = For(eng.RawEngine, Node);
            EventDispatcher.DispatchEvent(Node, evt, eng);
        });
    }

    public string type
    {
        get => Node.TagName == "OUTPUT"
            ? "output"
            : Node.Attributes.GetValueOrDefault("type", Node.TagName == "INPUT" ? "text" : string.Empty);
        set => Node.Attributes["type"] = value;
    }

    /// <summary>Reflects the <c>for</c> content attribute (HTMLLabelElement / HTMLOutputElement.htmlFor).
    /// For &lt;output&gt; this is a space-separated list of IDs; we expose it as a single string.</summary>
    public string htmlFor
    {
        get => Node.Attributes.GetValueOrDefault("for", string.Empty);
        set => Node.Attributes["for"] = value;
    }

    /// <summary>HTMLOutputElement.defaultValue — the value used on form reset (backed by an internal
    /// attribute; defaults to the current text content until explicitly set).</summary>
    public string defaultValue
    {
        get => Node.Attributes.TryGetValue("_defaultValue", out var d) ? d : textContent;
        set => Node.Attributes["_defaultValue"] = value;
    }

    /// <summary>HTMLSelectElement.options / HTMLDataListElement.options — the contained &lt;option&gt;s.</summary>
    public JsElement[] options => getElementsByTagName("option").Snapshot().ToArray();

    /// <summary>HTMLTemplateElement.content — the inert DocumentFragment holding the template's
    /// parsed content (null on non-template elements).</summary>
    public JsElement? content => Node.TemplateContent is { } frag ? For(_engine, frag) : null;

    // ---- iframe nested browsing context (Phase C) ----
    /// <summary>HTMLIFrameElement.contentWindow — a WindowProxy into the child Page (same-origin).</summary>
    public object? contentWindow =>
        Node.ChildPage is { } p && JsEngine.For(_engine) is { } eng ? eng.GetWindowProxy(p.Engine) : null;

    /// <summary>HTMLIFrameElement.contentDocument — the child Page's document (same-origin).</summary>
    public JsDocument? contentDocument =>
        Node.ChildPage is { } p ? p.Engine.DocumentFacade : null;

    /// <summary>HTMLImageElement.src / HTMLSourceElement.src — reflects the <c>src</c> attribute.</summary>
    public string src
    {
        get
        {
            if (!Node.Attributes.TryGetValue("src", out var value)) return string.Empty;
            return Uri.TryCreate(Node.OwningDocument?.BaseUrl, UriKind.Absolute, out var basis) &&
                Uri.TryCreate(basis, value, out var absolute) ? absolute.AbsoluteUri : value;
        }
        set => setAttribute("src", value);
    }

    /// <summary>HTMLImageElement.currentSrc — the URL actually chosen for display (after
    /// &lt;picture&gt;/&lt;source&gt; selection), falling back to the <c>src</c> attribute.</summary>
    public string currentSrc =>
        Node.Attributes.TryGetValue("_currentSrc", out var c) ? c : Node.Attributes.GetValueOrDefault("src", string.Empty);

    public string name
    {
        get => Node.Attributes.GetValueOrDefault("name", string.Empty);
        set => Node.Attributes["name"] = value;
    }

    // ---- HTMLMediaElement (audio/video) — Phase D ----

    private bool IsMedia => Node.TagName is "AUDIO" or "VIDEO";

    /// <summary>Gets the element's media backend, creating + loading it on first use (when
    /// <paramref name="create"/>) — the timeline is decoder-free (<see cref="Media.SimulatedMediaBackend"/>).</summary>
    private Media.IMediaBackend? MediaBackend(bool create)
    {
        if (!IsMedia) return null;
        if (Node.Media is null && create && JsEngine.For(_engine) is { } eng)
        {
            var node = Node;
            var backend = Media.MediaBackends.Create(
                schedule: a => eng.EnqueueMacrotask(a),
                dispatch: evtName => EventDispatcher.DispatchToNode(node, evtName, eng));
            Node.Media = backend;
            backend.Load(currentSrc, MediaDurationHint(), Node.Attributes.ContainsKey("loop"));
        }
        return Node.Media;
    }

    // The simulated backend has no real media, so the duration comes from an optional test hook
    // (data-duration, in seconds) or a small default.
    private double MediaDurationHint() =>
        Node.Attributes.TryGetValue("data-duration", out var d) &&
        double.TryParse(d, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) && v > 0
            ? v : 4.0;

    private bool GetBoolAttr(string n) => Node.Attributes.ContainsKey(n);
    private void SetBoolAttr(string n, bool value) { if (value) Node.Attributes[n] = ""; else Node.Attributes.Remove(n); }

    /// <summary>HTMLMediaElement.play() — begins playback; returns a resolved Promise.</summary>
    public JsValue play()
    {
        MediaBackend(true)?.Play();
        return _engine.Evaluate("Promise.resolve()");
    }
    public void pause() => MediaBackend(false)?.Pause();
    public void load() { Node.Media?.Dispose(); Node.Media = null; MediaBackend(true); }

    public bool paused => MediaBackend(false)?.Paused ?? true;
    public bool ended => MediaBackend(false)?.Ended ?? false;
    public double duration => MediaBackend(false) is { } b ? b.Duration : double.NaN;
    public int readyState => MediaBackend(false)?.ReadyState ?? 0;
    public int networkState => currentSrc.Length > 0 ? 1 : 0;   // IDLE=1 when a source is selected

    public double currentTime
    {
        get => MediaBackend(false)?.CurrentTime ?? 0.0;
        set { var b = MediaBackend(true); if (b != null) b.CurrentTime = value; }
    }
    public double volume
    {
        get => MediaBackend(false)?.Volume ?? 1.0;
        set { var b = MediaBackend(true); if (b != null) b.Volume = value; }
    }
    public bool muted
    {
        get => MediaBackend(false)?.Muted ?? GetBoolAttr("muted");
        set { var b = MediaBackend(true); if (b != null) b.Muted = value; }
    }

    public bool autoplay { get => GetBoolAttr("autoplay"); set => SetBoolAttr("autoplay", value); }
    public bool loop { get => GetBoolAttr("loop"); set => SetBoolAttr("loop", value); }
    public bool controls { get => GetBoolAttr("controls"); set => SetBoolAttr("controls", value); }
    public string preload { get => Node.Attributes.GetValueOrDefault("preload", ""); set => Node.Attributes["preload"] = value; }
    public string poster { get => Node.Attributes.GetValueOrDefault("poster", ""); set => Node.Attributes["poster"] = value; }

    // readyState constants (HTMLMediaElement)
    public int HAVE_NOTHING => 0;
    public int HAVE_METADATA => 1;
    public int HAVE_CURRENT_DATA => 2;
    public int HAVE_FUTURE_DATA => 3;
    public int HAVE_ENOUGH_DATA => 4;

    /// <summary>HTMLMediaElement.canPlayType — "probably" (type + codecs), "maybe", or "".</summary>
    public string canPlayType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type) || !Parser.IsPlayableMediaType(type)) return "";
        return type.Contains("codecs", StringComparison.OrdinalIgnoreCase) ? "probably" : "maybe";
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

    /// <summary>The control's validation message (custom message or a built-in one; empty if valid).</summary>
    public string validationMessage => FormValidation.GetValidationMessage(Node);

    /// <summary>Sets a custom validity message; a non-empty message makes the control invalid.</summary>
    public void setCustomValidity(string message)
    {
        if (string.IsNullOrEmpty(message)) Node.Attributes.Remove("_customValidity");
        else Node.Attributes["_customValidity"] = message;
    }

    /// <summary>HTMLInputElement.files — the chosen files for an &lt;input type=file&gt;
    /// (null on any other element, per the spec).</summary>
    public JsFileList? files =>
        Node.TagName == "INPUT" && Node.Attributes.GetValueOrDefault("type", "text").ToLowerInvariant() == "file"
            ? new JsFileList(FormState.GetFiles(Node.NodeKey))
            : null;

    /// <summary>HTMLFormElement.submit() — submits WITHOUT firing a submit event (per spec).
    /// No-op on non-form elements.</summary>
    public void submit()
    {
        if (Node.TagName != "FORM") return;
        if (JsEngine.For(_engine) is { } engine &&
            FormSubmitter.PrepareNavigation(Node, engine, fireSubmitEvent: false) is { } request)
            engine.RequestNavigation(request);
    }

    /// <summary>HTMLFormElement.requestSubmit() — fires a cancelable submit event first, then
    /// submits only if it wasn't prevented.</summary>
    public void requestSubmit(JsElement? submitter = null)
    {
        if (Node.TagName != "FORM" || JsEngine.For(_engine) is not { } engine) return;
        if (FormSubmitter.PrepareNavigation(Node, engine, fireSubmitEvent: true, submitter: submitter?.Node)
            is { } request)
            engine.RequestNavigation(request);
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
        DomNode is IElement element ? element.GetAttribute(name) :
        Node.Attributes.TryGetValue(name, out var v) ? v : null;

    public void setAttribute(string name, string val)
    {
        var old = Node.Attributes.TryGetValue(name, out var o) ? o : null;
        if (DomNode is IElement element) element.SetAttribute(name, val);
        Node.Attributes[name] = val;
        OnAttributeChanged(Node, name, old);
    }

    public void removeAttribute(string name)
    {
        var old = Node.Attributes.TryGetValue(name, out var o) ? o : null;
        if (DomNode is IElement element) element.RemoveAttribute(name);
        if (!Node.Attributes.Remove(name)) return;
        OnAttributeChanged(Node, name, old);
    }

    /// <summary>Shared attribute-change side effects: re-cascade for selector-affecting attributes
    /// (class/id) and queue a MutationRecord. Used by setAttribute/removeAttribute and Attr.value.</summary>
    internal static void OnAttributeChanged(LayoutNode node, string name, string? oldValue)
    {
        if (node.DomNode is IElement element && !name.StartsWith('_'))
        {
            if (node.Attributes.TryGetValue(name, out var value)) element.SetAttribute(name, value);
            else element.RemoveAttribute(name);
        }
        if (name is "class" or "id") StyleResolver.Apply(node);
        if ((node.OwningDocument?.Engine ?? JsEngine.Instance) is { } eng)
        {
            MutationObserverRegistry.NotifyAttribute(eng.RawEngine, node, name, oldValue);
            if (node.TagName == "DETAILS" && name == "open" &&
                (oldValue is not null) != node.Attributes.ContainsKey("open"))
                QueueDetailsToggle(node, eng);
            if (node.TagName == "IFRAME" && name is "src" or "srcdoc")
                For(eng.RawEngine, node).LoadInsertedIframe();
        }
    }

    internal static void QueueDetailsToggle(LayoutNode node, JsEngine engine)
    {
        var version = ++node.DetailsToggleVersion;
        engine.EnqueueMacrotask(() =>
        {
            // HTML 5.3 aborts an older notification if another is queued after it.
            if (version != node.DetailsToggleVersion) return;
            var evt = new JsEvent();
            evt.Init("toggle", false, false);
            evt.isTrusted = true;
            evt.target = For(engine.RawEngine, node);
            EventDispatcher.DispatchEvent(node, evt, engine);
        });
    }

    /// <summary>Element.setAttributeNode(attr) — sets the named attribute from the Attr's value.</summary>
    public JsAttr? setAttributeNode(JsAttr attr)
    {
        var old = Node.Attributes.TryGetValue(attr.name, out var o) ? o : null;
        Node.Attributes[attr.name] = attr.value;
        OnAttributeChanged(Node, attr.name, old);
        return old is null ? null : new JsAttr(Node, attr.name);
    }

    /// <summary>Element.removeAttributeNode(attr) — removes the attribute the Attr refers to.</summary>
    public JsAttr? removeAttributeNode(JsAttr attr)
    {
        var old = Node.Attributes.TryGetValue(attr.name, out var o) ? o : null;
        Node.Attributes.Remove(attr.name);
        OnAttributeChanged(Node, attr.name, old);
        return attr;
    }

    public bool hasAttribute(string name) => DomNode is IElement element
        ? element.HasAttribute(name) : Node.Attributes.ContainsKey(name);

    /// <summary>NamedNodeMap of this element's attributes (Element.attributes).</summary>
    public JsNamedNodeMap attributes => new(Node);

    /// <summary>True if the element has any attributes (excludes engine-internal keys).</summary>
    public bool hasAttributes() => DomNode is IElement element ? element.Attributes.Length > 0 :
        Node.Attributes.Keys.Any(k => !k.StartsWith('_'));

    /// <summary>The element's attribute names (Element.getAttributeNames()).</summary>
    public string[] getAttributeNames() => DomNode is IElement element
        ? element.Attributes.Select(a => a.Name).ToArray()
        : Node.Attributes.Keys.Where(k => !k.StartsWith('_')).ToArray();

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
            setAttribute(name, "");
        }
        else if (!shouldHave && present)
        {
            removeAttribute(name);
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

    public JsHtmlCollection children => _childrenCollection ??= new JsHtmlCollection(() =>
        DomNode is { } dom ? dom.ChildNodes.OfType<IElement>().Select(Wrap).ToArray() :
        Node.Children.Where(c => c.TagName != "#text").Select(c => For(_engine, c)).ToArray());

    public JsNodeList childNodes => _childNodeList ??= new JsNodeList(() =>
    {
        if (DomNode is { } dom) return dom.ChildNodes.Select(Wrap).ToArray();
        EnsureTextChildMaterialized();
        return Node.Children.Select(c => For(_engine, c)).ToArray();
    });

    public JsElement? parentElement => DomNode is { } dom ?
        dom.Parent is IElement parent ? Wrap(parent) : null :
        Node.Parent is { } p ? For(_engine, p) : null;

    public object? parentNode => DomNode is { } dom ? dom.Parent switch
    {
        IDocument => ownerDocument,
        { } parent => Wrap(parent),
        _ => null
    } : parentElement;

    public JsElement? firstChild
    {
        get
        {
            if (DomNode is { } dom) return dom.FirstChild is { } child ? Wrap(child) : null;
            EnsureTextChildMaterialized();
            return Node.Children.Count > 0 ? JsElement.For(_engine, Node.Children[0]) : null;
        }
    }

    public JsElement? lastChild
    {
        get
        {
            if (DomNode is { } dom) return dom.LastChild is { } child ? Wrap(child) : null;
            EnsureTextChildMaterialized();
            return Node.Children.Count > 0 ? JsElement.For(_engine, Node.Children[^1]) : null;
        }
    }

    public JsElement? firstElementChild => DomNode is { } dom
        ? dom.ChildNodes.OfType<IElement>().FirstOrDefault() is { } first ? Wrap(first) : null
        : Node.Children.FirstOrDefault(c => c.TagName != "#text") is { } n ? For(_engine, n) : null;

    public JsElement? lastElementChild => DomNode is { } dom
        ? dom.ChildNodes.OfType<IElement>().LastOrDefault() is { } last ? Wrap(last) : null
        : Node.Children.LastOrDefault(c => c.TagName != "#text") is { } n ? For(_engine, n) : null;

    public JsElement? nextSibling
    {
        get
        {
            if (DomNode is { } dom) return dom.NextSibling is { } next ? Wrap(next) : null;
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
            if (DomNode is { } dom) return dom.PreviousSibling is { } previous ? Wrap(previous) : null;
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
            if (DomNode is { } dom)
            {
                for (var next = dom.NextSibling; next is not null; next = next.NextSibling)
                    if (next is IElement) return Wrap(next);
                return null;
            }
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
            if (DomNode is { } dom)
            {
                for (var previous = dom.PreviousSibling; previous is not null; previous = previous.PreviousSibling)
                    if (previous is IElement) return Wrap(previous);
                return null;
            }
            if (Node.Parent is null) return null;
            var siblings = Node.Parent.Children;
            var idx = siblings.IndexOf(Node);
            for (int i = idx - 1; i >= 0; i--)
                if (siblings[i].TagName != "#text") return JsElement.For(_engine, siblings[i]);
            return null;
        }
    }

    public int childElementCount => DomNode is { } dom
        ? dom.ChildNodes.OfType<IElement>().Count() : Node.Children.Count(c => c.TagName != "#text");

    // ---- tree mutation ----

    /// <summary>Checks if childNode is an ancestor of parentNode (would create a cycle).</summary>
    private static bool IsAncestor(LayoutNode childNode, LayoutNode parentNode)
    {
        for (var n = parentNode; n != null; n = n.Parent)
            if (n == childNode) return true;
        return false;
    }

    /// <summary>Inserting a &lt;script&gt; into the document executes it (HTML §4.11.1 “prepare the
    /// script element”). Execution is deferred onto the engine's event loop — fetching/running it
    /// synchronously here would re-enter the engine during a host callback. External sources are
    /// fetched through the page session (cookies, UA) like parser-collected scripts. Each queued
    /// execution re-checks connectivity: a later-inserted script removed by an earlier-inserted
    /// script must not run (dom/nodes/insertion-removing-steps).</summary>
    private void ExecuteInsertedScript()
    {
        if (Node.TagName != "SCRIPT") return;
        var js = JsEngine.For(_engine);
        if (js is null) return;
        var type = Node.Attributes.GetValueOrDefault("type");
        if (type?.Equals("module", StringComparison.OrdinalIgnoreCase) == true)
        {
            var specifier = js.ResolveAgainstCurrent(Node.Attributes.GetValueOrDefault("src"));
            if (specifier is not null)
                js.EnqueueMacrotask(() =>
                {
                    if (!For(_engine, Node).isConnected) return;
                    js.ImportModule(specifier);
                });
            return;
        }
        if (Node.Attributes.GetValueOrDefault("src") is { Length: > 0 } src)
        {
            var url = js.ResolveAgainstCurrent(src);
            if (url is null) return;
            var session = js.DocumentState.Session;
            if (session is null) return;
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                string code;
                try { code = await session.Client.GetStringAsync(url).ConfigureAwait(false); }
                catch (Exception ex)
                {
                    session.Diagnostics.Enqueue($"script {url}: {ex.Message}");
                    return;
                }
                js.EnqueueMacrotask(() =>
                {
                    if (!For(_engine, Node).isConnected) return;
                    js.Execute(code, url);
                });
            });
        }
        else if (!string.IsNullOrWhiteSpace(GetTextContentRecursive(Node)))
        {
            var code = GetTextContentRecursive(Node);
            // HTML "prepare a script": a script-inserted inline classic script that is already in
            // a document runs synchronously once the insertion operation completes. Deferring it
            // would let later synchronous code reassign globals the script reads
            // (dom/nodes/insertion-removing-steps). Scripts inserted into detached trees stay on
            // the event loop and re-check connectivity, so attaching the tree later still runs them.
            if (For(_engine, Node).isConnected)
            {
                js.Execute(code, js.DocumentBaseUrl);
                return;
            }
            js.EnqueueMacrotask(() =>
            {
                if (!For(_engine, Node).isConnected) return;
                js.Execute(code, js.DocumentBaseUrl);
            });
        }
    }

    public int tabIndex
    {
        get => int.TryParse(Node.Attributes.GetValueOrDefault("tabindex"), out var value) ? value : -1;
        set => setAttribute("tabindex", value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    public void focus()
    {
        if (!isConnected || JsEngine.For(_engine) is not { } engine) return;
        var state = engine.DocumentState;
        if (ReferenceEquals(state.ActiveElement, Node)) return;
        if (state.ActiveElement is { } previous)
        {
            EventDispatcher.DispatchToNode(previous, "blur", engine);
            EventDispatcher.DispatchToNode(previous, "focusout", engine, bubbles: true);
        }
        state.ActiveElement = Node;
        if (Node.TagName is "INPUT" or "TEXTAREA" or "SELECT")
            FormState.FocusedInput = Node.NodeKey;
        EventDispatcher.DispatchToNode(Node, "focus", engine);
        EventDispatcher.DispatchToNode(Node, "focusin", engine, bubbles: true);
    }

    public void blur()
    {
        if (JsEngine.For(_engine) is not { } engine ||
            !ReferenceEquals(engine.DocumentState.ActiveElement, Node)) return;
        engine.DocumentState.ActiveElement = null;
        if (FormState.FocusedInput == Node.NodeKey) FormState.FocusedInput = null;
        EventDispatcher.DispatchToNode(Node, "blur", engine);
        EventDispatcher.DispatchToNode(Node, "focusout", engine, bubbles: true);
    }

    /// <summary>Navigate an iframe inserted by script after the current JS call returns.</summary>
    private void LoadInsertedIframe()
    {
        if (Node.TagName != "IFRAME" || JsEngine.For(_engine) is not { } parent) return;
        var node = Node;
        var navigationVersion = ++node.FrameNavigationVersion;
        parent.EnqueueMacrotask(() =>
        {
            if (navigationVersion != node.FrameNavigationVersion || !For(_engine, node).isConnected) return;
            var srcdoc = node.Attributes.GetValueOrDefault("srcdoc");
            var src = node.Attributes.GetValueOrDefault("src");
            var baseUrl = parent.DocumentBaseUrl;
            var width = int.TryParse(node.Attributes.GetValueOrDefault("width"), out var w) && w > 0 ? w : 300;
            var height = int.TryParse(node.Attributes.GetValueOrDefault("height"), out var h) && h > 0 ? h : 150;
            node.StyleOverrides.TryAdd("display", "block");
            node.StyleOverrides.TryAdd("width", width + "px");
            node.StyleOverrides.TryAdd("height", height + "px");
            try
            {
                Page child;
                if (srcdoc is not null)
                    child = Parser.ParseChildPage(srcdoc, true, baseUrl, width, height,
                        parent.DocumentState.Session, parent, node);
                else if (!string.IsNullOrWhiteSpace(src) && src.StartsWith("blob:", StringComparison.Ordinal))
                    // blob: URLs resolve through the in-process registry; no relative resolution.
                    child = Parser.ParseChildPage(src, false, src, width, height,
                        parent.DocumentState.Session, parent, node);
                else if (!string.IsNullOrWhiteSpace(src) &&
                    parent.ResolveAgainstCurrent(src) is { } childUrl &&
                    Uri.TryCreate(childUrl, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "blob")
                    child = Parser.ParseChildPage(childUrl, false, childUrl, width, height,
                        parent.DocumentState.Session, parent, node);
                else
                {
                    child = Parser.ParseChildPage("<!doctype html><html><body></body></html>", true,
                        baseUrl, width, height, parent.DocumentState.Session, parent, node);
                    child.IsInitialAboutBlank = true;
                    child.Engine.SetCurrentUrl("about:blank");
                }
                node.ChildPage = child;
                if (!child.IsInitialAboutBlank)
                    EventDispatcher.DispatchToNode(node, "load", parent);
            }
            catch (Exception ex)
            {
                parent.DocumentState.Session?.Diagnostics.Enqueue($"iframe {src}: {ex.Message}");
                Console.WriteLine($"[iframe load] {ex.Message}");
            }
        });
    }

    public JsElement appendChild(JsElement child)
    {
        // CharacterData (Text/Comment/PI) and other leaf node types cannot contain children.
        if (IsCharacterData)
            ThrowDom("HierarchyRequestError", $"A {nodeName} node cannot have children.");
        if (IsAncestor(child.Node, Node))
            throw new InvalidOperationException("The new child element contains the parent.");
        if (DomNode is { } parentDom && child.DomNode is { } childDom)
            parentDom.AppendChild(childDom);
        // Remove from old rendering parent if attached.
        child.Node.Parent?.Children.Remove(child.Node);
        Node.AddChild(child.Node);
        StyleResolver.ApplyTree(child.Node);
        var prev = Node.Children.Count >= 2 ? Node.Children[^2] : null;
        MutationObserverRegistry.NotifyChildList(_engine, Node, [child.Node], null, prev, null);
        child.ExecuteInsertedScript();
        child.LoadInsertedIframe();
        return child;
    }

    public JsElement removeChild(JsElement? child)
    {
        if (child is null) throw new InvalidOperationException("The node to be removed is not a child of this node.");
        if (DomNode is { } parentDom && child.DomNode is { } childDom)
        {
            if (!ReferenceEquals(childDom.Parent, parentDom))
                ThrowDom("NotFoundError", "The node to be removed is not a child of this node.");
            parentDom.RemoveChild(childDom);
        }
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
        if (IsCharacterData)
            ThrowDom("HierarchyRequestError", $"A {nodeName} node cannot have children.");
        if (IsAncestor(newNode.Node, Node))
            throw new InvalidOperationException("The new child element contains the parent.");
        if (DomNode is { } parentDom && newNode.DomNode is { } childDom)
        {
            if (refNode?.DomNode is { } reference && !ReferenceEquals(reference.Parent, parentDom))
                ThrowDom("NotFoundError", "Reference node not found");
            parentDom.InsertBefore(childDom, refNode?.DomNode);
        }
        newNode.Node.Parent?.Children.Remove(newNode.Node);
        if (refNode is null)
        {
            Node.AddChild(newNode.Node);
        }
        else
        {
            var idx = Node.Children.IndexOf(refNode.Node);
            if (idx < 0 && DomNode is null) throw new InvalidOperationException("Reference node not found");
            if (idx < 0) idx = Node.Children.Count;
            newNode.Node.Parent = Node;
            Node.Children.Insert(idx, newNode.Node);
        }
        StyleResolver.ApplyTree(newNode.Node);
        MutationObserverRegistry.NotifyChildList(_engine, Node, [newNode.Node], null, null, refNode?.Node);
        newNode.ExecuteInsertedScript();
        newNode.LoadInsertedIframe();
        return newNode;
    }

    public JsElement replaceChild(JsElement newNode, JsElement oldNode)
    {
        if (IsCharacterData)
            ThrowDom("HierarchyRequestError", $"A {nodeName} node cannot have children.");
        if (IsAncestor(newNode.Node, Node))
            throw new InvalidOperationException("The new child element contains the parent.");
        if (DomNode is { } parentDom && newNode.DomNode is { } newDom && oldNode.DomNode is { } oldDom)
        {
            if (!ReferenceEquals(oldDom.Parent, parentDom))
                ThrowDom("NotFoundError", "Old node not found");
            parentDom.ReplaceChild(newDom, oldDom);
        }
        // Remove newNode from its old parent first (may shift indices)
        newNode.Node.Parent?.Children.Remove(newNode.Node);
        // Re-find idx after potential removal
        var idx = Node.Children.IndexOf(oldNode.Node);
        if (idx < 0 && DomNode is null) throw new InvalidOperationException("Old node not found");
        if (idx < 0) Node.AddChild(newNode.Node);
        else Node.Children[idx] = newNode.Node;
        newNode.Node.Parent = Node;
        oldNode.Node.Parent = null;
        StyleResolver.ApplyTree(newNode.Node);
        newNode.ExecuteInsertedScript();
        newNode.LoadInsertedIframe();
        return oldNode;
    }

    // ---- modern mutation convenience methods (DOM Living Standard) ----

    /// <summary>Appends nodes/strings as the last children of this element.</summary>
    public void append(params JsValue[] items)
    {
            var inserted = new List<LayoutNode>();
            foreach (var (layout, dom) in ToInsertionNodes(items))
            {
                DetachInsertionNode(layout, dom);
                Node.AddChild(layout);
                MirrorAppend(dom);
                StyleResolver.ApplyTree(layout);
                inserted.Add(layout);
            }
            RunInsertedHooks(inserted);
        }

        /// <summary>Inserts nodes/strings as the first children of this element.</summary>
        public void prepend(params JsValue[] items)
        {
            var inserted = new List<LayoutNode>();
            foreach (var (layout, dom) in ToInsertionNodes(items))
            {
                DetachInsertionNode(layout, dom);
                layout.Parent = Node;
                Node.Children.Insert(0, layout);
                MirrorInsertAt(DomNode, dom, Node, 0);
                StyleResolver.ApplyTree(layout);
                inserted.Add(layout);
            }
            RunInsertedHooks(inserted);
        }

        /// <summary>Inserts nodes/strings into this element's parent, just before it. Anchored at the
        /// first preceding sibling not among the inserted nodes (DOM §4.2.8 "viable previous sibling"),
        /// so this node itself may be one of the arguments.</summary>
        public void before(params JsValue[] items) => InsertRelative(ToInsertionNodes(items), before: true);

        /// <summary>Inserts nodes/strings into this element's parent, just after it. Anchored at the
        /// first following sibling not among the inserted nodes (DOM §4.2.8 "viable next sibling").</summary>
        public void after(params JsValue[] items) => InsertRelative(ToInsertionNodes(items), before: false);

        /// <summary>Replaces this element with the given nodes/strings. This node itself may be among
        /// the arguments; the insertion point then falls back to the viable next sibling (DOM §4.2.8).</summary>
        public void replaceWith(params JsValue[] items)
        {
            var parent = Node.Parent;
            if (parent is null) return;
            var nodes = ToInsertionNodes(items);
            var parentDom = parent.DomNode;
            var viableNext = ViableSibling(parent, nodes, before: false);
            foreach (var (layout, dom) in nodes) DetachInsertionNode(layout, dom);
            int idx;
            if (Node.Parent == parent)
            {
                idx = parent.Children.IndexOf(Node);
                parent.Children.RemoveAt(idx);
                Node.Parent = null;
                if (DomNode is { } selfDom) parentDom?.RemoveChild(selfDom);
            }
            else
            {
                // This node was one of the arguments and has already been detached above.
                idx = viableNext is null ? parent.Children.Count : parent.Children.IndexOf(viableNext);
            }
            var inserted = new List<LayoutNode>();
            foreach (var (layout, dom) in nodes)
            {
                InsertLayoutNode(parent, idx, layout, parentDom, dom);
                inserted.Add(layout);
                idx++;
            }
            RunInsertedHooks(inserted);
        }

        private void InsertRelative(List<(LayoutNode Layout, INode? Dom)> nodes, bool before)
        {
            var parent = Node.Parent;
            if (parent is null) return;
            var parentDom = parent.DomNode;
            var viable = ViableSibling(parent, nodes, before);
            foreach (var (layout, dom) in nodes) DetachInsertionNode(layout, dom);
            // One insertion point for the whole list: every node lands before the same reference
            // (this node, or the viable sibling for after()), so argument order is preserved.
            var idx = before
                ? viable is null ? 0 : parent.Children.IndexOf(viable) + 1
                : viable is null ? parent.Children.Count : parent.Children.IndexOf(viable);
            var inserted = new List<LayoutNode>();
            foreach (var (layout, dom) in nodes)
            {
                InsertLayoutNode(parent, idx, layout, parentDom, dom);
                inserted.Add(layout);
                idx++;
            }
            RunInsertedHooks(inserted);
        }

        /// <summary>First sibling on the given side of this node that is not among the inserted
        /// nodes (DOM §4.2.8 "viable previous/next sibling").</summary>
        private LayoutNode? ViableSibling(LayoutNode parent,
            List<(LayoutNode Layout, INode? Dom)> nodes, bool before)
        {
            var self = parent.Children.IndexOf(Node);
            if (before)
            {
                for (var i = self - 1; i >= 0; i--)
                    if (!nodes.Any(n => n.Layout == parent.Children[i])) return parent.Children[i];
            }
            else
            {
                for (var i = self + 1; i < parent.Children.Count; i++)
                    if (!nodes.Any(n => n.Layout == parent.Children[i])) return parent.Children[i];
            }
            return null;
        }

        private static void DetachInsertionNode(LayoutNode layout, INode? dom)
        {
            layout.Parent?.Children.Remove(layout);
            layout.Parent = null;
            if (dom?.Parent is { } domParent) domParent.RemoveChild(dom);
    }

        /// <summary>Inserts a node into the layout tree at <paramref name="idx"/> and mirrors the
        /// same position into the authoritative DOM: before the first sibling at or after
        /// <paramref name="idx"/> that has a DOM node, or appended when none does.</summary>
        private void InsertLayoutNode(LayoutNode parent, int idx, LayoutNode layout,
            INode? parentDom, INode? dom)
        {
            INode? reference = null;
            for (var i = idx; i < parent.Children.Count; i++)
                if (parent.Children[i].DomNode is { } next) { reference = next; break; }
            layout.Parent = parent;
            parent.Children.Insert(idx, layout);
            if (dom is not null && parentDom is not null)
            {
                if (reference is not null) parentDom.InsertBefore(dom, reference);
                else parentDom.AppendChild(dom);
            }
            StyleResolver.ApplyTree(layout);
        }

        private void MirrorAppend(INode? dom)
        {
            if (dom is not null && DomNode is { } parentDom) parentDom.AppendChild(dom);
        }

        /// <summary>Runs the inserted-content hooks (script execution, iframe load) after the whole
        /// insertion operation has completed, so a script executed this way already sees its later
        /// siblings in the document (HTML "prepare a script" ordering).</summary>
        private void RunInsertedHooks(List<LayoutNode> nodes)
        {
            foreach (var layout in nodes)
            {
                var inserted = JsElement.For(_engine, layout);
                inserted.ExecuteInsertedScript();
                inserted.LoadInsertedIframe();
            }
        }

        /// <summary>Inserts <paramref name="dom"/> before the first sibling at or after layout-tree
        /// index <paramref name="idx"/> that has a DOM node (or appends it when none does).</summary>
        private static void MirrorInsertAt(INode? parentDom, INode? dom, LayoutNode parent, int idx)
        {
            if (dom is null || parentDom is null) return;
            INode? reference = null;
            for (var i = idx; i < parent.Children.Count; i++)
                if (parent.Children[i].DomNode is { } next) { reference = next; break; }
            if (reference is not null) parentDom.InsertBefore(dom, reference);
            else parentDom.AppendChild(dom);
        }

        /// <summary>Removes this element from its parent.</summary>
        public void remove()
        {
            DomNode?.Parent?.RemoveChild(DomNode);
            Node.Parent?.Children.Remove(Node);
            Node.Parent = null;
        }

        /// <summary>Coerces append/prepend/before/after/replaceWith arguments (Node or DOMString) into
        /// insertable nodes, carrying each node's authoritative DOM counterpart so both trees can be
        /// mutated together. WebIDL: every non-Node argument stringifies (null → "null", undefined →
        /// "undefined"); a DocumentFragment argument contributes its children; a node passed twice
        /// ends up at its last argument position.</summary>
        private List<(LayoutNode Layout, INode? Dom)> ToInsertionNodes(JsValue[] items)
        {
            var result = new List<(LayoutNode, INode?)>();
            var state = State;
            foreach (var item in items)
            {
                if (item.ToObject() is JsElement element)
                {
                    if (element.Node.TagName == "#document-fragment")
                    {
                        foreach (var child in element.Node.Children.ToList())
                        {
                            result.Remove((child, child.DomNode));
                            result.Add((child, child.DomNode));
                        }
                    }
                    else
                    {
                        result.Remove((element.Node, element.Node.DomNode));
                        result.Add((element.Node, element.Node.DomNode));
                    }
                }
                else
                {
                    // WebIDL (Node or DOMString): every non-Node argument stringifies, including
                    // null → "null" and undefined → "undefined".
                    var text = TypeConverter.ToString(item);
                    LayoutNode textNode;
                    if (state?.Document is { } document)
                    {
                        textNode = state.ForDomNode(document.CreateTextNode(text));
                    }
                    else
                    {
                        textNode = new LayoutNode(null, "#text", text, Node.Style);
                    }
                    textNode.StyleOverrides["display"] = "inline";
                    result.Add((textNode, textNode.DomNode));
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

    /// <summary>DOM Normalizer: merges adjacent #text children and removes empty ones,
    /// keeping the AngleSharp document and the projection in sync.</summary>
    public void normalize() => NormalizeNode(Node);

    internal static void NormalizeNode(LayoutNode node)
    {
        foreach (var child in node.Children) NormalizeNode(child);
        for (var i = node.Children.Count - 1; i > 0; i--)
        {
            var current = node.Children[i];
            var previous = node.Children[i - 1];
            if (current.TagName != "#text" || previous.TagName != "#text") continue;
            var merged = (previous.DomNode?.TextContent ?? previous.TextOverride ?? previous.Text) +
                (current.DomNode?.TextContent ?? current.TextOverride ?? current.Text);
            if (previous.DomNode is not null) previous.DomNode.TextContent = merged;
            previous.TextOverride = merged;
            if (current.DomNode?.Parent is { } domParent) domParent.RemoveChild(current.DomNode);
            node.Children.RemoveAt(i);
        }
        for (var i = node.Children.Count - 1; i >= 0; i--)
        {
            var child = node.Children[i];
            if (child.TagName != "#text") continue;
            var value = child.DomNode?.TextContent ?? child.TextOverride ?? child.Text;
            if (value.Length != 0) continue;
            if (child.DomNode?.Parent is { } domParent) domParent.RemoveChild(child.DomNode);
            node.Children.RemoveAt(i);
        }
    }

    // ---- class list (minimal) ----
    public JsClassList classList => new(Node);

    // ---- dataset (data-* attributes) ----
    private JsDataset? _dataset;
    public JsDataset dataset => _dataset ??= new(Node);

    // ---- events ----
    /// <summary>on* IDL attribute properties (element.onload = fn). Assignment replaces the
    /// previously registered handler for the same event type, per HTML §8.1.5.2.</summary>
    public JsValue? onload { get => GetOnProperty("onload"); set => SetOnProperty("onload", value); }
    public JsValue? onerror { get => GetOnProperty("onerror"); set => SetOnProperty("onerror", value); }
    public JsValue? onclick { get => GetOnProperty("onclick"); set => SetOnProperty("onclick", value); }
    public JsValue? ondblclick { get => GetOnProperty("ondblclick"); set => SetOnProperty("ondblclick", value); }
    public JsValue? onmousedown { get => GetOnProperty("onmousedown"); set => SetOnProperty("onmousedown", value); }
    public JsValue? onmouseup { get => GetOnProperty("onmouseup"); set => SetOnProperty("onmouseup", value); }
    public JsValue? onmouseover { get => GetOnProperty("onmouseover"); set => SetOnProperty("onmouseover", value); }
    public JsValue? onmouseout { get => GetOnProperty("onmouseout"); set => SetOnProperty("onmouseout", value); }
    public JsValue? onmousemove { get => GetOnProperty("onmousemove"); set => SetOnProperty("onmousemove", value); }
    public JsValue? onkeydown { get => GetOnProperty("onkeydown"); set => SetOnProperty("onkeydown", value); }
    public JsValue? onkeyup { get => GetOnProperty("onkeyup"); set => SetOnProperty("onkeyup", value); }
    public JsValue? onkeypress { get => GetOnProperty("onkeypress"); set => SetOnProperty("onkeypress", value); }
    public JsValue? onfocus { get => GetOnProperty("onfocus"); set => SetOnProperty("onfocus", value); }
    public JsValue? onblur { get => GetOnProperty("onblur"); set => SetOnProperty("onblur", value); }
    public JsValue? onchange { get => GetOnProperty("onchange"); set => SetOnProperty("onchange", value); }
    public JsValue? oninput { get => GetOnProperty("oninput"); set => SetOnProperty("oninput", value); }
    public JsValue? onsubmit { get => GetOnProperty("onsubmit"); set => SetOnProperty("onsubmit", value); }
    public JsValue? onreset { get => GetOnProperty("onreset"); set => SetOnProperty("onreset", value); }
    public JsValue? onselect { get => GetOnProperty("onselect"); set => SetOnProperty("onselect", value); }
    public JsValue? onscroll { get => GetOnProperty("onscroll"); set => SetOnProperty("onscroll", value); }
    public JsValue? onresize { get => GetOnProperty("onresize"); set => SetOnProperty("onresize", value); }
    public JsValue? oncontextmenu { get => GetOnProperty("oncontextmenu"); set => SetOnProperty("oncontextmenu", value); }
    public JsValue? onwheel { get => GetOnProperty("onwheel"); set => SetOnProperty("onwheel", value); }
    public JsValue? onabort { get => GetOnProperty("onabort"); set => SetOnProperty("onabort", value); }
    public JsValue? oncanplay { get => GetOnProperty("oncanplay"); set => SetOnProperty("oncanplay", value); }
    public JsValue? ondurationchange { get => GetOnProperty("ondurationchange"); set => SetOnProperty("ondurationchange", value); }
    public JsValue? onemptied { get => GetOnProperty("onemptied"); set => SetOnProperty("onemptied", value); }
    public JsValue? onended { get => GetOnProperty("onended"); set => SetOnProperty("onended", value); }
    public JsValue? onloadeddata { get => GetOnProperty("onloadeddata"); set => SetOnProperty("onloadeddata", value); }
    public JsValue? onloadedmetadata { get => GetOnProperty("onloadedmetadata"); set => SetOnProperty("onloadedmetadata", value); }
    public JsValue? onloadstart { get => GetOnProperty("onloadstart"); set => SetOnProperty("onloadstart", value); }
    public JsValue? onpause { get => GetOnProperty("onpause"); set => SetOnProperty("onpause", value); }
    public JsValue? onplay { get => GetOnProperty("onplay"); set => SetOnProperty("onplay", value); }
    public JsValue? onplaying { get => GetOnProperty("onplaying"); set => SetOnProperty("onplaying", value); }
    public JsValue? onprogress { get => GetOnProperty("onprogress"); set => SetOnProperty("onprogress", value); }
    public JsValue? onratechange { get => GetOnProperty("onratechange"); set => SetOnProperty("onratechange", value); }
    public JsValue? onseeked { get => GetOnProperty("onseeked"); set => SetOnProperty("onseeked", value); }
    public JsValue? onseeking { get => GetOnProperty("onseeking"); set => SetOnProperty("onseeking", value); }
    public JsValue? onstalled { get => GetOnProperty("onstalled"); set => SetOnProperty("onstalled", value); }
    public JsValue? onsuspend { get => GetOnProperty("onsuspend"); set => SetOnProperty("onsuspend", value); }
    public JsValue? ontimeupdate { get => GetOnProperty("ontimeupdate"); set => SetOnProperty("ontimeupdate", value); }
    public JsValue? onvolumechange { get => GetOnProperty("onvolumechange"); set => SetOnProperty("onvolumechange", value); }
    public JsValue? onwaiting { get => GetOnProperty("onwaiting"); set => SetOnProperty("onwaiting", value); }
    public JsValue? ontoggle { get => GetOnProperty("ontoggle"); set => SetOnProperty("ontoggle", value); }

    internal JsValue? GetOnProperty(string name) =>
        Node.OnProperties?.TryGetValue(name, out var handler) == true ? handler : null;

    internal void SetOnProperty(string name, JsValue? value)
    {
        Node.OnProperties ??= [];
        if (Node.OnProperties.Remove(name, out var previous) && previous is not null)
            Node.EventListeners.RemoveAll(e => ReferenceEquals(e.Handler, previous));
        if (value is null || value.IsUndefined()) return;
        Node.OnProperties[name] = value;
        Node.EventListeners.Add(new EventListenerEntry(name[2..], value, null, Capture: false));
    }

    public void addEventListener(string type, JsValue handler, JsValue? options = null)
    {
        bool capture = false, once = false;
        if (options is not null)
        {
            if (options.IsBoolean()) capture = options.AsBoolean();
            else if (options.IsObject())
            {
                var o = options.AsObject();
                var captureVal = o.Get("capture");
                if (captureVal.IsBoolean()) capture = captureVal.AsBoolean();
                var onceVal = o.Get("once");
                if (onceVal.IsBoolean()) once = onceVal.AsBoolean();
            }
        }
        // Event types are case-sensitive (DOM §2.7) — "DOMContentLoaded" must not be folded.
        Node.EventListeners.Add(new EventListenerEntry(type, handler, null, capture, once));
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
        Node.EventListeners.RemoveAll(l =>
            l.EventType == type && l.Capture == capture && l.Handler == handler);
    }

    // ---- dispatchEvent ----
    public bool dispatchEvent(JsEvent? evt = null)
    {
        if (evt is null)
            throw JsErrors.Native("TypeError",
                "Failed to execute 'dispatchEvent': parameter 1 is not of type 'Event'.");
        if (!evt.Initialized)
            throw JsErrors.Dom("InvalidStateError",
                "Failed to execute 'dispatchEvent': The event provided is uninitialized.");
        evt.target = this;
        if (JsEngine.For(_engine) is { } engine)
            EventDispatcher.DispatchEvent(Node, evt, engine);
        return !evt.DefaultPrevented;
    }

    // ---- click ----
    public void click()
    {
        var evt = new JsEvent();
        evt.Init("click", true, true);
        dispatchEvent(evt);
    }

    // ---- querySelector on element ----
    public JsElement? querySelector(string selector) => DomNode is IElement element
        ? element.QuerySelector(selector) is { } found ? Wrap(found) : null
        : SelectorEngine.QuerySelector(Node, selector, _engine);

    public JsNodeList querySelectorAll(string selector)
    {
        var snapshot = DomNode is IElement element
            ? element.QuerySelectorAll(selector).Select(Wrap).ToArray()
            : SelectorEngine.QuerySelectorAll(Node, selector, _engine);
        return new JsNodeList(() => snapshot);
    }

    public JsHtmlCollection getElementsByTagName(string tagName)
    {
        var tag = tagName.ToUpperInvariant();
        return new JsHtmlCollection(() => DomNode is IElement element
            ? element.QuerySelectorAll("*").Where(e => tag == "*" || e.TagName.ToUpperInvariant() == tag)
                .Select(Wrap).ToArray()
            : FindAll(Node, n => tag == "*" || n.TagName == tag)
                .Select(n => For(_engine, n)).ToArray());
    }

    public JsHtmlCollection getElementsByClassName(string classNames)
    {
        var classes = DomWhitespace.Split(classNames);
        return new JsHtmlCollection(() => DomNode is IElement element
            ? element.QuerySelectorAll("*").Where(e => classes.All(c =>
                DomWhitespace.Split(e.GetAttribute("class") ?? "").Contains(c)))
                .Select(Wrap).ToArray()
            : FindAll(Node, n =>
        {
            var nodeClasses = DomWhitespace.Split(n.Attributes.GetValueOrDefault("class", ""));
            return classes.All(c => nodeClasses.Contains(c));
        }).Select(n => For(_engine, n)).ToArray());
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
    private void EnsureLayout() => JsEngine.For(_engine)?.EnsureLayout();

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

    private string[] GetClasses() => DomWhitespace.Split(_node.Attributes.GetValueOrDefault("class", ""));
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

/// <summary>Proxy target for element.dataset — maps data-* attributes to camelCase properties
/// (HTML §2.7.3 DOMStringMap). Property access, deletion and enumeration are implemented by a
/// JS proxy in <see cref="Runtime.QuickJsInterop"/>; this class supplies the underlying lookups.</summary>
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

    /// <summary>Removes the data-* attribute for a camelCase key (delete dataset.key).</summary>
    public void remove(string key) => _node.Attributes.Remove("data-" + CamelToKebab(key));

    /// <summary>CamelCase property names of every data-* attribute, in attribute order.</summary>
    public string[] keys() => _node.Attributes.Keys
        .Where(k => k.StartsWith("data-", StringComparison.Ordinal) && k.Length > 5)
        .Select(k => KebabToCamel(k[5..])).ToArray();

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

    internal static string KebabToCamel(string attribute)
    {
        var sb = new System.Text.StringBuilder();
        var afterDash = false;
        foreach (var c in attribute)
        {
            if (c == '-') { afterDash = true; continue; }
            sb.Append(afterDash && char.IsAsciiLetterLower(c) ? char.ToUpperInvariant(c) : c);
            afterDash = false;
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
