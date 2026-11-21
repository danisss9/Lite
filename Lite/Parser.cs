using AngleSharp.Io;
using AngleSharp;
using AngleSharp.Css;
using AngleSharp.Dom;
using AngleSharp.Css.Dom;
using AngleSharp.Css.Values;
using AngleSharp.Scripting;
using AngleSharp.Xml;
using Lite.Animation;
using Lite.Extensions;
using Lite.Layout;
using Lite.Models;
using Lite.Network;
using Lite.Scripting;
using Lite.Scripting.Dom;
using System.Text;
using System.Text.RegularExpressions;

namespace Lite;

internal static partial class Parser
{
    static Parser()
    {
        // EncodingSniffer registers this for byte decoding; unregistering AngleSharp's
        // EncodingMetaHandler (which also registered it) must not leave a fragment-only
        // parse without the legacy code pages.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    private const string UserAgentStylesheet = """
        div, section, article, header, footer, main, nav, aside, form, ul, ol, li, fieldset, figure, figcaption, address, details, summary, dialog { display: block; }
        label { display: inline; }
        center { display: block; text-align: center; }
        center table { margin-left: auto; margin-right: auto; }
        body { display: block; margin: 8px; }
        h1 { display: block; font-size: 2em; margin-top: 0.67em; margin-bottom: 0.67em; margin-left: 0px; margin-right: 0px; font-weight: bold; }
        h2 { display: block; font-size: 1.5em; margin-top: 0.83em; margin-bottom: 0.83em; margin-left: 0px; margin-right: 0px; font-weight: bold; }
        h3 { display: block; font-size: 1.17em; margin-top: 1em; margin-bottom: 1em; margin-left: 0px; margin-right: 0px; font-weight: bold; }
        h4 { display: block; font-size: 1em; margin-top: 1.33em; margin-bottom: 1.33em; margin-left: 0px; margin-right: 0px; font-weight: bold; }
        h5 { display: block; font-size: 0.83em; margin-top: 1.67em; margin-bottom: 1.67em; margin-left: 0px; margin-right: 0px; font-weight: bold; }
        h6 { display: block; font-size: 0.67em; margin-top: 2.33em; margin-bottom: 2.33em; margin-left: 0px; margin-right: 0px; font-weight: bold; }
        p { display: block; margin-top: 1em; margin-bottom: 1em; margin-left: 0px; margin-right: 0px; cursor: text; }
        h1, h2, h3, h4, h5, h6 { cursor: text; }
        a[href] { color: blue; text-decoration: underline; cursor: pointer; }
        img { display: inline; }
        marquee { display: inline-block; overflow: hidden !important; }
        input { display: inline-block; cursor: text; }
        input[type="checkbox"] { cursor: default; }
        button { display: inline-block; cursor: pointer; border-top-width: 1px; border-right-width: 1px; border-bottom-width: 1px; border-left-width: 1px; border-top-color: #a0a0a0; border-right-color: #a0a0a0; border-bottom-color: #a0a0a0; border-left-color: #a0a0a0; border-top-style: solid; border-right-style: solid; border-bottom-style: solid; border-left-style: solid; }
        strong, b { font-weight: bold; }
        em, i, cite, dfn { font-style: italic; }
        u, ins { text-decoration: underline; }
        s, del, strike { text-decoration: line-through; }
        small { font-size: 0.83em; }
        sub { font-size: 0.83em; vertical-align: sub; }
        sup { font-size: 0.83em; vertical-align: super; }
        mark { background-color: yellow; color: black; }
        code, kbd, samp, var, tt { font-family: monospace; }
        pre { display: block; font-family: monospace; white-space: pre; margin-top: 1em; margin-bottom: 1em; }
        blockquote { display: block; margin-top: 1em; margin-bottom: 1em; margin-left: 40px; margin-right: 40px; }
        hr { display: block; border-top-width: 1px; border-top-color: gray; margin-top: 0.5em; margin-bottom: 0.5em; }
        br { display: inline; }
        dl { display: block; margin-top: 1em; margin-bottom: 1em; }
        dt { display: block; font-weight: bold; }
        dd { display: block; margin-left: 40px; }
        ul, dir, menu { display: block; list-style-type: disc; margin-top: 1em; margin-bottom: 1em; margin-left: 0px; padding-left: 40px; }
        ol { list-style-type: decimal; margin-top: 1em; margin-bottom: 1em; margin-left: 0px; padding-left: 40px; }
        ul ul, ul ol, ol ul, ol ol { margin-top: 0px; margin-bottom: 0px; }
        form { display: block; margin-top: 0px; margin-bottom: 1em; }
        fieldset { margin-top: 0px; margin-bottom: 0px; margin-left: 2px; margin-right: 2px; }
        li { display: list-item; }
        table { display: table; border-collapse: separate; }
        thead { display: table-header-group; }
        tbody { display: table-row-group; }
        tfoot { display: table-footer-group; }
        tr { display: table-row; }
        td { display: table-cell; padding-top: 1px; padding-right: 1px; padding-bottom: 1px; padding-left: 1px; }
        th { display: table-cell; font-weight: bold; padding-top: 1px; padding-right: 1px; padding-bottom: 1px; padding-left: 1px; }
        textarea { display: inline-block; cursor: text; font-family: monospace; font-size: 13px; }
        select { display: inline-block; cursor: pointer; }
        progress { display: inline-block; }
        meter { display: inline-block; }
        datalist { display: none; }
        output { display: inline; }
        input[type="radio"] { cursor: pointer; }
        input[type="password"] { cursor: text; }
        input[type="number"] { cursor: text; }
        input[type="range"] { cursor: pointer; }
        abbr { text-decoration: underline; }
        address { display: block; font-style: italic; }
        q::before { content: open-quote; }
        q::after { content: close-quote; }
        head, script, style, noscript, meta, link, title, base { display: none; }
        """;

    // Tags that should not appear in the layout tree.
    // NOTE: TEMPLATE is intentionally NOT here — a <template> element IS in the DOM (so JS can reach
    // it), but it renders nothing (display:none) and its parsed content lives in an inert fragment
    // exposed as template.content (see the TEMPLATE clause in Traverse). HEAD and SCRIPT follow the
    // same rule: they are real DOM nodes (document.head, document.currentScript, script-element
    // bootstraps like getElementsByTagName('script')[0].parentNode.insertBefore) that never render.
    // SkipTags now only means "projects into the DOM but renders nothing": the tags below ARE
    // projected (tree-construction fidelity, document.head.children, …) and are kept invisible
    // through the UA stylesheet's display:none rules.
    private static readonly HashSet<string> SkipTags =
        ["STYLE", "NOSCRIPT", "META", "LINK", "TITLE"];

    /// <summary>The parser configuration every HTML parse path shares. Registers a scripting
    /// service so AngleSharp parses with the scripting flag ON (REC §8.2.5: &lt;noscript&gt;
    /// content parses as raw text and the in-head-noscript rules apply — the parse-fidelity
    /// corpus runs scripting-enabled). Lite executes scripts through its own loader; the
    /// service's evaluation entry point deliberately no-ops.
    /// The parse always receives an already-decoded Unicode string, so AngleSharp's
    /// <see cref="AngleSharp.Browser.EncodingMetaHandler"/> must not run: it treats a
    /// &lt;meta charset&gt; declaration as an encoding change over the re-encoded UTF-8 bytes,
    /// which either mojibakes non-ASCII text or forces the engine to rewrite the author's
    /// declaration before parsing (the html5lib tests19 cases measured that rewrite as a
    /// pipeline tree deviation). Encoding sniffing belongs to <see cref="EncodingSniffer"/>,
    /// before the string pipeline starts.</summary>
    internal static IConfiguration ParseConfiguration(int width, int height) => BaseParseConfiguration()
        .WithRenderDevice(new DefaultRenderDevice
        {
            DeviceWidth = width,
            DeviceHeight = height,
            ViewPortWidth = width,
            ViewPortHeight = height
        });

    /// <summary>Device-less variant for pre-page fragment parses (see ParseFragmentCore).</summary>
    private static IConfiguration ParseConfiguration() => BaseParseConfiguration().WithRenderDevice();

    private static IConfiguration BaseParseConfiguration() => Configuration.Default
        .Without<AngleSharp.Browser.IMetaHandler>()
        .WithCss()
        .With(new ScriptingFlagOnlyService());

    private sealed class ScriptingFlagOnlyService : IScriptingService
    {
        public bool IsScripting => true;

        public bool SupportsType(string mimeType) => mimeType is "text/javascript" or "application/javascript"
            or "application/x-javascript" or "application/ecmascript" or "text/ecmascript" or "module";

        public Task EvaluateScriptAsync(IResponse response, ScriptOptions options,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    internal sealed record ScriptRecord(string? Code, string Url, bool IsModule = false, bool Inline = false,
        string? CrossOrigin = null, string? RequestedUrl = null, LayoutNode? Node = null,
        string? Continuation = null);

    internal sealed class ParseState
    {
        internal BrowserSession? Session;
        internal string? BaseUrl, DocumentBaseUrl;
        /// <summary>The exact source text handed to the DOM parser: document.write needs the
        /// raw suffix that follows a parser-inserted script (the parse-time writes splice
        /// into the live token stream).</summary>
        internal string? Source;
        /// <summary>The canonical name of the encoding the document bytes were decoded with
        /// (Encoding Standard name column): document.characterSet/inputEncoding/charset.</summary>
        internal string Charset = EncodingNames.Utf8;
        /// <summary>The response MIME type (see DocumentState.MediaType).</summary>
        internal string? MediaType;
        /// <summary>The response's Content-Language header: the document language used by
        /// ':lang()' when no lang attribute is in scope (CSS 2.1 §5.11.4).</summary>
        internal string? ContentLanguage;
        /// <summary>Cursor into <see cref="Source"/>: scripts are collected in document order,
        /// so each one consumes the next source span (its start tag through its end tag).</summary>
        internal int SourceCursor;
        internal IDocument? Document;
        internal int ViewportWidth = 800, ViewportHeight = 600, InlineModuleCounter;
        /// <summary>§12.3.2 quote nesting depth, advanced in document order by every
        /// open-quote/no-open-quote and close-quote/no-close-quote.</summary>
        internal int QuoteDepth;
        internal bool Verbose, IsFragment;
        /// <summary>True when the document was served as XML (application/xhtml+xml and
        /// friends): type selectors match case-sensitively per CSS 2.1 §5.1.</summary>
        internal bool IsXmlDocument;
        internal readonly List<ScriptRecord> PendingScripts = [], DeferredScripts = [], AsyncScripts = [];
        /// <summary>The sandboxing flag set this page inherited from its hosting frame (empty
        /// when unsandboxed) — nested iframes union it into their own parsed set.</summary>
        internal HashSet<string> InheritedSandbox = new(StringComparer.Ordinal);
        internal readonly Dictionary<string, List<int>> Counters = new();
        /// <summary>The counter instances each open element pushed (innermost last); an implicit
        /// counter-reset (§12.4.1) joins the enclosing element's list, so it ends with the
        /// scope of the element's parent rather than living for the rest of the document.</summary>
        internal readonly Stack<List<string>> CounterScopes = new();
        internal readonly List<CssRule> CssRules = [];
        internal readonly Dictionary<string, (string Value, int Count)> RawBackgrounds = new(StringComparer.Ordinal);
        internal readonly Dictionary<string, (string Value, int Count)> RawListStyles = new(StringComparer.Ordinal);
        internal readonly List<(string Selector, ICssStyleDeclaration Style)> PseudoElementRules = [];
        /// <summary>The injected UA style sheet element and the host-provided user style sheet
        /// elements: they decide each collected rule's cascade origin (CSS 2.1 §6.4.1).</summary>
        internal IElement? UaStyleElement;
        internal List<IElement> UserStyleElements = [];
        /// <summary>Style sheets suspended out of the cascade (style.disabled, host controls):
        /// their text is parked here while the element renders an empty sheet. AngleSharp drops
        /// disabled sheets from its collection but keeps serving cached computed reports, so
        /// suspension must change the sheet content the cache is keyed on.</summary>
        internal Dictionary<IElement, string> SuspendedSheetTexts = new();
        internal HashSet<IElement> SuspendedSheets = [];
        /// <summary>The rules of each suspended sheet, captured at suspension time: the
        /// recascade retracts exactly the properties these rules set on each element, because
        /// the AngleSharp style store never invalidates its parse-time computed reports.</summary>
        internal Dictionary<IElement, List<CssRule>> SuspendedRuleSnapshots = new();
    }

    [ThreadStatic] private static ParseState? _current;
    private static ParseState Current { get => _current ??= new(); set => _current = value; }
    /// <summary>Whether the document being parsed was served as XML; type selectors match
    /// case-sensitively there (CSS 2.1 §5.1). Read by the selector engine.</summary>
    internal static bool IsXmlDocument => Current.IsXmlDocument;
    private static string? _baseUrl { get => Current.BaseUrl; set => Current.BaseUrl = value; }
    internal static string? BaseUrl => _baseUrl;
    /// <summary>Base URL for resolving relative references — equals the document URL unless
    /// a &lt;base href&gt; element overrides it.</summary>
    private static string? _documentBaseUrl { get => Current.DocumentBaseUrl; set => Current.DocumentBaseUrl = value; }
    // Classic scripts that run "in document position" during parse: inline scripts and external
    // (src) scripts without defer/async. Executed in document order, before deferred/async.
    private static List<ScriptRecord> _pendingScripts => Current.PendingScripts;
    // External classic scripts marked `defer` — executed after parsing, in document order.
    private static List<ScriptRecord> _deferredScripts => Current.DeferredScripts;
    // External classic scripts marked `async` — executed on the task queue, not in any order.
    private static List<ScriptRecord> _asyncScripts => Current.AsyncScripts;
    private static int _inlineModuleCounter { get => Current.InlineModuleCounter; set => Current.InlineModuleCounter = value; }
    private static BrowserSession Session => Current.Session ??= new BrowserSession();

    /// <summary>The live AngleSharp document from the last page load, kept alive so that
    /// innerHTML fragments can be parsed with the page's full stylesheet cascade.</summary>
    internal static IDocument? Document { get => Current.Document; private set => Current.Document = value; }


    /// <summary>Suppresses per-element debug logging during fragment (innerHTML) parsing.</summary>
    private static bool _verbose { get => Current.Verbose; set => Current.Verbose = value; }
    internal static int ViewportWidth { get => Current.ViewportWidth; private set => Current.ViewportWidth = value; }
    internal static int ViewportHeight { get => Current.ViewportHeight; private set => Current.ViewportHeight = value; }

    // CSS counter state maintained during document-order traversal.
    // A stack per counter name (CSS 2.1 §12.4): counter-reset pushes a new nested instance,
    // popped when its element's subtree is left; counters(name, sep) joins the whole stack.
    private static Dictionary<string, List<int>> _counters => Current.Counters;

    internal static LayoutNode TraverseHtml(string address, int viewportWidth = 800, int viewportHeight = 600)
        => TraversePage(new NavigationRequest(address), viewportWidth, viewportHeight).Root;

    /// <summary>Loads a document through the window's HTTP session, preserving cookies on redirects.</summary>
    internal static Page TraversePage(NavigationRequest request, int viewportWidth = 800, int viewportHeight = 600,
        BrowserSession? session = null, Lite.Scripting.Dom.SessionHistory? sessionHistory = null)
    {
        Current = new ParseState { Session = session ?? new BrowserSession() };
        _baseUrl = request.Url;
        _documentBaseUrl = request.Url;
        _pendingScripts.Clear();
        _deferredScripts.Clear();
        _asyncScripts.Clear();
        _inlineModuleCounter = 0;
        _counters.Clear();
        ViewportWidth = viewportWidth;
        ViewportHeight = viewportHeight;

        var config = ParseConfiguration(viewportWidth, viewportHeight).WithXml();

        using var message = new HttpRequestMessage(new System.Net.Http.HttpMethod(request.Method), request.Url);
        if (request.Body is not null)
        {
            var body = new ByteArrayContent(Encoding.UTF8.GetBytes(request.Body));
            body.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(
                request.ContentType ?? "application/x-www-form-urlencoded");
            message.Content = body;
        }
        using var response = Session.Client.Send(message);
        var address = response.RequestMessage?.RequestUri?.AbsoluteUri ?? request.Url;
        // CSS 2.1 §5.11.4: the document language may come from the HTTP headers; it is the
        // fallback for ':lang()' on elements with no lang attribute in scope.
        Current.ContentLanguage = response.Content.Headers.ContentLanguage.FirstOrDefault();
        // REC §8.2.2.1: the document bytes are decoded by the encoding-sniffing algorithm
        // (BOM, then the transport charset, then the 1024-byte meta prescan, then Lite's
        // implementation-defined UTF-8 default, with the late-declaration restart).
        var (html, charset) = EncodingSniffer.DecodeDocumentWithEncoding(
            response.Content.ReadAsByteArrayAsync().Result,
            response.Content.Headers.ContentType?.CharSet);
        Current.Charset = charset;
        if (!response.IsSuccessStatusCode)
        {
            // A browser renders the server's error body (search engines rate-limit with HTML
            // interstitials) instead of failing the load; surface the status as a diagnostic.
            Session.Diagnostics.Enqueue($"http {(int)response.StatusCode} {address}");
            Console.WriteLine($"[HTTP {(int)response.StatusCode}] {address}");
            if (html.Length == 0)
                response.EnsureSuccessStatusCode();
        }
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        Current.MediaType = mediaType;
        Current.IsXmlDocument = mediaType is "application/xhtml+xml" or "text/xml" or "application/xml";
        // The XML parser mirrors the HTML one: it re-encodes the string content and honors
        // the XML declaration's encoding pseudo-attribute over those bytes. The string was
        // already decoded with the transport charset (§4.4: HTTP wins), so the declaration
        // now describes OUR bytes — align it with UTF-8 or a non-UTF-8 transport charset
        // double-decodes every non-ASCII attribute (character-encoding-003..016). The HTML
        // parser's <meta charset> handler is unregistered in ParseConfiguration for the same
        // reason, so the author's declaration is left intact in the tree.
        if (Current.IsXmlDocument)
            html = AlignXmlDeclarationCharset(html, "utf-8");
        var document = OpenDocument(config, response, address, html, mediaType);
        _baseUrl = address;
        _documentBaseUrl = address;
        return ParseOpenedDocument(document, address, viewportWidth, viewportHeight, sessionHistory: sessionHistory);
    }

    /// <summary>Opens the response body as a document, dispatching on the response's MIME
    /// type: XML types (XHTML 1.0 tests serve application/xhtml+xml) parse with the XML
    /// parser, whose case sensitivity, CDATA style text, self-closing syntax, and
    /// well-formedness handling all differ from the HTML parser. Everything else parses
    /// as HTML. The request's MIME type must be restated because Content() defaults it to
    /// text/html and the document factory dispatches on it.</summary>
    private static IDocument OpenDocument(IConfiguration config, HttpResponseMessage response,
        string address, string html, string? mediaType)
    {
        html = NormalizeImportantBangs(html);
        Current.Source = html;
        Current.SourceCursor = 0;
        var context = BrowsingContext.New(config.WithOnly<IDocumentFactory>(new XmlDispatchDocumentFactory()));
        return context.OpenAsync(req =>
        {
            req.Address(address);
            req.Content(html);
            if (mediaType is not null)
                req.Header("Content-Type", mediaType);
        }).Result;
    }

    /// <summary>The child-document variant of <see cref="OpenDocument"/>: the context is already
    /// built (an XML media type selects the XML parser).</summary>
    private static IDocument OpenDocument(IBrowsingContext context, string address, string html, string? mediaType) =>
        context.OpenAsync(req =>
        {
            req.Address(address);
            req.Content(html);
            if (mediaType is not null)
                req.Header("Content-Type", mediaType);
        }).Result;

    private static Page ParseOpenedDocument(IDocument document, string address, int viewportWidth, int viewportHeight,
        JsEngine? parentEngine = null, LayoutNode? frameNode = null, HashSet<string>? sandboxFlags = null,
        Lite.Scripting.Dom.SessionHistory? sessionHistory = null)
    {
        Document = document;

        // <base href> overrides the base used for resolving relative URLs (not the document URL).
        if (document.QuerySelector("base[href]")?.GetAttribute("href") is { Length: > 0 } baseHref &&
            Uri.TryCreate(new Uri(address), baseHref, out var resolvedBase))
            _documentBaseUrl = resolvedBase.AbsoluteUri;

        var head = document.Head ?? document.DocumentElement;

        // Track the UA and user style sheets that InjectOriginSheetsDom inserted into the
        // parsed document: every sheet is registered before CollectCssRules, so the document
        // order the cascade sees matches the DOM (UA < user < author). The marker attributes
        // come off once tracked so the DOM carries no engine fingerprints.
        InjectOriginSheetsDom(document);
        foreach (var el in document.QuerySelectorAll("style[data-lite-origin]").ToList())
        {
            var origin = el.GetAttribute("data-lite-origin");
            if (origin == "ua") Current.UaStyleElement = el;
            else if (origin == "user") Current.UserStyleElements.Add(el);
            MarkCascadeOrigin(el);
            el.RemoveAttribute("data-lite-origin");
        }

        // The user may disable author influence entirely (a normative UA conformance point) and
        // may select a preferred named set among alternate style sheets (HTML4 §14.3): both are
        // expressed by disabling sheets, which the cascade and CollectCssRules honor.
        ApplyUserAgentSheetControls(document);

        // Eagerly fetch and inline all <link rel="stylesheet"> files so that
        // ComputeCurrentStyle() sees the fully-cascaded styles synchronously. The rel is a
        // space-separated token set (HTML4 §14.3 / §6.12): alternate stylesheets belong to a
        // named set — the selected set's sheets are fetched and applied, every other
        // alternate is skipped (ApplyUserAgentSheetControls also disables its CSSOM sheet).
        foreach (var link in document.QuerySelectorAll("link[rel~='stylesheet']"))
        {
            var rel = link.GetAttribute("rel") ?? "";
            var isAlternate = rel.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Any(t => t.Equals("alternate", StringComparison.OrdinalIgnoreCase));
            if (isAlternate)
            {
                var selectedSet = Session?.SelectedStyleSheetSet;
                var title = link.GetAttribute("title");
                if (selectedSet is not { Length: > 0 } ||
                    string.IsNullOrEmpty(title) ||
                    !title.Equals(selectedSet, StringComparison.OrdinalIgnoreCase))
                    continue;
            }
            var href = link.GetAttribute("href");
            if (string.IsNullOrEmpty(href)) continue;
            try
            {
                string css;
                string cssBase;
                // The referrer charset is the encoding the ENGINE decided for the document
                // (Current.Charset) - not AngleSharp's IDocument.CharacterSet, which for XML
                // servings reports the XML declaration's own label, leaving the sheet decoding
                // a different encoding than the document's class attribute was read with
                // (character-encoding-010/011's selector mismatch).
                var sheetCharset = Current.Charset;
                if (DataUri.IsDataUri(href))
                {
                    if (!DataUri.TryDecodeText(href, out css, out _)) continue;
                    cssBase = _documentBaseUrl ?? _baseUrl ?? "";
                }
                else
                {
                    var cssUrl = ResolveUrl(href);
                    if (cssUrl is null) continue;
                    // §4.4 also consults the linking element's charset attribute and, failing
                    // that, the referring document's own encoding.
                    css = FetchCssText(cssUrl, link.GetAttribute("charset"), Current.Charset,
                                       out sheetCharset);
                    cssBase = cssUrl;
                }
                css = InlineImports(css, cssBase, sheetCharset);
                var styleEl = document.CreateElement("style");
                styleEl.TextContent = css;
                // Keep the sheet at the LINK's position in document order (§6.4.1 orders equal
                // origins by source order): appending every fetched sheet to the end of <head>
                // put it AFTER a later <style> block, so c32-cascading-000's inline
                // `.c { color: green }` beat the earlier linked `.c { color: red }`.
                if (link.Parent is { } linkParent) linkParent.InsertBefore(styleEl, link.NextSibling);
                else head.AppendChild(styleEl);
            }
            catch (Exception ex)
            {
                Session.Diagnostics.Enqueue($"css {href}: {ex.Message}");
                Console.WriteLine($"[CSS load error] {href}: {ex.Message}");
            }
        }

        // Decode HTML entities in author <style> text. Per HTML5, <style> is a raw-text element
        // (its content is never entity-decoded by an HTML parser) — but these vendored CSS2.1
        // reftests are authored as XHTML, where <style> content is normal PCDATA and a real
        // browser (parsing them as application/xhtml+xml) decodes it. Without this, a combinator
        // written as "body &gt; div" reaches the CSS parser as the literal, meaningless token
        // "&gt;" and the selector silently misbehaves. A style block with no entities is unchanged.
        // Strip XML CDATA markers for the same reason: an XHTML author writes
        //   <style type="text/css"><![CDATA[ ... ]]></style>
        // and the XML parser removes the wrapper before the CSS parser ever sees it. Parsed as
        // text/html, <style> is raw text, so the literal "<![CDATA[" is handed to the CSS parser,
        // which cannot tokenise it and drops the rules that follow. ~19% of the vendored CSS 2.1
        // corpus is authored this way. The markers are not valid CSS in any context, so removing
        // them unconditionally is safe; the common legacy "/* <![CDATA[ */ ... /* ]]> */" form
        // just leaves behind empty comments.
        // XML documents (application/xhtml+xml): AngleSharp.Xml builds generic elements
        // without the HTML style-sheet hookup, so parsed-in <style> elements never register
        // a CSSOM sheet and CollectCssRules sees nothing (the injected UA sheet registers
        // through the dynamic path). Rebuild each parsed style element as a fresh element,
        // which does register; attributes and text are copied verbatim.
        if (Current.IsXmlDocument)
        {
            foreach (var styleEl in document.QuerySelectorAll("style").ToList())
            {
                var replacement = document.CreateElement("style");
                foreach (var attr in styleEl.Attributes)
                    replacement.SetAttribute(attr.Name, attr.Value);
                replacement.TextContent = styleEl.TextContent;
                styleEl.Parent?.InsertBefore(replacement, styleEl);
                styleEl.Parent?.RemoveChild(styleEl);
                // Keep origin tracking pointing at the live element.
                if (Current.UaStyleElement == styleEl) Current.UaStyleElement = replacement;
                for (var u = 0; u < Current.UserStyleElements.Count; u++)
                    if (Current.UserStyleElements[u] == styleEl) Current.UserStyleElements[u] = replacement;
                MarkCascadeOrigin(replacement);
            }
        }

        foreach (var styleEl in document.QuerySelectorAll("style"))
        {
            var text = styleEl.TextContent;
            var cleaned = System.Net.WebUtility.HtmlDecode(text)
                .Replace("<![CDATA[", string.Empty, StringComparison.Ordinal)
                .Replace("]]>", string.Empty, StringComparison.Ordinal);
            cleaned = NormalizeSignedZero(cleaned);
            cleaned = NormalizeColorRanges(cleaned);
            if (cleaned != text) styleEl.TextContent = cleaned;
        }

        // Process @import in author <style> elements (skip the UA sheet, which has none).
        // The importing sheet is embedded: its referrer charset is the DOCUMENT's encoding
        // (§4.4 tier 4), the same value the <link> path hands its imports. Dropping it left
        // an inline import decoding UTF-8 and mismatching a Shift_JIS document's own class
        // attribute (at-charset-012).
        foreach (var styleEl in document.QuerySelectorAll("style"))
        {
            var text = styleEl.TextContent;
            if (text.Contains("@import", StringComparison.OrdinalIgnoreCase))
                styleEl.TextContent = InlineImports(text, _documentBaseUrl ?? _baseUrl ?? "", Current.Charset);
        }

        // §4.2 parsing errors: drop illegal declarations from the sheet text after import
        // inlining (so imported sheets are covered) — see DropIllegalDeclarations.
        foreach (var styleEl in document.QuerySelectorAll("style"))
        {
            var text = styleEl.TextContent;
            if (!text.Contains('-')) continue;
            var filtered = DropIllegalDeclarations(text);
            if (filtered != text) styleEl.TextContent = filtered;
        }

        // Collect @keyframes from all stylesheets before traversing the DOM
        AnimationRegistry.Clear();
        CollectKeyframes(document);

        // Collect @font-face rules and load custom fonts
        FontRegistry.Clear();
        CollectFontFaces(document);

        // Lift ::before/::after/::first-letter/::first-line rules out of the sheets BEFORE any
        // element style is computed — AngleSharp would otherwise match them against the element
        // itself. They are re-applied per element inside Traverse.
        NeutralizePseudoElementRules(document);

        // Collect all CSS rules for dynamic class-based style re-evaluation — and, before the
        // traversal, so it can ask which elements declare their own 'font-size'.
        CollectCssRules(document);

        var root = Traverse(document.DocumentElement, 0, DefaultFontSizePx);

        // Always create the JS engine so inline onclick/on* handlers work,
        // even when there are no external or inline script blocks.
        var state = new DocumentState(document, address, _documentBaseUrl ?? address, CssRules.ToArray())
        { ParserContext = Current, Session = Session, Charset = Current.Charset, MediaType = Current.MediaType };
        Session?.Attach(state);
        var jsEngine = JsEngine.Create(root, viewportWidth, viewportHeight, state);
        // The sandbox enforcement reads the flags mid-parse (the script pump gates on them),
        // so the engine carries the set before any script executes.
        jsEngine.SandboxFlags = sandboxFlags;
        // The browsing context's shared session history adopts this document before any
        // script runs, so history.length/state read the context's list from the start.
        sessionHistory?.AdoptFor(jsEngine, address);
        var page = new Page
        {
            Root = root,
            Engine = jsEngine,
            Document = document,
            BaseUrl = _documentBaseUrl,
            ViewportWidth = viewportWidth,
            ViewportHeight = viewportHeight,
            SandboxFlags = sandboxFlags,
        };
        if (parentEngine is not null && frameNode is not null)
        {
            frameNode.ChildPage = page;
            jsEngine.SetParentContext(parentEngine,
                Scripting.Dom.JsElement.For(parentEngine.RawEngine, frameNode));
        }
        QueueParsedDetailsNotifications(root, jsEngine);
        QueueParsedDetailsNotifications(root, jsEngine);

        // The body's link presentational hint paints its anchors (HTML §15.3.2): the anchor
        // consumption runs as a POST-TRaverse pass because a node's Parent link is unset
        // while its own hints run inside Traverse (hints fire before AddChild links it).
        ApplyLinkColorHints(root);

        // Now that the parent engine exists, wire each nested <iframe>'s child context
        // (parent/top/frameElement) and queue its load event. Done before the parent's scripts run
        // so they can immediately use iframe.contentWindow.
        WireChildFrames(root, jsEngine);

        // Start playback for any autoplaying <audio>/<video> (creates the backend + fires events).
        StartAutoplayMedia(root, jsEngine);

        // 1) In-position classic scripts (inline + external without defer/async), in document order.
        //    document.write() during these appends to the body (see JsDocument.write).
        //    A sandboxed page without the allow-scripts flag parses but never runs script
        //    (HTML §6.5.3) — none of the load pipeline's executions happen.
        var sandboxAllowsScripts = jsEngine.SandboxAllowsScripts;
        foreach (var script in _pendingScripts)
            if (sandboxAllowsScripts)
            {
                jsEngine.CurrentScriptContinuation = script.Continuation;
                try { jsEngine.Execute(script.Code!, script.Url, script.RequestedUrl); }
                finally
                {
                    jsEngine.CurrentScriptContinuation = null;
                    jsEngine.DocumentFacade.FlushParserWrites(script.Node);
                }
            }

        jsEngine.MarkDocumentInteractive();

        // Snapshot document-owned work before asynchronous completions can overlap another parse.
        var deferred = _deferredScripts.ToArray();
        var asyncScripts = _asyncScripts.ToArray();
        var remainingAsync = asyncScripts.Length;
        var deferredDone = false;
        var loaded = false;
        var bodyNode = FindFirst(root, n => n.TagName == "BODY");
        var onloadCode = bodyNode?.Attributes.GetValueOrDefault("onload");
        void FinishLoad()
        {
            if (loaded || !deferredDone || remainingAsync != 0) return;
            loaded = true;
            if (onloadCode is not null && sandboxAllowsScripts) jsEngine.Execute(onloadCode);
            jsEngine.DispatchLoad();
        }
        foreach (var script in asyncScripts)
            if (sandboxAllowsScripts)
                jsEngine.EnqueueMacrotask(() => jsEngine.RunDeferredScripts([script], () => { remainingAsync--; FinishLoad(); }));
        if (sandboxAllowsScripts)
            jsEngine.RunDeferredScripts(deferred, () =>
            {
                deferredDone = true;
                jsEngine.DispatchDomContentLoaded();
                FinishLoad();
            });
        else
        {
            deferredDone = true;
            jsEngine.DispatchLoad();
        }

        return page;
    }

    /// <summary>HTML §15.3.2: every anchor inside a body carrying a link presentational
    /// attribute takes that color (force-overriding the UA sheet's blue, which is exactly
    /// what the presentational tier means; author CSS beats it through the normal cascade).
    /// Runs post-traverse so the ancestor chain is wired.</summary>
    private static void ApplyLinkColorHints(LayoutNode root)
    {
        string? bodyLink = null;
        void Walk(LayoutNode node)
        {
            if (bodyLink is null && node.TagName == "BODY" &&
                node.StyleOverrides.TryGetValue("__link-color", out var found))
                bodyLink = found;
            // An author declaration of any tier outranks the presentational hint, so only
            // stamp the override when the anchor has no color from the cascade already.
            if (node.TagName == "A" && node.Attributes.ContainsKey("href") && bodyLink is not null
                && !node.StyleOverrides.ContainsKey("color"))
                node.StyleOverrides["color"] = bodyLink;
            foreach (var child in node.Children) Walk(child);
        }
        Walk(root);
    }

    /// <summary>Optimistically reports whether a media MIME type can be played. The simulated
    /// backend has no real decoder, so we accept the common container types (browsers return
    /// "maybe"/"probably" similarly). Used for &lt;source&gt; selection and canPlayType.</summary>
    internal static bool IsPlayableMediaType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type)) return true;   // no hint → try it
        var t = type.ToLowerInvariant();
        return t.Contains("mp4") || t.Contains("mpeg") || t.Contains("mp3") || t.Contains("ogg")
            || t.Contains("webm") || t.Contains("wav") || t.Contains("aac") || t.Contains("m4a")
            || t.Contains("avc1") || t.Contains("mp4a") || t.StartsWith("audio/") || t.StartsWith("video/");
    }

    /// <summary>Starts playback for autoplaying media elements once the engine exists.</summary>
    private static void StartAutoplayMedia(LayoutNode root, JsEngine engine)
    {
        var stack = new Stack<LayoutNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node.TagName is "AUDIO" or "VIDEO" && node.Attributes.ContainsKey("autoplay"))
                Scripting.Dom.JsElement.For(engine.RawEngine, node).play();
            foreach (var c in node.Children) stack.Push(c);
        }
    }

    /// <summary>Wires every nested &lt;iframe&gt; with a child Page into the parent browsing context:
    /// sets the child's parent/top/frameElement and queues the iframe's <c>load</c> event (deferred
    /// onto the parent's task queue so listeners added by parent scripts are attached first).</summary>
    private static void WireChildFrames(LayoutNode root, JsEngine parentEngine)
    {
        var stack = new Stack<LayoutNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node.TagName == "IFRAME" && node.ChildPage is { } child)
            {
                var frameEl = Scripting.Dom.JsElement.For(parentEngine.RawEngine, node);
                child.Engine.SetParentContext(parentEngine, frameEl);
                var captured = node;
                if (!child.IsInitialAboutBlank)
                    parentEngine.EnqueueMacrotask(() =>
                        Scripting.EventDispatcher.DispatchToNode(captured, "load", parentEngine));
            }
            foreach (var c in node.Children) stack.Push(c);
        }
    }

    /// <summary>
    /// Parses an iframe's child document into an independent <see cref="Page"/> (its own LayoutNode
    /// tree and JS engine), without disturbing the parent's parse. The parent's parse statics and
    /// the <see cref="JsEngine.Instance"/> singleton are saved and restored around the child parse;
    /// the child Page keeps its own engine (reachable via <see cref="JsEngine.For"/>).
    /// </summary>
    /// <param name="content">A URL when <paramref name="isSrcdoc"/> is false, else inline HTML.</param>
    internal static Page ParseChildPage(string content, bool isSrcdoc, string baseUrl, int viewportWidth, int viewportHeight,
        BrowserSession? session = null, JsEngine? parentEngine = null, LayoutNode? frameNode = null,
        HashSet<string>? sandboxFlags = null)
    {
        var savedContext = Current;
        var savedInstance = JsEngine.Instance;
        Current = new ParseState { Session = session ?? savedContext.Session };
        if (sandboxFlags is { Count: > 0 })
            Current.InheritedSandbox = new HashSet<string>(sandboxFlags, StringComparer.Ordinal);

        try
        {
            _baseUrl = baseUrl;
            _documentBaseUrl = baseUrl;
            _pendingScripts.Clear();
            _deferredScripts.Clear();
            _asyncScripts.Clear();
            _inlineModuleCounter = 0;
            _counters.Clear();
            ViewportWidth = viewportWidth;
            ViewportHeight = viewportHeight;

            var config = ParseConfiguration(viewportWidth, viewportHeight);
            var address = baseUrl;
            string html;
            string? mediaType = null;
            if (isSrcdoc) html = content;
            else if (BlobUrlRegistry.TryResolve(content, out var blob) && blob is not null)
            {
                // blob: URLs (File API dependency) resolve from the in-process registry.
                address = content;
                html = blob.Content;
            }
            else
            {
                using var response = Session.Client.GetAsync(content).Result;
                // HTTP error responses still complete iframe navigation and fire load.
                address = response.RequestMessage?.RequestUri?.AbsoluteUri ?? content;
                // Same REC §8.2.2.1 decode as the top-level load: an iframe's document
                // carries its own encoding declarations.
                var (decoded, charset) = EncodingSniffer.DecodeDocumentWithEncoding(
                    response.Content.ReadAsByteArrayAsync().Result,
                    response.Content.Headers.ContentType?.CharSet);
                html = decoded;
                Current.Charset = charset;
                mediaType = response.Content.Headers.ContentType?.MediaType;
                Current.MediaType = mediaType;
                Current.ContentLanguage = response.Content.Headers.ContentLanguage.FirstOrDefault();
            }
            // The child document dispatches on its MIME type exactly like the top-level load:
            // an XML media type (application/xml, text/xml, image/svg+xml, an XHTML document)
            // parses through the XML parser, not the HTML one.
            Current.IsXmlDocument = mediaType is "application/xhtml+xml" or "text/xml" or "application/xml"
                or "image/svg+xml" or "application/xhtml" or "text/xhtml";
            // Same XML declaration alignment as the top-level load (TraversePage). The HTML
            // parser's <meta charset> handler is unregistered in ParseConfiguration: the
            // content is already a decoded string, and letting the parser honor an unaligned
            // declaration over its re-encoded UTF-8 bytes mojibakes non-ASCII attributes (a
            // multibyte label such as eucjp_chars.html's <meta charset="euc-jp"> swallowed
            // the following tags' ASCII bytes) or forces a visible rewrite of the author's
            // declaration.
            if (Current.IsXmlDocument)
                html = AlignXmlDeclarationCharset(html, "utf-8");
            var parsedSource = NormalizeImportantBangs(html);
            Current.Source = parsedSource;
            Current.SourceCursor = 0;
            var childConfig = Current.IsXmlDocument ? config.WithXml() : config;
            var context = BrowsingContext.New(childConfig.WithOnly<IDocumentFactory>(new XmlDispatchDocumentFactory()));
            var document = OpenDocument(context, address, parsedSource, mediaType);
            return ParseOpenedDocument(document, address, viewportWidth, viewportHeight, parentEngine, frameNode, sandboxFlags);
        }
        finally
        {
            Current = savedContext;
            JsEngine.Instance = savedInstance;
        }
    }

    /// <summary>HTML §6.5: parse a sandbox attribute into its flag set — ASCII-whitespace
    /// separated tokens matched case-sensitively against the flag names, unknown tokens ignored
    /// — unioned with the set the frame inherited. Empty/absent with no inheritance is the
    /// unsandboxed case (a null set on the Page).</summary>
    internal static HashSet<string>? ParseSandboxFlags(string? attribute, HashSet<string> inherited)
    {
        // An ABSENT attribute leaves the frame unsandboxed (unless it inherited flags); a
        // PRESENT but empty attribute is the strongest sandbox — every restriction applies
        // (HTML §6.5: the empty value is not "no sandbox").
        if (attribute is null)
            return inherited.Count > 0 ? new HashSet<string>(inherited, StringComparer.Ordinal) : null;
        var flags = new HashSet<string>(inherited, StringComparer.Ordinal);
        foreach (var token in DomWhitespace.Split(attribute)) flags.Add(token);
        return flags;
    }

    /// <summary>
    /// Walks all stylesheets and registers every @keyframes rule into <see cref="AnimationRegistry"/>.
    /// </summary>
    private static void CollectKeyframes(AngleSharp.Dom.IDocument document)
    {
        if (document.StyleSheets is null) return;
        foreach (var sheet in document.StyleSheets.OfType<ICssStyleSheet>())
            CollectKeyframesFromRules(sheet.Rules);
    }

    private static void CollectKeyframesFromRules(ICssRuleList rules)
    {
        foreach (var rule in rules)
        {
            if (rule is ICssKeyframesRule kfRule)
            {
                var frames = new List<(float Offset, Dictionary<string, string> Props)>();
                foreach (var fr in kfRule.Rules.OfType<ICssKeyframeRule>())
                {
                    // KeyText can be "from", "to", "0%", "50%", or comma-separated like "0%, 100%"
                    foreach (var key in fr.KeyText.Split(',', StringSplitOptions.TrimEntries))
                    {
                        float offset;
                        if (key.Equals("from", StringComparison.OrdinalIgnoreCase)) offset = 0f;
                        else if (key.Equals("to", StringComparison.OrdinalIgnoreCase)) offset = 1f;
                        else if (key.EndsWith('%') &&
                                 float.TryParse(key[..^1].Trim(),
                                     System.Globalization.NumberStyles.Float,
                                     System.Globalization.CultureInfo.InvariantCulture, out var pct))
                            offset = pct / 100f;
                        else continue;

                        var props = ParseCssTextToDict(fr.Style.CssText);
                        frames.Add((offset, props));
                    }
                }
                if (frames.Count > 0)
                    AnimationRegistry.Register(kfRule.Name, frames);
                continue;
            }

            // Descend into @media blocks
            if (rule is ICssMediaRule mediaRule)
                CollectKeyframesFromRules(mediaRule.Rules);
        }
    }

    private static string? ResolveUrl(string src) =>
        Lite.Network.UrlUtils.Resolve(src, _documentBaseUrl ?? _baseUrl);

    /// <summary>Walks all stylesheets and loads @font-face rules into <see cref="FontRegistry"/>.</summary>
    private static void CollectFontFaces(AngleSharp.Dom.IDocument document)
    {
        if (document.StyleSheets is null) return;
        foreach (var sheet in document.StyleSheets.OfType<ICssStyleSheet>())
            CollectFontFacesFromRules(sheet.Rules);
    }

    private static void CollectFontFacesFromRules(ICssRuleList rules)
    {
        foreach (var rule in rules)
        {
            if (rule is ICssFontFaceRule ffRule)
            {
                var family = ffRule.Family?.Trim().Trim('"', '\'');
                var srcRaw = ffRule.Source;
                if (string.IsNullOrWhiteSpace(family) || string.IsNullOrWhiteSpace(srcRaw)) continue;

                // Parse font-weight and font-style
                var weightStr = ffRule.Weight?.Trim().ToLowerInvariant() ?? "normal";
                var styleStr = ffRule.Style?.Trim().ToLowerInvariant() ?? "normal";
                var bold = weightStr is "bold" or "700" or "800" or "900";
                var italic = styleStr is "italic" or "oblique";

                // Extract URL from src: url("path") format
                var url = ExtractFontUrl(srcRaw);
                if (url == null) continue;

                var resolved = ResolveUrl(url);
                if (resolved == null) continue;

                try
                {
                    var fontBytes = ResourceLoader.FetchBytes(resolved, _baseUrl, Session);
                    if (fontBytes != null && fontBytes.Length > 0)
                        FontRegistry.Register(family, bold, italic, fontBytes);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[FontFace] Failed to load '{family}' from {resolved}: {ex.Message}");
                }
                continue;
            }

            if (rule is ICssMediaRule mediaRule)
                CollectFontFacesFromRules(mediaRule.Rules);
        }
    }

    private static string? ExtractFontUrl(string src)
    {
        // Match url("...") or url('...') or url(...)
        var idx = src.IndexOf("url(", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        var start = idx + 4;
        if (start >= src.Length) return null;
        char? quote = null;
        if (src[start] == '"' || src[start] == '\'') { quote = src[start]; start++; }
        var end = quote.HasValue ? src.IndexOf(quote.Value, start) : src.IndexOf(')', start);
        if (end < 0) return null;
        return src[start..end].Trim();
    }

    /// <summary>
    /// Computes the cascade while tolerating values which AngleSharp.Css 1.0.2 parses but cannot
    /// convert to pixels (notably a unitless <c>line-height</c> from the <c>font</c> shorthand).
    /// Lite resolves relative and unitless values during layout, so retaining the cascaded value
    /// is both sufficient and more accurate than discarding the declaration.
    /// </summary>
    /// <summary>The guarded computed-style entry for code outside this class: AngleSharp's
    /// own extension throws on declarations it computes but cannot convert (unitless
    /// line-height from the font shorthand), which crashed presentational-hint resolution
    /// and DOM-projection styling the moment their pages rendered.</summary>
    internal static ICssStyleDeclaration ComputeCurrentStyleFor(IElement element) =>
        ComputeCurrentStyle(element);

    private static ICssStyleDeclaration ComputeCurrentStyle(IElement element) =>
        ComputeCurrentStyle(element, out _);

    private static ICssStyleDeclaration ComputeCurrentStyle(IElement element, out string? deferredLineHeight)
    {
        deferredLineHeight = null;
        var document = element.Owner
            ?? throw new InvalidOperationException("Cannot compute style for a detached element.");
        var window = document.DefaultView
            ?? throw new InvalidOperationException("Cannot compute style without a browsing context.");
        var device = document.Context.GetService<IRenderDevice>() ?? new DefaultRenderDevice();
        var styles = window.GetStyleCollection(device);
        var cascaded = styles.GetDeclarations(element);
        try
        {
            var computed = styles.ComputeDeclarations(element);
            RestoreRelativeLengths(computed, cascaded);
            return computed;
        }
        catch (Exception ex) when (IsUnsupportedCssUnitConversion(ex))
        {
            // AngleSharp.Css 1.0.2 represents a unitless line-height as a length with Unit.None,
            // then asks CssLengthValue.ToPixel to convert it. Temporarily neutralize only that
            // declaration so every other longhand (including font-size/family from `font`) is
            // still computed. Lite applies the saved multiplier during line layout.
            var lineHeight = cascaded.GetPropertyValueSafe(PropertyNames.LineHeight)?.Trim();
            if (double.TryParse(lineHeight, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out _))
            {
                var inline = element.GetStyle();
                var savedLineHeightCss = inline.CssText;
                try
                {
                    inline.SetProperty(PropertyNames.LineHeight, "normal", "important");
                    var computed = styles.ComputeDeclarations(element);
                    RestoreRelativeLengths(computed, cascaded);
                    deferredLineHeight = lineHeight;
                    return computed;
                }
                catch (Exception retry) when (IsUnsupportedCssUnitConversion(retry))
                {
                    // A second, unrelated unsupported conversion remains; retain the cascade.
                }
                finally
                {
                    inline.CssText = savedLineHeightCss;
                }
            }

            return cascaded;
        }
        catch (NullReferenceException ex) when (ex.StackTrace?.Contains(
            "AngleSharp.Css.Values.CssTupleValue", StringComparison.Ordinal) == true)
        {
            // AngleSharp.Css can dereference a missing member while computing a tuple-valued
            // declaration on modern result destinations. Keep its cascade so the document can
            // still load; Lite resolves the values it supports during layout.
            return cascaded;
        }
    }


    /// <summary>
    /// AngleSharp.Css resolves em/rem/percentage values against one global render device. CSS
    /// requires those units to use the element's computed context, which Lite owns. Preserve the
    /// cascaded typed length so layout can resolve it against the correct font or containing box.
    /// </summary>
    private static void RestoreRelativeLengths(
        ICssStyleDeclaration computed,
        ICssStyleDeclaration cascaded)
    {
        foreach (var property in cascaded)
        {
            if (property.RawValue is not CssLengthValue length || length.Type == CssLengthValue.Unit.Px)
                continue;

            computed.SetProperty(
                property.Name,
                property.Value,
                property.IsImportant ? "important" : null);
        }
    }

    private static bool IsUnsupportedCssUnitConversion(Exception ex) =>
        ex is InvalidOperationException &&
            ex.Message.Equals("Unsupported unit cannot be converted.", StringComparison.Ordinal) ||
        ex is ArgumentException &&
            ex.Message.StartsWith("A non null render device", StringComparison.Ordinal);

    /// <summary>The element's upper-case local tag name. An XML parse keeps a namespace prefix in
    /// TagName ("svg:svg" in XHTML), which no element-specific code recognised — prefixed inline
    /// SVG never rendered (absolute-replaced-width-*).</summary>
    internal static string TagOf(IElement element) =>
        (element.Prefix is null ? element.TagName : element.LocalName).ToUpperInvariant();

    private static LayoutNode Traverse(IElement element, int indent, float parentFontPx = DefaultFontSizePx, LayoutNode? parentNode = null)
    {
        // Normalize tag name to uppercase — AngleSharp returns lowercase for SVG namespace elements
        var tag = TagOf(element);
        if (_verbose)
        {
            var indentSpace = new string(' ', indent * 2);
            Console.WriteLine($"{indentSpace}Tag: {tag}, ID: {element.Id}, Class: {element.ClassName}");
        }

        // Determine whether this element has renderable element children (non-skipped, non-script).
        // If so, we walk ChildNodes in order so that interleaved text nodes (e.g. "text <em>italic</em> more")
        // are preserved as synthetic #TEXT children rather than being collapsed onto the parent.
        var hasMixedChildren = element.Children.Any(c =>
        {
            var ct = TagOf(c);
            return ct != "SCRIPT" && !SkipTags.Contains(ct);
        });

        // Trimming is right when this text is the whole line: CSS 2.1 §16.6.1 drops spaces at the
        // start and end of a LINE. It is wrong when a ::before/::after continues the line, so keep
        // the untrimmed form for CreatePseudoElementChildren to restore the adjacent boundary space.
        // Collapse here too, not just on the mixed-children path: with 'white-space: normal' a
        // newline or tab is collapsible whitespace and therefore a line-break opportunity. Line
        // breaking splits on U+0020 alone, so leaving raw newlines in meant source-wrapped text
        // with no spaces on a line (very common in the CSS 2.1 suite) never wrapped and overflowed.
        // ...but only when 'white-space' actually allows collapsing: pre / pre-wrap preserve every
        // space and newline, and pre-line preserves newlines as forced breaks while still
        // collapsing spaces. Collapsing unconditionally destroys those line breaks.
        var elementStyle = ComputeCurrentStyle(element, out var deferredLineHeight);
        var ws = elementStyle.GetPropertyValueSafe("white-space")?.Trim().ToLowerInvariant();
        var directTextRaw = hasMixedChildren
            ? ""
            : tag == "SCRIPT"
                ? string.Concat(element.ChildNodes.OfType<IText>().Select(t => t.Data)) // raw-text element: never collapse
                : NormalizeTextWhitespace(
                    string.Concat(element.ChildNodes.OfType<IText>().Select(t => t.Data)), ws);
        // Trimming implements §16.6.1's "remove the spaces at the start and end of a line", so it
        // only applies where 'white-space' collapses at all: under pre / pre-wrap every space is
        // significant, and a cell holding one space is a space wide, not zero.
        var preservesSpaces = ws is "pre" or "pre-wrap";
        var directText = hasMixedChildren
            ? ""   // text nodes become ordered #TEXT children below
            : preservesSpaces || tag == "SCRIPT" ? directTextRaw : directTextRaw.Trim(CssWhiteSpace);

        var href = tag == "A" ? element.GetAttribute("href") : null;
        var node = new LayoutNode(element.Id, tag, directText, elementStyle, href);
        if (directText.Length > 0 && directText.Length != directTextRaw.Length && !preservesSpaces && tag != "SCRIPT")
        {
            node.TrimmedLeadingSpace = Array.IndexOf(CssWhiteSpace, directTextRaw[0]) >= 0;
            node.TrimmedTrailingSpace = Array.IndexOf(CssWhiteSpace, directTextRaw[^1]) >= 0;
        }
        if (Current.IsXmlDocument)
            node.SourceTagName = element.TagName;
        // NOTE: node.DomNode is deliberately bound at the END of Traverse, not here. Attribute
        // capture below mirrors DOM values into the rendering cache; binding early would route
        // those writes back through element.SetAttribute — mutating the very NamedNodeMap the
        // capture loops enumerate ("Collection was modified") and materialising rendering-only
        // normalisations (option value fallbacks, canvas default sizes) as authored DOM attributes.
        if (deferredLineHeight is not null)
            node.StyleOverrides[PropertyNames.LineHeight] = deferredLineHeight;

        // CSS 2.1 §15.7: font-size COMPUTES to a length, and that length is what descendants
        // inherit. AngleSharp hands every descendant the specified value instead ("2em"), which
        // layout then re-resolved against the parent's already-scaled size, so a single
        // `font-size: 2em` doubled again at every level below it. Resolve it once here, against
        // the parent's computed size, and hand the result down.
        float fontPx;
        using (CssUnits.ForFont(parentNode)) fontPx = ResolveOwnFontSize(element, parentFontPx);
        node.ComputedFontSize = fontPx;
        // §15.6: font-weight computes to a number; 'bolder'/'lighter' resolve against the parent.
        node.ComputedFontWeight = TextMeasure.ResolveFontWeight(OwnFontWeightDeclaration(element),
            parentNode is null ? TextMeasure.NormalWeight : TextMeasure.ComputedFontWeight(parentNode));

        // §15.8: the 'font' shorthand's optional /<line-height> component. Nothing expands the
        // shorthand, so the component used to vanish and the element laid out at 'normal'.
        if (OwnFontLineHeightDeclaration(element) is { } fontLh &&
            !node.StyleOverrides.ContainsKey(PropertyNames.LineHeight))
            node.StyleOverrides[PropertyNames.LineHeight] = fontLh;

        // §15.8 / §4.2: when an invalid 'font' shorthand was rejected (e.g. negative line-height
        // in font-146), prevent AngleSharp's leaked 'font-family'/'line-height' longhands from applying.
        if (HasOnlyInvalidFontShorthand(element))
        {
            if (!node.StyleOverrides.ContainsKey(PropertyNames.FontFamily))
                node.StyleOverrides[PropertyNames.FontFamily] = "inherit";
            if (!node.StyleOverrides.ContainsKey(PropertyNames.LineHeight))
                node.StyleOverrides[PropertyNames.LineHeight] = "inherit";
        }

        // Extract flex-related CSS properties that AngleSharp doesn't cascade
        ExtractMatchedCssProperties(element, node);

        // §6.4.3: an inline declaration outranks every non-important rule — the override
        // channel is the engine's inline channel (script mutations live there too, and
        // StyleResolver.Apply's tiers overwrite overrides at the important tiers exactly as
        // §6.4.3 orders). AngleSharp keeps an inline SHORTHAND under its own key while the
        // longhand keys carry the stylesheet's values, so a static style="border-style:none|
        // solid" attribute never reached the border readers or the table's §17.6.2.1 conflict
        // resolution (border-collapse-dynamic-cell-001/004/005 resolved outset through a
        // static inline solid). Stamp the BORDER declarations expanded, the same way JsStyle
        // expands mutations. (A full all-properties stamp was tried and REVERTED: it regressed
        // table-anonymous-objects-015 — the anonymous-table machinery reads some inline
        // declarations differently through the override channel; the general design needs its
        // own pass, the conflict family needs only the borders.)
        if (element.GetAttribute("style") is { Length: > 0 } inlineCss)
        {
            var (inlineProps, _) = ParseDeclarations(inlineCss);
            foreach (var (prop, value) in inlineProps)
            {
                if (!prop.StartsWith("border")) continue;
                node.StyleOverrides[prop] = value;
                foreach (var (longhand, longhandValue) in Scripting.Dom.JsStyle.Expand(prop, value))
                    node.StyleOverrides[longhand] = longhandValue;
            }
        }

        if (tag == "IMG")
        {
            var src = element.GetAttribute("src");
            node.Alt = element.GetAttribute("alt") ?? string.Empty;
            if (src != null) node.Attributes["src"] = src;

            if (!string.IsNullOrEmpty(src))
            {
                node.Attributes["_currentSrc"] = ResolveAgainstBase(src);
                node.Image = ResourceLoader.FetchImage(src, _documentBaseUrl ?? _baseUrl, Session);
            }

            // HTML presentational hints: width/height are lengths in px, or (HTML 4) percentages.
            // They are CSS declarations, not the image's intrinsic size — mixing the two derived
            // the "missing" dimension from the attribute of one axis and the bitmap of the other
            // (width="100%" height="50" on a 1x1 image gave a height of 39200). The intrinsic size
            // is the bitmap's; the attributes only stand in for it when there is no bitmap.
            ApplyDimensionAttribute(element, node, "width", "width");
            ApplyDimensionAttribute(element, node, "height", "height");
        }

        // <object>: a replaced element that renders its `data` resource, falling back to its
        // child content (which may be a nested <object>) when the resource cannot be displayed
        // (CSS2.1/HTML4 §13.3). Acid2's eyes are a 3-deep object chain: unknown type → 404 →
        // the eyes PNG. We only render images here; any non-image `type` (e.g. text/html) or a
        // resource that fails to decode falls through to the fallback content.
        bool objectShowsImage = false;
        if (tag == "OBJECT")
        {
            var data = element.GetAttribute("data");
            var type = element.GetAttribute("type");
            var couldBeImage = string.IsNullOrEmpty(type) ||
                               type.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(data) && couldBeImage)
            {
                var img = ResourceLoader.FetchImage(data, _baseUrl, Session);
                if (img is not null)
                {
                    node.Image = img;
                    node.Attributes["src"] = data;   // so the replaced-element paint path fires
                    objectShowsImage = true;          // suppress fallback content
                }
            }
        }

        if (tag is "TD" or "TH")
        {
            foreach (var attr in new[] { "colspan", "rowspan" })
            {
                var val = element.GetAttribute(attr);
                if (val != null) node.Attributes[attr] = val;
            }
        }

        if (tag == "FORM")
        {
            foreach (var attr in new[] { "action", "method", "name", "enctype", "target", "novalidate" })
            {
                var val = element.GetAttribute(attr);
                if (val != null) node.Attributes[attr] = val;
            }
        }

        if (tag is "INPUT" or "BUTTON")
        {
            foreach (var attr in new[] { "type", "value", "placeholder", "checked", "min", "max", "step", "name", "disabled", "readonly", "required", "maxlength", "pattern" })
            {
                var val = element.GetAttribute(attr);
                if (val != null) node.Attributes[attr] = val;
            }
            // Hidden inputs participate in submission but are not rendered.
            if (tag == "INPUT" && string.Equals(element.GetAttribute("type"), "hidden", StringComparison.OrdinalIgnoreCase))
                node.StyleOverrides["display"] = "none";
        }

        if (tag == "TEXTAREA")
        {
            foreach (var attr in new[] { "placeholder", "rows", "cols", "name", "disabled", "readonly", "required", "maxlength" })
            {
                var val = element.GetAttribute(attr);
                if (val != null) node.Attributes[attr] = val;
            }
            // Capture textarea content as its value
            var textContent = element.TextContent;
            if (!string.IsNullOrEmpty(textContent))
                node.Attributes["value"] = textContent;
        }

        if (tag == "SELECT")
        {
            foreach (var attr in new[] { "name", "disabled", "multiple", "size" })
            {
                var val = element.GetAttribute(attr);
                if (val != null) node.Attributes[attr] = val;
            }
            // Collect options
            var options = element.QuerySelectorAll("option");
            var optionTexts = new List<string>();
            var optionValues = new List<string>();
            string? selectedValue = null;
            foreach (var opt in options)
            {
                var optText = opt.TextContent.Trim();
                var optVal = opt.GetAttribute("value") ?? optText;
                optionTexts.Add(optText);
                optionValues.Add(optVal);
                if (opt.HasAttribute("selected"))
                    selectedValue = optVal;
            }
            node.Attributes["_options"] = string.Join("|", optionTexts);
            node.Attributes["_optionValues"] = string.Join("|", optionValues);
            if (selectedValue != null) node.Attributes["value"] = selectedValue;
            else if (optionValues.Count > 0) node.Attributes["value"] = optionValues[0];
        }

        if (tag == "PROGRESS")
        {
            foreach (var attr in new[] { "value", "max" })
            {
                var val = element.GetAttribute(attr);
                if (val != null) node.Attributes[attr] = val;
            }
        }

        if (tag == "METER")
        {
            foreach (var attr in new[] { "value", "min", "max", "low", "high", "optimum" })
            {
                var val = element.GetAttribute(attr);
                if (val != null) node.Attributes[attr] = val;
            }
        }

        if (tag == "OUTPUT")
        {
            foreach (var attr in new[] { "for", "name" })
            {
                var val = element.GetAttribute(attr);
                if (val != null) node.Attributes[attr] = val;
            }
        }

        if (tag == "OPTION")
        {
            // The effective option value (its `value` attribute, falling back to the text
            // content) is IDL behavior, not an attribute: materializing the fallback here
            // leaked value="" into the script-visible DOM and every serialization. Mirror
            // only what the DOM carries; the JsElement.value getter applies the fallback.
            if (element.GetAttribute("value") is { } optionValue) node.Attributes["value"] = optionValue;
            foreach (var attr in new[] { "label", "selected", "disabled" })
            {
                var val = element.GetAttribute(attr);
                if (val != null) node.Attributes[attr] = val;
            }
        }

        if (tag == "TEMPLATE")
        {
            // The template's parsed content is held inert (template.content) and never rendered.
            node.TemplateContent = BuildTemplateContent(element);
            node.StyleOverrides["display"] = "none";
        }

        // HEAD and SCRIPT are real DOM nodes (loader bootstraps walk
        // document.getElementsByTagName('script') and read .parentNode) hidden by the UA sheet
        // alone, so an author 'display: block' shows them like any other element (content-067:
        // a displayed meta's ::before; content-072: a displayed script's ::before).

        // <details>/<dialog> open state (drives layout collapse + the .open DOM property)
        if (tag is "DETAILS" or "DIALOG" && element.HasAttribute("open"))
            node.Attributes["open"] = "";

        // Capture inline event handlers for any element
        foreach (var attr in new[] { "onclick", "onchange", "oninput", "onsubmit", "onkeyup", "onkeydown", "onload" })
        {
            var val = element.GetAttribute(attr);
            if (val != null) node.Attributes[attr] = val;
        }

        // Capture HTML class attribute for selector matching
        if (element.HasAttribute("class"))
            node.Attributes["class"] = element.GetAttribute("class") ?? string.Empty;

        // Capture every authored attribute, so attribute selectors, getAttribute and attr() in
        // 'content' all see what the markup actually declared — only data-* and a per-tag
        // whitelist used to survive, which left e.g. `td:before { content: attr(abbr) }` empty.
        // Values the tag-specific blocks above normalised (an <input>'s value, a resolved src)
        // are already in place and keep priority.
        foreach (var attr in element.Attributes)
        {
            if (!node.Attributes.ContainsKey(attr.Name))
                node.Attributes[attr.Name] = attr.Value;
        }

        // SVG elements — capture all attributes for rendering
        if (IsSvgElement(tag))
        {
            foreach (var attr in element.Attributes)
                node.Attributes[attr.Name] = attr.Value;

            // SVG is a replaced element — give it block display and explicit
            // width/height so the layout engine creates a proper box for it.
            if (tag == "SVG")
            {
                node.StyleOverrides["display"] = "block";
                var svgW = element.GetAttribute("width");
                var svgH = element.GetAttribute("height");
                if (string.IsNullOrEmpty(svgW)) svgW = "300";
                if (string.IsNullOrEmpty(svgH)) svgH = "150";
                // A bare number is px; a percentage or unit length stays as written ('50%px' was invalid).
                static string Length(string v) => double.TryParse(v, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out _) ? v + "px" : v;
                svgW = Length(svgW.Trim());
                svgH = Length(svgH.Trim());
                // The attributes are presentational hints: an author 'width'/'height' wins
                // (absolute-replaced-width-003's svg { height: 100px } with no height attribute
                // kept the 150px default).
                static bool Declared(string? v) => !string.IsNullOrWhiteSpace(v) && v.Trim() != "auto";
                if (!Declared(elementStyle.GetPropertyValueSafe("width"))) node.StyleOverrides["width"] = svgW;
                if (!Declared(elementStyle.GetPropertyValueSafe("height"))) node.StyleOverrides["height"] = svgH;
            }
        }

        // Canvas element — capture width/height and set as block with explicit dimensions
        if (tag == "CANVAS")
        {
            var canvasW = element.GetAttribute("width");
            var canvasH = element.GetAttribute("height");
            if (string.IsNullOrEmpty(canvasW)) canvasW = "300";
            if (string.IsNullOrEmpty(canvasH)) canvasH = "150";
            node.Attributes["width"] = canvasW;
            node.Attributes["height"] = canvasH;
            node.StyleOverrides["display"] = "block";
            if (!canvasW.EndsWith("px")) canvasW += "px";
            if (!canvasH.EndsWith("px")) canvasH += "px";
            node.StyleOverrides["width"] = canvasW;
            node.StyleOverrides["height"] = canvasH;
        }

        // <iframe> — a replaced element hosting a nested browsing context (child Page). Default
        // 300×150 (CSS 2.1 / HTML); width/height attributes give an explicit box. The child
        // document is parsed into its own Page (independent layout tree + JS engine).
        if (tag == "IFRAME")
        {
            // width/height attributes are presentational hints (a pixel length or a percentage)
            // that author CSS overrides; with neither, 'auto' resolves to the 300x150 default
            // object size of a replaced element with no intrinsic dimensions (§10.3.2, §10.6.2).
            var iframeStyle = ComputeCurrentStyle(element);
            string? IframeSize(string attribute, int fallback)
            {
                var declared = iframeStyle.GetPropertyValueSafe(attribute)?.Trim();
                if (!string.IsNullOrEmpty(declared) && declared != "auto") return null;
                var raw = element.GetAttribute(attribute)?.Trim();
                if (raw is { Length: > 1 } && raw.EndsWith('%') &&
                    float.TryParse(raw[..^1], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var pct))
                    return $"{pct.ToString(System.Globalization.CultureInfo.InvariantCulture)}%";
                return int.TryParse(raw?.Replace("px", ""), out var px) && px >= 0 ? $"{px}px" : $"{fallback}px";
            }
            var iframeWidth = IframeSize("width", 300);
            var iframeHeight = IframeSize("height", 150);
            int cw = iframeWidth is { } w0 && w0.EndsWith("px") && int.TryParse(w0[..^2], out var pw) ? pw : 300;
            int ch = iframeHeight is { } h0 && h0.EndsWith("px") && int.TryParse(h0[..^2], out var ph) ? ph : 150;
            node.StyleOverrides["display"] = "block";
            if (iframeWidth is not null) node.StyleOverrides["width"] = iframeWidth;
            if (iframeHeight is not null) node.StyleOverrides["height"] = iframeHeight;
            foreach (var attr in new[] { "src", "srcdoc", "name", "sandbox" })
            {
                var v = element.GetAttribute(attr);
                if (v != null) node.Attributes[attr] = v;
            }

            try
            {
                var srcdoc = element.GetAttribute("srcdoc");
                var src = element.GetAttribute("src");
                var baseForChild = _documentBaseUrl ?? _baseUrl ?? "about://lite/";
                // HTML §6.5: the frame's sandbox flag set is the parsed sandbox attribute unioned
                // with the flags the frame itself inherited; the child page enforces the set.
                var childSandbox = ParseSandboxFlags(element.GetAttribute("sandbox"), Current.InheritedSandbox);
                if (srcdoc is not null)
                    node.ChildPage = ParseChildPage(srcdoc, isSrcdoc: true, baseForChild, cw, ch, sandboxFlags: childSandbox);
                else if (!string.IsNullOrEmpty(src) && !src.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
                {
                    var childUrl = ResolveUrl(src) ?? src;
                    node.ChildPage = ParseChildPage(childUrl, isSrcdoc: false, childUrl, cw, ch, sandboxFlags: childSandbox);
                }
                else
                {
                    node.ChildPage = ParseChildPage("<!doctype html><html><head></head><body></body></html>",
                        isSrcdoc: true, baseForChild, cw, ch, sandboxFlags: childSandbox);
                    node.ChildPage.IsInitialAboutBlank = true;
                    node.ChildPage.Engine.SetCurrentUrl("about:blank");
                }
                node.ChildPage.SandboxFlags = childSandbox;
            }
            catch (Exception ex) { Console.WriteLine($"[iframe load] {ex.Message}"); }
        }

        // <audio> / <video> — replaced media elements. Capture reflected attributes, pick the
        // playable source (explicit src, else the first <source> with a supported type), and give
        // VIDEO a box (poster or 300×150). The backend/timeline is created lazily by JsElement.
        if (tag is "AUDIO" or "VIDEO")
        {
            foreach (var attr in new[] { "src", "controls", "autoplay", "loop", "muted",
                         "preload", "poster", "width", "height", "crossorigin", "data-duration" })
            {
                var v = element.GetAttribute(attr);
                if (v != null) node.Attributes[attr] = v;
            }

            var currentSrc = element.GetAttribute("src");
            if (string.IsNullOrEmpty(currentSrc))
            {
                foreach (var s in element.QuerySelectorAll("source"))
                {
                    var ss = s.GetAttribute("src");
                    if (string.IsNullOrEmpty(ss)) continue;
                    if (IsPlayableMediaType(s.GetAttribute("type"))) { currentSrc = ss; break; }
                }
            }
            // Resolve against the document base so the media backend gets an absolute URL
            // (VLC treats bare relative strings as filesystem paths, not http:// URLs).
            if (!string.IsNullOrEmpty(currentSrc)) node.Attributes["_currentSrc"] = ResolveAgainstBase(currentSrc);

            if (tag == "VIDEO")
            {
                var w = element.GetAttribute("width");
                var h = element.GetAttribute("height");
                node.StyleOverrides["display"] = "inline-block";
                node.StyleOverrides["width"] = string.IsNullOrEmpty(w) ? "300px" : (w.EndsWith("px") ? w : w + "px");
                node.StyleOverrides["height"] = string.IsNullOrEmpty(h) ? "150px" : (h.EndsWith("px") ? h : h + "px");
                var poster = element.GetAttribute("poster");
                if (!string.IsNullOrEmpty(poster)) node.Image = ResourceLoader.FetchImage(poster, _baseUrl, Session);
            }
            else // AUDIO renders a controls strip only when `controls` is present; otherwise no box.
            {
                if (element.HasAttribute("controls"))
                {
                    node.StyleOverrides["display"] = "inline-block";
                    node.StyleOverrides["width"] = "300px";
                    node.StyleOverrides["height"] = "54px";
                }
                else node.StyleOverrides["display"] = "none";
            }
        }

        // Apply CSS counters (counter-reset/counter-increment) in document order BEFORE traversing
        // children, and snapshot the resulting state onto the node (for counter()/counters() and the
        // ::before/::after generated content). The pushed instances are popped after the subtree.
        ApplyCounters(node, element, out var pushedCounters);
        Current.CounterScopes.Push(pushedCounters);
        // ::before comes next in document order: its counter properties apply now, inside this
        // element's scope, and its content reads the counters as they stand at that point.
        var beforeCounters = PseudoCounters(node, node.BeforeStyles, pushedCounters);
        var beforeQuoteDepth = AdvanceQuoteDepth(node, node.BeforeStyles);

        if (objectShowsImage || tag is "IFRAME" or "AUDIO" or "VIDEO")
        {
            // Replaced elements with no rendered children: a loaded <object> shows its image; an
            // <iframe> hosts a child Page; <audio>/<video> render their own controls/frame (their
            // <source>/<track> children and fallback text are inert).
        }
        else if (hasMixedChildren)
        {
            // Walk ChildNodes in DOM order so text nodes keep their position among element siblings.
            // e.g. <p>Hello <strong>world</strong>!</p> → [#TEXT("Hello"), strong, #TEXT("!")]
            var parentStyle = ComputeCurrentStyle(element);
            foreach (var childNode in element.ChildNodes)
            {
                if (childNode is IText textNode)
                {
                    // <script> is a raw-text element: its content is never collapsed — scripts
                    // read it back verbatim (document.currentScript.text) and whitespace can be
                    // syntactically significant (template strings, ASI hazards).
                    // CSS 2.1 §16.6: honor white-space (pre / pre-wrap / pre-line) on mixed-child
                    // text nodes too, not just leaf elements (line 1078).
                    var text = tag == "SCRIPT" ? textNode.Data : NormalizeTextWhitespace(textNode.Data, ws);
                    // Include any non-empty text — whitespace-only nodes (" ") between
                    // inline siblings need to produce a space; purely empty strings are skipped.
                    // Whitespace-only nodes between block siblings are filtered out later in
                    // LayoutChildren (runs consisting solely of whitespace nodes are skipped).
                    if (text.Length > 0)
                    {
                        var textChild = new LayoutNode(null, "#text", text, parentStyle) { DomNode = textNode };
                        textChild.ResetNonInheritedStyles();
                        textChild.StyleOverrides[AngleSharp.Css.PropertyNames.Display] = "inline";
                        node.AddChild(textChild);
                    }
                }
                else if (childNode is IElement childEl)
                {
                    var childTag = TagOf(childEl);
                    if (childTag == "SCRIPT")
                    {
                        var scriptNode = Traverse(childEl, indent + 1, fontPx, node);
                        node.AddChild(scriptNode);
                        CollectScript(childEl, scriptNode);
                        continue;
                    }
                    node.AddChild(Traverse(childEl, indent + 1, fontPx, node));
                }
                else if (childNode is IComment comment)
                {
                    // Comments never render but must stay in the projected tree on the first
                    // pass too — script's childNodes must see them after a plain parse, and the
                    // CharacterData anchors resolve through the wrapper's bound node. The
                    // re-projection path (ProjectChildren) carries the same shape. display:none
                    // takes the node out of every layout stage (the SkipTags mechanism): a
                    // projected comment is tree-visible and render-invisible (line-height-127).
                    var commentChild = new LayoutNode(null, "#comment", comment.Data ?? string.Empty, parentStyle)
                    {
                        DomNode = comment
                    };
                    commentChild.ResetNonInheritedStyles();
                    commentChild.StyleOverrides[AngleSharp.Css.PropertyNames.Display] = "none";
                    node.AddChild(commentChild);
                }
            }
            RestoreInlineBoundarySpaces(node);
        }
        else
        {
            foreach (var child in element.Children)
            {
                var childTag = TagOf(child);
                if (childTag == "SCRIPT")
                {
                    var scriptNode = Traverse(child, indent + 1, fontPx, node);
                    node.AddChild(scriptNode);
                    CollectScript(child, scriptNode);
                    continue;
                }
                node.AddChild(Traverse(child, indent + 1, fontPx, node));
            }
        }

        // <table cellspacing> / <table cellpadding>: HTML presentational hints for 'border-spacing'
        // on the table and 'padding' on every cell in it. Ignoring them left the UA defaults
        // (2px spacing, 1px cell padding) in place, so a `cellpadding="0" cellspacing="0"` table
        // sat 3px lower and wider than the CSS-equivalent it is meant to match.
        if (tag == "TABLE")
            ApplyTableSpacingAttributes(element, node);
        ApplyPresentationalHints(element, node, tag);
        // §6.2.1: the override channel can hold inherited values AngleSharp's cascade never saw
        // (:lang/:link/dynamic rules, presentational hints). The children's computed styles were
        // built from the parent's stale value, so carry the parent's effective values down. Runs
        // after the hints so the element's own attribute mapping is part of what it hands on.
        StyleResolver.InheritOverrideChannelIntoSubtree(node);

        // <picture>: now that the <img> and <source> children exist, pick the source the <img>
        // should display (media/type-based selection; falls back to the <img>'s own src).
        if (tag == "PICTURE")
            SelectPictureSource(element, node);

        // ::after follows the children: its counter properties apply after the whole subtree,
        // and a counter-reset it makes has no following siblings to scope over.
        var afterPushed = new List<string>();
        var afterCounters = PseudoCounters(node, node.AfterStyles, afterPushed);
        var afterQuoteDepth = AdvanceQuoteDepth(node, node.AfterStyles);
        PopCounters(afterPushed);

        // Leaving this node's subtree: pop the counter instances it introduced (scope ends).
        Current.CounterScopes.Pop();
        PopCounters(pushedCounters);

        // Create ::before and ::after pseudo-element children, each reading its own snapshot.
        CreatePseudoElementChildren(node, directTextRaw, beforeCounters, afterCounters, beforeQuoteDepth, afterQuoteDepth);
        // The list marker precedes ::before and the element's content (§12.5.1).
        CreateListMarkerChild(node, element);
        // ::first-letter runs after them: the spec includes ::before content in the first letter,
        // and the split needs the final child list.
        CreateFirstLetterChild(node);

        // Capture complete: only now is the rendering object bound to its DOM node, so any
        // later LayoutNode attribute write propagates to the authoritative AngleSharp element.
        node.DomNode = element;
        return node;
    }

    /// <summary>
    /// Maps an HTML <c>width</c>/<c>height</c> content attribute onto the node: a plain number is
    /// the replaced element's intrinsic size in px, a percentage is a CSS length that resolves
    /// against the containing block. Presentational hints lose to any author declaration, so they
    /// are only applied where the element does not already carry one.
    /// </summary>
    /// <summary>HTML's presentational hints (the 2014 rendering section, §10.3.x): legacy
    /// attributes such as table width, bgcolor, align and valign map to CSS declarations with
    /// the lowest author precedence — any author rule for the property wins. None were mapped,
    /// so a &lt;table width="300"&gt; shrank to its content (floats-wrap-bfc-001-*).</summary>
    private static void ApplyPresentationalHints(IElement element, LayoutNode node, string tag)
    {
        if (element.NamespaceUri is { } ns && ns != "http://www.w3.org/1999/xhtml") return;
        ICssStyleDeclaration? computed = null;
        void Hint(string property, string value)
        {
            computed ??= ComputeCurrentStyle(element);
            var declared = computed.GetPropertyValueSafe(property)?.Trim();
            if ((string.IsNullOrEmpty(declared) || declared == "auto") && !node.StyleOverrides.ContainsKey(property))
            {
                node.StyleOverrides[property] = value;
                node.HintAppliedProps.Add(property);
            }
        }
        static string? Length(string? raw)
        {
            raw = raw?.Trim();
            if (string.IsNullOrEmpty(raw)) return null;
            var digits = raw.TrimEnd('%');
            if (!double.TryParse(digits, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var n) || n < 0) return null;
            return raw.EndsWith('%') ? $"{digits}%" : $"{digits}px";
        }
        string? Attr(string name) => element.GetAttribute(name);

        if (tag is "TABLE" or "TD" or "TH" or "COL" or "COLGROUP" or "HR")
            if (Length(Attr("width")) is { } width) Hint("width", width);
        if (tag is "TABLE" or "TD" or "TH" or "TR")
            if (Length(Attr("height")) is { } height) Hint("height", height);
        if (tag is "BODY" or "TABLE" or "TR" or "TD" or "TH" && Attr("bgcolor") is { Length: > 0 } bgcolor)
            Hint("background-color", bgcolor.Trim());
        if (tag == "BODY")
        {
            if (Attr("text") is { Length: > 0 } text) Hint("color", text.Trim());
            // The link/vlink/alink attributes paint anchors: visited/unvisited distinction
            // needs history state the headless harness does not model, so the unvisited color
            // wins (a:visited styling is outside the headless scope).
            if (Attr("link") is { Length: > 0 } link) Hint("__link-color", link.Trim());
        }

        var align = Attr("align")?.Trim().ToLowerInvariant();
        if (tag is "TD" or "TH" or "TR" or "DIV" or "P" or "H1" or "H2" or "H3" or "H4" or "H5" or "H6" or "CAPTION" or "THEAD" or "TBODY" or "TFOOT"
            && align is "left" or "right" or "center" or "justify")
            Hint("text-align", align);
        var valign = Attr("valign")?.Trim().ToLowerInvariant();
        if (tag is "TD" or "TH" or "TR" or "THEAD" or "TBODY" or "TFOOT" && valign is "top" or "middle" or "bottom" or "baseline")
            Hint("vertical-align", valign);
        if (tag is "TD" or "TH" && element.HasAttribute("nowrap")) Hint("white-space", "nowrap");
        if (tag == "TABLE" && Attr("align")?.Trim().ToLowerInvariant() is { } tableAlign)
        {
            if (tableAlign is "left" or "right") Hint("float", tableAlign);
            else if (tableAlign == "center") { Hint("margin-left", "auto"); Hint("margin-right", "auto"); }
        }
        if (tag == "FONT")
        {
            if (Attr("color") is { Length: > 0 } color) Hint("color", color.Trim());
            if (Attr("face") is { Length: > 0 } face) Hint("font-family", face.Trim());
        }
    }

    private static void ApplyDimensionAttribute(IElement element, LayoutNode node, string attribute, string property)
    {
        var raw = element.GetAttribute(attribute)?.Trim();
        if (string.IsNullOrEmpty(raw)) return;

        if (raw.EndsWith("%", StringComparison.Ordinal) &&
            float.TryParse(raw[..^1], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var pct))
        {
            var declared = ComputeCurrentStyle(element).GetPropertyValueSafe(property)?.Trim();
            if (string.IsNullOrEmpty(declared) || declared == "auto")
                node.StyleOverrides[property] = $"{pct.ToString(System.Globalization.CultureInfo.InvariantCulture)}%";
            return;
        }
        if (int.TryParse(raw, out var px))
        {
            var declared = ComputeCurrentStyle(element).GetPropertyValueSafe(property)?.Trim();
            if (string.IsNullOrEmpty(declared) || declared == "auto")
                node.StyleOverrides[property] = $"{px}px";
            // With no bitmap to measure, the attribute is all the intrinsic size there is.
            if (node.Image is null)
            {
                if (property == "width") node.IntrinsicWidth = px;
                else node.IntrinsicHeight = px;
            }
        }
    }

    /// <summary>
    /// Splits a 'background' shorthand into the longhands the painter reads (§14.2). AngleSharp
    /// drops the whole declaration when any component is one it will not parse — `background:
    /// bottom green` loses the colour as well as the position — and the shorthand also has to
    /// reset the components it does not mention, which is what makes `background: green` clear an
    /// inherited-looking image. Tokens are classified by shape, so their order does not matter.
    /// </summary>
    private static void ExpandBackgroundShorthand(LayoutNode node, string value)
    {
        var longhands = SplitBackgroundShorthand(value);
        if (longhands is null) return;
        foreach (var (prop, val) in longhands)
        {
            if (val is null) node.StyleOverrides.Remove(prop);
            else node.StyleOverrides[prop] = val;
        }
    }

    /// <summary>Decomposes a `background` shorthand (§14.2) into its longhands. A null value in the
    /// result means "no declaration" (only ever background-color, which the shorthand leaves to
    /// whatever the cascade already had when it names no colour). Returns null for 'inherit',
    /// which the cascade resolves on its own, and for an empty value.</summary>
    private static List<(string Prop, string? Val)>? SplitBackgroundShorthand(string value)
    {
        var v = value.Trim();
        if (v.Length == 0) return null;
        if (v is "initial" or "unset" or "none")
            return
            [
                ("background-image", "none"),
                ("background-repeat", "repeat"),
                ("background-position", "0% 0%"),
                ("background-color", "transparent"),
            ];
        if (v == "inherit") return null;   // leave the cascade to it

        string? image = null, repeat = null, attachment = null, color = null;
        var position = new List<string>();

        foreach (var token in SplitBackgroundTokens(v))
        {
            var t = token.Trim();
            if (t.Length == 0) continue;
            var lower = t.ToLowerInvariant();

            if (lower.StartsWith("url(", StringComparison.Ordinal) || lower == "none") image = t;
            else if (lower is "repeat" or "repeat-x" or "repeat-y" or "no-repeat") repeat = lower;
            else if (lower is "scroll" or "fixed" or "local") attachment = lower;
            else if (lower is "left" or "right" or "top" or "bottom" or "center"
                     || lower.EndsWith("%", StringComparison.Ordinal)
                     || char.IsAsciiDigit(lower[0]) || lower[0] == '.' || lower[0] == '-')
                position.Add(lower);
            else color = t;   // a colour keyword or function
        }

        // The shorthand resets everything it does not name to its initial value.
        return
        [
            ("background-image", image ?? "none"),
            ("background-repeat", repeat ?? "repeat"),
            ("background-attachment", attachment ?? "scroll"),
            ("background-position", position.Count switch
            {
                0 => "0% 0%",
                1 => position[0],
                _ => $"{position[0]} {position[1]}",
            }),
            ("background-color", color),
        ];
    }

    /// <summary>Splits a shorthand value on whitespace while keeping bracketed functions
    /// (<c>url(…)</c>, <c>rgba(…)</c>) in one piece.</summary>
    private static List<string> SplitBackgroundTokens(string value)
    {
        var tokens = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '(') depth++;
            else if (c == ')') depth = Math.Max(0, depth - 1);
            else if (char.IsWhiteSpace(c) && depth == 0)
            {
                if (i > start) tokens.Add(value[start..i]);
                start = i + 1;
            }
        }
        if (start < value.Length) tokens.Add(value[start..]);
        return tokens;
    }

    /// <summary>
    /// Applies the <c>cellspacing</c> and <c>cellpadding</c> content attributes (HTML 4 §11.2.1):
    /// the first is 'border-spacing' on the table, the second 'padding' on each of its cells.
    /// Presentational hints lose to an author declaration, so a cell that sets its own padding
    /// keeps it. Called after the subtree is built, since it has to reach the cells.
    /// </summary>
    /// <summary>Re-runs the presentational-hint pass after a hint attribute changed: the
    /// previously stamped properties are retracted first so the new attribute value replaces
    /// the old one (body bgcolor yellow → green, body-bgcolor-attribute-change).</summary>
    internal static void RefreshPresentationalHints(LayoutNode node)
    {
        if (node.DomNode is not IElement element) return;
        foreach (var property in node.HintAppliedProps) node.StyleOverrides.Remove(property);
        node.HintAppliedProps.Clear();
        ApplyPresentationalHints(element, node, node.TagName);
    }

    private static void ApplyTableSpacingAttributes(IElement element, LayoutNode node)
    {
        if (element.GetAttribute("cellspacing")?.Trim() is { Length: > 0 } spacing &&
            float.TryParse(spacing.TrimEnd('%'), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var sp))
        {
            // An author declaration for 'border-spacing' is already in StyleOverrides (it is one of
            // the properties the engine cascades itself), so its absence there means the attribute
            // is free to apply. AngleSharp's computed value always reports something, so asking it
            // would suppress the hint every time.
            if (!node.StyleOverrides.ContainsKey("border-spacing"))
                node.StyleOverrides["border-spacing"] = $"{sp.ToString(System.Globalization.CultureInfo.InvariantCulture)}px";
        }

        if (element.GetAttribute("cellpadding")?.Trim() is not { Length: > 0 } padding ||
            !float.TryParse(padding.TrimEnd('%'), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var pad))
            return;

        var value = $"{pad.ToString(System.Globalization.CultureInfo.InvariantCulture)}px";
        void ApplyToCells(LayoutNode n)
        {
            foreach (var child in n.Children)
            {
                if (child.TagName is "TD" or "TH")
                    foreach (var side in new[] { "top", "right", "bottom", "left" })
                    {
                        // A cell that declares its own padding keeps it.
                        var own = child.Style.GetPropertyValueSafe($"padding-{side}")?.Trim();
                        if (string.IsNullOrEmpty(own) || own == "1px") child.StyleOverrides[$"padding-{side}"] = value;
                    }
                // A nested table has its own cellpadding; do not reach into it.
                if (child.TagName != "TABLE") ApplyToCells(child);
            }
        }
        ApplyToCells(node);
    }

    /// <summary>The initial font size — the value the root element inherits.</summary>
    internal const float DefaultFontSizePx = 16f;

    /// <summary>
    /// The used font size of <paramref name="element"/> in px: its OWN cascaded 'font-size'
    /// declaration resolved against <paramref name="parentPx"/>, or the inherited
    /// <paramref name="parentPx"/> when it declares none. Only a declaration on the element
    /// itself may re-scale a relative unit — an inherited 'font-size' is already a computed
    /// length and must not be applied a second time.
    /// </summary>
    private static float ResolveOwnFontSize(IElement element, float parentPx)
    {
        var declared = OwnFontSizeDeclaration(element);
        return declared is null ? parentPx : ResolveFontSizeValue(declared, parentPx);
    }

    /// <summary>The line-height component of the winning 'font' shorthand (§15.8's optional
    /// <c>/<line-height></c>), or null. The declaration block keeps the shorthand as one value
    /// and nothing expanded it, so <c>font: 20px/1 Ahem</c> left line-height at 'normal' — every
    /// Ahem vertical-alignment/leading reftest then drew its boxes one half-leading off. A direct
    /// 'line-height' longhand participates in the same cascade and wins when it outranks the
    /// shorthand.</summary>
    private static string? OwnFontLineHeightDeclaration(IElement element)
    {
        string? best = null;
        var bestKey = (Important: false, Specificity: -1, Order: -1);

        foreach (var rule in CssRules)
        {
            // A 'font' shorthand without an explicit /<line-height> component RESETS
            // line-height to 'normal' (§15.8: the shorthand sets every font longhand) — the
            // span of font-044 inherited its parent's line-height: 1 and its message-box
            // content area stopped covering the inline-block's red box. The shorthand's
            // line-height value is its /<line-height> component, or 'normal' when unstated.
            var isFont = rule.Properties.TryGetValue("font", out var font) &&
                         !string.IsNullOrWhiteSpace(font) && IsValidFontShorthand(font);
            var direct = rule.Properties.TryGetValue("line-height", out var lh) &&
                         !string.IsNullOrWhiteSpace(lh);
            if (!isFont && !direct) continue;
            try { if (!element.Matches(rule.Selector)) continue; }
            catch { continue; }
            var key = (rule.ImportantProps.Contains("line-height") || rule.ImportantProps.Contains("font"),
                       rule.Specificity, rule.Order);
            if (best is null || key.CompareTo(bestKey) > 0)
            {
                best = direct ? lh : (LineHeightComponentOf(font!) ?? "normal");
                bestKey = key;
            }
        }

        // An inline declaration outranks every non-important rule (CSS 2.1 §6.4.3).
        if (element.GetAttribute("style") is { Length: > 0 } inline && !bestKey.Important)
        {
            var (props, _) = ParseDeclarations(inline);
            if (props.TryGetValue("line-height", out var direct) && !string.IsNullOrWhiteSpace(direct))
                return direct;
            if (props.TryGetValue("font", out var font) && !string.IsNullOrWhiteSpace(font) &&
                IsValidFontShorthand(font))
                return LineHeightComponentOf(font) ?? "normal";
        }
        return best;
    }

    /// <summary>The value of the 'font' declaration in a rule's raw CSS text, or null. Splits on
    /// top-level semicolons only (strings and parens are respected), so a quoted family containing
    /// ';' cannot split the declaration.</summary>
    private static string? FontDeclarationValue(string cssText)
    {
        foreach (var decl in SplitTopLevelDeclarations(cssText))
        {
            var colon = decl.IndexOf(':');
            if (colon <= 0) continue;
            if (!decl[..colon].Trim().Equals("font", StringComparison.OrdinalIgnoreCase)) continue;
            var value = decl[(colon + 1)..].Trim();
            // Strip a trailing !important (the cascade already tracks importance separately).
            var bang = value.LastIndexOf('!');
            if (bang > 0 && value[bang..].Trim().StartsWith("!important", StringComparison.OrdinalIgnoreCase))
                value = value[..bang].Trim();
            return value.Length == 0 ? null : value;
        }
        return null;
    }

    /// <summary>Splits a declaration block body on semicolons that are not inside strings,
    /// comments or parentheses.</summary>
    private static IEnumerable<string> SplitTopLevelDeclarations(string css)
    {
        var depth = 0;
        var start = 0;
        char quote = '\0';
        for (var i = 0; i < css.Length; i++)
        {
            var ch = css[i];
            if (quote != '\0')
            {
                if (ch == '\\') i++;
                else if (ch == quote) quote = '\0';
                continue;
            }
            switch (ch)
            {
                case '"' or '\'': quote = ch; break;
                case '(': depth++; break;
                case ')': depth = Math.Max(0, depth - 1); break;
                case ';' when depth == 0:
                    yield return css[start..i];
                    start = i + 1;
                    break;
            }
        }
        if (start < css.Length) yield return css[start..];
    }

    /// <summary>Extracts the <c>/<line-height></c> component of a 'font' shorthand value
    /// (§15.8): a unitless multiplier, a length or a percentage; negative is invalid. The slash
    /// may carry whitespace on either side — the CSSOM serializes the shorthand as
    /// <c>font: 20px / 1 Ahem</c> — so the component is the next token AFTER the slash, not the
    /// remainder of the slash's own whitespace token.</summary>
    private static string? LineHeightComponentOf(string shorthand)
    {
        var slash = shorthand.IndexOf('/');
        if (slash < 0 || slash == shorthand.Length - 1) return null;
        var rest = shorthand[(slash + 1)..].TrimStart();
        if (rest.Length == 0) return null;
        var end = rest.IndexOfAny(new[] { ' ', '\t' });
        var lh = (end < 0 ? rest : rest[..end]).Trim();
        if (lh.Length == 0) return null;
        var numeric = lh.TrimEnd('%');
        if (float.TryParse(numeric, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v) && v >= 0f)
            return lh;
        return null;
    }

    /// <summary>The winning 'font-size' declaration for the element itself (inline style, then
    /// author/UA rules ordered by !important, specificity and source order), or null when none
    /// of them names the property.</summary>
    private static string? OwnFontSizeDeclaration(IElement element)
    {
        string? best = null;
        var bestKey = (Important: false, Specificity: -1, Order: -1);

        foreach (var rule in CssRules)
        {
            if (FontSizeOf(rule.Properties) is not { } val) continue;
            try { if (!element.Matches(rule.Selector)) continue; }
            catch { continue; }
            var key = (rule.ImportantProps.Contains("font-size") || rule.ImportantProps.Contains("font"),
                       rule.Specificity, rule.Order);
            if (best is null || key.CompareTo(bestKey) > 0) { best = val; bestKey = key; }
        }

        // An inline declaration outranks every non-important rule (CSS 2.1 §6.4.3).
        if (element.GetAttribute("style") is { Length: > 0 } inline && !bestKey.Important)
        {
            var (props, _) = ParseDeclarations(inline);
            if (FontSizeOf(props) is { } iv) return iv;
        }
        return best;
    }

    /// <summary>The font-size a declaration block sets, either directly or through the 'font'
    /// shorthand (§15.8) — <c>font: 1in Ahem</c> is how most of the CSS 2.1 suite sizes text, and
    /// missing it would leave those elements at the inherited size.</summary>
    private static string? FontSizeOf(Dictionary<string, string> props)
    {
        if (props.TryGetValue("font-size", out var direct) && !string.IsNullOrWhiteSpace(direct))
            return direct;
        if (!props.TryGetValue("font", out var shorthand) || string.IsNullOrWhiteSpace(shorthand) ||
            !IsValidFontShorthand(shorthand))
            return null;
        // <style> <variant> <weight> <size>[/<line-height>] <family>: the size is the last token
        // before the family list, so scan for the first token that parses as a length/percentage.
        foreach (var token in shorthand.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var t = token.Split('/')[0].Trim();
            if (t.Length == 0 || t.EndsWith(",", StringComparison.Ordinal)) continue;
            if (t.EndsWith("%", StringComparison.Ordinal)) return t;
            if (char.IsAsciiDigit(t[0]) || t[0] == '.') return t;
            if (t.ToLowerInvariant() is "xx-small" or "x-small" or "small" or "medium" or "large"
                or "x-large" or "xx-large" or "larger" or "smaller") return t;
        }
        return null;
    }

    // CSS 2.1 §15.7 absolute-size table (xx-small .. xx-large). 'larger' and 'smaller' step
    // through these entries when the parent's computed size sits within the table, and fall back
    // to the 1.2 ratio outside it (font-size-rule-002, font-size-121).
    private static readonly float[] AbsoluteFontSizes = [9f, 10f, 13f, 16f, 18f, 24f, 32f];

    /// <summary>Resolves one 'font-size' value against the parent's computed size, including the
    /// absolute/relative keywords (§15.7).</summary>
    private static float ResolveFontSizeValue(string value, float parentPx)
    {
        value = value.Trim();
        switch (value.ToLowerInvariant())
        {
            case "xx-small": return AbsoluteFontSizes[0];
            case "x-small": return AbsoluteFontSizes[1];
            case "small": return AbsoluteFontSizes[2];
            case "medium": return AbsoluteFontSizes[3];
            case "large": return AbsoluteFontSizes[4];
            case "x-large": return AbsoluteFontSizes[5];
            case "xx-large": return AbsoluteFontSizes[6];
            // The table steps apply only while the parent's size lies within the table's range;
            // outside it (a 20em parent, font-size-119) the 1.2 ratio applies, so 'smaller' from
            // 320px is 266.67px, not the table's top entry.
            case "larger":
                if (parentPx >= AbsoluteFontSizes[0] - 0.01f)
                    for (var i = 0; i < AbsoluteFontSizes.Length; i++)
                        if (AbsoluteFontSizes[i] > parentPx + 0.01f) return AbsoluteFontSizes[i];
                return parentPx * 1.2f;
            case "smaller":
                if (parentPx <= AbsoluteFontSizes[^1] + 0.01f)
                    for (var i = AbsoluteFontSizes.Length - 1; i >= 0; i--)
                        if (AbsoluteFontSizes[i] < parentPx - 0.01f) return AbsoluteFontSizes[i];
                return parentPx / 1.2f;
            case "inherit": case "unset": return parentPx;
            case "initial": return DefaultFontSizePx;
        }
        if (value.EndsWith("%", StringComparison.Ordinal) &&
            float.TryParse(value[..^1], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var pct))
            return parentPx * pct / 100f;
        // em/ex resolve against the PARENT size for this property alone; rem against the root.
        // A used size of 0 is legitimate ('font-size: 0' hides text) — only an unparseable value
        // falls back to the inherited size, and a negative one is invalid, so it clamps to 0.
        return CssUnits.TryParse(value, parentPx, parentPx, 0, 0, out var px) ? Math.Max(0f, px) : parentPx;
    }

    private static void CollectScript(IElement scriptEl, LayoutNode scriptNode)
    {
        if (Current.IsFragment) return;
        var continuation = ScriptContinuation(scriptEl);
        var type = scriptEl.GetAttribute("type");
        bool isModule = string.Equals(type, "module", StringComparison.OrdinalIgnoreCase);
        var src = scriptEl.GetAttribute("src");
        // defer/async only apply to external (src) scripts; they are ignored on inline scripts.
        bool hasSrc = !string.IsNullOrEmpty(src);
        bool isAsync = (hasSrc || isModule) && scriptEl.HasAttribute("async");
        bool isDefer = hasSrc && scriptEl.HasAttribute("defer");
        // crossorigin only matters where a fetch carries it; empty string means "anonymous".
        var crossOrigin = scriptEl.GetAttribute("crossorigin");

        if (src != null)
        {
            // Handle data: URIs inline (HttpClient doesn't support them)
            if (src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                var code = DecodeDataUri(src);
                if (string.IsNullOrWhiteSpace(code)) return;
                if (isModule) (isAsync ? _asyncScripts : _deferredScripts).Add(new(code, src, true,
                    Node: scriptNode, Continuation: continuation));
                else BucketClassic(code, src, isAsync, isDefer, scriptNode, continuation);
                return;
            }

            var scriptUrl = ResolveUrl(src);
            if (scriptUrl != null)
            {
                if (isModule)
                {
                    // Let the module loader fetch it on import (so its own imports resolve).
                    (isAsync ? _asyncScripts : _deferredScripts).Add(new(null, scriptUrl, true,
                        CrossOrigin: crossOrigin, Node: scriptNode, Continuation: continuation));
                    return;
                }
                try
                {
                    // Follow the fetch to its final response: after a redirect the script's own
                    // URL (and therefore the base for its dynamic imports) is the response URL,
                    // while the request URL stays the script element's src for currentScript.
                    using var response = Session.Client.GetAsync(scriptUrl).Result;
                    var code = response.Content.ReadAsStringAsync().Result;
                    var responseUrl = response.RequestMessage?.RequestUri?.AbsoluteUri ?? scriptUrl;
                    if (!string.IsNullOrWhiteSpace(code))
                        BucketClassic(code, responseUrl, isAsync, isDefer, scriptNode, continuation, scriptUrl);
                }
                catch (Exception ex)
                {
                    Session.Diagnostics.Enqueue($"script {scriptUrl}: {ex.Message}");
                    Console.WriteLine($"[Script load error] {scriptUrl}: {ex.Message}");
                }
            }
        }
        else if (!string.IsNullOrWhiteSpace(scriptEl.TextContent))
        {
            // Inline scripts always run in document position (defer/async do not apply).
            var inlineCode = StripCdata(scriptEl.TextContent);
            if (isModule) (isAsync ? _asyncScripts : _deferredScripts).Add(new(inlineCode, NextInlineModuleSpecifier(), true, true,
                Node: scriptNode, Continuation: continuation));
            else _pendingScripts.Add(new(inlineCode, _documentBaseUrl ?? _baseUrl ?? "about:blank", Inline: true,
                Node: scriptNode, Continuation: continuation));
        }
    }

    /// <summary>The raw source between a script element's end tag and the next script start:
    /// a parse-time document.write splices into the tokenizer at this exact point, so the
    /// words that follow the writing script complete a tag/attribute/entity/rawtext run the
    /// script left open (document-write/019-031, 042-043, 050-051).</summary>
    private static string? ScriptContinuation(IElement scriptEl)
    {
        var source = Current.Source;
        if (source is null) return null;
        var start = IndexOfStartTag(source, "<script", Math.Clamp(Current.SourceCursor, 0, source.Length));
        if (start < 0 || start >= source.Length) return null;
        var endTag = source.IndexOf("</script", start, StringComparison.OrdinalIgnoreCase);
        if (endTag < 0) return null;
        var close = source.IndexOf('>', endTag + 8);
        var end = close < 0 ? source.Length : close + 1;
        Current.SourceCursor = end;
        var next = IndexOfStartTag(source, "<script", end);
        return source[end..(next < 0 ? source.Length : next)];
    }

    private static int IndexOfStartTag(string source, string name, int from)
    {
        for (var i = source.IndexOf(name, from, StringComparison.OrdinalIgnoreCase); i >= 0;
             i = source.IndexOf(name, i + 1, StringComparison.OrdinalIgnoreCase))
        {
            var after = i + name.Length;
            if (after >= source.Length || char.IsWhiteSpace(source[after]) || source[after] is '>' or '/')
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Unwraps the <c>&lt;![CDATA[ … ]]&gt;</c> section XHTML documents wrap inline scripts in, so
    /// the markup stays well-formed XML. The characters are not JavaScript: left in place the whole
    /// script fails to parse on its first token, and any function it defines never exists. Most of
    /// the CSS 2.1 suite's scripted tests are XHTML and written this way.
    /// </summary>
    private static string StripCdata(string code)
    {
        var trimmed = code.Trim();
        if (!trimmed.StartsWith("<![CDATA[", StringComparison.Ordinal)) return code;
        var end = trimmed.LastIndexOf("]]>", StringComparison.Ordinal);
        return end < 9 ? trimmed[9..] : trimmed[9..end];
    }

    /// <summary>Routes an external classic script's code into the in-position, deferred, or async
    /// execution bucket (HTML §"prepare the script element").</summary>
    private static void BucketClassic(string code, string url, bool isAsync, bool isDefer,
        LayoutNode? node = null, string? continuation = null, string? requestedUrl = null)
    {
        var script = new ScriptRecord(code, url, RequestedUrl: requestedUrl, Node: node, Continuation: continuation);
        if (isAsync) _asyncScripts.Add(script);
        else if (isDefer) _deferredScripts.Add(script);
        else _pendingScripts.Add(script);
    }

    /// <summary>Builds a unique absolute specifier for an inline module so its relative
    /// imports resolve against the page's base URL.</summary>
    private static string NextInlineModuleSpecifier()
    {
        var name = $"__inline_module_{_inlineModuleCounter++}.js";
        if (_baseUrl is not null && Uri.TryCreate(new Uri(_baseUrl), name, out var uri))
            return uri.AbsoluteUri;
        return name;
    }

    /// <summary>Decodes a data: URI and returns the text content.</summary>
    private static string DecodeDataUri(string dataUri)
    {
        // data:[<mediatype>][;base64],<data>
        var afterScheme = dataUri.AsSpan(5); // skip "data:"
        var commaIdx = afterScheme.IndexOf(',');
        if (commaIdx < 0) return string.Empty;

        var meta = afterScheme[..commaIdx].ToString();
        var data = afterScheme[(commaIdx + 1)..].ToString();

        if (meta.Contains("base64", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                // URL-decode first, then base64-decode, stripping whitespace
                var urlDecoded = Uri.UnescapeDataString(data);
                var cleaned = System.Text.RegularExpressions.Regex.Replace(urlDecoded, @"\s+", "");
                var bytes = Convert.FromBase64String(cleaned);
                return System.Text.Encoding.UTF8.GetString(bytes);
            }
            catch { return string.Empty; }
        }
        else
        {
            return Uri.UnescapeDataString(data);
        }
    }

    // ── Transition / Animation parsing ───────────────────────────────────────

    /// <summary>
    /// Parses `transition` and `animation` declarations from a rule's CssText
    /// and stores them on the node so <see cref="AnimationEngine"/> can use them.
    /// </summary>
    private static void ExtractTransitionAndAnimation(string cssText, LayoutNode node)
    {
        if (string.IsNullOrEmpty(cssText)) return;

        var props = ParseCssTextToDict(cssText);

        if (props.TryGetValue("transition", out var transVal))
            node.TransitionSpecs.AddRange(ParseTransitionValue(transVal));

        if (props.TryGetValue("animation", out var animVal))
            node.AnimationSpecs.AddRange(ParseAnimationValue(animVal));
    }

    /// <summary>
    /// Parses a `transition` value (possibly comma-separated) into <see cref="TransitionSpec"/> entries.
    /// Format per entry: property duration timing-function delay
    /// </summary>
    private static IEnumerable<TransitionSpec> ParseTransitionValue(string value)
    {
        // Split on commas that are NOT inside parentheses
        foreach (var segment in SplitOutsideParens(value, ','))
        {
            var tokens = segment.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) continue;

            var property = tokens[0].ToLowerInvariant();
            var duration = tokens.Length > 1 ? ParseSeconds(tokens[1]) : 0f;
            var timingFunc = tokens.Length > 2 ? tokens[2] : "ease";
            var delay = tokens.Length > 3 ? ParseSeconds(tokens[3]) : 0f;

            if (duration > 0 || delay > 0)
                yield return new TransitionSpec(property, duration, delay, timingFunc);
        }
    }

    /// <summary>
    /// Parses an `animation` value (possibly comma-separated) into <see cref="AnimationSpec"/> entries.
    /// Format per entry: name duration timing-function delay iteration-count direction fill-mode
    /// </summary>
    private static IEnumerable<AnimationSpec> ParseAnimationValue(string value)
    {
        foreach (var segment in SplitOutsideParens(value, ','))
        {
            var tokens = segment.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) continue;

            // Heuristic token assignment — browsers are flexible about order.
            // We scan tokens to identify each role.
            string name = "none";
            float duration = 0f;
            float delay = 0f;
            string timingFunc = "ease";
            int iterations = 1;
            bool alternate = false;
            bool fillForwards = false;

            var timesSeen = 0; // first time-value = duration, second = delay
            foreach (var tok in tokens)
            {
                var t = tok.ToLowerInvariant();
                if (t == "none") continue;
                if (t == "infinite") { iterations = -1; continue; }
                if (t == "alternate" || t == "alternate-reverse") { alternate = true; continue; }
                if (t == "reverse") continue;
                if (t == "forwards" || t == "both") { fillForwards = true; continue; }
                if (t is "backwards" or "normal") continue;
                if (t is "ease" or "linear" or "ease-in" or "ease-out" or "ease-in-out" or
                    "step-start" or "step-end" || t.StartsWith("cubic-bezier("))
                {
                    timingFunc = t;
                    continue;
                }
                if (IsTimeValue(tok))
                {
                    var secs = ParseSeconds(tok);
                    if (timesSeen == 0) duration = secs;
                    else delay = secs;
                    timesSeen++;
                    continue;
                }
                if (int.TryParse(t, System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out var n) && n >= 0)
                {
                    iterations = n;
                    continue;
                }
                // Anything else is the animation name
                name = tok;
            }

            if (name != "none" && duration > 0)
                yield return new AnimationSpec(name, duration, delay, timingFunc,
                    iterations, alternate, fillForwards);
        }
    }

    private static bool IsTimeValue(string tok)
    {
        var t = tok.ToLowerInvariant();
        return t.EndsWith("ms") || t.EndsWith('s');
    }

    private static float ParseSeconds(string tok)
    {
        tok = tok.Trim().ToLowerInvariant();
        if (tok.EndsWith("ms") &&
            float.TryParse(tok[..^2], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var ms))
            return ms / 1000f;
        if (tok.EndsWith('s') &&
            float.TryParse(tok[..^1], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var s))
            return s;
        return 0f;
    }

    /// <summary>Parses a CSS declaration block string into a property→value dictionary.</summary>
    internal static Dictionary<string, string> ParseCssTextToDict(string cssText)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(cssText)) return dict;

        foreach (var decl in cssText.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var colon = decl.IndexOf(':');
            if (colon < 0) continue;
            var prop = decl[..colon].Trim().ToLowerInvariant();
            var val = decl[(colon + 1)..].Trim().Replace("!important", "").Trim();
            if (!string.IsNullOrEmpty(val))
                dict[prop] = val;
        }
        return dict;
    }

    /// <summary>
    /// Splits a CSS value string on a delimiter character, ignoring occurrences inside parentheses.
    /// </summary>
    private static IEnumerable<string> SplitOutsideParens(string value, char delimiter)
    {
        var depth = 0;
        var start = 0;
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] == '(') depth++;
            else if (value[i] == ')') depth--;
            else if (value[i] == delimiter && depth == 0)
            {
                yield return value[start..i];
                start = i + 1;
            }
        }
        if (start < value.Length)
            yield return value[start..];
    }

    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Collapses runs of whitespace to a single space but does NOT trim boundary spaces.
    /// Boundary spaces are significant in inline content (e.g. " and " between two inline elements).
    /// </summary>
    /// <summary>Generated text goes through §16.6 white-space processing like any other text,
    /// under the pseudo-element's own 'white-space' (content-white-space-001: a pre-line
    /// string's runs of spaces collapse and its "\A" stays a forced break).</summary>
    private static void NormalizeGeneratedWhitespace(LayoutNode pseudo)
    {
        var text = pseudo.DisplayText;
        if (string.IsNullOrEmpty(text)) return;
        var normalized = NormalizeTextWhitespace(text, pseudo.GetWhiteSpace() switch
        {
            WhiteSpace.Pre => "pre",
            WhiteSpace.PreWrap => "pre-wrap",
            WhiteSpace.PreLine => "pre-line",
            _ => "normal",
        });
        if (normalized != text) pseudo.TextOverride = normalized;
    }

    /// <summary>§16.6.1 removes collapsible spaces only at the start and end of a LINE. A leaf's
    /// own text was trimmed as if it stood at a line edge, so "&lt;span&gt;x &lt;/span&gt;&lt;span&gt;y&lt;/span&gt;"
    /// lost the space between the words (word-spacing-007's first line came out 16px short).
    /// Put one back where an inline neighbour continues the line and brings no space itself.</summary>
    private static void RestoreInlineBoundarySpaces(LayoutNode parent)
    {
        static bool InlineLevel(LayoutNode n) => n.TagName == "#text" ||
            n.GetDisplay() is DisplayType.Inline or DisplayType.InlineBlock or DisplayType.InlineTable or DisplayType.InlineFlex;
        static bool StartsWithSpace(LayoutNode n) => n.TagName == "#text"
            ? n.DisplayText.Length > 0 && Array.IndexOf(CssWhiteSpace, n.DisplayText[0]) >= 0
            : n.TrimmedLeadingSpace;
        // What the previous sibling RENDERS: a trailing space it deferred to this node is not there.
        static bool EndsWithSpace(LayoutNode n) =>
            n.DisplayText.Length > 0 && Array.IndexOf(CssWhiteSpace, n.DisplayText[^1]) >= 0;
        var kids = parent.Children;
        for (var i = 0; i < kids.Count; i++)
        {
            var child = kids[i];
            if (child.TagName.StartsWith('#') || child.Children.Count > 0 || child.DisplayText.Length == 0 ||
                child.GetDisplay() != DisplayType.Inline)
                continue;
            if (child.TrimmedTrailingSpace && i + 1 < kids.Count && InlineLevel(kids[i + 1]) && !StartsWithSpace(kids[i + 1]))
                child.TextOverride = child.DisplayText + " ";
            if (child.TrimmedLeadingSpace && i > 0 && InlineLevel(kids[i - 1]) && !EndsWithSpace(kids[i - 1]))
                child.TextOverride = " " + child.DisplayText;
        }
    }

    /// <summary>
    /// Applies CSS 2.1 §16.6 white-space processing to an element's own text.
    /// <c>pre</c>/<c>pre-wrap</c> preserve everything; <c>pre-line</c> collapses spaces and tabs
    /// but keeps newlines as forced breaks; anything else collapses all runs to a single space.
    /// </summary>
    private static string NormalizeTextWhitespace(string text, string? whiteSpace)
    {
        if (whiteSpace is "pre" or "pre-wrap") return text;
        if (whiteSpace is not "pre-line") return CollapseWhitespace(text);

        // pre-line: collapse horizontal whitespace only, keeping '\n'.
        var sb = new System.Text.StringBuilder(text.Length);
        var lastWasSpace = false;
        foreach (var ch in text)
        {
            if (ch == '\n')
            {
                // §16.6.1: a space immediately before or after a line feed is removed — drop the
                // collapsed space already emitted, and let lastWasSpace swallow the ones after.
                if (lastWasSpace && sb.Length > 0 && sb[^1] == ' ') sb.Length--;
                sb.Append('\n');
                lastWasSpace = true;
                continue;
            }
            if (ch == '\r') continue;
            if (IsCollapsibleSpace(ch))
            {
                if (!lastWasSpace) { sb.Append(' '); lastWasSpace = true; }
            }
            else { sb.Append(ch); lastWasSpace = false; }
        }
        return sb.ToString();
    }

    /// <summary>The characters §16.6.1 processes: space, tab and the line breaks (plus form
    /// feed, white space to the tokenizer). Everything else char.IsWhiteSpace accepts is an
    /// ordinary character that must survive as authored: U+00A0 must never collapse or become
    /// a break opportunity, and neither may U+2000..U+200A or U+3000
    /// (white-space-processing-054: three EN QUADs between "XX" and "XX").</summary>
    private static bool IsCollapsibleSpace(char ch) => ch is ' ' or '\t' or '\n' or '\r' or '\f';

    /// <summary>The characters a line-edge trim may remove — never U+00A0, which a cell's
    /// leading "&amp;nbsp;Text" relies on (color-applies-to-006 lost its space to string.Trim()).</summary>
    private static readonly char[] CssWhiteSpace = [' ', '\t', '\n', '\r', '\f'];

    private static string CollapseWhitespace(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var sb = new System.Text.StringBuilder(text.Length);
        var lastWasSpace = false;
        foreach (var ch in text)
        {
            if (IsCollapsibleSpace(ch))
            {
                if (!lastWasSpace) { sb.Append(' '); lastWasSpace = true; }
            }
            else
            {
                sb.Append(ch);
                lastWasSpace = false;
            }
        }
        return sb.ToString();
    }

    private static readonly HashSet<string> SvgTags =
        ["SVG", "RECT", "CIRCLE", "ELLIPSE", "LINE", "POLYLINE", "POLYGON", "PATH", "G", "TEXT", "TSPAN", "DEFS", "USE", "CLIPPATH", "MASK", "PATTERN", "LINEARGRADIENT", "RADIALGRADIENT", "STOP"];

    private static bool IsSvgElement(string tagName) => SvgTags.Contains(tagName);

    /// <summary>
    /// Parses an HTML fragment string into a list of <see cref="LayoutNode"/>s, cascaded
    /// against the current page's stylesheets. Used by Element.innerHTML / insertAdjacentHTML.
    /// Per HTML semantics, &lt;script&gt; elements in the fragment are parsed but not executed.
    /// </summary>
    internal static List<LayoutNode> ParseFragment(string html, string contextTag = "DIV", DocumentState? owner = null,
        bool includeScripts = false)
    {
        var previous = Current;
        if (owner is not null)
        {
            Current = owner.ParserContext ?? new ParseState
            {
                Document = owner.Document,
                BaseUrl = owner.Address,
                DocumentBaseUrl = owner.BaseUrl,
                Session = owner.Session
            };
            if (owner.ParserContext is null) Current.CssRules.AddRange(owner.StyleRules);
        }
        var wasFragment = Current.IsFragment;
        Current.IsFragment = true;
        try
        {
            var nodes = ParseFragmentCore(html, contextTag, includeScripts);
            if (owner is not null) foreach (var node in nodes)
            {
                owner.Bind(node);
                if (owner.Engine is { } engine) QueueParsedDetailsNotifications(node, engine);
            }
            return nodes;
        }
        finally { Current.IsFragment = wasFragment; Current = previous; }
    }

    /// <summary>Projects existing DOM children into rendering objects after an HTML mutation.
    /// This never reparses markup: wrappers retain the original AngleSharp node identities.</summary>
    internal static List<LayoutNode> ProjectChildren(IElement context, DocumentState owner)
    {
        var previous = Current;
        Current = owner.ParserContext ?? new ParseState
        {
            Document = owner.Document,
            BaseUrl = owner.Address,
            DocumentBaseUrl = owner.BaseUrl,
            Session = owner.Session
        };
        if (owner.ParserContext is null) Current.CssRules.AddRange(owner.StyleRules);
        // Projecting DOM children is a fragment operation: scripts found in the subtree must be
        // parsed but never queued for execution (ParseFragment sets the same flag). Without it,
        // CollectScript would append to _pendingScripts while ParseOpenedDocument is still
        // enumerating that queue — "Collection was modified" — and would run markup-authored
        // script text that innerHTML must not execute.
        var wasFragment = Current.IsFragment;
        Current.IsFragment = true;
        var savedCounters = _counters.ToDictionary(kv => kv.Key, kv => new List<int>(kv.Value));
        var savedVerbose = _verbose;
        _verbose = false;
        var result = new List<LayoutNode>();
        try
        {
            var children = context is AngleSharp.Html.Dom.IHtmlTemplateElement template
                ? template.Content.ChildNodes : context.ChildNodes;
            foreach (var child in children)
            {
                if (child is IText text)
                {
                    var rendered = CollapseWhitespace(text.Data);
                    if (rendered.Length == 0) continue;
                    var projection = new LayoutNode(null, "#text", rendered, ComputeCurrentStyle(context))
                    {
                        DomNode = text
                    };
                    projection.StyleOverrides["display"] = "inline";
                    result.Add(projection);
                }
                else if (child is IElement element)
                    result.Add(Traverse(element, 0));
            }
            foreach (var node in result) owner.Bind(node);
            return result;
        }
        finally
        {
            Current.IsFragment = wasFragment;
            _verbose = savedVerbose;
            _counters.Clear();
            foreach (var kv in savedCounters) _counters[kv.Key] = kv.Value;
            Current = previous;
        }
    }

    /// <summary>
    /// Re-projects an already-rendered element's DOM children onto its LayoutNode after a DOM
    /// mutation (appendChild/insertBefore/…). Unlike <see cref="ProjectChildren"/> — a full
    /// rebuild for never-rendered markup — this reuses the LayoutNode already bound to each DOM
    /// child (first-bind-wins, <see cref="DocumentState.FindBound"/>) and rebuilds the child
    /// list in DOM order. That is what repairs the stale topology the layout normalizers leave
    /// behind: the block-in-inline splitter (§9.2.1.1) detaches the original inline element node
    /// in favour of anonymous pieces, and the anonymous-table wrappers splice children into
    /// <c>#anon-*</c> boxes, so a direct LayoutNode splice can no longer find its reference node
    /// (it lives inside a piece) and falls off the end of the list. Re-projection restores DOM
    /// order after every mutation; the normalizers then regenerate their anonymous boxes from
    /// the repaired tree on the next layout pass.
    /// </summary>
    private static bool IsInLiveLayoutTree(LayoutNode node)
    {
        var cur = node;
        while (cur.Parent is { } parent)
        {
            if (!parent.Children.Contains(cur)) return false;
            cur = parent;
        }
        return true;
    }

    internal static void ReProjectChildren(IElement context, DocumentState owner)
    {
        var layout = owner.FindBound(context);
        if (layout is null) return; // never rendered — nothing to re-project

        // A layout normalizer (e.g. block-in-inline splitting) may have evicted this element's
        // LayoutNode from its parent's Children and replaced it with split pieces. Re-projecting
        // the detached node alone would leave the live tree stale; walk up to the nearest
        // ancestor still connected in the live layout tree so the whole split container rebuilds.
        while (!IsInLiveLayoutTree(layout) && context.ParentElement is { } parentEl)
        {
            var parentLayout = owner.FindBound(parentEl);
            if (parentLayout is null) break;
            context = parentEl;
            layout = parentLayout;
        }

        var previous = Current;
        Current = owner.ParserContext ?? new ParseState
        {
            Document = owner.Document,
            BaseUrl = owner.Address,
            DocumentBaseUrl = owner.BaseUrl,
            Session = owner.Session
        };
        if (owner.ParserContext is null) Current.CssRules.AddRange(owner.StyleRules);
        // Same fragment discipline as ProjectChildren: scripts in the subtree are parsed but
        // never queued — the JS insertion hooks own execution for programmatically-inserted nodes.
        var wasFragment = Current.IsFragment;
        Current.IsFragment = true;
        var savedCounters = _counters.ToDictionary(kv => kv.Key, kv => new List<int>(kv.Value));
        var savedVerbose = _verbose;
        _verbose = false;
        try
        {
            ReProjectInto(layout, context, owner);
            // Programmatically-created projections carry NeedsStyleResolution; reused nodes
            // resolved long ago. ApplyTree applies only where the flag is still set, so this is
            // a no-op walk for the common reuse case.
            StyleResolver.ApplyTree(layout);
        }
        finally
        {
            Current.IsFragment = wasFragment;
            _verbose = savedVerbose;
            _counters.Clear();
            foreach (var kv in savedCounters) _counters[kv.Key] = kv.Value;
            Current = previous;
        }
    }

    /// <summary>True for an element child that turns a parent into mixed content (the
    /// <see cref="Traverse"/> hasMixedChildren test): anything but script and the projected-
    /// but-never-rendered tags. When no such child exists, the parent keeps Traverse's folded
    /// representation — its own text carries the content, and no #text children exist.</summary>
    private static bool CountsAsMixedChild(IElement element)
    {
        var tag = TagOf(element);
        return tag != "SCRIPT" && !SkipTags.Contains(tag);
    }

    private static void ReProjectInto(LayoutNode layout, IElement dom, DocumentState owner)
    {
        if (!dom.ChildNodes.OfType<IElement>().Any(CountsAsMixedChild))
        {
            SyncFoldedText(layout, dom, owner);
            return;
        }

        // Synthetic parse-time children (::before/::after/::first-letter, DomNode-less) have no
        // DOM node to project from; keep them, anchored where CreatePseudoElementChildren put
        // them. A previous normalize pass may have spliced them INSIDE #anon-* wrappers (a
        // table-cell ::after is wrapped with the real cells), so harvest them back out to
        // direct-child position — the normalizers will re-wrap them identically. Split pieces
        // and wrapper husks themselves are functions of the current tree (the normalizers
        // rebuild them next layout), so they are dropped.
        var leading = new List<LayoutNode>();
        var trailing = new List<LayoutNode>();
        HarvestSyntheticPseudos(layout, leading, trailing);

        var rebuilt = new List<LayoutNode>(leading);
        foreach (var child in dom.ChildNodes)
        {
            switch (child)
            {
                case IText text:
                {
                    var rendered = CollapseWhitespace(text.Data);
                    if (rendered.Length == 0) break;
                    if (owner.FindBound(text) is { } bound && bound.TagName == "#text"
                        && ReferenceEquals(bound.DomNode, text))
                    {
                        // Pick up .data mutations; the bound node keeps its identity and style.
                        if (bound.DisplayText != rendered) bound.TextOverride = rendered;
                        rebuilt.Add(bound);
                    }
                    else
                    {
                        var projection = new LayoutNode(null, "#text", rendered, ComputeCurrentStyle(dom))
                        {
                            DomNode = text
                        };
                        projection.ResetNonInheritedStyles();
                        projection.StyleOverrides[AngleSharp.Css.PropertyNames.Display] = "inline";
                        owner.Bind(projection);
                        rebuilt.Add(projection);
                    }
                    break;
                }
                case IElement element:
                {
                    var tag = TagOf(element);
                    if (SkipTags.Contains(tag))
                    {
                        // Projected but never rendered (style/link/…): keep the parse-time box.
                        if (owner.FindBound(element) is { } skip) rebuilt.Add(skip);
                        break;
                    }
                    if (owner.FindBound(element) is { } bound && ReferenceEquals(bound.DomNode, element))
                    {
                        // Recurse: the reused subtree's child lists are the thing that went
                        // stale (splits hoisted its children into anonymous pieces).
                        ReProjectInto(bound, element, owner);
                        rebuilt.Add(bound);
                    }
                    else
                    {
                        var fresh = Traverse(element, 0);
                        owner.Bind(fresh);
                        rebuilt.Add(fresh);
                    }
                    break;
                }
                case IComment comment:
                {
                    // Comments never render but must stay in the projected tree: CharacterData
                    // anchors (before/after/replaceWith) resolve through the wrapper's bound
                    // node, and the projection must not orphan them on re-projection.
                    var data = comment.Data ?? string.Empty;
                    if (owner.FindBound(comment) is { } bound && bound.TagName == "#comment"
                        && ReferenceEquals(bound.DomNode, comment))
                    {
                        if (bound.DisplayText != data) bound.TextOverride = data;
                        rebuilt.Add(bound);
                    }
                    else
                    {
                        var fresh = new LayoutNode(null, "#comment", data, ComputeCurrentStyle(dom))
                        {
                            DomNode = comment
                        };
                        fresh.ResetNonInheritedStyles();
                        fresh.StyleOverrides[AngleSharp.Css.PropertyNames.Display] = "none";
                        owner.Bind(fresh);
                        rebuilt.Add(fresh);
                    }
                    break;
                }
                case IProcessingInstruction instruction:
                {
                    var data = instruction.Data ?? string.Empty;
                    if (owner.FindBound(instruction) is { } bound && bound.TagName == "#pi"
                        && ReferenceEquals(bound.DomNode, instruction))
                    {
                        if (bound.DisplayText != data) bound.TextOverride = data;
                        rebuilt.Add(bound);
                    }
                    else
                    {
                        var fresh = new LayoutNode(null, "#pi", data, ComputeCurrentStyle(dom))
                        {
                            DomNode = instruction
                        };
                        fresh.Attributes["_pi_target"] = instruction.Target;
                        fresh.ResetNonInheritedStyles();
                        fresh.StyleOverrides[AngleSharp.Css.PropertyNames.Display] = "none";
                        owner.Bind(fresh);
                        rebuilt.Add(fresh);
                    }
                    break;
                }
            }
        }
        rebuilt.AddRange(trailing);

        layout.Children.Clear();
        foreach (var c in rebuilt)
        {
            c.Parent = layout;
            layout.Children.Add(c);
        }
    }

    /// <summary>Syncs the folded-text representation of a parent whose DOM children are all
    /// text (or empty): Traverse stores such content as the node's own text, not as #text
    /// children. The exception is the pseudo-element fixup, which materializes the own text as
    /// an unbound #text child (keeping boundary spaces around ::before/::after) — that carrier
    /// is updated in place rather than the element's own text.</summary>
    /// <summary>Pulls DomNode-less generated-content nodes (#pseudo-before/::after/first-letter)
    /// out of <paramref name="layout"/>'s child list — and out of any DomNode-less anonymous
    /// wrappers they were spliced into by a previous normalize pass — collecting them into the
    /// leading (::before, ::first-letter) and trailing (::after) buckets for re-anchoring.</summary>
    private static void HarvestSyntheticPseudos(LayoutNode layout, List<LayoutNode> leading, List<LayoutNode> trailing)
    {
        foreach (var child in layout.Children.ToList())
        {
            if (child.DomNode is null && child.TagName.StartsWith("#pseudo", StringComparison.Ordinal))
            {
                layout.Children.Remove(child);
                (child.TagName == "#pseudo-after" ? trailing : leading).Add(child);
            }
            else if (child.DomNode is null)
            {
                HarvestSyntheticPseudos(child, leading, trailing);
            }
        }
    }

    /// <summary>Materializes the element's DOM text children as bound #text layout children
    /// (the mixed-content representation Traverse builds), inserted between the preserved
    /// leading/trailing synthetic children. Used when the folded own-text representation cannot
    /// carry the content: generated-content pseudos sit in the child list, or the own text was
    /// already migrated into the anonymous-table structure. <paramref name="covered"/> is the
    /// already-represented collapsed text (the migrated own text) — DOM text nodes composing it
    /// are skipped so the content is not duplicated.</summary>
    private static void MaterializeTextChildren(LayoutNode layout, IElement dom, DocumentState owner,
        List<LayoutNode> leading, List<LayoutNode> trailing, string covered = "")
    {
        var created = new List<LayoutNode>();
        var accumulated = "";
        foreach (var t in dom.ChildNodes.OfType<IText>())
        {
            var rendered = CollapseWhitespace(t.Data);
            if (rendered.Length == 0) continue;
            accumulated += rendered;
            if (covered.Length > 0 && accumulated.Length <= covered.Length
                && covered.StartsWith(accumulated, StringComparison.Ordinal)) continue;
            var textChild = new LayoutNode(null, "#text", rendered, layout.Style) { DomNode = t };
            textChild.ResetNonInheritedStyles();
            textChild.StyleOverrides[AngleSharp.Css.PropertyNames.Display] = "inline";
            owner.Bind(textChild);
            created.Add(textChild);
        }
        if (created.Count == 0) return;
        layout.SetOwnText(""); // the folded form must not double-paint beside the children
        layout.Children.Clear();
        foreach (var c in leading.Concat(created).Concat(trailing)) layout.Children.Add(c);
    }

    private static void SyncFoldedText(LayoutNode layout, IElement dom, DocumentState owner)
    {
        // Children whose DOM node no longer belongs to this element were removed or moved by
        // script; the textContent setter replaces element children with a single text node.
        layout.Children.RemoveAll(c => c.DomNode is { } d && !ReferenceEquals(d.Parent, dom));

        // CharacterData children never render but must exist in the projected tree with a live
        // Parent pointer: a wrapper anchoring before/after/replaceWith resolves through
        // Node.Parent, and an orphaned comment silently no-ops the whole insertion. The folded
        // representation cannot interleave them with own text, so new ones join at the end in
        // DOM order; kept ones get the invariant restored.
        foreach (var child in dom.ChildNodes)
        {
            var (tag, data, target) = child switch
            {
                IComment comment => ("#comment", comment.Data ?? string.Empty, null),
                IProcessingInstruction instruction => ("#pi", instruction.Data ?? string.Empty, instruction.Target),
                _ => default,
            };
            if (tag is null) continue;
            var bound = owner.FindBound(child);
            if (bound is not null && bound.TagName == tag
                && ReferenceEquals(bound.DomNode, child) && bound.Parent == layout)
            {
                if (bound.DisplayText != data) bound.TextOverride = data;
                continue;
            }
            if (bound is not null)
            {
                bound.Parent?.Children.Remove(bound);
                bound.Parent = null;
                if (bound.DisplayText != data) bound.TextOverride = data;
                layout.Children.Add(bound);
                continue;
            }
            var fresh = new LayoutNode(null, tag, data, layout.Style) { DomNode = child };
            if (target is not null) fresh.Attributes["_pi_target"] = target;
            owner.Bind(fresh);
            layout.Children.Add(fresh);
        }

        var raw = string.Concat(dom.ChildNodes.OfType<IText>().Select(t => t.Data));
        var ws = layout.Style.GetPropertyValueSafe("white-space")?.Trim().ToLowerInvariant();
        var preserves = ws is "pre" or "pre-wrap";
        var collapsed = NormalizeTextWhitespace(raw, ws);
        var boundaryKept = preserves ? collapsed : collapsed.Trim();

        var textChildren = layout.Children.Where(c => c.TagName == "#text").ToList();
        if (textChildren.Count > 0)
        {
            // One text carrier suffices; the collapsed form keeps the boundary spaces (a line's
            // leading/trailing spaces are stripped by the §16.6.1 line assembly, not here).
            var first = textChildren[0];
            if (first.DisplayText != collapsed) first.TextOverride = collapsed;
            foreach (var extra in textChildren.Skip(1)) layout.Children.Remove(extra);
        }
        else if (layout.Children.Any(c => c.DomNode is null && c.TagName.StartsWith("#pseudo", StringComparison.Ordinal))
                 && layout.Children.All(c => c.DomNode is not null
                     || c.TagName.StartsWith("#pseudo", StringComparison.Ordinal)))
        {
            MaterializeTextChildren(layout, dom, owner,
                leading: layout.Children.Where(c => c.TagName != "#pseudo-after").ToList(),
                trailing: layout.Children.Where(c => c.TagName == "#pseudo-after").ToList());
        }
        else if (layout.OwnTextMigratedToChild)
        {
            // The anonymous-table migration already turned this node's own text into a #text
            // child (now living inside #anon-* wrappers). Writing the folded text back would
            // double-represent content the unwrap/rewrap cycle can no longer heal — materialize
            // only the NEW DOM text as children; the next normalize pass re-wraps them into
            // the same anonymous cell.
            MaterializeTextChildren(layout, dom, owner,
                leading: [.. layout.Children], trailing: [], covered: layout.DisplayText);
        }
        else if (layout.DisplayText != boundaryKept)
        {
            // The folded representation: write the node's OWN text (as a fresh parse of the
            // same markup would), so every consumer — the anonymous-table migration, the
            // painter, textContent — sees the mutation. Clear any stale override first.
            // Text-node anchors whose wrapper needs a tree position are resolved through the
            // authoritative DOM by the ChildNode operations instead (see JsElement).
            layout.TextOverride = null;
            layout.SetOwnText(boundaryKept);
        }

        // Restate the "child in Children ⇒ child.Parent == layout" invariant: re-projection
        // rebuilt the child list here, and an anchor resolves through Node.Parent.
        foreach (var kept in layout.Children)
            if (kept.Parent != layout)
                kept.Parent = layout;
    }

    private static void QueueParsedDetailsNotifications(LayoutNode node, JsEngine engine)
    {
        if (node.TagName == "DETAILS" && node.Attributes.ContainsKey("open"))
            Scripting.Dom.JsElement.QueueDetailsToggle(node, engine);
        // Template contents are inert; child frame documents own their task queues.
        foreach (var child in node.Children) QueueParsedDetailsNotifications(child, engine);
    }

    private static List<LayoutNode> ParseFragmentCore(string html, string contextTag, bool includeScripts = false)
    {
        var result = new List<LayoutNode>();
        if (string.IsNullOrEmpty(html)) return result;

        var doc = Document;
        if (doc is null)
        {
            // Include the render device so absolute units (in/pt/cm/…) resolve at 96 DPI,
            // matching TraverseHtml — otherwise a fragment parsed before any page load
            // computes 1in as 64px instead of 96px. The scripting flag matters here too:
            // fragments must parse with the same noscript semantics as documents.
            var cfg = ParseConfiguration();
            doc = BrowsingContext.New(cfg).OpenNewAsync().Result;
            Document = doc;
        }

        var container = doc.CreateElement(string.IsNullOrEmpty(contextTag) ? "div" : contextTag.ToLowerInvariant());
        // Attach under <body> so contextual selectors (e.g. "body .foo") cascade correctly,
        // then detach again so the live document is left untouched.
        var attached = doc.Body;
        attached?.AppendChild(container);

        // Preserve the document-global counter state — fragment parsing must not corrupt it.
        var savedCounters = _counters.ToDictionary(kv => kv.Key, kv => new List<int>(kv.Value));
        var savedVerbose = _verbose;
        _verbose = false;
        try
        {
            container.InnerHtml = html;
            foreach (var childNode in container.ChildNodes)
            {
                if (childNode is IText textNode)
                {
                    var text = CollapseWhitespace(textNode.Data);
                    if (text.Length == 0) continue;
                    var tn = new LayoutNode(null, "#text", text, ComputeCurrentStyle(container)) { DomNode = textNode };
                    tn.StyleOverrides["display"] = "inline";
                    result.Add(tn);
                }
                else if (childNode is IElement childEl)
                {
                    var ct = TagOf(childEl);
                    // innerHTML never runs scripts, but a document.write stream must keep the
                    // script elements so the write path can execute them at the insertion point.
                    if (ct == "SCRIPT" && !includeScripts) continue;
                    result.Add(Traverse(childEl, 0));
                }
                else if (childNode is IComment commentNode)
                {
                    var cn = new LayoutNode(null, "#comment", commentNode.Data ?? string.Empty, ComputeCurrentStyle(container))
                    {
                        DomNode = commentNode
                    };
                    cn.ResetNonInheritedStyles();
                    cn.StyleOverrides[AngleSharp.Css.PropertyNames.Display] = "none";
                    result.Add(cn);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[innerHTML parse error] {ex.Message}");
        }
        finally
        {
            container.Remove();
            _verbose = savedVerbose;
            _counters.Clear();
            foreach (var kv in savedCounters) _counters[kv.Key] = kv.Value;
        }
        return result;
    }

    /// <summary>Builds the inert <c>#document-fragment</c> for a &lt;template&gt; from its
    /// <c>HTMLTemplateElement.content</c> (a separate AngleSharp fragment, not the element's own
    /// child nodes). Global counter state is preserved — template content must not perturb it.</summary>
    private static LayoutNode BuildTemplateContent(IElement templateEl)
    {
        var fragStyle = ComputeCurrentStyle(templateEl);
        var frag = new LayoutNode(null, "#document-fragment", string.Empty, fragStyle);
        if (templateEl is not AngleSharp.Html.Dom.IHtmlTemplateElement tmpl) return frag;
        frag.DomNode = tmpl.Content;

        var savedCounters = _counters.ToDictionary(kv => kv.Key, kv => new List<int>(kv.Value));
        try
        {
            foreach (var childNode in tmpl.Content.ChildNodes)
            {
                if (childNode is IText textNode)
                {
                    var text = CollapseWhitespace(textNode.Data);
                    if (text.Length == 0) continue;
                    var tn = new LayoutNode(null, "#text", text, fragStyle) { DomNode = textNode };
                    tn.StyleOverrides[AngleSharp.Css.PropertyNames.Display] = "inline";
                    frag.AddChild(tn);
                }
                else if (childNode is IElement childEl)
                {
                    if (childEl.TagName.Equals("script", StringComparison.OrdinalIgnoreCase)) continue;
                    frag.AddChild(Traverse(childEl, 0));
                }
            }
        }
        finally
        {
            _counters.Clear();
            foreach (var kv in savedCounters) _counters[kv.Key] = kv.Value;
        }
        return frag;
    }

    /// <summary>Implements the &lt;picture&gt; source-selection algorithm (simplified): the first
    /// &lt;source&gt; whose <c>media</c> matches the viewport and whose <c>type</c> is a supported
    /// image type (or absent) wins; its first <c>srcset</c> URL becomes the &lt;img&gt;'s resource.
    /// If no source matches, the &lt;img&gt;'s own <c>src</c> is kept. The chosen URL is recorded as
    /// <c>_currentSrc</c> (exposed via <c>img.currentSrc</c>). Density/width descriptors are ignored.</summary>
    private static void SelectPictureSource(IElement pictureEl, LayoutNode pictureNode)
    {
        var imgNode = pictureNode.Children.FirstOrDefault(c => c.TagName == "IMG");
        if (imgNode is null) return;

        foreach (var source in pictureEl.QuerySelectorAll("source"))
        {
            var srcset = source.GetAttribute("srcset");
            if (string.IsNullOrWhiteSpace(srcset)) continue;

            var type = source.GetAttribute("type");
            if (!string.IsNullOrEmpty(type) && !type.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                continue;

            var media = source.GetAttribute("media");
            if (!string.IsNullOrEmpty(media) && !MediaQueryEvaluator.Matches(media, ViewportWidth, ViewportHeight))
                continue;

            var url = FirstSrcsetUrl(srcset);
            if (string.IsNullOrEmpty(url)) continue;

            // Selection changes the current request, not the author's src attribute.
            imgNode.Attributes["_currentSrc"] = ResolveAgainstBase(url);
            imgNode.Image = ResourceLoader.FetchImage(url, _documentBaseUrl ?? _baseUrl, Session);
            return;
        }

        // No source matched: the chosen resource is the <img>'s own src.
        if (imgNode.Attributes.TryGetValue("src", out var imgSrc) && !string.IsNullOrEmpty(imgSrc))
            imgNode.Attributes["_currentSrc"] = ResolveAgainstBase(imgSrc);
    }

    /// <summary>Returns the first URL in a <c>srcset</c> value (the token before any whitespace/descriptor
    /// in the first comma-separated candidate).</summary>
    private static string? FirstSrcsetUrl(string srcset)
    {
        var first = srcset.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        return first?.Split(' ', '\t', '\n')[0];
    }

    /// <summary>Resolves a possibly-relative URL against the document base; returns it unchanged when
    /// there is no usable base (e.g. fragments parsed before any page load).</summary>
    private static string ResolveAgainstBase(string url)
    {
        return Uri.TryCreate(_documentBaseUrl ?? _baseUrl, UriKind.Absolute, out var basis) &&
            Uri.TryCreate(basis, url, out var absolute) ? absolute.AbsoluteUri : url;
    }

    // ---- tree helper for LayoutNode trees ----
    private static LayoutNode? FindFirst(LayoutNode node, Func<LayoutNode, bool> predicate)
    {
        if (predicate(node)) return node;
        foreach (var child in node.Children)
        {
            var result = FindFirst(child, predicate);
            if (result is not null) return result;
        }
        return null;
    }
}
