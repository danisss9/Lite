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
        div { display: block; }
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
        button { display: inline-block; cursor: pointer; }
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

        var uaStyle = document.CreateElement("style");
        uaStyle.TextContent = UserAgentStylesheet;
        var head = document.Head ?? document.DocumentElement;
        head.InsertBefore(uaStyle, head.FirstChild);

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

        var directText = string.Concat(element.ChildNodes.OfType<IText>().Select(t => t.Data)).Trim();
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

        foreach (var child in element.Children)
        {
            // Collect scripts separately; skip layout-irrelevant tags
            if (child.TagName == "SCRIPT")
            {
                var src = child.GetAttribute("src");
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
                else if (!string.IsNullOrWhiteSpace(child.TextContent))
                {
                    _pendingScripts.Add(child.TextContent);
                }
                continue;
            }

            if (SkipTags.Contains(child.TagName)) continue;

            node.AddChild(Traverse(child, indent + 1));
        }

        return node;
    }
}
