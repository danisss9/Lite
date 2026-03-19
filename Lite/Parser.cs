using AngleSharp.Io;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Css.Dom;
using Lite.Models;
using Lite.Network;
using Lite.Scripting;

namespace Lite;

internal static class Parser
{
    private const string UserAgentStylesheet = """
        div, section, article, header, footer, main, nav, aside, form, ul, ol, li, fieldset, figure, figcaption, address, details, summary { display: block; }
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
        """;

    // Tags that should not appear in the layout tree
    private static readonly HashSet<string> SkipTags =
        ["HEAD", "STYLE", "NOSCRIPT", "META", "LINK", "TITLE", "TEMPLATE"];

    private static string? _baseUrl;
    private static readonly List<string> _pendingScripts = [];
    private static readonly HttpClient _httpClient = new();

    internal static LayoutNode TraverseHtml(string address)
    {
        _baseUrl = address;
        _pendingScripts.Clear();

        var config = Configuration.Default
            .WithDefaultLoader(new LoaderOptions { IsResourceLoadingEnabled = true })
            .WithCss()
            .WithRenderDevice();

        var context  = BrowsingContext.New(config);
        var document = context.OpenAsync(address).Result;

        var head = document.Head ?? document.DocumentElement;

        // Inject UA stylesheet first
        var uaStyle = document.CreateElement("style");
        uaStyle.TextContent = UserAgentStylesheet;
        head.InsertBefore(uaStyle, head.FirstChild);

        // Eagerly fetch and inline all <link rel="stylesheet"> files so that
        // ComputeCurrentStyle() sees the fully-cascaded styles synchronously.
        foreach (var link in document.QuerySelectorAll("link[rel='stylesheet']"))
        {
            var href = link.GetAttribute("href");
            if (string.IsNullOrEmpty(href)) continue;
            var cssUrl = ResolveUrl(href);
            if (cssUrl is null) continue;
            try
            {
                var css     = _httpClient.GetStringAsync(cssUrl).Result;
                var styleEl = document.CreateElement("style");
                styleEl.TextContent = css;
                head.AppendChild(styleEl);
            }
            catch (Exception ex) { Console.WriteLine($"[CSS load error] {cssUrl}: {ex.Message}"); }
        }

        var root = Traverse(document.DocumentElement, 0);

        // Always create the JS engine so inline onclick/on* handlers work,
        // even when there are no external or inline script blocks.
        var jsEngine = JsEngine.Create(root);
        foreach (var script in _pendingScripts)
            jsEngine.Execute(script);

        return root;
    }

    private static string? ResolveUrl(string src)
    {
        if (Uri.TryCreate(src, UriKind.Absolute, out _)) return src;
        if (_baseUrl != null && Uri.TryCreate(new Uri(_baseUrl), src, out var resolved))
            return resolved.ToString();
        return null;
    }

    private static LayoutNode Traverse(IElement element, int indent)
    {
        var indentSpace = new string(' ', indent * 2);
        Console.WriteLine($"{indentSpace}Tag: {element.TagName}, ID: {element.Id}, Class: {element.ClassName}");

        // Determine whether this element has renderable element children (non-skipped, non-script).
        // If so, we walk ChildNodes in order so that interleaved text nodes (e.g. "text <em>italic</em> more")
        // are preserved as synthetic #TEXT children rather than being collapsed onto the parent.
        var hasMixedChildren = element.Children.Any(c => c.TagName != "SCRIPT" && !SkipTags.Contains(c.TagName));

        var directText = hasMixedChildren
            ? ""   // text nodes become ordered #TEXT children below
            : string.Concat(element.ChildNodes.OfType<IText>().Select(t => t.Data)).Trim();

        var href = element.TagName == "A" ? element.GetAttribute("href") : null;
        var node = new LayoutNode(element.Id, element.TagName, directText, element.ComputeCurrentStyle(), href);

        // Extract flex-related CSS properties that AngleSharp doesn't cascade
        ExtractMatchedCssProperties(element, node);

        if (element.TagName == "IMG")
        {
            var src = element.GetAttribute("src");
            node.Alt = element.GetAttribute("alt") ?? string.Empty;

            if (int.TryParse(element.GetAttribute("width"),  out var w)) node.IntrinsicWidth  = w;
            if (int.TryParse(element.GetAttribute("height"), out var h)) node.IntrinsicHeight = h;

            if (!string.IsNullOrEmpty(src))
                node.Image = ResourceLoader.FetchImage(src, _baseUrl);
        }

        if (element.TagName is "INPUT" or "BUTTON")
        {
            foreach (var attr in new[] { "type", "value", "placeholder", "checked" })
            {
                var val = element.GetAttribute(attr);
                if (val != null) node.Attributes[attr] = val;
            }
        }

        // Capture inline event handlers for any element
        foreach (var attr in new[] { "onclick", "onchange", "oninput", "onsubmit", "onkeyup", "onkeydown" })
        {
            var val = element.GetAttribute(attr);
            if (val != null) node.Attributes[attr] = val;
        }

        if (hasMixedChildren)
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
                        var textChild = new LayoutNode(null, "#TEXT", text, parentStyle);
                        textChild.StyleOverrides[AngleSharp.Css.PropertyNames.Display] = "inline";
                        node.AddChild(textChild);
                    }
                }
                else if (childNode is IElement childEl)
                {
                    if (childEl.TagName == "SCRIPT")
                    {
                        CollectScript(childEl);
                        continue;
                    }
                    if (SkipTags.Contains(childEl.TagName)) continue;
                    node.AddChild(Traverse(childEl, indent + 1));
                }
            }
        }
        else
        {
            foreach (var child in element.Children)
            {
                if (child.TagName == "SCRIPT") { CollectScript(child); continue; }
                if (SkipTags.Contains(child.TagName)) continue;
                node.AddChild(Traverse(child, indent + 1));
            }
        }

        return node;
    }

    private static void CollectScript(IElement scriptEl)
    {
        var src = scriptEl.GetAttribute("src");
        if (src != null)
        {
            var scriptUrl = ResolveUrl(src);
            if (scriptUrl != null)
            {
                try
                {
                    var code = _httpClient.GetStringAsync(scriptUrl).Result;
                    if (!string.IsNullOrWhiteSpace(code))
                        _pendingScripts.Add(code);
                }
                catch (Exception ex) { Console.WriteLine($"[Script load error] {scriptUrl}: {ex.Message}"); }
            }
        }
        else if (!string.IsNullOrWhiteSpace(scriptEl.TextContent))
        {
            _pendingScripts.Add(scriptEl.TextContent);
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
        "border-radius"
    ];

    /// <summary>
    /// Iterates all stylesheet rules that match <paramref name="element"/> and copies
    /// flex-related property values into the node's StyleOverrides dictionary.
    /// This works around AngleSharp.Css not cascading these properties through ComputeCurrentStyle().
    /// </summary>
    private static bool _debugCssOnce = false;
    private static void ExtractMatchedCssProperties(IElement element, LayoutNode node)
    {
        if (element.Owner?.StyleSheets is null) return;

        var sheets = element.Owner.StyleSheets.ToList();
        if (!_debugCssOnce)
        {
            _debugCssOnce = true;
            Console.WriteLine($"[CSS-DEBUG] Total stylesheets: {sheets.Count}");
            foreach (var s in sheets)
                Console.WriteLine($"  Sheet type: {s.GetType().FullName}, isCss: {s is ICssStyleSheet}");
            if (sheets.OfType<ICssStyleSheet>().FirstOrDefault() is { } firstCss)
            {
                Console.WriteLine($"  First CSS sheet rules: {firstCss.Rules.Length}");
                foreach (var r in firstCss.Rules.Take(5))
                    Console.WriteLine($"    Rule type: {r.GetType().FullName}, isCssStyle: {r is ICssStyleRule}, text: {r.CssText?[..Math.Min(r.CssText?.Length ?? 0, 80)]}");
            }
        }

        foreach (var sheet in sheets.OfType<ICssStyleSheet>())
        {
            foreach (var rule in sheet.Rules.OfType<ICssStyleRule>())
            {
                try
                {
                    if (!element.Matches(rule.SelectorText)) continue;
                }
                catch { continue; } // malformed selector

                // Try GetPropertyValue first, then fall back to iterating CssText
                var style = rule.Style;
                foreach (var prop in s_extraProps)
                {
                    var val = style.GetPropertyValue(prop);
                    if (!string.IsNullOrEmpty(val))
                    {
                        StoreProp(node, prop, val);
                        continue;
                    }
                }

                // Some versions of AngleSharp.Css don't expose flex properties via GetPropertyValue
                // on the rule's style declaration. Fall back to parsing CssText.
                ParseCssTextForFlexProps(rule.Style.CssText, node);
            }
        }
    }

    private static readonly HashSet<string> s_flexDirectionValues =
        ["row", "row-reverse", "column", "column-reverse"];
    private static readonly HashSet<string> s_flexWrapValues =
        ["nowrap", "wrap", "wrap-reverse"];

    private static void StoreProp(LayoutNode node, string prop, string val)
    {
        if (prop == "gap")
        {
            var parts = val.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            node.StyleOverrides["row-gap"]    = parts[0];
            node.StyleOverrides["column-gap"] = parts.Length > 1 ? parts[1] : parts[0];
            Console.WriteLine($"  [CSS] {node.TagName}#{node.Id}.{node.Text[..Math.Min(node.Text.Length, 20)]}: gap → row-gap={parts[0]}, column-gap={(parts.Length > 1 ? parts[1] : parts[0])}");
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
        else
        {
            node.StyleOverrides[prop] = val;
            Console.WriteLine($"  [CSS] {node.TagName}#{node.Id}: {prop} = {val}");
        }
    }

    private static void DecomposeFlexShorthand(LayoutNode node, string val)
    {
        val = val.Trim().ToLowerInvariant();
        switch (val)
        {
            case "none":
                node.StyleOverrides["flex-grow"]   = "0";
                node.StyleOverrides["flex-shrink"] = "0";
                node.StyleOverrides["flex-basis"]  = "auto";
                return;
            case "auto":
                node.StyleOverrides["flex-grow"]   = "1";
                node.StyleOverrides["flex-shrink"] = "1";
                node.StyleOverrides["flex-basis"]  = "auto";
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
                node.StyleOverrides["flex-grow"]   = parts[0];
                node.StyleOverrides["flex-shrink"] = "1";
                node.StyleOverrides["flex-basis"]  = "0px";
            }
            else
            {
                node.StyleOverrides["flex-grow"]   = "1";
                node.StyleOverrides["flex-shrink"] = "1";
                node.StyleOverrides["flex-basis"]  = parts[0];
            }
        }
        else if (parts.Length == 2)
        {
            node.StyleOverrides["flex-grow"]   = parts[0];
            // Second value: number → shrink, length → basis
            if (float.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out _))
            {
                node.StyleOverrides["flex-shrink"] = parts[1];
                node.StyleOverrides["flex-basis"]  = "0px";
            }
            else
            {
                node.StyleOverrides["flex-shrink"] = "1";
                node.StyleOverrides["flex-basis"]  = parts[1];
            }
        }
        else if (parts.Length >= 3)
        {
            node.StyleOverrides["flex-grow"]   = parts[0];
            node.StyleOverrides["flex-shrink"] = parts[1];
            node.StyleOverrides["flex-basis"]  = parts[2];
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
            var val  = declaration[(colonIdx + 1)..].Trim();

            if (string.IsNullOrEmpty(val)) continue;

            // Only extract our target properties
            if (Array.IndexOf(s_extraProps, prop) >= 0)
                StoreProp(node, prop, val);
        }
    }

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
}
