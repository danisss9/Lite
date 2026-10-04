using System.Diagnostics;
using System.Text.Json;
using Lite.Models;
using Lite.Network;
using Lite.Scripting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

public static class RecaptchaTests
{
    // Google's public v2 test key; never a production credential.
    private const string TestKey = "6LeIxAcTAAAAAJcZVRqyHh71UMIEGNQ_MXjiZKhI";

    [Test]
    public static void TaskFailure_IsReportedAndNextTaskRuns()
    {
        using var session = new BrowserSession();
        var page = Parser.ParseChildPage("<!doctype html><body></body>", true,
            "https://embed.test/", 400, 200, session);
        var engine = page.Engine;
        engine.Execute("globalThis.errors = []; addEventListener('error', e => errors.push(e.message));");
        engine.EnqueueMacrotask(() => engine.RawEngine.Execute("throw new TypeError('embed failed');"));
        var continued = false;
        engine.EnqueueMacrotask(() => continued = true);
        engine.DrainTasks();
        True(continued, "An embed failure must not discard subsequent tasks.");
        Contains("TypeError: embed failed", engine.RawEngine.Evaluate("errors.join(',')").ToString());
        True(session.Diagnostics.Any(d => d.Contains("javascript task") && d.Contains("embed failed")));
    }

    [Test]
    public static void DeepDynamicFunction_CompletesInQuickJs()
    {
        // Reduced reproduction of the deep declaration-walker case from Google's widget.
        using var session = new BrowserSession();
        var page = Parser.ParseChildPage("<!doctype html><body></body>", true,
            "https://embed.test/", 400, 200, session);
        var engine = page.Engine;
        engine.RawEngine.SetValue("deepSource", "return " + string.Concat(Enumerable.Repeat("false ? 0 : ", 300)) + "42;");
        engine.Execute("""
            globalThis.deepResult = null;
            globalThis.runtimeErrors = [];
            addEventListener('error', e => runtimeErrors.push(e.message));
            Promise.resolve().then(() => { deepResult = new Function(deepSource)(); });
            """);
        engine.DrainTasks();
        Equal("42", engine.RawEngine.GetValue("deepResult").ToString());
        Equal("", engine.RawEngine.Evaluate("runtimeErrors.join(',')").ToString());
    }

    internal static int LiveSmoke()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions());
        builder.WebHost.UseKestrelCore().UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        var proxy = new HttpClient();
        app.MapGet("/recaptcha/api.js", async () =>
        {
            var js = await proxy.GetStringAsync("https://www.google.com/recaptcha/api.js");
            Console.WriteLine($"[proxy api.js] len={js.Length} starts={js[..Math.Min(60, js.Length)]}");
            js = js.Replace("https://www.gstatic.com/recaptcha/releases/", "/recaptcha/releases/")
                   .Replace("recaptcha__pt_pt.js", "widget.js");
            return Results.Text(js, "application/javascript");
        });
        app.MapGet("/recaptcha/releases/{release}/{file}", async (string release, string file) =>
        {
            var js = await proxy.GetStringAsync(
                $"https://www.gstatic.com/recaptcha/releases/{release}/{file}");
            Console.WriteLine($"[proxy widget] {file} len={js.Length}");
            // Guard every `function(J){return J.parse(x)}` callback so a promise that
            // resolves without a parser reports the payload instead of dying silently.
            js = js.Replace("function(J){return J.parse(", "function(J){return __pchk(J, n).parse(");
            js = "function __pchk(J,n){if(J===undefined||J===null){try{__recordBoundary('PARSE-UNDEF payload='+String(n).slice(0,120)+' stack='+new Error().stack.split('\\n').slice(1,4).join(' | '));}catch(e){}}return J;}\n" + js;
            return Results.Text(js, "application/javascript");
        });
        app.MapFallback(async ctx =>
        {
            ctx.Response.ContentType = "text/html; charset=utf-8";
            await ctx.Response.WriteAsync($"""
            <!doctype html><html><head><title>reCAPTCHA test</title></head><body>
            <div class="g-recaptcha" data-sitekey="{TestKey}"></div>
            <script src="/recaptcha/api.js" async defer></script>
            </body></html>
            """);
        });
        app.StartAsync().GetAwaiter().GetResult();
        using var session = new BrowserSession();
        session.Client.Timeout = TimeSpan.FromSeconds(30);
        var output = Path.Combine(AppContext.BaseDirectory, "artifacts", "recaptcha-live");
        Directory.CreateDirectory(output);
        var runtimeErrors = new HashSet<string>(StringComparer.Ordinal);
        var engines = new List<JsEngine>();
        void Observe(JsEngine engine)
        {
            engines.Add(engine);
            engine.ScriptFailed += error => runtimeErrors.Add($"[{engine.CurrentUrl}] {error}");
            engine.RawEngine.SetValue("__recordRecaptchaRejection", new Action<string>(error => runtimeErrors.Add($"[{engine.CurrentUrl}] {error}")));
            engine.Execute("""
                addEventListener('unhandledrejection', e => __recordRecaptchaRejection(
                    String(e.reason) + '\n' + String(e.reason && e.reason.stack || '')));
                """);
            // Diagnostic probe: record every host boundary the widget crosses so a
            // missing/undefined host answer can be told apart from script logic bugs.
            engine.RawEngine.SetValue("__recordBoundary", new Action<string>(line =>
                engine.RecordBoundary(line)));
            engine.Execute("""
                (function() {
                  var log = function(kind, detail) { try { __recordBoundary(kind + ' ' + detail); } catch (e) {} };
                  try { log('ae', typeof addEventListener + ' ' + String(addEventListener).slice(0, 80)); } catch (e) { log('ae-err', String(e)); }
                  try { log('jw', typeof __jsWindow); } catch (e) { log('jw-err', String(e)); }
                  var of = globalThis.fetch;
                  if (of) globalThis.fetch = function(input) {
                    var url = String(input && input.url || input);
                    log('fetch', url);
                    return of.apply(this, arguments).then(function(r) {
                      log('fetch-done', r.status + ' ' + url); return r;
                    }, function(e) { log('fetch-err', url + ' ' + e); throw e; });
                  };
                  var oo = XMLHttpRequest.prototype.open, os = XMLHttpRequest.prototype.send;
                  XMLHttpRequest.prototype.open = function(m, u) {
                    this.__dbg = m + ' ' + u; return oo.apply(this, arguments);
                  };
                  XMLHttpRequest.prototype.send = function() {
                    var x = this; log('xhr', x.__dbg);
                    x.addEventListener('load', function() {
                      log('xhr-done', x.status + ' ' + x.__dbg + ' ' + String(x.responseText).slice(0, 120));
                    });
                    x.addEventListener('error', function() { log('xhr-err', x.__dbg); });
                    return os.apply(this, arguments);
                  };
                  addEventListener('message', function(e) {
                    log('msg-in', String(e.origin) + ' type=' + typeof e.data + ' ports=' + (e.ports ? e.ports.length : 'n/a') + ' data=' + String(typeof e.data === 'string' ? e.data : JSON.stringify(e.data)).slice(0, 160));
                  });
                  var op = globalThis.postMessage;
                  globalThis.postMessage = function(m) {
                    log('msg-out', String(typeof m === 'string' ? m : JSON.stringify(m)).slice(0, 160));
                    return op.apply(this, arguments);
                  };
                  var mc = globalThis.MessageChannel;
                  if (mc) {
                    var OM = mc;
                    globalThis.MessageChannel = function() {
                      log('channel', 'created');
                      return new OM();
                    };
                    globalThis.MessageChannel.prototype = OM.prototype;
                  }
                })();
                """);
        }
        JsEngine.OnCreated += Observe;
        try
        {
            var page = Parser.TraversePage(new NavigationRequest(app.Urls.Single()), 800, 600, session);
            var deadline = Stopwatch.StartNew();
            while (deadline.Elapsed < TimeSpan.FromSeconds(20))
            {
                page.Engine.DrainTree();
                Thread.Sleep(20);
            }
            var frameIndex = 0;
            Dump(page, ref frameIndex);
            var checkbox = engines.Any(e => e.DocumentFacade.getElementById("recaptcha-anchor") is not null);
            // This probe checks bootstrap only. Token verification and interactive challenges
            // are separate acceptance gates; a loaded API or iframe is not full support.
            var passed = checkbox && runtimeErrors.Count == 0 && session.Diagnostics.IsEmpty;
            var boundaryLogs = engines.Select((e, i) => (Engine: e, Index: i))
                .Where(x => x.Engine.BoundaryLog.Count > 0)
                .Select(x => new { frame = x.Index, url = x.Engine.CurrentUrl,
                    lines = x.Engine.BoundaryLog.ToArray() })
                .ToArray();
            foreach (var e in engines)
                foreach (var line in e.BoundaryLog)
                    Console.WriteLine($"[boundary] {line}");
            File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(new
            {
                scope = "v2 test-key widget bootstrap; no verification or challenge coverage",
                passed, checkbox, runtimeErrors, diagnostics = session.Diagnostics.ToArray(),
                boundaryLogs,
                quickJsBridge = typeof(Lite.QuickJs.QuickJsRuntime).Assembly.GetName().Version?.ToString(),
                capturedAt = DateTimeOffset.UtcNow,
            }, new JsonSerializerOptions { WriteIndented = true }));
            using var bitmap = Drawer.DrawToBitmap(800, 600, page.Root, new Lite.Layout.Viewport { ViewportHeight = 600 });
            using var image = SKImage.FromBitmap(bitmap);
            using var png = image.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(Path.Combine(output, "widget.png"), png.ToArray());
            foreach (var diagnostic in session.Diagnostics) Console.WriteLine(diagnostic);
            Console.WriteLine($"Widget bootstrap: {(passed ? "PASS" : "FAIL")}. Full verification: NOT TESTED. Artifacts: {output}");
            return passed ? 0 : 1;
        }
        catch (Exception error)
        {
            Console.WriteLine("reCAPTCHA live check failed: " + error.Message);
            File.WriteAllText(Path.Combine(output, "failure.txt"), error.ToString());
            return 1;
        }
        finally
        {
            JsEngine.OnCreated -= Observe;
            foreach (var engine in engines) engine.CancelModuleLoads();
            app.StopAsync().GetAwaiter().GetResult();
            app.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static void Dump(Page page, ref int frameIndex)
    {
        var depth = frameIndex++;
        Console.WriteLine($"frame {depth}: {page.Engine.CurrentUrl}");
        var output = Path.Combine(AppContext.BaseDirectory, "artifacts", "recaptcha-live");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, $"frame-{depth}.html"), Lite.Rendering.HtmlSerializer.SerializeOuter(page.Root));
        foreach (var node in Descendants(page.Root))
            if (node.ChildPage is { } child) Dump(child, ref frameIndex);
    }

    private static IEnumerable<LayoutNode> Descendants(LayoutNode node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var descendant in Descendants(child)) yield return descendant;
    }
}
