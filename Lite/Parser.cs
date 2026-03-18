using AngleSharp.Io;
using AngleSharp;
using AngleSharp.Dom;
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
                    // Skip inter-element whitespace (e.g. newlines between block tags),
                    // but keep text that has real content even if it has surrounding spaces
                    // (e.g. " and " between two inline elements must not lose its spaces).
                    if (text.Trim().Length > 0)
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
