using AngleSharp.Io;
using AngleSharp;
using AngleSharp.Dom;
using Lite.Models;

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
        """;

    internal static List<DrawCommand> TraverseHtml(string address)
    {
        // Create a configuration that loads external resources and includes CSS support.
        var config = Configuration.Default
            .WithDefaultLoader(new LoaderOptions { IsResourceLoadingEnabled = true })
            .WithCss()
            .WithRenderDevice();

        var context = BrowsingContext.New(config);
        var document = context.OpenAsync(address).Result;

        // Inject the user-agent stylesheet as the first style element so author
        // styles always win via source-order cascade.
        var uaStyle = document.CreateElement("style");
        uaStyle.TextContent = UserAgentStylesheet;
        var head = document.Head ?? document.DocumentElement;
        head.InsertBefore(uaStyle, head.FirstChild);

        // Start traversing from the root element
        return Traverse(document.DocumentElement, 0);
    }

    // Recursive traversal method
    private static List<DrawCommand> Traverse(IElement element, int indent)
    {
        var result = new List<DrawCommand>();

        string indentSpace = new string(' ', indent * 2);
        Console.WriteLine($"{indentSpace}Tag: {element.TagName}, ID: {element.Id}, Class: {element.ClassName}");

        var directText = string.Concat(element.ChildNodes.OfType<IText>().Select(t => t.Data)).Trim();
        var href = element.TagName == "A" ? element.GetAttribute("href") : null;
        result.Add(new DrawCommand(element.Id, element.TagName, directText, element.ComputeCurrentStyle(), href));

        // At this point, if you want to consider external CSS, 
        // you'd need to implement rule matching to resolve corresponding CSS properties.

        // Recursively traverse child elements
        foreach (var child in element.Children)
        {
            var childResult = Traverse(child, indent + 1);
            result.AddRange(childResult);
        }

        return result;
    }
}