using Lite;
using Lite.Models;
using Lite.Scripting;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>Phase 4 — MutationObserver (childList, attributes, characterData, subtree).</summary>
public static class MutationObserverTests
{
    [Test]
    public static void Observers_SurviveAnotherDocumentAndUseTheirOwnRealm()
    {
        var first = NewEngine();
        first.Execute("globalThis.calls = 0; var observer = new MutationObserver(function(){ calls++; }); observer.observe(document.body, {childList:true});");
        var second = NewEngine();
        second.Execute("globalThis.calls = 0; var observer = new MutationObserver(function(){ calls++; }); observer.observe(document.body, {childList:true});");
        first.RawEngine.Execute("document.body.appendChild(document.createElement('div'));");
        second.FlushMicrotasks();
        Equal(0, Convert.ToInt32(Global(first, "calls")));
        first.FlushMicrotasks();
        Equal(1, Convert.ToInt32(Global(first, "calls")));
        Equal(0, Convert.ToInt32(Global(second, "calls")));
        // Create another observer in the older realm after the global last-page pointer changed.
        first.RawEngine.Execute("var later = new MutationObserver(function(){ calls += 10; }); later.observe(document.body, {childList:true}); document.body.appendChild(document.createElement('p'));");
        first.FlushMicrotasks();
        Equal(12, Convert.ToInt32(Global(first, "calls")));
    }

    private static JsEngine NewEngine()
    {
        var sample = Parser.ParseFragment("<span></span>")[0];
        var root = new LayoutNode(null, "HTML", "", sample.Style);
        root.AddChild(new LayoutNode(null, "BODY", "", sample.Style));
        return JsEngine.Create(root);
    }

    private static object? Global(JsEngine e, string name) => e.RawEngine.GetValue(name).ToObject();

    [Test]
    public static void ChildList_RecordsAppendedNode()
    {
        var e = NewEngine();
        e.Execute(@"
            globalThis.__type = null; globalThis.__added = 0;
            var target = document.createElement('div');
            document.body.appendChild(target);
            var mo = new MutationObserver(function (records) {
                globalThis.__type = records[0].type;
                globalThis.__added = records[0].addedNodes.length;
            });
            mo.observe(target, { childList: true });
            target.appendChild(document.createElement('span'));
        ");
        // Callback fires at the microtask checkpoint.
        e.FlushMicrotasks();
        Equal("childList", Global(e, "__type")?.ToString());
        Equal(1, Convert.ToInt32(Global(e, "__added")));
    }

    [Test]
    public static void Attributes_RecordsOldValue()
    {
        var e = NewEngine();
        e.Execute(@"
            globalThis.__attr = null; globalThis.__old = null;
            var t = document.createElement('div');
            t.setAttribute('data-x', 'one');
            document.body.appendChild(t);
            var mo = new MutationObserver(function (records) {
                globalThis.__attr = records[0].attributeName;
                globalThis.__old = records[0].oldValue;
            });
            mo.observe(t, { attributes: true, attributeOldValue: true });
            t.setAttribute('data-x', 'two');
        ");
        e.FlushMicrotasks();
        Equal("data-x", Global(e, "__attr")?.ToString());
        Equal("one", Global(e, "__old")?.ToString());
    }

    [Test]
    public static void Subtree_ObservesDescendantMutations()
    {
        var e = NewEngine();
        e.Execute(@"
            globalThis.__count = 0;
            var root = document.createElement('div');
            var mid = document.createElement('div');
            root.appendChild(mid);
            document.body.appendChild(root);
            var mo = new MutationObserver(function (records) { globalThis.__count += records.length; });
            mo.observe(root, { childList: true, subtree: true });
            mid.appendChild(document.createElement('span')); // descendant mutation
        ");
        e.FlushMicrotasks();
        Equal(1, Convert.ToInt32(Global(e, "__count")));
    }

    [Test]
    public static void TakeRecords_ReturnsAndClearsSynchronously()
    {
        var e = NewEngine();
        e.Execute(@"
            var t = document.createElement('div');
            document.body.appendChild(t);
            var mo = new MutationObserver(function () {});
            mo.observe(t, { childList: true });
            t.appendChild(document.createElement('span'));
            globalThis.__n1 = mo.takeRecords().length;
            globalThis.__n2 = mo.takeRecords().length;
        ");
        Equal(1, Convert.ToInt32(Global(e, "__n1")));
        Equal(0, Convert.ToInt32(Global(e, "__n2")));
    }

    [Test]
    public static void Disconnect_StopsObserving()
    {
        var e = NewEngine();
        e.Execute(@"
            globalThis.__fired = 0;
            var t = document.createElement('div');
            document.body.appendChild(t);
            var mo = new MutationObserver(function () { globalThis.__fired++; });
            mo.observe(t, { childList: true });
            mo.disconnect();
            t.appendChild(document.createElement('span'));
        ");
        e.FlushMicrotasks();
        Equal(0, Convert.ToInt32(Global(e, "__fired")));
    }
}
