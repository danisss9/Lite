using Lite;
using Lite.Scripting;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>Regression coverage for the obsolete-and-rendering package's reflection surface:
/// the marquee/frameset/frame/dir/font reflected IDL attributes (enum defaults, unsigned-long
/// fallback defaults, boolean presence) exposed on the per-interface prototypes, and the
/// HTMLScriptElement event/htmlFor reflection.</summary>
public static class ObsoleteTests
{
    private static JsEngine Parsed(string html) =>
        Parser.ParseChildPage(html, isSrcdoc: true, "http://test/", 800, 600).Engine!;

    private static void Run(JsEngine engine, string js) => engine.RawEngine.Execute(js);

    private static string Eval(JsEngine engine, string expr) => engine.RawEngine.Evaluate(expr).ToString() ?? "";

    [Test]
    public static void Marquee_ReflectionDefaultsAndParsing()
    {
        var engine = Parsed(
            "<!doctype html><body><marquee id=m1 loop='a'>t</marquee>" +
            "<marquee id=m2 behavior='slide' direction='up' scrollamount='12'>t</marquee></body></html>");
        Run(engine, """
            var out = [];
            var m1 = document.getElementById('m1');
            out.push(m1.behavior);
            out.push(m1.direction);
            out.push(m1.scrollAmount);
            out.push(m1.scrollDelay);
            var m2 = document.getElementById('m2');
            out.push(m2.behavior);
            out.push(m2.direction);
            out.push(m2.scrollAmount);
            m2.scrollAmount = 30;
            out.push(m2.scrollAmount);
            out.push(m2.getAttribute('scrollamount'));
            // marquee.loop: malformed/absent reads -1; a positive integer reads back; the
            // setter writes the content attribute (document-color/loop clusters).
            out.push(m1.loop);
            m2.setAttribute('loop', '2');
            out.push(m2.loop);
            m2.loop = 5;
            out.push(m2.loop + '/' + m2.getAttribute('loop'));
            globalThis.out = out.join('|');
            """);
        // Defaults: scroll/left/6/85; reflected values read through; a setter writes the
        // content attribute.
        Equal("scroll|left|6|85|slide|up|12|30|30|-1|2|5/5", Eval(engine, "out"));
    }

    [Test]
    public static void FrameFontDir_Reflection()
    {
        var engine = Parsed(
            "<!doctype html><body><font color=red face=x size=3>f</font></body></html>");
        Run(engine, """
            var out = [];
            var font = document.querySelector('font');
            out.push(font.color + '/' + font.face + '/' + font.size);
            // Frameset/frame via constructors: the tree builder drops a frameset parsed in
            // body, so the reflection is exercised on created elements.
            var fs = document.createElement('frameset');
            fs.setAttribute('cols', '1,2');
            out.push(fs.cols);
            var frame = document.createElement('frame');
            frame.setAttribute('name', 'n');
            frame.setAttribute('noresize', '');
            out.push(frame.name);
            out.push(frame.noResize);
            globalThis.out = out.join('|');
            """);
        Equal("red/x/3|1,2|n|true", Eval(engine, "out"));
    }

    [Test]
    public static void Script_EventHtmlFor_Reflection()
    {
        var engine = Parsed("<!doctype html><body></body></html>");
        Run(engine, """
            var out = [];
            var script = document.createElement('script');
            out.push(script.event);
            out.push(script.htmlFor);
            script.setAttribute('event', 'blah');
            script.setAttribute('for', 'blah');
            out.push(script.event + '/' + script.htmlFor);
            script.event = 'foo';
            out.push(script.getAttribute('event'));
            globalThis.out = out.join('|');
            """);
        Equal("||blah/blah|foo", Eval(engine, "out"));
    }
}
