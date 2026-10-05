using Lite;
using Lite.Extensions;
using Lite.Models;
using Lite.Network;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SkiaSharp;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>Documents served as XML (application/xhtml+xml) must be parsed with the XML
/// parser and their author styles must cascade. Known gap: AngleSharp matches type
/// selectors case-insensitively in every document language, so CSS 2.1 §5.1's
/// case-sensitive XML type matching is enforced on Lite's own selector engine
/// (script-side queries), not yet through the cascade matcher.</summary>
public static class XhtmlTests
{
    [Test]
    public static void XhtmlDocument_ParsesWithXmlCaseSensitivity()
    {
        using var server = new XmlTestServer();
        using var session = new BrowserSession();
        var page = Parser.TraversePage(new NavigationRequest(server.BaseUrl + "/doc.xhtml"), 800, 600, session);
        // XML DOM tells: no special body element, source tag case preserved, and the
        // parsed-in author <style> cascades onto the element.
        True(Parser.Document?.Body is null, "an XML document must not expose an HTML body element");
        True(Parser.IsXmlDocument, "the document mode must be XML");
        var p = Find(page.Root, "P");
        True(p != null, "expected the p element in the layout tree");
        Equal("p", p!.SourceTagName ?? "<null>");
        Equal(new SKColor(0, 128, 0).ToString(), p.GetColor().ToString());
    }

    private static LayoutNode? Find(LayoutNode node, string tagName)
    {
        if (node.TagName == tagName) return node;
        foreach (var child in node.Children)
        {
            var found = Find(child, tagName);
            if (found is not null) return found;
        }
        return null;
    }

    private sealed class XmlTestServer : IDisposable
    {
        private readonly WebApplication _app;
        internal string BaseUrl => _app.Urls.Single();

        internal XmlTestServer()
        {
            var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions());
            builder.WebHost.UseKestrelCore();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddRoutingCore();
            _app = builder.Build();
            _app.Run(async ctx =>
            {
                ctx.Response.ContentType = "application/xhtml+xml; charset=utf-8";
                await ctx.Response.WriteAsync(
                    "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                    "<html xmlns=\"http://www.w3.org/1999/xhtml\"><head><title>x</title><style>" +
                    "html { color: green } p { color: green }" +
                    "</style></head><body><p>x</p></body></html>");
            });
            _app.Start();
        }

        public void Dispose() => _app.StopAsync().Wait();
    }
}
