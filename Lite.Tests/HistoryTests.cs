using Lite;
using Lite.Scripting;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>Regression coverage for the history-and-navigation package: the browsing context's
/// shared session history (entries carry their document and their creation kind), pushState
/// entries traversing in place with popstate, cross-document entries re-requesting their URL
/// through the navigation pipeline on traversal (the confirmed gap — traversal used to treat a
/// cross-document entry as same-document, changing the URL but never reloading), forward
/// truncation, and history.length spanning documents.</summary>
public static class HistoryTests
{
    private static JsEngine Parsed(string html, string url = "http://test/a.html") =>
        Parser.ParseChildPage(html, isSrcdoc: true, url, 800, 600).Engine!;

    private static void Run(JsEngine engine, string js) => engine.RawEngine.Execute(js);

    private static string Eval(JsEngine engine, string expr) => engine.RawEngine.Evaluate(expr).ToString() ?? "";

    [Test]
    public static void History_PushStateBackFiresPopstateInPlace()
    {
        var engine = Parsed("<!doctype html><body></body></html>");
        var requested = "";
        engine.OnNavigate = req => requested = req.Url;
        Run(engine, """
            var pop = '';
            window.onpopstate = function (e) { pop = JSON.stringify(e.state); };
            history.pushState({ n: 1 }, '', 'a.html?p=1');
            history.pushState({ n: 2 }, '', 'a.html?p=2');
            var lenAfter = history.length;
            history.back();
            globalThis.out = pop + '|' + JSON.stringify(history.state) + '|' + lenAfter + '|';
            """);
        engine.DrainTasks();
        // Two pushes grow the list to 3; back lands on the first push's entry IN PLACE — no
        // navigation request fires and the state restores.
        Equal("{\"n\":1}|{\"n\":1}|3|", Eval(engine, "out") + requested);
    }

    [Test]
    public static void History_CrossDocumentTraversalRequestsTheTargetUrl()
    {
        var shared = new Lite.Scripting.Dom.SessionHistory();
        var a = Parsed("<!doctype html><body>A</body></html>", "http://test/a.html");
        shared.AdoptFor(a, "http://test/a.html");
        // A navigates to B (cross-document): the entry appends, the host loads B's document,
        // and the new engine adopts the same session onto B's entry.
        Run(a, "location.assign('http://test/b.html');");
        a.DrainTasks();
        var b = Parsed("<!doctype html><body>B</body></html>", "http://test/b.html");
        shared.AdoptFor(b, "http://test/b.html");
        var requested = "";
        b.OnNavigate = req => requested = req.Url;
        // history.back from B: the previous entry is CROSS-DOCUMENT — the traversal must
        // re-request A's URL (the old behavior popped in place, changing the URL but keeping
        // B's DOM live).
        Run(b, "history.back();");
        b.DrainTasks();
        Equal("http://test/a.html", requested);
        True(shared.Index == 0, "the session must reposition onto the A entry");
    }

    [Test]
    public static void History_LengthSpansDocumentsAndForwardTruncates()
    {
        var shared = new Lite.Scripting.Dom.SessionHistory();
        var a = Parsed("<!doctype html><body></body></html>", "http://test/a.html");
        shared.AdoptFor(a, "http://test/a.html");
        Run(a, "history.pushState({}, '', 'a2.html'); location.assign('http://test/b.html');");
        a.DrainTasks();
        var b = Parsed("<!doctype html><body></body></html>", "http://test/b.html");
        shared.AdoptFor(b, "http://test/b.html");
        // A's list: [a, a2(push), b(nav)] — B reads the shared length; a fresh navigation from
        // B truncates the forward entries and the length stays at 4 after the new entry.
        Run(b, "history.pushState({}, '', 'b2.html');");
        Equal("4", Eval(b, "String(history.length)"));
        Run(b, "location.assign('http://test/c.html');");
        b.DrainTasks();
        var c = Parsed("<!doctype html><body></body></html>", "http://test/c.html");
        shared.AdoptFor(c, "http://test/c.html");
        Equal("5", Eval(c, "String(history.length)"));
    }

    [Test]
    public static void History_DeadPushStateEntryReloads()
    {
        var shared = new Lite.Scripting.Dom.SessionHistory();
        var a = Parsed("<!doctype html><body></body></html>", "http://test/a.html");
        shared.AdoptFor(a, "http://test/a.html");
        Run(a, "history.pushState({ n: 9 }, '', 'a.html?p=1');");
        a.DrainTasks();
        var b = Parsed("<!doctype html><body>B</body></html>", "http://test/b.html");
        shared.AdoptFor(b, "http://test/b.html");
        var requested = "";
        b.OnNavigate = req => requested = req.Url;
        // back() from B targets A's pushState entry — A's engine is no longer the live owner,
        // so the traversal reloads the URL instead of restoring state in place.
        Run(b, "history.back();");
        b.DrainTasks();
        Equal("http://test/a.html?p=1", requested);
    }

    [Test]
    public static void History_OutOfRangeGoIsNoOp()
    {
        var engine = Parsed("<!doctype html><body></body></html>");
        Run(engine, "history.go(-5); history.go(3);");
        engine.DrainTasks();
        Equal("http://test/a.html", Eval(engine, "location.href"));
    }
}
