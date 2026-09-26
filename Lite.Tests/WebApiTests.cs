using System.Diagnostics;
using Lite;
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

/// <summary>Web-platform APIs added for real-site compatibility: base64 utilities,
/// navigator.sendBeacon, and script-initiated navigation in headless (pumped) loads.</summary>
public static class WebApiTests
{
    private static JsEngine NewEngine()
    {
        var sample = Parser.ParseFragment("<span></span>")[0];
        var root = new LayoutNode(null, "HTML", "", sample.Style);
        root.AddChild(new LayoutNode(null, "BODY", "", sample.Style));
        return JsEngine.Create(root);
    }

    private static object? Global(JsEngine e, string name) => e.RawEngine.GetValue(name).ToObject();

    [Test]
    public static void AtobBtoa_RoundTripWhitespaceAndErrors()
    {
        var e = NewEngine();
        e.Execute("globalThis.__round = atob(btoa('hello world'));");
        Equal("hello world", Global(e, "__round")?.ToString());
        // Decoded bytes are code points, not UTF-8: 0xFF decodes to U+00FF.
        e.Execute("globalThis.__bin = atob('/w=='); globalThis.__enc = btoa(atob('/w=='));");
        Equal("\u00FF", Global(e, "__bin")?.ToString());
        Equal("/w==", Global(e, "__enc")?.ToString());
        // ASCII whitespace in the input is ignored (HTML §6.2).
        e.Execute("globalThis.__ws = atob(' Q\\tQ\\n=\\r= ');");
        Equal("A", Global(e, "__ws")?.ToString());
        e.Execute("globalThis.__err = ''; try { atob('A'); } catch (err) { globalThis.__err = err.name; }");
        Equal("InvalidCharacterError", Global(e, "__err")?.ToString());
        e.Execute("globalThis.__err2 = ''; try { btoa('\\u0100'); } catch (err) { globalThis.__err2 = err.name; }");
        Equal("InvalidCharacterError", Global(e, "__err2")?.ToString());
        e.Execute("globalThis.__err3 = ''; try { atob('A$=='); } catch (err) { globalThis.__err3 = err.name; }");
        Equal("InvalidCharacterError", Global(e, "__err3")?.ToString());
    }

    [Test]
    public static void ScriptElements_AreDomReachableAndHeadExists()
    {
        const string html = """
            <!doctype html><html><head><title>t</title><script>globalThis.__inHead = document.currentScript !== null;</script></head>
            <body><div id="d"><script src="/x.js"></script></div>
            <script>globalThis.__inline = document.currentScript !== null;</script></body></html>
            """;
        var engine = Parser.ParseChildPage(html, true, "http://probe.test/", 800, 600).Engine!;
        bool EvalBool(string code) => Convert.ToBoolean(engine.RawEngine.Evaluate(code).ToObject());
        string EvalString(string code) => engine.RawEngine.Evaluate(code).ToObject()?.ToString() ?? "";
        True(EvalBool("document.head !== null && document.head.tagName === 'HEAD'"),
            "document.head must exist (HEAD is in the DOM even though it never renders)");
        Equal("3", EvalString("document.getElementsByTagName('script').length"));
        True(EvalBool("globalThis.__inHead === true && globalThis.__inline === true"),
            "document.currentScript is the executing script during classic script execution");
        Equal("object", EvalString("typeof document.currentScript")); // object === null outside scripts
        True(EvalBool("document.currentScript === null"),
            "currentScript is null outside script execution");
        // The loader pattern used by real embed scripts (recaptcha and friends):
        // getElementsByTagName('script')[0].parentNode.insertBefore(next, first)
        True(EvalBool("(function(){ var s = document.getElementsByTagName('script')[0];" +
            " var n = document.createElement('script'); n.src = '/later.js';" +
            " s.parentNode.insertBefore(n, s); return n.parentNode === s.parentNode && s.parentNode.childNodes.length >= 1; })()"),
            "insertBefore(next, firstScript) must work on the script's real parent");
    }

    [Test]
    public static void DynamicallyInsertedScript_Executes()
    {
        using var server = new MiniServer();
        using var session = new BrowserSession();
        var page = Parser.TraversePage(new NavigationRequest(server.BaseUrl + "/dynamic"), 800, 600, session);
        var engine = page.Engine;
        string loaded = "";
        var deadline = Stopwatch.GetTimestamp() + 5_000 * Stopwatch.Frequency / 1000;
        while (Stopwatch.GetTimestamp() < deadline)
        {
            engine.DrainTree();
            loaded = engine.RawEngine.Evaluate("globalThis.__loaded").ToObject()?.ToString() ?? "";
            // Dynamic external scripts are async (they run when the fetch completes), so the
            // inline script is allowed to execute first; both must have run.
            if (loaded.Contains("external") && loaded.Contains("inline")) break;
            Thread.Sleep(10);
        }
        True(loaded.Contains("external") && loaded.Contains("inline"),
            $"createElement('script') + appendChild must fetch src and run inline text, got '{loaded}'");
    }

    private sealed class MiniServer : IDisposable
    {
        private readonly WebApplication _app;
        internal readonly List<(string Body, string? ContentType)> Beacons = [];
        internal string BaseUrl => _app.Urls.Single();

        internal MiniServer()
        {
            var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions());
            builder.WebHost.UseKestrelCore();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddRoutingCore();
            _app = builder.Build();
            _app.Run(async ctx =>
            {
                var path = ctx.Request.Path.Value ?? "/";
                switch (path)
                {
                    case "/":
                        ctx.Response.ContentType = "text/html; charset=utf-8";
                        await ctx.Response.WriteAsync("""
                            <!doctype html><title>Start</title><body>
                            <script>navigator.sendBeacon('/beacon', 'lite-beacon');</script>
                            <script>setTimeout(function () { location.replace('/next'); }, 5);</script>
                            </body>
                            """);
                        return;
                    case "/dynamic":
                        ctx.Response.ContentType = "text/html; charset=utf-8";
                        await ctx.Response.WriteAsync("""
                            <!doctype html><title>Dynamic</title><body>
                            <script>
                            globalThis.__loaded = '';
                            var s = document.createElement('script');
                            s.src = '/later.js';
                            document.body.appendChild(s);
                            var t = document.createElement('script');
                            t.text = "globalThis.__loaded += 'inline';";
                            document.body.appendChild(t);
                            </script>
                            </body>
                            """);
                        return;
                    case "/later.js":
                        ctx.Response.ContentType = "text/javascript";
                        await ctx.Response.WriteAsync("globalThis.__loaded += 'external:';");
                        return;
                    case "/next":
                        ctx.Response.ContentType = "text/html; charset=utf-8";
                        await ctx.Response.WriteAsync("<!doctype html><title>Arrived</title><h3 id='arrived'>next page</h3>");
                        return;
                    case "/beacon":
                        var body = await new StreamReader(ctx.Request.Body).ReadToEndAsync();
                        lock (Beacons) Beacons.Add((body, ctx.Request.ContentType));
                        ctx.Response.StatusCode = 204;
                        return;
                    default:
                        ctx.Response.StatusCode = 404;
                        return;
                }
            });
            _app.Start();
        }

        public void Dispose() => _app.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    [Test]
    public static void SendBeacon_PostsPayloadAndLocationReplaceIsFollowable()
    {
        using var server = new MiniServer();
        using var session = new BrowserSession();
        const int width = 800, height = 600;
        // A headless host pumps the event loop and follows script-requested navigations.
        var page = Parser.TraversePage(new NavigationRequest(server.BaseUrl + "/"), width, height, session);
        NavigationRequest? pending = null;
        page.Engine.OnNavigate = r => pending = r;
        var deadline = Stopwatch.GetTimestamp() + 5_000 * Stopwatch.Frequency / 1000;
        while (Stopwatch.GetTimestamp() < deadline && pending is null)
        {
            page.Engine.DrainTree();
            Thread.Sleep(10);
        }
        True(pending is not null, "location.replace from a timer should request a navigation");
        Equal(server.BaseUrl + "/next", pending!.Value.Url);

        bool beaconSeen = false;
        var beaconDeadline = Stopwatch.GetTimestamp() + 5_000 * Stopwatch.Frequency / 1000;
        while (Stopwatch.GetTimestamp() < beaconDeadline)
        {
            lock (server.Beacons) beaconSeen = server.Beacons.Count > 0;
            if (beaconSeen) break;
            Thread.Sleep(10);
        }
        True(beaconSeen, "navigator.sendBeacon should deliver a POST");
        lock (server.Beacons)
        {
            var (body, contentType) = server.Beacons.Single();
            Equal("lite-beacon", body);
            True(contentType?.StartsWith("text/plain", StringComparison.Ordinal) == true,
                $"string payloads are text/plain, got {contentType}");
        }

        var next = Parser.TraversePage(pending.Value, width, height, session);
        Equal("Arrived", next.Document?.Title);
        True(next.Document?.GetElementById("arrived") is not null, "replaced document should contain the next page");
    }
}
