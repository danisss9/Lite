using AngleSharp.Io;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Css.Dom;
using Lite.Animation;
using Lite.Extensions;
using Lite.Layout;
using Lite.Models;
using Lite.Network;
using Lite.Scripting;

namespace Lite;

internal static class Parser
{
    private const string UserAgentStylesheet = """
        div, section, article, header, footer, main, nav, aside, form, ul, ol, li, fieldset, figure, figcaption, address, details, summary, dialog { display: block; }
        label { display: inline; }
        body { display: block; margin: 8px; }
        h1 { display: block; font-size: 2em; margin-top: 0.67em; margin-bottom: 0.67em; margin-left: 0px; margin-right: 0px; font-weight: bold; }
        h2 { display: block; font-size: 1.5em; margin-top: 0.83em; margin-bottom: 0.83em; margin-left: 0px; margin-right: 0px; font-weight: bold; }
        h3 { display: block; font-size: 1.17em; margin-top: 1em; margin-bottom: 1em; margin-left: 0px; margin-right: 0px; font-weight: bold; }
        h4 { display: block; font-size: 1em; margin-top: 1.33em; margin-bottom: 1.33em; margin-left: 0px; margin-right: 0px; font-weight: bold; }
        h5 { display: block; font-size: 0.83em; margin-top: 1.67em; margin-bottom: 1.67em; margin-left: 0px; margin-right: 0px; font-weight: bold; }
        h6 { display: block; font-size: 0.67em; margin-top: 2.33em; margin-bottom: 2.33em; margin-left: 0px; margin-right: 0px; font-weight: bold; }
        p { display: block; margin-top: 1em; margin-bottom: 1em; margin-left: 0px; margin-right: 0px; cursor: text; }
        h1, h2, h3, h4, h5, h6 { cursor: text; }
        a { color: blue; text-decoration: underline; cursor: pointer; }
        img { display: inline; }
        input { display: inline-block; cursor: text; }
        input[type="checkbox"] { cursor: default; }
        button { display: inline-block; cursor: pointer; border-top-width: 1px; border-right-width: 1px; border-bottom-width: 1px; border-left-width: 1px; border-top-color: #a0a0a0; border-right-color: #a0a0a0; border-bottom-color: #a0a0a0; border-left-color: #a0a0a0; }
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
        ul { list-style-type: disc; margin-top: 1em; margin-bottom: 1em; padding-left: 40px; }
        ol { list-style-type: decimal; margin-top: 1em; margin-bottom: 1em; padding-left: 40px; }
        li { display: list-item; }
        table { display: table; border-collapse: separate; }
        thead, tbody, tfoot { display: block; }
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
        """;

    // Tags that should not appear in the layout tree.
    // NOTE: TEMPLATE is intentionally NOT here — a <template> element IS in the DOM (so JS can reach
    // it), but it renders nothing (display:none) and its parsed content lives in an inert fragment
    // exposed as template.content (see the TEMPLATE clause in Traverse).
    private static readonly HashSet<string> SkipTags =
        ["HEAD", "STYLE", "NOSCRIPT", "META", "LINK", "TITLE"];

    private static string? _baseUrl;
    internal static string? BaseUrl => _baseUrl;
    /// <summary>Base URL for resolving relative references — equals the document URL unless
    /// a &lt;base href&gt; element overrides it.</summary>
    private static string? _documentBaseUrl;
    // Classic scripts that run "in document position" during parse: inline scripts and external
    // (src) scripts without defer/async. Executed in document order, before deferred/async.
    private static readonly List<string> _pendingScripts = [];
    // External classic scripts marked `defer` — executed after parsing, in document order.
    private static readonly List<string> _deferredScripts = [];
    // External classic scripts marked `async` — executed on the task queue, not in any order.
    private static readonly List<string> _asyncScripts = [];
    // ES modules to import after the engine is created: (specifier, code) — code is null for src modules.
    private static readonly List<(string Specifier, string? Code)> _pendingModules = [];
    private static int _inlineModuleCounter;
    private static readonly HttpClient _httpClient = new();

    /// <summary>The live AngleSharp document from the last page load, kept alive so that
    /// innerHTML fragments can be parsed with the page's full stylesheet cascade.</summary>
    internal static IDocument? Document { get; private set; }

    /// <summary>Suppresses per-element debug logging during fragment (innerHTML) parsing.</summary>
    private static bool _verbose = false;
    internal static int ViewportWidth { get; private set; } = 800;
    internal static int ViewportHeight { get; private set; } = 600;

    // CSS counter state maintained during document-order traversal.
    // A stack per counter name (CSS 2.1 §12.4): counter-reset pushes a new nested instance,
    // popped when its element's subtree is left; counters(name, sep) joins the whole stack.
    private static readonly Dictionary<string, List<int>> _counters = new();

    internal static LayoutNode TraverseHtml(string address, int viewportWidth = 800, int viewportHeight = 600)
    {
        _baseUrl = address;
        _documentBaseUrl = address;
        _pendingScripts.Clear();
        _deferredScripts.Clear();
        _asyncScripts.Clear();
        _pendingModules.Clear();
        _inlineModuleCounter = 0;
        _counters.Clear();
        ViewportWidth = viewportWidth;
        ViewportHeight = viewportHeight;

        var config = Configuration.Default
            .WithDefaultLoader(new LoaderOptions { IsResourceLoadingEnabled = true })
            .WithCss()
            .WithRenderDevice();

        var context = BrowsingContext.New(config);
        var document = context.OpenAsync(address).Result;
        Document = document;

        // <base href> overrides the base used for resolving relative URLs (not the document URL).
        if (document.QuerySelector("base[href]")?.GetAttribute("href") is { Length: > 0 } baseHref &&
            Uri.TryCreate(new Uri(address), baseHref, out var resolvedBase))
            _documentBaseUrl = resolvedBase.AbsoluteUri;

        var head = document.Head ?? document.DocumentElement;

        // Inject UA stylesheet first
        var uaStyle = document.CreateElement("style");
        uaStyle.TextContent = UserAgentStylesheet;
        head.InsertBefore(uaStyle, head.FirstChild);

        // Eagerly fetch and inline all <link rel="stylesheet"> files so that
        // ComputeCurrentStyle() sees the fully-cascaded styles synchronously. The rel is a
        // space-separated token set (HTML4 §6.12), so match any rel containing the "stylesheet"
        // token (e.g. Acid2's rel="appendix stylesheet") — but skip alternate stylesheets.
        foreach (var link in document.QuerySelectorAll("link[rel~='stylesheet']"))
        {
            var rel = link.GetAttribute("rel") ?? "";
            if (rel.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                   .Any(t => t.Equals("alternate", StringComparison.OrdinalIgnoreCase)))
                continue;
            var href = link.GetAttribute("href");
            if (string.IsNullOrEmpty(href)) continue;
            try
            {
                string css;
                string cssBase;
                if (DataUri.IsDataUri(href))
                {
                    if (!DataUri.TryDecodeText(href, out css, out _)) continue;
                    cssBase = _documentBaseUrl ?? _baseUrl ?? "";
                }
                else
                {
                    var cssUrl = ResolveUrl(href);
                    if (cssUrl is null) continue;
                    css = _httpClient.GetStringAsync(cssUrl).Result;
                    cssBase = cssUrl;
                }
                css = InlineImports(css, cssBase);
                var styleEl = document.CreateElement("style");
                styleEl.TextContent = css;
                head.AppendChild(styleEl);
            }
            catch (Exception ex) { Console.WriteLine($"[CSS load error] {href}: {ex.Message}"); }
        }

        // Process @import in author <style> elements (skip the UA sheet, which has none).
        foreach (var styleEl in document.QuerySelectorAll("style"))
        {
            var text = styleEl.TextContent;
            if (text.Contains("@import", StringComparison.OrdinalIgnoreCase))
                styleEl.TextContent = InlineImports(text, _documentBaseUrl ?? _baseUrl ?? "");
        }

        // Collect @keyframes from all stylesheets before traversing the DOM
        AnimationRegistry.Clear();
        CollectKeyframes(document);

        // Collect @font-face rules and load custom fonts
        FontRegistry.Clear();
        CollectFontFaces(document);

        var root = Traverse(document.DocumentElement, 0);

        // Collect all CSS rules for dynamic class-based style re-evaluation
        CollectCssRules(document);

        // Always create the JS engine so inline onclick/on* handlers work,
        // even when there are no external or inline script blocks.
        var jsEngine = JsEngine.Create(root, viewportWidth, viewportHeight);

        // 1) In-position classic scripts (inline + external without defer/async), in document order.
        //    document.write() during these appends to the body (see JsDocument.write).
        foreach (var script in _pendingScripts)
            jsEngine.Execute(script);

        // 2) Deferred classic scripts run after parsing, in document order.
        foreach (var script in _deferredScripts)
            jsEngine.Execute(script);

        // 3) ES modules (deferred by spec) in document order.
        foreach (var (specifier, code) in _pendingModules)
        {
            if (code is not null) jsEngine.AddModule(specifier, code);
            jsEngine.ImportModule(specifier);
        }

        // 4) Async classic scripts run on the task queue, not in document order (after the sync phase).
        foreach (var script in _asyncScripts)
        {
            var code = script;
            jsEngine.EnqueueMacrotask(() => jsEngine.Execute(code));
        }

        // Fire body onload handler if present
        var bodyNode = FindFirst(root, n => n.TagName == "BODY");
        if (bodyNode?.Attributes.TryGetValue("onload", out var onloadCode) == true)
            jsEngine.Execute(onloadCode);

        // Fire the window 'load' event for listeners registered via addEventListener.
        jsEngine.DispatchLoad();

        return root;
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

    private static string? ResolveUrl(string src)
    {
        if (Uri.TryCreate(src, UriKind.Absolute, out _)) return src;
        var baseUrl = _documentBaseUrl ?? _baseUrl;
        if (baseUrl != null && Uri.TryCreate(new Uri(baseUrl), src, out var resolved))
            return resolved.ToString();
        return null;
    }

    /// <summary>
    /// Recursively inlines <c>@import</c> rules (CSS 2.1 §6.3): fetches each imported sheet
    /// (resolved against <paramref name="baseUrl"/>), inlines ITS imports, and substitutes the
    /// text. A media-qualified import (<c>@import url(x) print;</c>) is wrapped in an @media block.
    /// </summary>
    private static string InlineImports(string css, string baseUrl, int depth = 0)
    {
        if (depth > 8 || string.IsNullOrEmpty(css) ||
            !css.Contains("@import", StringComparison.OrdinalIgnoreCase))
            return css;

        // @import url("x") media;  |  @import "x" media;  |  @import 'x';
        var rx = new System.Text.RegularExpressions.Regex(
            @"@import\s+(?:url\(\s*)?['""]?([^'""\)\s]+)['""]?\s*\)?\s*([^;]*);",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        return rx.Replace(css, m =>
        {
            var importUrl = m.Groups[1].Value.Trim();
            var media = m.Groups[2].Value.Trim();
            string resolved;
            if (Uri.TryCreate(importUrl, UriKind.Absolute, out var abs)) resolved = abs.ToString();
            else if (Uri.TryCreate(new Uri(baseUrl), importUrl, out var rel)) resolved = rel.ToString();
            else return "";
            try
            {
                var imported = _httpClient.GetStringAsync(resolved).Result;
                imported = InlineImports(imported, resolved, depth + 1);
                return string.IsNullOrEmpty(media) ? imported : $"@media {media} {{\n{imported}\n}}";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[@import load error] {resolved}: {ex.Message}");
                return "";
            }
        });
    }

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
                    var fontBytes = ResourceLoader.FetchBytes(resolved, _baseUrl);
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

    private static LayoutNode Traverse(IElement element, int indent)
    {
        // Normalize tag name to uppercase — AngleSharp returns lowercase for SVG namespace elements
        var tag = element.TagName.ToUpperInvariant();
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
            var ct = c.TagName.ToUpperInvariant();
            return ct != "SCRIPT" && !SkipTags.Contains(ct);
        });

        var directText = hasMixedChildren
            ? ""   // text nodes become ordered #TEXT children below
            : string.Concat(element.ChildNodes.OfType<IText>().Select(t => t.Data)).Trim();

        var href = tag == "A" ? element.GetAttribute("href") : null;
        var node = new LayoutNode(element.Id, tag, directText, element.ComputeCurrentStyle(), href);

        // Extract flex-related CSS properties that AngleSharp doesn't cascade
        ExtractMatchedCssProperties(element, node);

        if (tag == "IMG")
        {
            var src = element.GetAttribute("src");
            node.Alt = element.GetAttribute("alt") ?? string.Empty;
            if (src != null) node.Attributes["src"] = src;   // source of truth for .src / .currentSrc

            if (int.TryParse(element.GetAttribute("width"), out var w)) node.IntrinsicWidth = w;
            if (int.TryParse(element.GetAttribute("height"), out var h)) node.IntrinsicHeight = h;

            if (!string.IsNullOrEmpty(src))
                node.Image = ResourceLoader.FetchImage(src, _baseUrl);
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
                var img = ResourceLoader.FetchImage(data, _baseUrl);
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
            // The option's value is its `value` attribute, falling back to its text content.
            node.Attributes["value"] = element.GetAttribute("value") ?? element.TextContent.Trim();
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
        if (element.ClassName != null)
            node.Attributes["class"] = element.ClassName;

        // Capture data-* attributes for attribute selectors and getAttribute
        foreach (var attr in element.Attributes)
        {
            if (attr.Name.StartsWith("data-"))
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
                if (!svgW.EndsWith("px")) svgW += "px";
                if (!svgH.EndsWith("px")) svgH += "px";
                node.StyleOverrides["width"] = svgW;
                node.StyleOverrides["height"] = svgH;
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

        // Apply CSS counters (counter-reset/counter-increment) in document order BEFORE traversing
        // children, and snapshot the resulting state onto the node (for counter()/counters() and the
        // ::before/::after generated content). The pushed instances are popped after the subtree.
        ApplyCounters(node, out var pushedCounters);

        if (objectShowsImage)
        {
            // Replaced <object> showing an image: its fallback children are not rendered.
        }
        else if (hasMixedChildren)
        {
            // Walk ChildNodes in DOM order so text nodes keep their position among element siblings.
            // e.g. <p>Hello <strong>world</strong>!</p> → [#TEXT("Hello"), strong, #TEXT("!")]
            var parentStyle = element.ComputeCurrentStyle();
            foreach (var childNode in element.ChildNodes)
            {
                if (childNode is IText textNode)
                {
                    var text = CollapseWhitespace(textNode.Data);
                    // Include any non-empty text — whitespace-only nodes (" ") between
                    // inline siblings need to produce a space; purely empty strings are skipped.
                    // Whitespace-only nodes between block siblings are filtered out later in
                    // LayoutChildren (runs consisting solely of whitespace nodes are skipped).
                    if (text.Length > 0)
                    {
                        var textChild = new LayoutNode(null, "#text", text, parentStyle);
                        textChild.StyleOverrides[AngleSharp.Css.PropertyNames.Display] = "inline";
                        node.AddChild(textChild);
                    }
                }
                else if (childNode is IElement childEl)
                {
                    var childTag = childEl.TagName.ToUpperInvariant();
                    if (childTag == "SCRIPT")
                    {
                        CollectScript(childEl);
                        continue;
                    }
                    if (SkipTags.Contains(childTag)) { CollectScriptsRecursive(childEl); continue; }
                    node.AddChild(Traverse(childEl, indent + 1));
                }
            }
        }
        else
        {
            foreach (var child in element.Children)
            {
                var childTag = child.TagName.ToUpperInvariant();
                if (childTag == "SCRIPT") { CollectScript(child); continue; }
                if (SkipTags.Contains(childTag)) { CollectScriptsRecursive(child); continue; }
                node.AddChild(Traverse(child, indent + 1));
            }
        }

        // <picture>: now that the <img> and <source> children exist, pick the source the <img>
        // should display (media/type-based selection; falls back to the <img>'s own src).
        if (tag == "PICTURE")
            SelectPictureSource(element, node);

        // Leaving this node's subtree: pop the counter instances it introduced (scope ends).
        PopCounters(pushedCounters);

        // Create ::before and ::after pseudo-element children (using the snapshot taken above)
        CreatePseudoElementChildren(node);

        return node;
    }

    /// <summary>Applies counter-reset/counter-increment for <paramref name="node"/> in document order
    /// and snapshots the current counter state onto it. Returns the counter names this node pushed
    /// via counter-reset (to be popped after its subtree). Call BEFORE traversing children.</summary>
    private static void ApplyCounters(LayoutNode node, out List<string> pushed)
    {
        pushed = new List<string>();

        if (node.StyleOverrides.TryGetValue("counter-reset", out var crReset) &&
            !crReset.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var part in ParseCounterSpec(crReset))
            {
                if (!_counters.TryGetValue(part.Name, out var stack))
                    _counters[part.Name] = stack = new List<int>();
                stack.Add(part.Value);              // push a new nested instance
                pushed.Add(part.Name);
            }
        }

        if (node.StyleOverrides.TryGetValue("counter-increment", out var crInc) &&
            !crInc.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var part in ParseCounterSpec(crInc))
            {
                if (!_counters.TryGetValue(part.Name, out var stack) || stack.Count == 0)
                    // Not in scope: auto-create a document-root instance (not popped by this node).
                    _counters[part.Name] = stack = new List<int> { 0 };
                stack[^1] += part.Value == 0 ? 1 : part.Value;
            }
        }

        if (_counters.Count == 0) return;
        foreach (var (name, stack) in _counters)
        {
            if (stack.Count == 0) continue;
            (node.CounterValues ??= new())[name] = stack[^1];
            (node.CounterStacks ??= new())[name] = new List<int>(stack);
        }
    }

    /// <summary>Pops the counter instances pushed by a node's counter-reset when its subtree ends.</summary>
    private static void PopCounters(List<string> pushed)
    {
        foreach (var name in pushed)
            if (_counters.TryGetValue(name, out var stack) && stack.Count > 0)
                stack.RemoveAt(stack.Count - 1);
    }

    /// <summary>
    /// If the node has ::before or ::after styles with a content property,
    /// creates synthetic inline children at the start/end of the children list.
    /// </summary>
    private static void CreatePseudoElementChildren(LayoutNode node)
    {
        bool hasBefore = node.BeforeStyles != null && node.BeforeStyles.TryGetValue("content", out var beforeContent)
                         && ParseContentValue(beforeContent!, node) != null;
        bool hasAfter = node.AfterStyles != null && node.AfterStyles.TryGetValue("content", out var afterContent)
                        && ParseContentValue(afterContent!, node) != null;

        if (!hasBefore && !hasAfter) return;

        // If the parent node has its own text, move it into a #text child so that
        // pseudo-elements and the original text flow together as inline children.
        if (!string.IsNullOrEmpty(node.DisplayText))
        {
            var textChild = new LayoutNode(null, "#text", node.DisplayText, node.Style);
            textChild.StyleOverrides["display"] = "inline";
            textChild.Parent = node;
            node.Children.Add(textChild);
            node.TextOverride = "";
        }

        if (hasBefore)
        {
            var text = ParseContentValue(node.BeforeStyles!["content"], node);
            var pseudoNode = new LayoutNode(null, "#pseudo-before", text!, node.Style);
            pseudoNode.StyleOverrides["display"] = node.BeforeStyles!.GetValueOrDefault("display", "inline");
            foreach (var (p, v) in node.BeforeStyles!)
            {
                if (p != "content") pseudoNode.StyleOverrides[p] = v;
            }
            pseudoNode.Parent = node;
            node.Children.Insert(0, pseudoNode);
        }

        if (hasAfter)
        {
            var text = ParseContentValue(node.AfterStyles!["content"], node);
            var pseudoNode = new LayoutNode(null, "#pseudo-after", text!, node.Style);
            pseudoNode.StyleOverrides["display"] = node.AfterStyles!.GetValueOrDefault("display", "inline");
            foreach (var (p, v) in node.AfterStyles!)
            {
                if (p != "content") pseudoNode.StyleOverrides[p] = v;
            }
            pseudoNode.Parent = node;
            node.Children.Add(pseudoNode);
        }
    }

    /// <summary>Parses a CSS content property value, stripping quotes and handling basic values.
    /// Supports concatenated tokens like: "Chapter " counter(section) ". "</summary>
    private static string? ParseContentValue(string value, LayoutNode? node = null)
    {
        value = value.Trim();
        if (value is "none" or "normal" or "") return null;

        // Check if this is a simple single-token value
        if (!value.Contains("counter(") && !value.Contains("counters(") && !value.Contains("attr("))
        {
            // Simple single value
            if ((value.StartsWith('"') && value.EndsWith('"')) ||
                (value.StartsWith('\'') && value.EndsWith('\'')))
            {
                var inner = value[1..^1];
                return DecodeCssEscapes(inner);
            }
            if (value == "open-quote") return "\u201C";
            if (value == "close-quote") return "\u201D";
            return value;
        }

        // Simple attr(name) \u2014 the named attribute's value (CSS 2.1 \u00A712.2).
        if (value.StartsWith("attr(", StringComparison.OrdinalIgnoreCase) && value.EndsWith(")") &&
            !value[5..].Contains("attr(") && !value.Contains(' '))
        {
            var attrName = value[5..^1].Trim();
            return node?.Attributes.GetValueOrDefault(attrName) ?? "";
        }

        // Tokenize concatenated content value: "text" counter(name) "more"
        var sb = new System.Text.StringBuilder();
        int i = 0;
        while (i < value.Length)
        {
            if (char.IsWhiteSpace(value[i])) { i++; continue; }

            // Quoted string
            if (value[i] == '"' || value[i] == '\'')
            {
                var quote = value[i];
                int end = value.IndexOf(quote, i + 1);
                if (end < 0) break;
                sb.Append(DecodeCssEscapes(value[(i + 1)..end]));
                i = end + 1;
                continue;
            }

            // counter(name) or counter(name, style)
            if (value[i..].StartsWith("counter("))
            {
                var paren = value.IndexOf(')', i);
                if (paren < 0) break;
                var args = SplitCounterArgs(value[(i + 8)..paren]);
                var counterVal = 0;
                if (args.Count > 0) node?.CounterValues?.TryGetValue(args[0], out counterVal);
                sb.Append(FormatCounter(counterVal, args.Count > 1 ? args[1] : "decimal"));
                i = paren + 1;
                continue;
            }

            // counters(name, separator) or counters(name, separator, style) — joins the whole
            // nested scope stack with the separator (CSS 2.1 §12.4.1, e.g. nested-list "1.2.1").
            if (value[i..].StartsWith("counters("))
            {
                var paren = value.IndexOf(')', i);
                if (paren < 0) break;
                var args = SplitCounterArgs(value[(i + 9)..paren]);
                var counterName = args.Count > 0 ? args[0] : "";
                var sep = args.Count > 1 ? args[1] : "";
                var style = args.Count > 2 ? args[2] : "decimal";
                if (node?.CounterStacks != null && node.CounterStacks.TryGetValue(counterName, out var stack))
                    sb.Append(string.Join(sep, stack.Select(v => FormatCounter(v, style))));
                else
                {
                    var counterVal = 0;
                    node?.CounterValues?.TryGetValue(counterName, out counterVal);
                    sb.Append(FormatCounter(counterVal, style));
                }
                i = paren + 1;
                continue;
            }

            // attr(name) \u2014 the named attribute's value
            if (value[i..].StartsWith("attr(", StringComparison.OrdinalIgnoreCase))
            {
                var paren = value.IndexOf(')', i);
                if (paren < 0) break;
                var attrName = value[(i + 5)..paren].Trim();
                if (node?.Attributes.TryGetValue(attrName, out var av) == true) sb.Append(av);
                i = paren + 1;
                continue;
            }

            // open-quote / close-quote
            if (value[i..].StartsWith("open-quote")) { sb.Append('\u201C'); i += 10; continue; }
            if (value[i..].StartsWith("close-quote")) { sb.Append('\u201D'); i += 11; continue; }

            // Skip unknown token until whitespace
            while (i < value.Length && !char.IsWhiteSpace(value[i])) i++;
        }

        return sb.Length > 0 ? sb.ToString() : null;
    }

    /// <summary>Splits counter()/counters() arguments. AngleSharp normalizes
    /// <c>counters(item, ".")</c> to <c>counters(item .)</c> (comma dropped, separator unquoted),
    /// so we split on commas OR whitespace while keeping quoted tokens intact and dropping quotes.</summary>
    private static List<string> SplitCounterArgs(string inner)
    {
        var args = new List<string>();
        var sb = new System.Text.StringBuilder();
        char quote = '\0';
        foreach (var c in inner)
        {
            if (quote != '\0')
            {
                if (c == quote) quote = '\0';
                else sb.Append(c);
            }
            else if (c is '"' or '\'') quote = c;
            else if (c == ',' || char.IsWhiteSpace(c))
            {
                if (sb.Length > 0) { args.Add(sb.ToString()); sb.Clear(); }
            }
            else sb.Append(c);
        }
        if (sb.Length > 0) args.Add(sb.ToString());
        return args;
    }

    private static string FormatCounter(int value, string style) => style switch
    {
        "lower-alpha" or "lower-latin" => value >= 1 && value <= 26 ? ((char)('a' + value - 1)).ToString() : value.ToString(),
        "upper-alpha" or "upper-latin" => value >= 1 && value <= 26 ? ((char)('A' + value - 1)).ToString() : value.ToString(),
        "lower-roman" => ToRomanLower(value),
        "upper-roman" => ToRomanUpper(value),
        _ => value.ToString(),
    };

    private static string ToRomanLower(int num) => ToRomanUpper(num).ToLowerInvariant();

    private static string ToRomanUpper(int num)
    {
        if (num <= 0 || num > 3999) return num.ToString();
        string[] thousands = ["", "M", "MM", "MMM"];
        string[] hundreds = ["", "C", "CC", "CCC", "CD", "D", "DC", "DCC", "DCCC", "CM"];
        string[] tens = ["", "X", "XX", "XXX", "XL", "L", "LX", "LXX", "LXXX", "XC"];
        string[] ones = ["", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX"];
        return thousands[num / 1000] + hundreds[num % 1000 / 100] + tens[num % 100 / 10] + ones[num % 10];
    }

    /// <summary>Parses "counter-reset: name value name2 value2" or "counter-increment: name value" specs.</summary>
    private static List<(string Name, int Value)> ParseCounterSpec(string spec)
    {
        var result = new List<(string, int)>();
        var tokens = spec.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int i = 0;
        while (i < tokens.Length)
        {
            if (tokens[i] == "none") break;
            var name = tokens[i];
            int val = 0;
            if (i + 1 < tokens.Length && int.TryParse(tokens[i + 1], out var parsed))
            {
                val = parsed;
                i += 2;
            }
            else
            {
                i++;
            }
            result.Add((name, val));
        }
        return result;
    }

    /// <summary>Decodes CSS unicode escape sequences like \201C into actual characters.</summary>
    private static string DecodeCssEscapes(string s)
    {
        if (!s.Contains('\\')) return s;
        var sb = new System.Text.StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 1 < s.Length)
            {
                // Collect up to 6 hex digits
                int start = i + 1;
                int end = start;
                while (end < s.Length && end - start < 6 && Uri.IsHexDigit(s[end])) end++;
                if (end > start)
                {
                    var codePoint = Convert.ToInt32(s[start..end], 16);
                    sb.Append(char.ConvertFromUtf32(codePoint));
                    i = end - 1;
                    // Skip optional single trailing space after hex escape
                    if (i + 1 < s.Length && s[i + 1] == ' ') i++;
                }
                else
                {
                    // Not a hex escape — literal escaped char
                    sb.Append(s[i + 1]);
                    i++;
                }
            }
            else
            {
                sb.Append(s[i]);
            }
        }
        return sb.ToString();
    }

    /// <summary>Recursively collect scripts from elements that are otherwise skipped (e.g. HEAD).</summary>
    private static void CollectScriptsRecursive(IElement element)
    {
        foreach (var child in element.Children)
        {
            if (child.TagName == "SCRIPT") CollectScript(child);
            else CollectScriptsRecursive(child);
        }
    }

    private static void CollectScript(IElement scriptEl)
    {
        var type = scriptEl.GetAttribute("type");
        bool isModule = string.Equals(type, "module", StringComparison.OrdinalIgnoreCase);
        var src = scriptEl.GetAttribute("src");
        // defer/async only apply to external (src) scripts; they are ignored on inline scripts.
        bool hasSrc = !string.IsNullOrEmpty(src);
        bool isAsync = hasSrc && scriptEl.HasAttribute("async");
        bool isDefer = hasSrc && scriptEl.HasAttribute("defer");

        if (src != null)
        {
            // Handle data: URIs inline (HttpClient doesn't support them)
            if (src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                var code = DecodeDataUri(src);
                if (string.IsNullOrWhiteSpace(code)) return;
                if (isModule) _pendingModules.Add((NextInlineModuleSpecifier(), code));
                else BucketClassic(code, isAsync, isDefer);
                return;
            }

            var scriptUrl = ResolveUrl(src);
            if (scriptUrl != null)
            {
                if (isModule)
                {
                    // Let the module loader fetch it on import (so its own imports resolve).
                    _pendingModules.Add((scriptUrl, null));
                    return;
                }
                try
                {
                    var code = _httpClient.GetStringAsync(scriptUrl).Result;
                    if (!string.IsNullOrWhiteSpace(code))
                        BucketClassic(code, isAsync, isDefer);
                }
                catch (Exception ex) { Console.WriteLine($"[Script load error] {scriptUrl}: {ex.Message}"); }
            }
        }
        else if (!string.IsNullOrWhiteSpace(scriptEl.TextContent))
        {
            // Inline scripts always run in document position (defer/async do not apply).
            if (isModule) _pendingModules.Add((NextInlineModuleSpecifier(), scriptEl.TextContent));
            else _pendingScripts.Add(scriptEl.TextContent);
        }
    }

    /// <summary>Routes an external classic script's code into the in-position, deferred, or async
    /// execution bucket (HTML §"prepare the script element").</summary>
    private static void BucketClassic(string code, bool isAsync, bool isDefer)
    {
        if (isAsync) _asyncScripts.Add(code);
        else if (isDefer) _deferredScripts.Add(code);
        else _pendingScripts.Add(code);
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

    // CSS properties that AngleSharp.Css doesn't reliably cascade via ComputeCurrentStyle()
    private static readonly string[] s_extraProps =
    [
        "flex-direction", "flex-wrap", "justify-content", "align-items",
        "align-self", "align-content", "flex-grow", "flex-shrink", "flex-basis", "order",
        "row-gap", "column-gap", "gap",
        "flex", "flex-flow",
        "min-width", "max-width", "min-height", "max-height", "visibility",
        "border-radius", "box-shadow", "text-shadow",
        "float", "clear",
        // CSS2 text properties
        "text-transform", "letter-spacing", "word-spacing", "text-indent",
        // CSS2 border style
        "border-style", "border-top-style", "border-right-style", "border-bottom-style", "border-left-style",
        // CSS2 list properties
        "list-style-type", "list-style-position", "list-style",
        // CSS2 outline
        "outline", "outline-width", "outline-style", "outline-color", "outline-offset",
        // CSS2 vertical-align
        "vertical-align",
        // CSS2 background image
        "background-image", "background-repeat", "background-position", "background-size",
        // CSS2 table properties
        "border-collapse", "border-spacing", "table-layout", "caption-side", "empty-cells",
        // CSS2 counter properties
        "counter-reset", "counter-increment",
        // CSS3 text overflow
        "text-overflow",
        // CSS3 filter
        "filter"
    ];

    /// <summary>
    /// Iterates all stylesheet rules that match <paramref name="element"/> and copies
    /// flex-related property values into the node's StyleOverrides dictionary.
    /// This works around AngleSharp.Css not cascading these properties through ComputeCurrentStyle().
    /// </summary>
    private static void ExtractMatchedCssProperties(IElement element, LayoutNode node)
    {
        if (element.Owner?.StyleSheets is null) return;

        foreach (var sheet in element.Owner.StyleSheets.OfType<ICssStyleSheet>())
        {
            ProcessRules(sheet.Rules, element, node, mediaText: null);
        }
    }

    /// <summary>
    /// Recursively processes a rule list, descending into @media blocks.
    /// <paramref name="mediaText"/> is non-null when inside a media rule.
    /// </summary>
    private static void ProcessRules(ICssRuleList rules, IElement element, LayoutNode node, string? mediaText)
    {
        foreach (var rule in rules)
        {
            if (rule is ICssMediaRule mediaRule)
            {
                var text = mediaRule.Media.MediaText;
                // Descend into nested rules, passing the media condition down
                ProcessRules(mediaRule.Rules, element, node, text);
                continue;
            }

            if (rule is not ICssStyleRule styleRule) continue;

            // Handle ::before / ::after pseudo-element selectors
            if (TryExtractPseudoElementRule(element, node, styleRule))
                continue;

            // Handle pseudo-class selectors (:hover, :focus, :active)
            if (TryExtractPseudoClassRule(element, node, styleRule, mediaText))
                continue;

            var selectorText = styleRule.SelectorText;
            try { if (!element.Matches(selectorText)) continue; }
            catch { continue; } // malformed selector

            if (mediaText is null)
            {
                // Regular rule — apply directly to overrides
                var style = styleRule.Style;
                foreach (var prop in s_extraProps)
                {
                    var val = style.GetPropertyValueSafe(prop);
                    if (!string.IsNullOrEmpty(val))
                        StoreProp(node, prop, val);
                }
                ParseCssTextForFlexProps(styleRule.Style.CssText, node);
                ExtractTransitionAndAnimation(styleRule.Style.CssText, node);
            }
            else
            {
                // Media-conditional rule — store for deferred evaluation and apply if currently matches
                StoreMediaProps(node, mediaText, styleRule.Style.CssText, styleRule.Style, target: "override");
            }
        }
    }

    /// <summary>
    /// Stores all properties from a media-conditional style rule on the node.
    /// Each property is saved into MediaConditionalStyles so it can be re-evaluated on resize,
    /// and applied immediately if the media query currently matches.
    /// </summary>
    private static void StoreMediaProps(LayoutNode node, string mediaText, string cssText,
        ICssStyleDeclaration style, string target)
    {
        var props = new Dictionary<string, string>();

        // Collect via GetPropertyValue for known extra props
        foreach (var prop in s_extraProps)
        {
            var val = style.GetPropertyValueSafe(prop);
            if (!string.IsNullOrEmpty(val))
                props[prop] = val;
        }

        // Also parse CssText to get all properties (display, color, etc.)
        if (!string.IsNullOrEmpty(cssText))
        {
            foreach (var decl in cssText.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var colon = decl.IndexOf(':');
                if (colon < 0) continue;
                var prop = decl[..colon].Trim().ToLowerInvariant();
                var val = decl[(colon + 1)..].Trim().Replace("!important", "").Trim();
                if (!string.IsNullOrEmpty(val))
                    props.TryAdd(prop, val);
            }
        }

        if (props.Count == 0) return;

        var matches = MediaQueryEvaluator.Matches(mediaText, ViewportWidth, ViewportHeight);
        var targetDict = target switch
        {
            "hover" => node.MediaHoverStyles,
            "focus" => node.MediaFocusStyles,
            "active" => node.MediaActiveStyles,
            _ => node.MediaOverrides,
        };

        foreach (var (prop, val) in props)
        {
            // Record for future resize re-evaluation
            node.MediaConditionalStyles.Add(new MediaConditionalStyle(mediaText, prop, val, target));

            // Apply immediately if the query matches at current viewport
            if (matches)
                targetDict[prop] = val;
        }
    }

    private static readonly HashSet<string> s_flexDirectionValues =
        ["row", "row-reverse", "column", "column-reverse"];
    private static readonly HashSet<string> s_flexWrapValues =
        ["nowrap", "wrap", "wrap-reverse"];

    /// <summary>
    /// Stores a property whose value contains <c>var()</c> into StyleOverrides.
    /// Expands common shorthands (padding, margin) into their longhand forms.
    /// </summary>
    private static void StoreVarProp(LayoutNode node, string prop, string val)
    {
        if (prop is "padding" or "margin")
        {
            node.StyleOverrides[$"{prop}-top"] = val;
            node.StyleOverrides[$"{prop}-right"] = val;
            node.StyleOverrides[$"{prop}-bottom"] = val;
            node.StyleOverrides[$"{prop}-left"] = val;
        }
        else if (prop == "gap")
        {
            node.StyleOverrides["row-gap"] = val;
            node.StyleOverrides["column-gap"] = val;
        }
        else
        {
            node.StyleOverrides[prop] = val;
        }
    }

    private static void StoreProp(LayoutNode node, string prop, string val)
    {
        if (prop == "gap")
        {
            var parts = val.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            node.StyleOverrides["row-gap"] = parts[0];
            node.StyleOverrides["column-gap"] = parts.Length > 1 ? parts[1] : parts[0];
        }
        else if (prop == "flex")
        {
            // Decompose flex shorthand into flex-grow, flex-shrink, flex-basis
            DecomposeFlexShorthand(node, val);
        }
        else if (prop == "flex-flow")
        {
            // Decompose flex-flow shorthand into flex-direction + flex-wrap
            var parts = val.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var p in parts)
            {
                var lower = p.ToLowerInvariant();
                if (s_flexDirectionValues.Contains(lower))
                    node.StyleOverrides["flex-direction"] = lower;
                else if (s_flexWrapValues.Contains(lower))
                    node.StyleOverrides["flex-wrap"] = lower;
            }
        }
        else if (prop == "border-style")
        {
            // Expand shorthand to individual sides
            var parts = val.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1)
            {
                node.StyleOverrides["border-top-style"] = parts[0];
                node.StyleOverrides["border-right-style"] = parts[0];
                node.StyleOverrides["border-bottom-style"] = parts[0];
                node.StyleOverrides["border-left-style"] = parts[0];
            }
            else if (parts.Length == 2)
            {
                node.StyleOverrides["border-top-style"] = parts[0];
                node.StyleOverrides["border-bottom-style"] = parts[0];
                node.StyleOverrides["border-right-style"] = parts[1];
                node.StyleOverrides["border-left-style"] = parts[1];
            }
            else if (parts.Length == 3)
            {
                node.StyleOverrides["border-top-style"] = parts[0];
                node.StyleOverrides["border-right-style"] = parts[1];
                node.StyleOverrides["border-left-style"] = parts[1];
                node.StyleOverrides["border-bottom-style"] = parts[2];
            }
            else if (parts.Length >= 4)
            {
                node.StyleOverrides["border-top-style"] = parts[0];
                node.StyleOverrides["border-right-style"] = parts[1];
                node.StyleOverrides["border-bottom-style"] = parts[2];
                node.StyleOverrides["border-left-style"] = parts[3];
            }
        }
        else if (prop == "outline")
        {
            // outline shorthand: [width] [style] [color]
            var parts = val.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var p in parts)
            {
                var lower = p.ToLowerInvariant();
                if (lower is "solid" or "dotted" or "dashed" or "double" or "groove" or "ridge" or "inset" or "outset" or "none")
                    node.StyleOverrides["outline-style"] = lower;
                else if (lower is "thin" or "medium" or "thick" || lower.EndsWith("px"))
                    node.StyleOverrides["outline-width"] = lower;
                else
                    node.StyleOverrides["outline-color"] = lower;
            }
        }
        else if (prop == "list-style")
        {
            // list-style shorthand: [type] [position] [image]
            var parts = val.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var p in parts)
            {
                var lower = p.ToLowerInvariant();
                if (lower is "inside" or "outside")
                    node.StyleOverrides["list-style-position"] = lower;
                else if (lower is "none" or "disc" or "circle" or "square" or "decimal"
                    or "decimal-leading-zero" or "lower-alpha" or "upper-alpha" or "lower-latin"
                    or "upper-latin" or "lower-roman" or "upper-roman")
                    node.StyleOverrides["list-style-type"] = lower;
            }
        }
        else
        {
            node.StyleOverrides[prop] = val;
        }
    }

    private static void DecomposeFlexShorthand(LayoutNode node, string val)
    {
        val = val.Trim().ToLowerInvariant();
        switch (val)
        {
            case "none":
                node.StyleOverrides["flex-grow"] = "0";
                node.StyleOverrides["flex-shrink"] = "0";
                node.StyleOverrides["flex-basis"] = "auto";
                return;
            case "auto":
                node.StyleOverrides["flex-grow"] = "1";
                node.StyleOverrides["flex-shrink"] = "1";
                node.StyleOverrides["flex-basis"] = "auto";
                return;
        }

        var parts = val.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1)
        {
            // Single value: if it's a number → flex: <grow> 1 0px
            // If it's a length/percent → flex: 1 1 <basis>
            if (float.TryParse(parts[0], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out _))
            {
                node.StyleOverrides["flex-grow"] = parts[0];
                node.StyleOverrides["flex-shrink"] = "1";
                node.StyleOverrides["flex-basis"] = "0px";
            }
            else
            {
                node.StyleOverrides["flex-grow"] = "1";
                node.StyleOverrides["flex-shrink"] = "1";
                node.StyleOverrides["flex-basis"] = parts[0];
            }
        }
        else if (parts.Length == 2)
        {
            node.StyleOverrides["flex-grow"] = parts[0];
            // Second value: number → shrink, length → basis
            if (float.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out _))
            {
                node.StyleOverrides["flex-shrink"] = parts[1];
                node.StyleOverrides["flex-basis"] = "0px";
            }
            else
            {
                node.StyleOverrides["flex-shrink"] = "1";
                node.StyleOverrides["flex-basis"] = parts[1];
            }
        }
        else if (parts.Length >= 3)
        {
            node.StyleOverrides["flex-grow"] = parts[0];
            node.StyleOverrides["flex-shrink"] = parts[1];
            node.StyleOverrides["flex-basis"] = parts[2];
        }
    }

    /// <summary>
    /// Parses the raw CssText of a rule's style declaration to extract flex properties
    /// that AngleSharp.Css may not expose via GetPropertyValue.
    /// </summary>
    private static void ParseCssTextForFlexProps(string cssText, LayoutNode node)
    {
        if (string.IsNullOrEmpty(cssText)) return;

        foreach (var declaration in cssText.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var colonIdx = declaration.IndexOf(':');
            if (colonIdx < 0) continue;

            var prop = declaration[..colonIdx].Trim().ToLowerInvariant();
            var val = declaration[(colonIdx + 1)..].Trim();

            if (string.IsNullOrEmpty(val)) continue;

            // CSS custom properties (--*): store for var() resolution
            if (prop.StartsWith("--"))
            {
                node.CustomProperties[prop] = val;
                continue;
            }

            // Properties with var() references must be stored in overrides
            // because AngleSharp cannot resolve CSS custom properties.
            if (val.Contains("var(", StringComparison.OrdinalIgnoreCase))
            {
                StoreVarProp(node, prop, val);
                continue;
            }

            // Only extract our target properties
            if (Array.IndexOf(s_extraProps, prop) >= 0)
                StoreProp(node, prop, val);
        }
    }

    /// <summary>
    /// Detects ::before / ::after pseudo-element selectors, strips them,
    /// matches the base selector, and stores all properties for later synthetic child creation.
    /// </summary>
    private static bool TryExtractPseudoElementRule(IElement element, LayoutNode node, ICssStyleRule rule)
    {
        var selector = rule.SelectorText;
        if (selector is null) return false;

        bool isBefore = selector.Contains("::before") || selector.Contains(":before");
        bool isAfter = selector.Contains("::after") || selector.Contains(":after");
        bool isFirstLetter = selector.Contains("::first-letter") || selector.Contains(":first-letter");
        bool isFirstLine = selector.Contains("::first-line") || selector.Contains(":first-line");
        if (!isBefore && !isAfter && !isFirstLetter && !isFirstLine) return false;

        // Strip pseudo-element to get base selector
        var baseSelector = selector
            .Replace("::before", "").Replace(":before", "")
            .Replace("::after", "").Replace(":after", "")
            .Replace("::first-letter", "").Replace(":first-letter", "")
            .Replace("::first-line", "").Replace(":first-line", "")
            .Trim();
        if (string.IsNullOrEmpty(baseSelector)) return true;

        try { if (!element.Matches(baseSelector)) return true; }
        catch { return true; }

        var cssText = rule.Style.CssText;
        if (string.IsNullOrEmpty(cssText)) return true;

        var props = ParseCssTextToDict(cssText);

        if (isBefore)
        {
            node.BeforeStyles ??= new Dictionary<string, string>();
            foreach (var (p, v) in props) node.BeforeStyles[p] = v;
        }
        if (isAfter)
        {
            node.AfterStyles ??= new Dictionary<string, string>();
            foreach (var (p, v) in props) node.AfterStyles[p] = v;
        }
        if (isFirstLetter)
        {
            node.FirstLetterStyles ??= new Dictionary<string, string>();
            foreach (var (p, v) in props) node.FirstLetterStyles[p] = v;
        }
        if (isFirstLine)
        {
            node.FirstLineStyles ??= new Dictionary<string, string>();
            foreach (var (p, v) in props) node.FirstLineStyles[p] = v;
        }

        return true;
    }

    /// <summary>
    /// Detects pseudo-class selectors (:hover, :focus, :active), strips them,
    /// matches the base selector, and stores properties in the appropriate dict.
    /// Returns true if the rule was a pseudo-class rule (whether matched or not).
    /// When <paramref name="mediaText"/> is non-null the rule is media-conditional.
    /// </summary>
    private static bool TryExtractPseudoClassRule(IElement element, LayoutNode node, ICssStyleRule rule,
        string? mediaText = null)
    {
        var selector = rule.SelectorText;

        if (selector is null)
            return false;

        // Only intercept dynamic pseudo-classes (not structural ones)
        if (!selector.Contains(":hover") && !selector.Contains(":focus") && !selector.Contains(":active")
            && !selector.Contains(":link") && !selector.Contains(":visited"))
            return false;

        // Skip if this contains structural pseudo-classes (handled by AngleSharp Matches)
        // but no dynamic ones
        var hasHover = selector.Contains(":hover");
        var hasFocus = selector.Contains(":focus");
        var hasActive = selector.Contains(":active");
        var hasLink = selector.Contains(":link");
        var hasVisited = selector.Contains(":visited");
        if (!hasHover && !hasFocus && !hasActive && !hasLink && !hasVisited) return false;

        // Strip pseudo-classes to get the base selector
        var baseSelector = selector
            .Replace(":hover", "")
            .Replace(":focus", "")
            .Replace(":active", "")
            .Replace(":visited", "")
            .Replace(":link", "")
            .Trim();
        if (string.IsNullOrEmpty(baseSelector)) return true;

        try { if (!element.Matches(baseSelector)) return true; }
        catch { return true; }

        // Extract all CSS properties from this rule
        var cssText = rule.Style.CssText;
        if (string.IsNullOrEmpty(cssText)) return true;

        var props = new Dictionary<string, string>();

        // Parse from CssText to get all properties
        foreach (var decl in cssText.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var colon = decl.IndexOf(':');
            if (colon < 0) continue;
            var prop = decl[..colon].Trim().ToLowerInvariant();
            var val = decl[(colon + 1)..].Trim().Replace("!important", "").Trim();
            if (!string.IsNullOrEmpty(val))
                props[prop] = val;
        }

        // Also try GetPropertyValue for known extra properties
        foreach (var prop in s_extraProps)
        {
            var val = rule.Style.GetPropertyValueSafe(prop);
            if (!string.IsNullOrEmpty(val) && !props.ContainsKey(prop))
                props[prop] = val;
        }

        if (mediaText is null)
        {
            // Regular pseudo-class rule
            if (hasHover) foreach (var (p, v) in props) node.HoverStyles[p] = v;
            if (hasFocus) foreach (var (p, v) in props) node.FocusStyles[p] = v;
            if (hasActive) foreach (var (p, v) in props) node.ActiveStyles[p] = v;
            // :link applies immediately to unvisited anchors (we treat all as unvisited)
            if (hasLink) foreach (var (p, v) in props) node.StyleOverrides.TryAdd(p, v);
            // :visited is ignored (privacy) — no styles applied
        }
        else
        {
            // Media-conditional pseudo-class rule — store and apply if matching
            if (hasHover)
                StoreMediaProps(node, mediaText, cssText, rule.Style, target: "hover");
            if (hasFocus)
                StoreMediaProps(node, mediaText, cssText, rule.Style, target: "focus");
            if (hasActive)
                StoreMediaProps(node, mediaText, cssText, rule.Style, target: "active");
        }

        return true;
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
    private static string CollapseWhitespace(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var sb = new System.Text.StringBuilder(text.Length);
        var lastWasSpace = false;
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch))
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
    internal static List<LayoutNode> ParseFragment(string html, string contextTag = "DIV")
    {
        var result = new List<LayoutNode>();
        if (string.IsNullOrEmpty(html)) return result;

        var doc = Document;
        if (doc is null)
        {
            // Include the render device so absolute units (in/pt/cm/…) resolve at 96 DPI,
            // matching TraverseHtml — otherwise a fragment parsed before any page load
            // computes 1in as 64px instead of 96px.
            var cfg = Configuration.Default.WithCss().WithRenderDevice();
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
                    var tn = new LayoutNode(null, "#text", text, container.ComputeCurrentStyle());
                    tn.StyleOverrides["display"] = "inline";
                    result.Add(tn);
                }
                else if (childNode is IElement childEl)
                {
                    var ct = childEl.TagName.ToUpperInvariant();
                    if (ct == "SCRIPT") continue;          // innerHTML never runs scripts
                    if (SkipTags.Contains(ct)) continue;
                    result.Add(Traverse(childEl, 0));
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
        var fragStyle = templateEl.ComputeCurrentStyle();
        var frag = new LayoutNode(null, "#document-fragment", string.Empty, fragStyle);
        if (templateEl is not AngleSharp.Html.Dom.IHtmlTemplateElement tmpl) return frag;

        var savedCounters = _counters.ToDictionary(kv => kv.Key, kv => new List<int>(kv.Value));
        try
        {
            foreach (var childNode in tmpl.Content.ChildNodes)
            {
                if (childNode is IText textNode)
                {
                    var text = CollapseWhitespace(textNode.Data);
                    if (text.Length == 0) continue;
                    var tn = new LayoutNode(null, "#text", text, fragStyle);
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

            // This source wins — point the <img> at it.
            imgNode.Attributes["src"] = url;
            imgNode.Attributes["_currentSrc"] = ResolveAgainstBase(url);
            imgNode.Image = ResourceLoader.FetchImage(url, _baseUrl);
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
        if (string.IsNullOrEmpty(_baseUrl)) return url;
        return Uri.TryCreate(new Uri(_baseUrl), url, out var abs) ? abs.AbsoluteUri : url;
    }

    // ---- CSS rule storage for the LayoutNode-based cascade (StyleResolver) ----

    /// <summary>A collected author style rule with the data needed for a correct cascade.</summary>
    internal sealed record CssRule(
        string Selector,
        int Specificity,
        int Order,
        Dictionary<string, string> Properties,
        HashSet<string> ImportantProps);

    internal static readonly List<CssRule> CssRules = [];

    /// <summary>
    /// Collects all CSS style rules from the document's stylesheets for the runtime cascade
    /// (StyleResolver) used by dynamically created elements and className changes.
    /// </summary>
    private static void CollectCssRules(AngleSharp.Dom.IDocument document)
    {
        CssRules.Clear();
        if (document.StyleSheets is null) return;
        foreach (var sheet in document.StyleSheets.OfType<ICssStyleSheet>())
            CollectRulesFromSheet(sheet.Rules);
    }

    private static void CollectRulesFromSheet(ICssRuleList rules)
    {
        foreach (var rule in rules)
        {
            if (rule is ICssMediaRule mediaRule)
            {
                CollectRulesFromSheet(mediaRule.Rules);
                continue;
            }
            if (rule is not ICssStyleRule styleRule) continue;

            var (props, important) = ParseDeclarations(styleRule.Style.CssText);
            if (props.Count == 0) continue;
            if (string.IsNullOrEmpty(styleRule.SelectorText)) continue;

            // A selector list ("a, b") gets one entry per selector so each keeps its own specificity.
            foreach (var sel in SplitSelectorList(styleRule.SelectorText))
            {
                var s = sel.Trim();
                if (s.Length == 0) continue;
                CssRules.Add(new CssRule(s, ComputeSpecificity(s), CssRules.Count, props, important));
            }
        }
    }

    /// <summary>Parses a declaration block, separating normal and !important declarations.</summary>
    private static (Dictionary<string, string> Props, HashSet<string> Important) ParseDeclarations(string cssText)
    {
        var props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var important = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(cssText)) return (props, important);

        foreach (var decl in cssText.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var colon = decl.IndexOf(':');
            if (colon < 0) continue;
            var prop = decl[..colon].Trim().ToLowerInvariant();
            var raw = decl[(colon + 1)..].Trim();
            if (raw.EndsWith("!important", StringComparison.OrdinalIgnoreCase))
            {
                raw = raw[..^"!important".Length].Trim();
                important.Add(prop);
            }
            if (!string.IsNullOrEmpty(raw)) props[prop] = raw;
        }
        return (props, important);
    }

    /// <summary>Computes a CSS specificity score (a*100 + b*10 + c) for a compound/complex selector.</summary>
    internal static int ComputeSpecificity(string selector)
    {
        int a = 0, b = 0, c = 0;
        int i = 0;
        while (i < selector.Length)
        {
            var ch = selector[i];
            if (ch == '#') { a++; i++; SkipIdent(selector, ref i); }
            else if (ch == '.' || ch == '[') { b++; i++; if (ch == '[') SkipTo(selector, ref i, ']'); else SkipIdent(selector, ref i); }
            else if (ch == ':')
            {
                i++;
                if (i < selector.Length && selector[i] == ':') { c++; i++; SkipIdent(selector, ref i); } // ::pseudo-element
                else { b++; SkipIdent(selector, ref i); if (i < selector.Length && selector[i] == '(') SkipTo(selector, ref i, ')'); }
            }
            else if (char.IsLetter(ch)) { c++; SkipIdent(selector, ref i); } // type selector
            else i++; // combinators, '*', whitespace
        }
        return a * 100 + b * 10 + c;
    }

    private static void SkipIdent(string s, ref int i)
    {
        while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '-' || s[i] == '_')) i++;
    }

    private static void SkipTo(string s, ref int i, char end)
    {
        while (i < s.Length && s[i] != end) i++;
        if (i < s.Length) i++;
    }

    private static IEnumerable<string> SplitSelectorList(string selectorText)
    {
        if (string.IsNullOrEmpty(selectorText)) yield break;
        int depth = 0, start = 0;
        for (int i = 0; i < selectorText.Length; i++)
        {
            if (selectorText[i] == '(') depth++;
            else if (selectorText[i] == ')') depth--;
            else if (selectorText[i] == ',' && depth == 0)
            {
                yield return selectorText[start..i];
                start = i + 1;
            }
        }
        yield return selectorText[start..];
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
