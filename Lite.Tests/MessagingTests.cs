using Lite;
using Lite.Scripting;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>Regression coverage for the messaging half of events-and-messaging: HTML §9.3
/// targetOrigin validation on postMessage (the "*" wildcard and "/" skip the check, an
/// unparseable value raises SyntaxError, a mismatched origin raises SecurityError) and the
/// §8.3 structured clone — typed values (Date, RegExp, Map, Set, typed arrays, Error) survive
/// with their identities, cyclic references resolve in the clone, functions and DOM wrappers
/// raise DataCloneError, and a cross-realm clone materializes with the TARGET realm's
/// constructors.</summary>
public static class MessagingTests
{
    private static JsEngine Parsed(string html) =>
        Parser.ParseChildPage(html, isSrcdoc: true, "http://test/", 800, 600).Engine!;

    private static void Run(JsEngine engine, string js) => engine.RawEngine.Execute(js);

    private static string Eval(JsEngine engine, string expr) => engine.RawEngine.Evaluate(expr).ToString() ?? "";

    private static void Drain(JsEngine engine)
    {
        try { engine.DrainTasks(); }
        catch (Exception ex) { Console.WriteLine("DRAIN: " + ex.GetType().Name + ": " + ex.Message + " @ " + ex.StackTrace); }
    }

    [Test]
    public static void PostMessage_TargetOriginChecks()
    {
        var engine = Parsed("<!doctype html><body></body></html>");
        Run(engine, """
            var notes = [];
            var got = [];
            window.onmessage = function (e) { try { got.push(String(e.data)); } catch (err) { got.push('h:' + err.message); } };
            try { postMessage('x', 'http://other.test/'); notes.push('delivered'); } catch (e) { notes.push(e.name); }
            try { postMessage('x', 'not a url'); notes.push('delivered'); } catch (e) { notes.push(e.name); }
            try { postMessage('a', '/'); notes.push('ok1'); } catch (e) { notes.push('p1:' + e.name); }
            try { postMessage('b'); notes.push('ok2'); } catch (e) { notes.push('p2:' + e.name); }
            globalThis.notes = notes.join('|');
            """);
        Drain(engine);
        // The mismatched origin raises SecurityError, the unparseable one SyntaxError, the
        // "/" form plus the omitted default deliver into the macrotask queue, and the handler
        // receives both cloned payloads.
        Equal("SecurityError|SyntaxError|ok1|ok2|a,b",
            Eval(engine, "notes") + "|" + Eval(engine, "got.join(',')"));
    }

    [Test]
    public static void StructuredClone_TypedValuesAndCyclesSurvive()
    {
        var engine = Parsed("<!doctype html><body></body></html>");
        Run(engine, """
            var payload = {
                when: new Date(1234),
                re: /ab+/gi,
                tags: new Set(['a', 'b']),
                scores: new Map([['x', 1]]),
                buf: new Uint8Array([1, 2, 3]),
                err: new TypeError('bad'),
            };
            payload.self = payload;
            var r = null;
            window.onmessage = function (e) { r = e.data; };
            postMessage(payload, '/');
            globalThis.out = 'pending';
            """);
        Drain(engine);
        Run(engine, """
            var out = [];
            out.push(r !== payload);
            out.push(r.when instanceof Date && r.when.getTime() === 1234);
            out.push(r.re instanceof RegExp && r.re.source === 'ab+' && r.re.global === true);
            out.push(r.tags instanceof Set && r.tags.has('b'));
            out.push(r.scores instanceof Map && r.scores.get('x') === 1);
            out.push(r.buf instanceof Uint8Array && r.buf[1] === 2);
            out.push(r.err instanceof TypeError && r.err.message === 'bad');
            out.push(r.self === r);
            globalThis.out = out.join('|');
            """);
        Equal("true|true|true|true|true|true|true|true", Eval(engine, "out"));
    }

    [Test]
    public static void StructuredClone_NonClonablesRaiseDataCloneError()
    {
        var engine = Parsed("<!doctype html><body></body></html>");
        Run(engine, """
            var out = [];
            try { postMessage(function () { return 1; }, '/'); out.push('delivered'); } catch (e) { out.push(e.name); }
            try { postMessage(document.body, '/'); out.push('delivered'); } catch (e) { out.push(e.name); }
            try { postMessage(Symbol('x'), '/'); out.push('delivered'); } catch (e) { out.push(e.name); }
            globalThis.out = out.join('|');
            """);
        Equal("DataCloneError|DataCloneError|DataCloneError", Eval(engine, "out"));
    }

    [Test]
    public static void StructuredClone_CrossRealmMaterializesWithTargetConstructors()
    {
        var first = Parsed("<!doctype html><body></body></html>");
        var second = Parsed("<!doctype html><body></body></html>");
        Run(first, "globalThis.payload = { when: new Date(5000), nested: { deep: [7, 8] } };");
        var payload = first.RawEngine.GetValue("payload");
        var cloned = Lite.Scripting.Dom.StructuredClone.Clone(first.RawEngine, payload, second.RawEngine);
        second.RawEngine.SetValue("g", cloned);
        Run(second, """
            var out = [];
            out.push(g.when instanceof Date && g.when.getTime() === 5000);
            out.push(g.nested.deep[1] === 8);
            out.push(g.when.constructor === Date);
            globalThis.out = out.join('|');
            """);
        // The clone materialized in the second realm with its own constructors.
        Equal("true|true|true", Eval(second, "out"));
    }
}
