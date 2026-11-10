using Lite;
using Lite.Scripting;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>Regression coverage for the obsolete document-color surface (HTML §15.3.2):
/// document.fgColor/bgColor/linkColor/vlinkColor/alinkColor read and write the body's
/// presentational content attributes, the body IDL (bgColor/link/vLink/aLink) mirrors the same
/// attributes, and the setters route through the shared attribute-changed path so the
/// cascade sees the values.</summary>
public static class DocumentColorTests
{
    private static JsEngine Parsed(string html) =>
        Parser.ParseChildPage(html, isSrcdoc: true, "http://test/", 800, 600).Engine!;

    private static void Run(JsEngine engine, string js) => engine.RawEngine.Execute(js);

    private static string Eval(JsEngine engine, string expr) => engine.RawEngine.Evaluate(expr).ToString() ?? "";

    [Test]
    public static void DocumentColor_SettersMirrorTheBodyAttributes()
    {
        var engine = Parsed("<!doctype html><body>content</body></html>");
        Run(engine, """
            var out = [];
            document.bgColor = 'green';
            out.push(document.bgColor + '/' + document.body.getAttribute('bgcolor'));
            document.linkColor = 'red';
            out.push(document.linkColor + '/' + document.body.link + '/' + document.body.getAttribute('link'));
            document.vlinkColor = 'yellow';
            out.push(document.vlinkColor + '/' + document.body.vLink);
            document.alinkColor = 'silver';
            out.push(document.alinkColor + '/' + document.body.aLink);
            document.fgColor = 'blue';
            out.push(document.fgColor + '/' + document.body.getAttribute('text'));
            globalThis.out = out.join('|');
            """);
        Equal("green/green|red/red/red|yellow/yellow|silver/silver|blue/blue",
            Eval(engine, "out"));
    }

    [Test]
    public static void DocumentColor_BodyIdlMirrorBack()
    {
        var engine = Parsed("<!doctype html><body>content</body></html>");
        Run(engine, """
            var out = [];
            document.body.bgColor = 'green';
            out.push(document.bgColor + '/' + document.body.bgColor);
            document.body.link = 'red';
            out.push(document.linkColor + '/' + document.body.link);
            document.body.aLink = 'silver';
            out.push(document.alinkColor + '/' + document.body.aLink);
            globalThis.out = out.join('|');
            """);
        // The body IDL and the document IDL share one attribute store.
        Equal("green/green|red/red|silver/silver", Eval(engine, "out"));
    }

    [Test]
    public static void DocumentColor_EmptyBodyReadsEmpty()
    {
        var engine = Parsed("<!doctype html><body></body></html>");
        Run(engine, """
            var out = [];
            out.push(document.fgColor);
            out.push(document.bgColor);
            out.push(document.linkColor);
            document.body.remove();
            out.push(document.fgColor);
            globalThis.out = out.join('|');
            """);
        // With no body element the getters read the empty default (document-color-01).
        True(Eval(engine, "out").Split('|').All(v => v.Length == 0), "all reads must be empty");
    }
}
