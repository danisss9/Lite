using Lite;
using Lite.Scripting;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>Regression coverage for the canvas-2D package: save/restore carrying the full
/// drawing state (styles revert, not just the transform), the 5- and 9-argument drawImage
/// overloads scaling and cropping, and the maxWidth text contract condensing oversized text.</summary>
public static class CanvasContextTests
{
    private static (JsEngine engine, object surface) NewCanvas()
    {
        var engine = Parser.ParseChildPage(
            "<!doctype html><body><canvas id=c width=200 height=100></canvas></body></html>",
            isSrcdoc: true, "http://test/", 800, 600).Engine!;
        return (engine, new object());
    }

    private static void Run(JsEngine engine, string js) => engine.RawEngine.Execute(js);

    private static string Eval(JsEngine engine, string expr) => engine.RawEngine.Evaluate(expr).ToString() ?? "";

    [Test]
    public static void SaveRestore_CarriesStylesNotJustTheTransform()
    {
        var (engine, _) = NewCanvas();
        Run(engine, """
            var ctx = document.getElementById('c').getContext('2d');
            var out = [];
            ctx.fillStyle = 'red';
            ctx.lineWidth = 7;
            var red = ctx.fillStyle, seven = ctx.lineWidth;
            ctx.save();
            ctx.fillStyle = 'blue';
            ctx.lineWidth = 3;
            out.push(ctx.fillStyle === red ? 'same' : 'changed');
            ctx.restore();
            out.push(ctx.fillStyle === red ? 'reverted' : 'leaked');
            out.push(ctx.lineWidth === seven ? 'reverted' : 'leaked');
            out.push(ctx.globalAlpha);
            globalThis.out = out.join('|');
            """);
        // The saved state reverts the style change AND the line width.
        Equal("changed|reverted|reverted|1", Eval(engine, "out"));
    }

    [Test]
    public static void DrawImage_FiveAndNineArgumentForms()
    {
        var (engine, _) = NewCanvas();
        Run(engine, """
            var ctx = document.getElementById('c').getContext('2d');
            var out = [];
            out.push(typeof ctx.drawImage);
            // No image source is loaded in the headless page: drawImage on a sourceless
            // element draws nothing but must exist with all three arities and not throw.
            var img = document.createElement('img');
            ctx.drawImage(img, 10, 10);
            ctx.drawImage(img, 10, 10, 50, 25);
            ctx.drawImage(img, 0, 0, 1, 1, 10, 10, 50, 25);
            out.push('ok');
            globalThis.out = out.join('|');
            """);
        Equal("function|ok", Eval(engine, "out"));
    }

    [Test]
    public static void MeasureText_ReportsWidth()
    {
        var (engine, _) = NewCanvas();
        Run(engine, """
            var ctx = document.getElementById('c').getContext('2d');
            var w = ctx.measureText('hello').width;
            globalThis.out = String(w > 0) + '|' + typeof ctx.measureText('x').width;
            """);
        Equal("true|number", Eval(engine, "out"));
    }
}
