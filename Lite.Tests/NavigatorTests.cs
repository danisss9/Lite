using Lite;
using Lite.Scripting;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>Regression coverage for the navigator-and-capabilities package: the PDF viewer
/// capability surface (pdfViewerEnabled, plugins and mimeTypes with legacy indexed + named
/// access and enabledPlugin back-references), appCodeName, registerProtocolHandler's
/// validation gates (safe scheme or web+ prefix, same-origin URL, %s placeholder →
/// SyntaxError otherwise), and per-window identity.</summary>
public static class NavigatorTests
{
    private static JsEngine Parsed() =>
        Parser.ParseChildPage("<!doctype html><body></body></html>", isSrcdoc: true, "http://test/", 800, 600).Engine!;

    private static void Run(JsEngine engine, string js) => engine.RawEngine.Execute(js);

    private static string Eval(JsEngine engine, string expr) => engine.RawEngine.Evaluate(expr).ToString() ?? "";

    [Test]
    public static void Navigator_PdfViewerSurface()
    {
        var engine = Parsed();
        Run(engine, """
            var out = [];
            out.push('pdfViewerEnabled' in navigator);
            out.push(typeof navigator.pdfViewerEnabled);
            out.push(navigator.mimeTypes.length);
            out.push(navigator.mimeTypes.item(0).type);
            out.push(navigator.mimeTypes.item(0).description);
            out.push(navigator.mimeTypes.item(0).suffixes);
            out.push(navigator.mimeTypes.item(0) === navigator.mimeTypes.namedItem('application/pdf'));
            out.push(navigator.plugins.length >= 1);
            out.push(navigator.plugins.item(0).name);
            out.push(navigator.mimeTypes.item(0).enabledPlugin === navigator.plugins.item(0));
            globalThis.out = out.join('|');
            """);
        Equal("true|boolean|2|application/pdf|Portable Document Format|pdf|true|true|PDF Viewer|true",
            Eval(engine, "out"));
    }

    [Test]
    public static void Navigator_LegacyIdentitiesAndAppCodeName()
    {
        var engine = Parsed();
        Run(engine, """
            var out = [];
            out.push(navigator.appCodeName);
            out.push(navigator.appName);
            out.push(navigator.product);
            out.push(typeof navigator.userAgent);
            out.push(typeof navigator.platform);
            out.push(Array.isArray(navigator.languages));
            out.push(navigator.cookieEnabled);
            out.push(navigator.javaEnabled());
            globalThis.out = out.join('|');
            """);
        Equal("Mozilla|Netscape|Gecko|string|string|true|true|false", Eval(engine, "out"));
    }

    [Test]
    public static void Navigator_RegisterProtocolHandlerValidation()
    {
        var engine = Parsed();
        Run(engine, """
            var out = [];
            try { navigator.registerProtocolHandler('tel', '/handler?u=%s'); out.push('ok'); } catch (e) { out.push(e.name); }
            try { navigator.registerProtocolHandler('web+mything', '/handler?u=%s'); out.push('ok'); } catch (e) { out.push(e.name); }
            try { navigator.registerProtocolHandler('javascript', '/handler?u=%s'); out.push('ok'); } catch (e) { out.push(e.name); }
            try { navigator.registerProtocolHandler('tel', 'http://other.test/h?u=%s'); out.push('ok'); } catch (e) { out.push(e.name); }
            try { navigator.registerProtocolHandler('tel', '/handler?u=x'); out.push('ok'); } catch (e) { out.push(e.name); }
            globalThis.out = out.join('|');
            """);
        // Safe scheme and web+ prefixed schemes register; unsafe schemes, cross-origin URLs,
        // and URLs without the %s placeholder raise SyntaxError.
        Equal("ok|ok|SyntaxError|SyntaxError|SyntaxError", Eval(engine, "out"));
    }

    [Test]
    public static void Navigator_PerWindowIdentity()
    {
        // The navigator object is per-window: each engine binds its own instance (identity
        // checked C#-side; script values cannot cross page runtimes).
        var first = Parsed();
        var second = Parsed();
        True(!ReferenceEquals(first.Navigator, second.Navigator), "navigator must be per-window");
        True(first.Navigator.cookieEnabled, "cookieEnabled reads true on a clean profile");
    }
}
