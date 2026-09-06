using Lite;
using Lite.Layout;
using Lite.Models;
using SkiaSharp;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>
/// Phase C — iframe / nested browsing contexts. An &lt;iframe&gt; parses its child document into an
/// independent Page (own LayoutNode tree + JS engine) and renders it clipped into the frame box.
/// </summary>
public static class IframeTests
{
    private static readonly AngleSharp.Css.Dom.ICssStyleDeclaration _style =
        Parser.ParseFragment("<div></div>")[0].Style;

    private static LayoutNode Wrap(LayoutNode child)
    {
        var root = new LayoutNode(null, "HTML", "", _style);
        var body = new LayoutNode(null, "BODY", "", _style);
        root.StyleOverrides["display"] = "block";
        body.StyleOverrides["display"] = "block";
        root.AddChild(body);
        body.AddChild(child);
        return root;
    }

    [Test]
    public static void Iframe_SrcdocBuildsChildPage()
    {
        var iframe = Parser.ParseFragment("<iframe srcdoc=\"<p id='inner'>hello</p>\"></iframe>")[0];
        Equal("IFRAME", iframe.TagName);
        True(iframe.ChildPage is not null, "iframe should host a child Page from srcdoc");
        // The child's own layout tree contains the <p id=inner>.
        var found = FindById(iframe.ChildPage!.Root, "inner");
        True(found is not null, "child Page tree should contain #inner");
        // The iframe's own element children are NOT rendered (srcdoc is the content).
        True(iframe.Children.Count == 0, "iframe element children are inert fallback");
    }

    [Test]
    public static void Iframe_BlankAndEmptySrcdocHaveStableDocuments()
    {
        var page = Parser.ParseChildPage("<!doctype html><iframe id='blank'></iframe><iframe id='empty' src='http://invalid.test/' srcdoc=''></iframe>",
            true, "http://parent.test/page", 400, 200);
        var blank = FindById(page.Root, "blank")!.ChildPage;
        var empty = FindById(page.Root, "empty")!.ChildPage;
        True(blank is not null && empty is not null);
        Equal("about:blank", blank!.Engine.CurrentUrl);
        Equal("http://parent.test", blank.Engine.Origin);
        page.Engine.RawEngine.Execute("var b=document.getElementById('blank'); globalThis.__stable=b.contentDocument!==null && b.contentDocument===b.contentDocument; globalThis.__url=b.contentDocument.URL;");
        Equal(true, (bool)page.Engine.RawEngine.GetValue("__stable").ToObject()!);
        Equal("about:blank", page.Engine.RawEngine.GetValue("__url").ToString());
    }

    [Test]
    public static void Iframe_DocumentMetadataBelongsToItsOwnPage()
    {
        var page = Parser.ParseChildPage("<!doctype html><title>parent</title><iframe id='frame' srcdoc=\"<title>child</title><p>content</p>\"></iframe>",
            true, "http://parent.test/", 400, 200);
        var child = FindById(page.Root, "frame")!.ChildPage!;
        Equal("child", child.Engine.DocumentFacade.title);
        child.Engine.DocumentFacade.title = "changed child";
        Equal("parent", page.Engine.DocumentFacade.title);
        Equal("changed child", child.Document!.Title);
        Equal("CSS1Compat", page.Engine.DocumentFacade.compatMode);
        Equal("BackCompat", child.Engine.DocumentFacade.compatMode);
    }

    [Test]
    public static void Documents_KeepRuntimeStylesAndFormUrlsIndependent()
    {
        var first = Parser.ParseChildPage("<!doctype html><base href='/first/'><style>.selected { color: red }</style><form id=f action=send></form><p id=p>one</p>",
            true, "http://one.test/page", 400, 200);
        var second = Parser.ParseChildPage("<!doctype html><base href='/second/'><style>.selected { color: blue }</style><form id=f action=send></form><p id=p>two</p>",
            true, "http://two.test/page", 400, 200);
        string? firstUrl = null, secondUrl = null;
        first.Engine.OnNavigate = url => firstUrl = url;
        second.Engine.OnNavigate = url => secondUrl = url;
        foreach (var page in new[] { first, second })
        {
            page.Engine.RawEngine.Execute("document.getElementById('p').className='selected'; document.getElementById('f').submit();");
            page.Engine.DrainTasks();
        }
        Equal("rgba(255, 0, 0, 1)", FindById(first.Root, "p")!.StyleOverrides["color"]);
        Equal("rgba(0, 0, 255, 1)", FindById(second.Root, "p")!.StyleOverrides["color"]);
        True(firstUrl?.StartsWith("http://one.test/first/send", StringComparison.Ordinal) == true, firstUrl);
        True(secondUrl?.StartsWith("http://two.test/second/send", StringComparison.Ordinal) == true, secondUrl);
        Equal("http://one.test/page", first.Engine.CurrentUrl);
        Equal("http://one.test", first.Engine.Origin);
    }

    [Test]
    public static void DomWrappers_AreStablePerNodeAndRealm()
    {
        var page = Parser.ParseChildPage("<!doctype html><p id=p>one</p>", true, "http://one.test/", 400, 200);
        var other = Parser.ParseChildPage("<!doctype html><p>two</p>", true, "http://two.test/", 400, 200);
        var node = FindById(page.Root, "p")!;
        var original = Scripting.Dom.JsElement.For(page.Engine.RawEngine, node);
        var foreign = Scripting.Dom.JsElement.For(other.Engine.RawEngine, node);
        True(ReferenceEquals(original, Scripting.Dom.JsElement.For(page.Engine.RawEngine, node)));
        True(ReferenceEquals(foreign, Scripting.Dom.JsElement.For(other.Engine.RawEngine, node)));
        True(!ReferenceEquals(original, foreign));
    }

    [Test]
    public static void Iframe_DefaultSizeIs300x150()
    {
        var iframe = Parser.ParseFragment("<iframe srcdoc=\"<p>x</p>\"></iframe>")[0];
        var root = Wrap(iframe);
        BoxEngine.Layout(root, 800, 600);
        True(Math.Abs(iframe.Box.ContentBox.Width - 300) < 1, $"default width 300, got {iframe.Box.ContentBox.Width}");
        True(Math.Abs(iframe.Box.ContentBox.Height - 150) < 1, $"default height 150, got {iframe.Box.ContentBox.Height}");
    }

    [Test]
    public static void Iframe_RendersChildContent()
    {
        // Child paints a 100×100 lime block at its top-left; sample a pixel inside the iframe.
        var iframe = Parser.ParseFragment(
            "<iframe width='200' height='120' srcdoc=\"<body style='margin:0'>" +
            "<div style='width:100px;height:100px;background:lime'></div></body>\"></iframe>")[0];
        var root = Wrap(iframe);
        var viewport = new Viewport { ViewportHeight = 400 };
        using var bmp = Drawer.DrawToBitmap(400, 400, root, viewport);

        // The iframe sits at the body's top-left (body margin defaults aside, sample well inside).
        var px = bmp.GetPixel((int)iframe.Box.ContentBox.Left + 20, (int)iframe.Box.ContentBox.Top + 20);
        True(px.Green > 180 && px.Red < 120 && px.Blue < 120,
            $"expected lime child content inside the iframe, got {px}");
    }

    private static LayoutNode? FindById(LayoutNode node, string id)
    {
        if (node.Id == id) return node;
        foreach (var c in node.Children)
        {
            var f = FindById(c, id);
            if (f is not null) return f;
        }
        return null;
    }

    private static LayoutNode? FindByTag(LayoutNode node, string tag)
    {
        if (node.TagName == tag) return node;
        foreach (var c in node.Children)
        {
            var f = FindByTag(c, tag);
            if (f is not null) return f;
        }
        return null;
    }

    private static void Pump(params Scripting.JsEngine[] engines)
    {
        for (int i = 0; i < 6; i++)
            foreach (var e in engines) { e.DrainTasks(); e.FlushMicrotasks(); }
    }

    [Test]
    public static void Iframe_ContentDocumentReachableFromParent()
    {
        var page = Parser.ParseChildPage(
            "<body><iframe id='f' srcdoc=\"<p id='inner'>hi</p>\"></iframe></body>",
            isSrcdoc: true, "http://parent.test/", 400, 200);
        page.Engine.Execute(
            "globalThis.__txt = document.getElementById('f').contentDocument.getElementById('inner').textContent;");
        Equal("hi", (string?)page.Engine.RawEngine.GetValue("__txt").ToObject());
    }

    [Test]
    public static void Iframe_PostMessageRoundTrip()
    {
        // Child echoes any message back to its sender via event.source; parent records the reply.
        var page = Parser.ParseChildPage(
            "<body>" +
            "<iframe id='f' srcdoc=\"<script>window.addEventListener('message',function(e){" +
            "e.source.postMessage('child-got:'+e.data, '*');});</script>\"></iframe>" +
            "<script>window.__reply=null;window.addEventListener('message',function(e){window.__reply=e.data;});</script>" +
            "</body>",
            isSrcdoc: true, "http://parent.test/", 400, 200);
        var parent = page.Engine;
        var child = FindByTag(page.Root, "IFRAME")!.ChildPage!.Engine;

        parent.Execute("document.getElementById('f').contentWindow.postMessage('hello','*');");
        Pump(parent, child);

        Equal("child-got:hello", (string?)parent.RawEngine.GetValue("__reply").ToObject());
    }

    [Test]
    public static void Iframe_ChildSeesParentAndFrameElement()
    {
        // The child reports parent presence + its frameElement id when a message arrives (by which
        // point the parent context has been wired in).
        var page = Parser.ParseChildPage(
            "<body>" +
            "<iframe id='myframe' srcdoc=\"<script>window.addEventListener('message',function(e){" +
            "e.source.postMessage((window.parent!==window)+'|'+(frameElement?frameElement.id:'none'),'*');});</script>\"></iframe>" +
            "<script>window.__info=null;window.addEventListener('message',function(e){window.__info=e.data;});</script>" +
            "</body>",
            isSrcdoc: true, "http://parent.test/", 400, 200);
        var parent = page.Engine;
        var child = FindByTag(page.Root, "IFRAME")!.ChildPage!.Engine;

        parent.Execute("document.getElementById('myframe').contentWindow.postMessage('ping','*');");
        Pump(parent, child);

        Equal("true|myframe", (string?)parent.RawEngine.GetValue("__info").ToObject());
    }
}
