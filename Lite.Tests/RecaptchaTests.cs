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
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions());
        builder.WebHost.UseKestrelCore().UseUrls("http://127.0.0.1:0");
        builder.Services.AddRoutingCore();
        var app = builder.Build();
        app.Run(async ctx =>
        {
            ctx.Response.ContentType = "text/html; charset=utf-8";
            await ctx.Response.WriteAsync($"""
            <!doctype html><html><head><title>reCAPTCHA test</title></head><body>
            <div class="g-recaptcha" data-sitekey="{TestKey}"></div>
            <script src="https://www.google.com/recaptcha/api.js" async defer></script>
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
            engine.ScriptFailed += error => runtimeErrors.Add(error.ToString());
            engine.RawEngine.SetValue("__recordRecaptchaRejection", new Action<string>(error => runtimeErrors.Add(error)));
            engine.Execute("""
                addEventListener('unhandledrejection', e => __recordRecaptchaRejection(
                    String(e.reason) + '\n' + String(e.reason && e.reason.stack || '')));
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
            File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(new
            {
                scope = "v2 test-key widget bootstrap; no verification or challenge coverage",
                passed, checkbox, runtimeErrors, diagnostics = session.Diagnostics.ToArray(),
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
