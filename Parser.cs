using AngleSharp.Io;
using AngleSharp;
using AngleSharp.Dom;

namespace Lite;

internal static class Parser
{
    internal static List<DrawCommand> TraverseHtml(string address)
    {
        // Create a configuration that loads external resources and includes CSS support.
        var config = Configuration.Default
            .WithDefaultLoader(new LoaderOptions { IsResourceLoadingEnabled = true })
            .WithCss()
            .WithRenderDevice();

        var context = BrowsingContext.New(config);
        var document = context.OpenAsync(address).Result;

        // Start traversing from the root element
        return Traverse(document.DocumentElement, 0);
    }

    // Recursive traversal method
    private static List<DrawCommand> Traverse(IElement element, int indent)
    {
        var result = new List<DrawCommand>();

        string indentSpace = new string(' ', indent * 2);
        Console.WriteLine($"{indentSpace}Tag: {element.TagName}, ID: {element.Id}, Class: {element.ClassName}");

        result.Add(new DrawCommand(element.Id, element.TagName, element.GetInnerText(), element.ComputeCurrentStyle()));

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