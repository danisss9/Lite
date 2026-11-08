using Lite;
using Lite.Scripting;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>Regression coverage for the events-and-messaging package's first half: the full
/// propagation path with the window and document as its first entries (DOM §2.9), per-phase
/// listener selection, at-target registration order, the propagation-flag lifecycle (cancelBubble
/// reads false once the path completes), the Event constants on the interface object and its
/// prototype, the new-only Event constructor, and the on* handler reflection semantics
/// (function stored as-is, empty string removes, strings compile).</summary>
public static class EventsTests
{
    private static JsEngine Parsed(string html) =>
        Parser.ParseChildPage(html, isSrcdoc: true, "http://test/", 800, 600).Engine!;

    private static void Run(JsEngine engine, string js) => engine.RawEngine.Execute(js);

    private static string Eval(JsEngine engine, string expr) => engine.RawEngine.Evaluate(expr).ToString() ?? "";

    [Test]
    public static void Dispatch_PathWalksWindowDocumentThenAncestors()
    {
        var engine = Parsed("""
            <!doctype html><html><body><div id=outer><table><tr><td id=cell>x</td></tr></table></div></body></html>
            """);
        Run(engine, """
            var log = [];
            var t = document.getElementById('cell');
            window.addEventListener('click', function () { log.push('win-capture'); }, true);
            window.addEventListener('click', function () { log.push('win-bubble'); }, false);
            document.addEventListener('click', function () { log.push('doc-capture'); }, true);
            document.addEventListener('click', function () { log.push('doc-bubble'); }, false);
            document.documentElement.addEventListener('click', function (e) {
                log.push('html-capture:' + (e.currentTarget === document.documentElement) + ':' + (e.eventPhase === 1));
            }, true);
            t.addEventListener('click', function () { log.push('target-bubble'); }, false);
            t.addEventListener('click', function () { log.push('target-capture'); }, true);
            t.dispatchEvent(new Event('click', { bubbles: true }));
            globalThis.out = log.join('|');
            """);
        // window → document → html capture; at-target fires capture listeners first, then
        // non-capture (the pinned DOM4-era dispatch contract); bubble walks document/window.
        Equal("win-capture|doc-capture|html-capture:true:true|target-capture|target-bubble|doc-bubble|win-bubble",
            Eval(engine, "out"));
    }

    [Test]
    public static void Dispatch_NonBubblingEventStillRunsWindowCapture()
    {
        var engine = Parsed("<!doctype html><body><p id=p>x</p></body></html>");
        Run(engine, """
            var log = [];
            window.addEventListener('x', function () { log.push('win-capture'); }, true);
            window.addEventListener('x', function () { log.push('win-bubble'); }, false);
            document.getElementById('p').dispatchEvent(new Event('x'));
            globalThis.out = log.join('|');
            """);
        // Capture reaches the window even for non-bubbling events; the bubble phase does not.
        Equal("win-capture", Eval(engine, "out"));
    }

    [Test]
    public static void Dispatch_StopPropagationSkipsTargetAndResets()
    {
        var engine = Parsed("<!doctype html><body><div id=d>x</div></body></html>");
        Run(engine, """
            var log = [];
            window.addEventListener('s', function () { log.push('win-capture'); }, true);
            var stopper = function (e) { e.stopPropagation(); };
            window.addEventListener('s', stopper, true);
            document.addEventListener('s', function () { log.push('doc-capture'); }, true);
            document.getElementById('d').addEventListener('s', function () { log.push('target'); });
            var e1 = new Event('s', { bubbles: true });
            document.getElementById('d').dispatchEvent(e1);
            var after = e1.cancelBubble;
            window.removeEventListener('s', stopper, true);
            var e2 = new Event('s', { bubbles: true });
            document.getElementById('d').dispatchEvent(e2);
            document.addEventListener('s', function () { log.push('doc-bubble-2'); }, false);
            var e3 = new Event('s', { bubbles: true });
            document.getElementById('d').dispatchEvent(e3);
            globalThis.out = log.join('|') + '|' + after + '|' + (e1.cancelBubble === false);
            """);
        // stopPropagation during capture ends the whole path (target skipped); the flags reset
        // when each dispatch completes, so the later dispatches run fully.
        Equal("win-capture|win-capture|doc-capture|target|win-capture|doc-capture|target|doc-bubble-2|false|true",
            Eval(engine, "out"));
    }

    [Test]
    public static void Event_ConstantsOnConstructorAndPrototype()
    {
        var engine = Parsed("<!doctype html><body></body></html>");
        Run(engine, """
            var out = [];
            out.push(Event.NONE === 0 && Event.CAPTURING_PHASE === 1 && Event.AT_TARGET === 2 && Event.BUBBLING_PHASE === 3);
            out.push(Event.prototype.NONE === 0);
            out.push((new Event('x')).NONE === 0 && (new Event('x')).AT_TARGET === 2);
            globalThis.out = out.join('|');
            """);
        Equal("true|true|true", Eval(engine, "out"));
    }

    [Test]
    public static void Event_ConstructorWithoutNewThrows()
    {
        var engine = Parsed("<!doctype html><body></body></html>");
        Run(engine, """
            var out = [];
            try { Event('x'); out.push('no-throw'); } catch (e) { out.push(e.name); }
            var e = new Event('ready', { bubbles: true });
            out.push(e.type + '/' + e.bubbles + '/' + e.isTrusted);
            globalThis.out = out.join('|');
            """);
        Equal("TypeError|ready/true/false", Eval(engine, "out"));
    }

    [Test]
    public static void EventHandlerReflection_StringEmptyRemovesAndCompiles()
    {
        var engine = Parsed("<!doctype html><body></body></html>");
        Run(engine, """
            var body = document.body;
            var out = [];
            out.push(body.onblur === null);
            function nop() {}
            body.onblur = nop;
            out.push(body.onblur === nop);
            body.onblur = '';
            out.push(body.onblur === null);
            body.onblur = 'mark(1)';
            out.push(typeof body.onblur);
            var marks = [];
            function mark(n) { marks.push(n); }
            body.dispatchEvent(new Event('blur'));
            out.push(marks.join(','));
            body.onblur = null;
            out.push(body.onblur === null);
            globalThis.out = out.join('|');
            """);
        // Function → same function; empty string → null; a string compiles into a function
        // that the dispatch invokes; null removes.
        Equal("true|true|true|function|1|true", Eval(engine, "out"));
    }

    [Test]
    public static void EventHandlerReflection_WindowHandlersFireOnNodePathBubble()
    {
        var engine = Parsed("<!doctype html><body><button id=b>x</button></body></html>");
        Run(engine, """
            var log = [];
            window.onbang = function () { log.push('window-onbang'); };
            document.getElementById('b').dispatchEvent(new Event('bang', { bubbles: true }));
            window.onbang = null;
            globalThis.out = log.join('|');
            """);
        // The window's on{type} property fires during the bubble phase of a node dispatch.
        Equal("window-onbang", Eval(engine, "out"));
    }
}
