using System.Net;
using Lite;
using Lite.Interaction;
using Lite.Layout;
using Lite.Models;
using Lite.Network;
using Lite.Scripting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>A small server-rendered search fixture with DuckDuckGo Lite's GET/POST shape.</summary>
public static class DuckDuckGoLiteTests
{
    private sealed record RequestSeen(string Method, string Path, string? ContentType, string Body);

    private sealed class SearchServer : IDisposable
    {
        private readonly WebApplication _app;
        internal readonly List<RequestSeen> Requests = [];
        internal string BaseUrl => _app.Urls.Single();

        internal SearchServer()
        {
            var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions());
            builder.WebHost.UseKestrelCore();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddRoutingCore();
            _app = builder.Build();
            _app.Run(async ctx =>
            {
                string body;
                using (var reader = new StreamReader(ctx.Request.Body)) body = await reader.ReadToEndAsync();
                lock (Requests) Requests.Add(new RequestSeen(ctx.Request.Method,
                    ctx.Request.Path.Value ?? "", ctx.Request.ContentType, body));

                if (ctx.Request.Path == "/site.css")
                {
                    ctx.Response.ContentType = "text/css";
                    await ctx.Response.WriteAsync("body{height:100%}#lite_wrapper{position:relative;top:25%}" +
                        ".query{width:60%;max-width:600px;height:28px;padding:5px 6px;border:1px solid #dc5e47}" +
                        ".submit{height:40px;font-size:20px}");
                    return;
                }
                if (ctx.Request.Path == "/redirect")
                {
                    ctx.Response.StatusCode = StatusCodes.Status303SeeOther;
                    ctx.Response.Headers.Location = "/destination";
                    return;
                }
                if (ctx.Request.Path == "/latin-redirect")
                {
                    ctx.Response.StatusCode = StatusCodes.Status303SeeOther;
                    ctx.Response.Headers.Location = "/latin-destination";
                    return;
                }
                if (ctx.Request.Path == "/latin-destination")
                {
                    ctx.Response.ContentType = "text/html; charset=iso-8859-1";
                    await ctx.Response.Body.WriteAsync(System.Text.Encoding.Latin1.GetBytes(
                        "<!doctype html><title>Caf\u00e9</title><p>encoded response</p>"));
                    return;
                }
                ctx.Response.ContentType = "text/html; charset=utf-8";
                if (ctx.Request.Path == "/destination")
                {
                    await ctx.Response.WriteAsync("<!doctype html><title>Destination</title><p id='destination'>opened</p>");
                    return;
                }
                if (ctx.Request.Path != "/lite/") { ctx.Response.StatusCode = 404; return; }
                if (ctx.Request.Method == "POST")
                {
                    var next = body.Contains("s=10", StringComparison.Ordinal);
                    var nextForm = "<form id='next' action='/lite/' method='post'>" +
                        "<input type='hidden' name='q' value='small web café'>" +
                        "<input type='hidden' name='s' value='10'>" +
                        "<input type='hidden' name='vqd' value='fixture-token'>" +
                        "<input type='submit' value='Next Page &gt;'></form>";
                    var pager = next
                        ? "<table id='pager'><tr><td><form id='prev' action='/lite/' method='post'>" +
                          "<input type='submit' value='&lt; Previous Page'></form></td><td>" +
                          nextForm + "</td></tr></table>"
                        : nextForm;
                    await ctx.Response.WriteAsync("<!doctype html><title>Search results</title>" +
                        "<form id='search' action='/lite/' method='post'>" +
                        "<input name='q' value='small web café'>" +
                        "<select name='kl'><option value=''>All Regions</option>" +
                        "<option value='pt-pt' selected>Portugal</option><option value='us-en'>US</option></select>" +
                        "<select name='df'><option value='' selected>Any Time</option>" +
                        "<option value='m'>Past Month</option></select><input type='submit' value='Search'></form>" +
                        "<table><tr><td>1.</td><td><a id='result' href='/destination'>Result</a></td></tr>" +
                        "<tr><td></td><td>Readable snippet</td></tr></table>" +
                        pager +
                        $"<div id='page'>{(next ? "2" : "1")}</div>");
                    return;
                }
                await ctx.Response.WriteAsync("<!doctype html><html><head>" +
                    "<link rel='stylesheet' href='/site.css'></head><body>" +
                    "<center id='lite_wrapper'><span>DuckDuckGo</span>" +
                    "<form id='search' action='/lite/' method='post'>" +
                    "<input class='query' type='text' name='q' autofocus>" +
                    "<input class='submit' type='submit' name='go' value='Search'></form></center>" +
                    "</body></html>");
            });
            _app.Start();
        }

        internal RequestSeen LastPost()
        {
            lock (Requests) return Requests.Last(r => r.Method == "POST");
        }

        public void Dispose() => _app.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private static LayoutNode Find(LayoutNode root, string id)
    {
        if (root.Id == id) return root;
        foreach (var child in root.Children)
        {
            var found = FindOrNull(child, id);
            if (found != null) return found;
        }
        throw new Exception($"Missing element #{id}");
    }

    private static LayoutNode? FindOrNull(LayoutNode root, string id)
    {
        if (root.Id == id) return root;
        foreach (var child in root.Children)
            if (FindOrNull(child, id) is { } found) return found;
        return null;
    }

    private static LayoutNode FindControl(LayoutNode form, string name)
    {
        if (form.Attributes.GetValueOrDefault("name") == name) return form;
        foreach (var child in form.Children)
            if (FindControlOrNull(child, name) is { } found) return found;
        throw new Exception($"Missing form control {name}");
    }

    private static LayoutNode? FindControlOrNull(LayoutNode root, string name)
    {
        if (root.Attributes.GetValueOrDefault("name") == name) return root;
        foreach (var child in root.Children)
            if (FindControlOrNull(child, name) is { } found) return found;
        return null;
    }

    private static LayoutNode FindSubmit(LayoutNode root)
    {
        if (root.TagName == "INPUT" && root.Attributes.GetValueOrDefault("type") == "submit") return root;
        foreach (var child in root.Children)
            if (FindSubmitOrNull(child) is { } found) return found;
        throw new Exception("Missing submit input");
    }

    private static LayoutNode? FindSubmitOrNull(LayoutNode root)
    {
        if (root.TagName == "INPUT" && root.Attributes.GetValueOrDefault("type") == "submit") return root;
        foreach (var child in root.Children)
            if (FindSubmitOrNull(child) is { } found) return found;
        return null;
    }

    [Test]
    public static void SearchPost_FiltersPaginationLinksAndRedirects()
    {
        using var server = new SearchServer();
        var home = Parser.TraversePage(new NavigationRequest(server.BaseUrl + "/lite/"), 800, 600);
        var search = Find(home.Root, "search");
        var query = FindControl(search, "q");
        var button = FindControl(search, "go");
        FormState.TextInputValues[query.NodeKey] = "small web café";

        // Both native click (activated submitter) and Enter (no explicit submitter) use
        // PrepareNavigation, which also dispatches the cancelable submit event.
        var click = FormSubmitter.PrepareNavigation(search, home.Engine, true, button)!.Value;
        var enter = FormSubmitter.PrepareNavigation(search, home.Engine, true)!.Value;
        Equal("POST", click.Method);
        Equal("q=small+web+caf%C3%A9&go=Search", click.Body);
        Equal("q=small+web+caf%C3%A9", enter.Body);
        Equal("application/x-www-form-urlencoded", click.ContentType);

        var results = Parser.TraversePage(click, 800, 600);
        Equal(server.BaseUrl + "/lite/", results.Root.DocumentState?.Address);
        Equal("Search results", results.Document?.Title);
        Equal("1", Find(results.Root, "page").Text);
        var received = server.LastPost();
        Equal("/lite/", received.Path);
        Equal(click.Body, received.Body);
        Contains("application/x-www-form-urlencoded", received.ContentType);

        var resultLink = Find(results.Root, "result");
        var (_, regions) = Drawer.Draw(800, 600, results.Root, new Viewport { ViewportHeight = 600 });
        True(regions.Any(region => region.NodeKey == resultLink.NodeKey && region.Href != null),
            "the rendered result link needs a clickable hit region");
        True(resultLink.Box.BorderBox.Width > 40f, "result table collapsed the link");
        var destination = Parser.TraversePage(new NavigationRequest(
            new Uri(new Uri(results.Root.DocumentState!.Address), resultLink.Href!).AbsoluteUri));
        Equal("Destination", destination.Document?.Title);

        var filterForm = Find(results.Root, "search");
        FormState.TextInputValues[FindControl(filterForm, "kl").NodeKey] = "us-en";
        FormState.TextInputValues[FindControl(filterForm, "df").NodeKey] = "m";
        var filteredRequest = FormSubmitter.PrepareNavigation(filterForm, results.Engine, true)!.Value;
        var filtered = Parser.TraversePage(filteredRequest);
        Equal("Search results", filtered.Document?.Title);
        Contains("kl=us-en", server.LastPost().Body);
        Contains("df=m", server.LastPost().Body);

        var nextForm = Find(results.Root, "next");
        var nextRequest = FormSubmitter.PrepareNavigation(nextForm, results.Engine, true)!.Value;
        var page2 = Parser.TraversePage(nextRequest);
        Equal("2", Find(page2.Root, "page").Text);
        Contains("q=small+web+caf%C3%A9", server.LastPost().Body);
        Contains("s=10", server.LastPost().Body);
        Contains("vqd=fixture-token", server.LastPost().Body);
        Drawer.Draw(800, 600, page2.Root, new Viewport { ViewportHeight = 600 });
        var previousButton = FindSubmit(Find(page2.Root, "prev"));
        var nextButton = FindSubmit(Find(page2.Root, "next"));
        True(previousButton.Box.BorderBox.Right <= nextButton.Box.BorderBox.Left,
            "previous and next buttons overlap in the pager table");

        var redirected = Parser.TraversePage(new NavigationRequest(server.BaseUrl + "/redirect",
            "POST", "q=redirect", "application/x-www-form-urlencoded"));
        Equal(server.BaseUrl + "/destination", redirected.Root.DocumentState?.Address);
        Equal("Destination", redirected.Document?.Title);

        var latin = Parser.TraversePage(new NavigationRequest(server.BaseUrl + "/latin-redirect",
            "POST", "q=encoding", "application/x-www-form-urlencoded"));
        Equal(server.BaseUrl + "/latin-destination", latin.Root.DocumentState?.Address);
        Equal("Caf\u00e9", latin.Document?.Title);
    }

    [Test]
    public static void SubmitEvent_CanCancelAndDomMethodsKeepPostBody()
    {
        using var server = new SearchServer();
        var home = Parser.TraversePage(new NavigationRequest(server.BaseUrl + "/lite/"));
        var form = Find(home.Root, "search");
        home.Engine.Execute("document.getElementById('search').addEventListener('submit', e => e.preventDefault())");
        True(FormSubmitter.PrepareNavigation(form, home.Engine, true) is null,
            "preventDefault should cancel native form navigation");
        NavigationRequest? canceled = null;
        home.Engine.OnNavigate = req => canceled = req;
        home.Engine.Execute("document.getElementById('search').requestSubmit()");
        home.Engine.DrainTasks();
        True(canceled is null, "preventDefault should cancel requestSubmit");

        home.Engine.Execute("document.getElementById('search').submit()");
        home.Engine.DrainTasks();
        Equal("POST", canceled?.Method);
        Contains("q=", canceled?.Body);
    }

    [Test]
    public static void Homepage_UsesDesktopControlWidthAndRelativeHitRegions()
    {
        using var server = new SearchServer();
        foreach (var (width, height, expectedWidth) in new[] { (800, 600, 470.4f), (1280, 800, 600f) })
        {
            var page = Parser.TraversePage(new NavigationRequest(server.BaseUrl + "/lite/"), width, height);
            var viewport = new Viewport { ViewportHeight = height };
            var (_, hits) = Drawer.Draw(width, height, page.Root, viewport);
            var query = FindControl(Find(page.Root, "search"), "q");
            True(Math.Abs(query.Box.ContentBox.Width - expectedWidth) < 2f,
                $"query width was {query.Box.ContentBox.Width}, expected {expectedWidth}");
            var hit = hits.Single(h => h.NodeKey == query.NodeKey && h.InputAction == InputAction.TextInput);
            True(hit.Bounds.Top >= height * 0.25f,
                $"relative search hit region stayed at layout position {hit.Bounds.Top}");
            var submit = FindControl(Find(page.Root, "search"), "go");
            True(hits.Any(h => h.NodeKey == submit.NodeKey && h.InputAction == InputAction.Button),
                "submit inputs must paint and hit-test as buttons");
            True(query.Box.ContentBox.Width > submit.Box.ContentBox.Width,
                "search input should be wider than the submit button");
        }
    }
}
