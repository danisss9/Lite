using Lite;
using Lite.Models;
using Lite.Platform;
using Lite.Scripting;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>Headless input injection over BrowserWindow's real pipeline — the same
/// OnMouseDown/OnMouseUp/SendKey/OnChar callbacks the native window hosts drive, exposed
/// through the TestDriverBridge host function. Each test asserts an observable effect
/// (trusted events, focus, value text), so a silently no-op injection fails here rather
/// than passing conformance tests vacuously.</summary>
public static class TestDriverTests
{
    private static JsEngine Load(string html)
    {
        var page = Parser.ParseChildPage(html, isSrcdoc: true, "http://driver.test/", 500, 300);
        var window = new BrowserWindow("http://driver.test/");
        var engine = window.RunHeadless(new HeadlessHost(), page);
        TestDriverBridge.Register(engine!, window);
        return engine!;
    }

    /// <summary>Drives one bridge operation from JS exactly like the vendor script does:
    /// the payload is a JSON object literal, stringified at runtime into the host call.</summary>
    private static void Drive(JsEngine engine, string opJson) =>
        engine.Execute($"__lite_testdriver(JSON.stringify({opJson}))");

    /// <summary>Clicks an element by id through the bridge, centering the gesture with the
    /// same geometry the vendor script computes.</summary>
    private static void ClickById(JsEngine engine, string id) => engine.Execute($$"""
        var r = document.getElementById({{id}}).getBoundingClientRect();
        __lite_testdriver(JSON.stringify({op: "click", x: Math.round(r.left + r.width / 2), y: Math.round(r.top + r.height / 2)}));
        """);

    [Test]
    public static void Click_DispatchesTrustedPointerAndClickEvents()
    {
        var engine = Load("<button id='b'>go</button>");
        engine.Execute("""
            window.got = [];
            var b = document.getElementById('b');
            ['pointerdown', 'mousedown', 'pointerup', 'mouseup', 'click'].forEach(function (t) {
              b.addEventListener(t, function (e) { got.push(t + ':' + e.isTrusted); });
            });
            """);
        ClickById(engine, "'b'");
        engine.DrainTree();
        Equal("pointerdown:true,mousedown:true,pointerup:true,mouseup:true,click:true",
            engine.RawEngine.GetValue("got").ToString());
    }

    [Test]
    public static void Click_FocusesTextInputAndKeyOpsType()
    {
        var engine = Load("<input id='t' value=''>");
        engine.Execute("""
            window.keys = [];
            var t = document.getElementById('t');
            t.addEventListener('keydown', function (e) { keys.push('down:' + e.key); });
            t.addEventListener('input', function () { keys.push('input'); });
            """);
        ClickById(engine, "'t'");
        Drive(engine, """{"op":"key","kind":"down","key":"h"}""");
        Drive(engine, """{"op":"char","ch":104}""");
        Drive(engine, """{"op":"key","kind":"up","key":"h"}""");
        Drive(engine, """{"op":"char","ch":105}""");
        engine.DrainTree();
        var state = engine.RawEngine.Evaluate(
            "document.activeElement.id + '|' + document.getElementById('t').value + '|' + keys.join(',')").ToString();
        // one keydown + one input event per typed character (h, i)
        Equal("t|hi|down:h,input,input", state);
    }

    [Test]
    public static void ElementsFromPoint_ResolvesDeepestElementFirst()
    {
        var engine = Load("<div id='outer' style='padding:40px'><span id='inner'>x</span></div>");
        engine.Execute("""
            var r = document.getElementById('inner').getBoundingClientRect();
            var chain = document.elementsFromPoint(r.left + r.width / 2, r.top + r.height / 2);
            globalThis.__hit = chain.length >= 2 && chain[0].id === 'inner' && chain[1].id === 'outer'
              ? 'yes' : 'no:' + chain.map(function (e) { return e.id || e.tagName; }).join(',');
            """);
        Equal("yes", engine.RawEngine.GetValue("__hit").ToString());
    }
}
