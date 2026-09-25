using System.Net;
using System.Diagnostics;
using Lite;
using Lite.Interaction;
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

/// <summary>Deterministic replay of the signed-out Search and consent journey observed in Portugal.</summary>
public static class GoogleSearchTests
{
    private const string FixtureVersion = "2026-09-25";

    private sealed class ReplayServer : IDisposable
    {
        private readonly WebApplication _app;
        private readonly string _fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Google", FixtureVersion);
        internal readonly List<string> Choices = [];
        internal string BaseUrl => _app.Urls.Single();

        internal ReplayServer()
        {
            var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions());
            builder.WebHost.UseKestrelCore();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddRoutingCore();
            _app = builder.Build();
            _app.Run(async ctx =>
            {
                var path = ctx.Request.Path.Value ?? "/";
                if (path.StartsWith("/assets/", StringComparison.Ordinal))
                {
                    var name = path[8..];
                    if (name is not ("home.css" or "home.js" or "consent.css" or "results.css" or "results.js" or "logo.png"))
                    { ctx.Response.StatusCode = 404; return; }
                    ctx.Response.ContentType = name.EndsWith(".css") ? "text/css" : name.EndsWith(".js")
                        ? "text/javascript" : "image/png";
                    await ctx.Response.Body.WriteAsync(File.ReadAllBytes(Path.Combine(_fixtures, name)));
                    return;
                }
                ctx.Response.ContentType = "text/html; charset=utf-8";
                if (path == "/reference/consent")
                {
                    await ctx.Response.WriteAsync(Read("consent.html").Replace("{{CONTINUE}}",
                        "/search?q=lite+browser+engine", StringComparison.Ordinal));
                    return;
                }
                if (path == "/reference/consent-controls")
                {
                    await ctx.Response.WriteAsync(Read("consent-controls.html"));
                    return;
                }
                if (path == "/reference/results")
                {
                    await ctx.Response.WriteAsync(Read("results.html").Replace("{{QUERY}}",
                        "lite browser engine", StringComparison.Ordinal));
                    return;
                }
                if (path == "/") { await ctx.Response.WriteAsync(Read("home.html")); return; }
                if (path == "/search")
                {
                    if (!ctx.Request.Cookies.ContainsKey("CONSENT"))
                    {
                        ctx.Response.Redirect("/consent?continue=" + Uri.EscapeDataString(ctx.Request.Path + ctx.Request.QueryString));
                        return;
                    }
                    var query = WebUtility.HtmlEncode(ctx.Request.Query["q"].ToString());
                    await ctx.Response.WriteAsync(Read("results.html").Replace("{{QUERY}}", query, StringComparison.Ordinal));
                    return;
                }
                if (path == "/consent")
                {
                    var destination = WebUtility.HtmlEncode(ctx.Request.Query["continue"].ToString());
                    await ctx.Response.WriteAsync(Read("consent.html").Replace("{{CONTINUE}}", destination, StringComparison.Ordinal));
                    return;
                }
                if (path == "/save" && ctx.Request.Method == "POST")
                {
                    var form = await ctx.Request.ReadFormAsync();
                    var choice = form["choice"].ToString();
                    if (choice is not ("reject" or "accept")) { ctx.Response.StatusCode = 400; return; }
                    lock (Choices) Choices.Add(choice);
                    ctx.Response.Cookies.Append("CONSENT", choice, new CookieOptions
                    {
                        HttpOnly = true, Path = "/", SameSite = SameSiteMode.Lax,
                    });
                    ctx.Response.StatusCode = StatusCodes.Status303SeeOther;
                    ctx.Response.Headers.Location = form["continue"].ToString();
                    return;
                }
                if (path == "/result")
                { await ctx.Response.WriteAsync("<!doctype html><title>Destination</title><p id='destination'>opened</p>"); return; }
                ctx.Response.StatusCode = 404;
            });
            _app.Start();
        }

        private string Read(string name) => File.ReadAllText(Path.Combine(_fixtures, name));
        public void Dispose() => _app.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private static LayoutNode Find(LayoutNode root, string id)
    {
        if (root.Id == id) return root;
        foreach (var child in root.Children)
            if (FindOrNull(child, id) is { } found) return found;
        throw new Exception($"Missing #{id}");
    }

    private static LayoutNode? FindOrNull(LayoutNode root, string id)
    {
        if (root.Id == id) return root;
        foreach (var child in root.Children)
            if (FindOrNull(child, id) is { } found) return found;
        return null;
    }

    private static LayoutNode FindTag(LayoutNode root, string tag)
    {
        if (root.TagName == tag) return root;
        foreach (var child in root.Children)
            if (FindTagOrNull(child, tag) is { } found) return found;
        throw new Exception($"Missing <{tag}>");
    }

    private static LayoutNode? FindTagOrNull(LayoutNode root, string tag)
    {
        if (root.TagName == tag) return root;
        foreach (var child in root.Children)
            if (FindTagOrNull(child, tag) is { } found) return found;
        return null;
    }

    private static LayoutNode? FindWhere(LayoutNode root, Func<LayoutNode, bool> condition)
    {
        if (condition(root)) return root;
        foreach (var child in root.Children)
            if (FindWhere(child, condition) is { } found) return found;
        return null;
    }

    internal static int CaptureReferences(string outputDirectory)
    {
        var edge = @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe";
        if (!File.Exists(edge)) { Console.WriteLine("Microsoft Edge is unavailable"); return 2; }
        Directory.CreateDirectory(outputDirectory);
        using var server = new ReplayServer();
        foreach (var (width, height) in new[] { (1280, 800), (800, 600) })
        foreach (var (name, route) in new[]
        {
            ("home", "/"), ("consent", "/reference/consent"),
            ("consent-controls", "/reference/consent-controls"), ("results", "/reference/results"),
        })
        {
            var label = $"{name}-{width}x{height}";
            var referencePath = Path.GetFullPath(Path.Combine(outputDirectory, label + "-edge.png"));
            var litePath = Path.GetFullPath(Path.Combine(outputDirectory, label + "-lite.png"));
            var profile = Path.Combine(Path.GetTempPath(), "lite-google-reference-" + Guid.NewGuid().ToString("N"));
            var start = new ProcessStartInfo(edge) { UseShellExecute = false, CreateNoWindow = true };
            foreach (var arg in new[] { "--headless=new", "--disable-gpu", "--hide-scrollbars", "--no-first-run",
                "--user-data-dir=" + profile, $"--window-size={width},{height}", "--screenshot=" + referencePath,
                server.BaseUrl + route }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start) ?? throw new Exception("Could not launch Edge");
            if (!process.WaitForExit(20_000) || !File.Exists(referencePath))
            { Console.WriteLine($"reference capture failed: {label}"); return 1; }
            using var session = new BrowserSession();
            var page = Parser.TraversePage(new NavigationRequest(server.BaseUrl + route), width, height, session);
            using var bitmap = Drawer.DrawToBitmap(width, height, page.Root,
                new Lite.Layout.Viewport { ViewportHeight = height });
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(litePath, data.ToArray());
            Console.WriteLine($"captured {label}");
        }
        return 0;
    }

    /// <summary>Explicit live check; the deterministic suite never depends on Google's availability.</summary>
    internal static int LiveSmoke()
    {
        using var session = new BrowserSession();
        const int width = 1280, height = 800;
        var output = Path.Combine(AppContext.BaseDirectory, "artifacts", "google-live");
        Directory.CreateDirectory(output);
        try
        {
            var home = Parser.TraversePage(new NavigationRequest("https://www.google.com/"), width, height, session);
            Save(home, "home.png");
            Console.WriteLine($"home: {Describe(home)}");
            var form = FindWhere(home.Root, n => n.TagName == "FORM" &&
                n.Attributes.GetValueOrDefault("action", "").Contains("search", StringComparison.OrdinalIgnoreCase))
                ?? throw new Exception("Search form missing");
            var query = FindWhere(form, n => n.TagName == "INPUT" && n.Attributes.GetValueOrDefault("name") == "q")
                ?? throw new Exception("Query input missing");
            Console.WriteLine($"home search field: {query.Box.BorderBox}");
            FormState.TextInputValues[query.NodeKey] = "lite browser engine";
            var request = FormSubmitter.PrepareNavigation(form, home.Engine, true)
                ?? throw new Exception("Search form did not submit");
            var next = Parser.TraversePage(request, width, height, session);
            Console.WriteLine($"next: {Describe(next)}");
            if (next.Root.DocumentState?.Address.Contains("consent.google.com", StringComparison.OrdinalIgnoreCase) == true)
            {
                Save(next, "consent.png");
                var reject = FindWhere(next.Root, n => n.TagName == "INPUT" &&
                    n.Attributes.GetValueOrDefault("type") == "submit" &&
                    n.Attributes.GetValueOrDefault("value", "").Contains("Rejeitar", StringComparison.OrdinalIgnoreCase))
                    ?? throw new Exception("Consent reject choice missing");
                var consentForm = reject.Parent ?? throw new Exception("Consent form missing");
                var save = FormSubmitter.PrepareNavigation(consentForm, next.Engine, true, reject)
                    ?? throw new Exception("Consent form did not submit");
                next = Parser.TraversePage(save, width, height, session);
                Console.WriteLine($"results: {Describe(next)}");
            }
            Save(next, "results.png");
            var heading = FindWhere(next.Root, n => n.TagName == "H3");
            Console.WriteLine($"results DOM: forms={next.Document?.QuerySelectorAll("form").Length}, links={next.Document?.QuerySelectorAll("a").Length}, scripts={next.Document?.QuerySelectorAll("script").Length}");
            var (_, regions) = Drawer.Draw(width, height, next.Root,
                new Lite.Layout.Viewport { ViewportHeight = height });
            Console.WriteLine($"result heading: {(heading is null ? "missing" : heading.DisplayText)}; clickable links: {regions.Count(r => r.Href is not null)}");
            if (heading is null && next.Document?.DocumentElement?.OuterHtml.Contains("/httpservice/retry/", StringComparison.Ordinal) == true)
                Console.WriteLine("Google returned a search challenge rather than result markup.");
            foreach (var diagnostic in session.Diagnostics.Take(25)) Console.WriteLine("diagnostic: " + diagnostic);
            Console.WriteLine($"diagnostic count: {session.Diagnostics.Count}; images: {output}");
            return heading is not null && regions.Any(r => r.Href is not null) ? 0 : 1;
        }
        catch (Exception error)
        {
            Console.WriteLine("live smoke failed: " + error);
            foreach (var diagnostic in session.Diagnostics.Take(25)) Console.WriteLine("diagnostic: " + diagnostic);
            return 1;
        }

        void Save(Page page, string filename)
        {
            using var bitmap = Drawer.DrawToBitmap(width, height, page.Root,
                new Lite.Layout.Viewport { ViewportHeight = height });
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(Path.Combine(output, filename), data.ToArray());
        }

        static string Describe(Page page)
        {
            var address = page.Root.DocumentState?.Address ?? "";
            return Uri.TryCreate(address, UriKind.Absolute, out var uri)
                ? $"{uri.GetLeftPart(UriPartial.Path)} title={page.Document?.Title}"
                : $"{address} title={page.Document?.Title}";
        }
    }

    [Test]
    public static void SearchConsentAndResult_WorkAtDesktopViewports()
    {
        using var server = new ReplayServer();
        foreach (var (width, height) in new[] { (1280, 800), (800, 600) })
        foreach (var choice in new[] { "reject", "accept" })
        {
            using var session = new BrowserSession();
            var home = Parser.TraversePage(new NavigationRequest(server.BaseUrl + "/"), width, height, session);
            var logo = Find(home.Root, "logo");
            var query = Find(home.Root, "q");
            var button = Find(home.Root, "search-button");
            Equal("loaded", FindTag(home.Root, "BODY").Attributes.GetValueOrDefault("data-home-script"));
            True(logo.Image is not null, "logo asset should load through the browser session");
            var (_, homeRegions) = Drawer.Draw(width, height, home.Root, new Lite.Layout.Viewport { ViewportHeight = height });
            True(query.Box.BorderBox.Width > 500,
                $"search field is too narrow: input={query.Box.BorderBox.Width}, form={Find(home.Root, "search").Box.BorderBox.Width}, viewport={width}");
            True(query.Box.BorderBox.Right <= width && query.Box.BorderBox.Left >= 0, "search field is clipped");
            True(logo.Box.BorderBox.Right <= width && logo.Box.BorderBox.Left >= 0, "logo is clipped");
            True(homeRegions.Any(region => region.NodeKey == button.NodeKey), "search button is not clickable");

            FormState.TextInputValues[query.NodeKey] = "lite browser engine";
            var search = FormSubmitter.PrepareNavigation(Find(home.Root, "search"), home.Engine,
                fireSubmitEvent: true, submitter: choice == "accept" ? button : null)!.Value;
            Contains("q=lite+browser+engine", search.Url);
            if (choice == "accept") Contains("btnK=Pesquisa+Google", search.Url);
            var consent = Parser.TraversePage(search, width, height, session);
            Contains("/consent?", consent.Root.DocumentState?.Address);
            var selectedForm = Find(consent.Root, choice);
            var rejectButton = Find(consent.Root, "reject").Children.First(child => child.TagName == "INPUT" &&
                child.Attributes.GetValueOrDefault("type") == "submit");
            var acceptButton = Find(consent.Root, "accept").Children.First(child => child.TagName == "INPUT" &&
                child.Attributes.GetValueOrDefault("type") == "submit");
            var submit = selectedForm.Children.First(child => child.TagName == "INPUT" &&
                child.Attributes.GetValueOrDefault("type") == "submit");
            Drawer.Draw(width, height, consent.Root, new Lite.Layout.Viewport { ViewportHeight = height });
            True(acceptButton.Box.BorderBox.Left > rejectButton.Box.BorderBox.Right,
                $"consent buttons overlap: reject={rejectButton.Box.BorderBox}, accept={acceptButton.Box.BorderBox}, rejectForm={Find(consent.Root, "reject").Box.BorderBox}, acceptForm={Find(consent.Root, "accept").Box.BorderBox}");
            True(submit.Box.BorderBox.Right <= width && submit.Box.BorderBox.Bottom <= height,
                "consent choice is clipped");
            var save = FormSubmitter.PrepareNavigation(selectedForm, consent.Engine, true, submit)!.Value;
            Equal("POST", save.Method);
            Contains("choice=" + choice, save.Body);
            var results = Parser.TraversePage(save, width, height, session);
            Contains("/search?", results.Root.DocumentState?.Address);
            Equal("Google Search", results.Document?.Title);
            Equal("loaded", FindTag(results.Root, "BODY").Attributes.GetValueOrDefault("data-results-script"));
            True(string.IsNullOrEmpty(session.GetDocumentCookie(server.BaseUrl + "/search")),
                "HTTP-only consent cookie must not be visible to script");
            var result = Find(results.Root, "first-result");
            var (_, regions) = Drawer.Draw(width, height, results.Root,
                new Lite.Layout.Viewport { ViewportHeight = height });
            True(regions.Any(region => region.NodeKey == result.NodeKey && region.Href != null),
                $"result link is not clickable: href={result.Href}, matching={regions.Count(region => region.NodeKey == result.NodeKey)}, links={regions.Count(region => region.Href != null)}, regions={regions.Count}, attached={FindOrNull(results.Root, "first-result") is not null}, box={result.Box.BorderBox}, heading={FindTag(result, "H3").Box.BorderBox}");
            True(FindTag(result, "H3").Box.BorderBox.Right <= width, "result link is clipped");
            var destination = Parser.TraversePage(new NavigationRequest(server.BaseUrl + result.Href),
                width, height, session);
            Equal("Destination", destination.Document?.Title);

            using var isolated = new BrowserSession();
            var another = Parser.TraversePage(search, width, height, isolated);
            Contains("/consent?", another.Root.DocumentState?.Address);
            True(session.Diagnostics.IsEmpty, string.Join("; ", session.Diagnostics));
        }
        Equal(4, server.Choices.Count);
    }

    [Test]
    public static void ConsentInlineFlexButtons_AreFullWidthAndClickTheirOwnForms()
    {
        using var server = new ReplayServer();
        foreach (var (width, height) in new[] { (1280, 800), (800, 600) })
        {
            using var session = new BrowserSession();
            var page = Parser.TraversePage(new NavigationRequest(server.BaseUrl + "/reference/consent-controls"),
                width, height, session);
            var viewport = new Lite.Layout.Viewport { ViewportHeight = height };
            Drawer.Draw(width, height, page.Root, viewport);
            var firstButton = Find(page.Root, "reject").Children.First(n => n.TagName == "INPUT" &&
                n.Attributes.GetValueOrDefault("type") == "submit");
            True(firstButton.Box.BorderBox.Top >= Find(page.Root, "content").Box.BorderBox.Bottom - 1f,
                "consent buttons overlap wrapped explanatory text");
            viewport.ScrollTo(Math.Max(0, firstButton.Box.BorderBox.MidY - height / 2f));
            var (_, hits) = Drawer.Draw(width, height, page.Root, viewport);
            foreach (var choice in new[] { "reject", "accept" })
            {
                var form = Find(page.Root, choice);
                var button = form.Children.First(n => n.TagName == "INPUT" &&
                    n.Attributes.GetValueOrDefault("type") == "submit");
                var box = button.Box.BorderBox;
                True(box.Width >= 170, $"{choice} label is clipped: {box}");
                var x = box.MidX;
                var y = box.MidY;
                True(y - viewport.ScrollY >= 0 && y - viewport.ScrollY < height,
                    $"{choice} button is outside the scrolled viewport");
                var topHit = hits.LastOrDefault(h => h.Bounds.Contains(x, y));
                True(topHit?.NodeKey == button.NodeKey && topHit.InputAction == InputAction.Button,
                    $"{choice} button is covered by another hit region");
                var paddedEdgeHit = hits.LastOrDefault(h => h.Bounds.Contains(box.Left + 2, y));
                True(paddedEdgeHit?.NodeKey == button.NodeKey && paddedEdgeHit.InputAction == InputAction.Button,
                    $"{choice} button padding is not clickable");
                var request = FormSubmitter.PrepareNavigation(form, page.Engine, true, button)!.Value;
                Equal("POST", request.Method);
                Contains("choice=" + choice, request.Body);
                var destination = Parser.TraversePage(request, width, height, session);
                Equal("Destination", destination.Document?.Title);
            }
        }
    }
}
