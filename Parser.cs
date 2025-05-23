using AngleSharp.Io;
using AngleSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AngleSharp.Dom;
using System.Reflection.Metadata;
using System.Xml.Linq;
using AngleSharp.Css.Dom;
using AngleSharp.Css;

namespace Lite
{
    internal class Parser
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
            return Traverse(document.DocumentElement, 0, document);
        }

        // Recursive traversal method
        static List<DrawCommand> Traverse(IElement element, int indent, IDocument document)
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
               var childResult = Traverse(child, indent + 1, document);
               result.AddRange(childResult);
            }

            return result;
        }
    }
}
