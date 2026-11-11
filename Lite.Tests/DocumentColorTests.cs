using Lite;
using Lite;
using Lite.Layout;
using Lite.Models;
using Lite.Extensions;
using Lite.Scripting;
using SkiaSharp;
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
    public static void DocumentColor_LinkHintPaintsAnchors()
    {
        var page = Parser.ParseChildPage(
            "<!doctype html><body link='red'><a href='x.html'>link</a></body></html>",
            isSrcdoc: true, "http://test/", 800, 600);
        BoxEngine.Layout(page.Root, 800, 600);
        // The body's link hint colors the anchor (the UA blue default applies without it).
        var anchor = Find(page.Root, "A");
        True(anchor != null, "anchor missing");
        var color = anchor!.StyleOverrides.GetValueOrDefault("color");
        True(color == "red", $"the body's link hint must color the anchor red, got {color}");
        True(Lite.Extensions.StyleExtensions.GetColor(anchor) == SKColors.Red,
            $"the hint must reach the paint path, got {Lite.Extensions.StyleExtensions.GetColor(anchor)}");
        // Without the hint the UA blue default applies.
        var page2 = Parser.ParseChildPage(
            "<!doctype html><body><a href='x.html'>link</a></body></html>",
            isSrcdoc: true, "http://test/", 800, 600);
        BoxEngine.Layout(page2.Root, 800, 600);
        var anchor2 = Find(page2.Root, "A");
        True(anchor2 != null, "anchor missing");
        True(Lite.Extensions.StyleExtensions.GetColor(anchor2!) == SKColors.Blue,
            $"the UA default anchor color must be blue, got {Lite.Extensions.StyleExtensions.GetColor(anchor2!)}");
    }

    [Test]
    public static void DocumentColor_BodyTextIdlReflectsTheAttribute()
    {
        var engine = Parsed("<!doctype html><body text='blue'>content</body></html>");
        Run(engine, "globalThis.out = document.body.text + '/' + document.body.textContent;");
        // body.text is the obsolete IDL reflection of the text attribute, not the body text.
        Equal("blue/content", Eval(engine, "out"));
        Run(engine, "document.body.text = 'red'; globalThis.out2 = document.fgColor + '/' + document.body.getAttribute('text');");
        Equal("red/red", Eval(engine, "out2"));
    }

    [Test]
    public static void DocumentColor_BodyAttributeChangeRepaints()
    {
        var page = Parser.ParseChildPage(
            "<!doctype html><body bgcolor='yellow'>content</body>",
            isSrcdoc: true, "http://test/", 800, 600);
        var body = Find(page.Root, "BODY")!;
        // The presentational hint paints the parsed value...
        Equal("yellow", body.StyleOverrides.GetValueOrDefault("background-color") ?? "");
        page.Engine!.RawEngine.Execute("document.body.setAttribute('bgcolor', 'green');");
        // ...and a later attribute change retracts the old hint value (body-bgcolor-attribute-change).
        Equal("green", body.StyleOverrides.GetValueOrDefault("background-color") ?? "");
    }

    private static LayoutNode? Find(LayoutNode node, string tag)
    {
        if (node.TagName == tag) return node;
        foreach (var c in node.Children) { var f = Find(c, tag); if (f != null) return f; }
        return null;
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
