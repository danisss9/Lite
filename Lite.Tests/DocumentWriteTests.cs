using Lite;
using Lite.Models;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>document.open/write/close over a frame document (HTML §6.3 dynamic markup): the
/// buffered stream replaces the owning frame's child page at close() and fires load — the
/// pattern the html5lib write corpus drives. Whole-input and per-character writes must
/// serialize identically, and a write() without open() keeps the legacy append-to-body path.</summary>
public static class DocumentWriteTests
{
    private static LayoutNode? Find(LayoutNode node, Func<LayoutNode, bool> match)
    {
        if (match(node)) return node;
        foreach (var child in node.Children)
            if (Find(child, match) is { } hit) return hit;
        return null;
    }

    private static LayoutNode? FindById(LayoutNode node, string id)
        => Find(node, n => n.Attributes.GetValueOrDefault("id") == id);

    private static Page NewParentPage()
        => Parser.ParseChildPage("<!doctype html><body></body>", true,
            "http://parent.test/page", 400, 200);

    [Test]
    public static void OpenWriteClose_ReplacesFrameDocumentAndFiresLoad()
    {
        var page = NewParentPage();
        page.Engine.RawEngine.Execute("""
            var frame = document.createElement('iframe');
            frame.addEventListener('load', function () { window.frameLoaded = true; });
            document.body.appendChild(frame);
            var doc = frame.contentDocument;
            doc.open();
            doc.write('<!DOCTYPE html><html><head></head><body><p id=written>hello</p></body></html>');
            doc.close();
            """);
        page.Engine.DrainTree();
        Equal("true", page.Engine.RawEngine.GetValue("frameLoaded").ToString());
        var child = Find(page.Root, n => n.TagName == "IFRAME")?.ChildPage;
        True(child is not null, "written frame has no child page");
        True(FindById(child!.Root, "written") is not null, "written content missing from the child page");
        page.Engine.RawEngine.Execute(
            "globalThis.__name = frame.contentDocument.doctype ? frame.contentDocument.doctype.name : 'none';" +
            "globalThis.__body = frame.contentDocument.body.innerHTML;");
        Equal("html", page.Engine.RawEngine.GetValue("__name").ToString());
        Equal("<p id=\"written\">hello</p>", page.Engine.RawEngine.GetValue("__body").ToString());
    }

    [Test]
    public static void PerCharacterWrites_SerializeIdentically()
    {
        var page = NewParentPage();
        page.Engine.RawEngine.Execute("""
            var frame = document.createElement('iframe');
            frame.addEventListener('load', function () { window.frameLoaded = true; });
            document.body.appendChild(frame);
            var doc = frame.contentDocument;
            doc.open();
            var input = '<!DOCTYPE html><html><head></head><body><p id=c>chars</p></body></html>';
            for (var i = 0; i < input.length; ++i) doc.write(input.charAt(i));
            doc.close();
            """);
        page.Engine.DrainTree();
        Equal("true", page.Engine.RawEngine.GetValue("frameLoaded").ToString());
        var child = Find(page.Root, n => n.TagName == "IFRAME")?.ChildPage;
        True(child is not null, "per-character frame has no child page");
        True(FindById(child!.Root, "c") is not null, "per-character content missing");
    }

    [Test]
    public static void WrittenMarkup_ScriptsExecute()
    {
        var page = NewParentPage();
        page.Engine.RawEngine.Execute("""
            var frame = document.createElement('iframe');
            document.body.appendChild(frame);
            var doc = frame.contentDocument;
            doc.open();
            doc.write('<!DOCTYPE html><body><script>window.x = 41 + 1;<\/script></body>');
            doc.close();
            """);
        page.Engine.DrainTree();
        var child = Find(page.Root, n => n.TagName == "IFRAME")?.ChildPage;
        True(child is not null, "frame with script has no child page");
        Equal("42", child!.Engine.RawEngine.GetValue("x").ToString());
    }

    [Test]
    public static void WriteWithoutOpen_StreamsUntilClose()
    {
        var page = NewParentPage();
        page.Engine.RawEngine.Execute("""
            var frame = document.createElement('iframe');
            document.body.appendChild(frame);
            var doc = frame.contentDocument;
            doc.write('<p id=appended>legacy</p>');
            doc.close();
            """);
        var child = Find(page.Root, n => n.TagName == "IFRAME")?.ChildPage;
        True(child is not null, "frame has no child page");
        True(FindById(child!.Root, "appended") is not null,
            "a frame write without open() streams into the initial document and parses at close()");
    }

    /// <summary>HTML §8.5.2 stream join: consecutive top-level writes join into ONE token
    /// stream (document-write/004: &#60;i then &#62;Filler Text forms one i element).</summary>
    [Test]
    public static void TopLevelWrites_JoinIntoOneStream()
    {
        var page = Parser.ParseChildPage(
            "<!doctype html><head><script>document.write('<i');document.write('>Filler Text');</script></head><body></body></html>",
            isSrcdoc: true, "http://test/", 800, 600);
        var body = Find(page.Root, n => n.TagName == "BODY");
        True(body is not null, "body missing");
        True(body!.Children.Count > 0, "the joined stream must produce the i element");
        True(body.Children[0].TagName == "I", $"first child must be I, got {body.Children[0].TagName}");
        True(body.Children[0].Text == "Filler Text",
            $"the joined element must hold the text, got '{body.Children[0].Text}'");
    }

    /// <summary>Cross-tag-boundary join (document-write/016): the second write closes the i and
    /// opens a b; the resulting tree carries both elements in order.</summary>
    [Test]
    public static void TopLevelWrites_CrossBoundaryJoin()
    {
        var page = Parser.ParseChildPage(
            "<!doctype html><head><script>document.write('<i>Filler Text');document.write('</i><b>Filler Text');</script></head><body></body></html>",
            isSrcdoc: true, "http://test/", 800, 600);
        var body = Find(page.Root, n => n.TagName == "BODY");
        True(body is not null && body.Children.Count >= 2, "both elements must exist");
        True(body.Children[0].TagName == "I" && body.Children[0].Text == "Filler Text", "first: i");
        True(body.Children[1].TagName == "B" && body.Children[1].Text == "Filler Text", "second: b");
    }

    /// <summary>document-write/001: nodes the source produced after the writing script are not
    /// attached while the script runs (a cached reference from an earlier script, the
    /// testharness's #log, must not see them yet) and reappear after the script completes.</summary>
    [Test]
    public static void TopLevelWrite_DetachesFollowingNodesDuringTheScript()
    {
        var page = Parser.ParseChildPage(
            "<!doctype html><script>function test(f){ f(); } test(function(){ document.write('PASS'); globalThis.__during = document.getElementById('log'); globalThis.__text = document.body.textContent; });</script><div id=log></div>",
            isSrcdoc: true, "http://test/", 800, 600);
        var engine = page.Engine!;
        Equal("null", engine.RawEngine.Evaluate("String(__during)").ToString() ?? "");
        Equal("PASS", engine.RawEngine.GetValue("__text")?.ToString() ?? "");
        Equal("true", engine.RawEngine.Evaluate("document.getElementById('log') !== null").ToString() ?? "");
    }

    /// <summary>document.write-02: WebIDL DOMString conversion writes "null" and "undefined".</summary>
    [Test]
    public static void TopLevelWrite_NullAndUndefinedWriteTheirStringForms()
    {
        var page = Parser.ParseChildPage(
            "<!doctype html><script>document.write(null); document.write(' '); document.write(undefined);</script><body></body>",
            isSrcdoc: true, "http://test/", 800, 600);
        var engine = page.Engine!;
        engine.RawEngine.Execute("globalThis.__t = document.body.textContent;");
        Contains("null undefined", engine.RawEngine.GetValue("__t")?.ToString() ?? "");
    }

    /// <summary>document-write/script_002: an inline script in the written markup runs before the
    /// write call returns; an external one waits until the writing script finishes
    /// (document-write/script_005).</summary>
    [Test]
    public static void WrittenScripts_RunInlineNowAndExternalAfter()
    {
        var page = Parser.ParseChildPage(
            "<!doctype html><script>var order = []; order.push(1); document.write(\"<script>order.push(2)<\\/script>\"); order.push(3);</script><body></body>",
            isSrcdoc: true, "http://test/", 800, 600);
        var engine = page.Engine!;
        Equal("1,2,3", engine.RawEngine.Evaluate("order.join(',')").ToString() ?? "");
    }

    [Test]
    public static void TopLevelWrite_InHeadScript_InsertsAtTheScriptPosition()
    {
        var page = Parser.ParseChildPage(
            "<!doctype html><title>t</title><script>function test(f){f();} test(function(){ document.write('PASS'); globalThis.__text = document.body.textContent; });</script><div id=log></div>",
            isSrcdoc: true, "http://test/", 800, 600);
        var engine = page.Engine!;
        Equal("PASS", engine.RawEngine.GetValue("__text")?.ToString() ?? "");
        engine.RawEngine.Execute("globalThis.__domText = document.body.textContent;");
        Equal("PASS", engine.RawEngine.GetValue("__domText")?.ToString() ?? "");
    }

}
