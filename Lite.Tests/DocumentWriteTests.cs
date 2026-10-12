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
    public static void WriteWithoutOpen_KeepsTheLegacyAppendPath()
    {
        var page = NewParentPage();
        page.Engine.RawEngine.Execute("""
            var frame = document.createElement('iframe');
            document.body.appendChild(frame);
            frame.contentDocument.write('<p id=appended>legacy</p>');
            """);
        var child = Find(page.Root, n => n.TagName == "IFRAME")?.ChildPage;
        True(child is not null, "frame has no child page");
        True(FindById(child!.Root, "appended") is not null,
            "a write without open() must append to the live document, not buffer");
    }
}
