using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Lite.Conformance.Harness;

/// <summary>
/// Kestrel server for vendored test files (adapted from Example\StaticFileServer).
/// Serves <c>overrides\</c> at higher priority than <c>vendor\wpt\</c>, so the custom
/// testharnessreport.js shadows the upstream one. Also generates the standard wrapper
/// page for WPT <c>.any.js</c> tests on the fly.
/// </summary>
internal static class ConformanceServer
{
    public const int Port = 4455;
    public static string BaseUrl => $"http://localhost:{Port}";

    private static WebApplication? _app;

    public static void Start()
    {
        if (_app is not null) return;

        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions());
        builder.WebHost.UseKestrelCore();
        builder.WebHost.UseUrls(BaseUrl);
        builder.Services.AddRoutingCore();

        var app = builder.Build();

        var contentTypes = new FileExtensionContentTypeProvider();
        contentTypes.Mappings[".any.js"] = "text/javascript";
        // WPT CSS reftests are frequently XHTML (.xht/.xhtml); serve as HTML so the parser
        // builds a document rather than treating the markup as plain text.
        contentTypes.Mappings[".xht"] = "text/html";
        contentTypes.Mappings[".xhtml"] = "text/html";

        // WPT .any.js tests are addressed as <name>.any.html — generate the standard wrapper.
        app.Use(async (ctx, next) =>
        {
            var path = ctx.Request.Path.Value ?? "";
            if (path.EndsWith(".any.html", StringComparison.OrdinalIgnoreCase))
            {
                var jsPath = path[..^".html".Length] + ".js";
                var file = ResolveFile(jsPath);
                if (file is not null)
                {
                    ctx.Response.ContentType = "text/html";
                    await ctx.Response.WriteAsync($$"""
                        <!doctype html>
                        <meta charset="utf-8">
                        <script>
                        self.GLOBAL = {
                          isWindow: function() { return true; },
                          isWorker: function() { return false; },
                          isShadowRealm: function() { return false; }
                        };
                        </script>
                        <script src="/resources/testharness.js"></script>
                        <script src="/resources/testharnessreport.js"></script>
                        <div id="log"></div>
                        <script src="{{jsPath}}"></script>
                        """);
                    return;
                }
            }
            await next();
        });

        // overrides\ first (custom testharnessreport.js, local smoke tests), then vendor\wpt\.
        foreach (var root in new[] { ConformancePaths.Overrides, Path.Combine(ConformancePaths.Vendor, "wpt"), ConformancePaths.Vendor })
        {
            if (!Directory.Exists(root)) continue;
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(root),
                ContentTypeProvider = contentTypes,
                ServeUnknownFileTypes = true,
                DefaultContentType = "text/plain",
            });
        }

        app.Start();
        _app = app;
    }

    /// <summary>Maps a URL path to a file under overrides\ or vendor\, or null.</summary>
    public static string? ResolveFile(string urlPath)
    {
        var rel = urlPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        foreach (var root in new[] { ConformancePaths.Overrides, Path.Combine(ConformancePaths.Vendor, "wpt"), ConformancePaths.Vendor })
        {
            var candidate = Path.Combine(root, rel);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    public static void Stop()
    {
        _app?.StopAsync().GetAwaiter().GetResult();
        _app = null;
    }
}
