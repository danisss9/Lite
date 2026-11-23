using Lite;
using Lite.Layout;
using Lite.Models;
using Lite.Network;
using Lite.Scripting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SkiaSharp;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>CSS 2.1 §9.4.3 relative-positioning regressions: the over-constrained case where
/// neither 'left' nor 'right' is 'auto' — the containing block's 'direction' picks the winner.</summary>
public static class RelativePositioningTests
{
    private static LayoutNode Layout(string html)
    {
        var page = Parser.ParseChildPage(html, isSrcdoc: true, "http://test/", 800, 600);
        BoxEngine.Layout(page.Root, 800, 600);
        return page.Root;
    }

    private static LayoutNode? Find(LayoutNode node, string id)
    {
        if (node.Id == id) return node;
        foreach (var c in node.Children)
        {
            var found = Find(c, id);
            if (found is not null) return found;
        }
        return null;
    }

    [Test]
    public static void OverConstrainedLeftRight_ContainingBlockDirectionPicksWinner()
    {
        // position-relative-010/032/035, relpos-calcs-006: with both offsets set the position
        // is over-constrained (§9.4.3 rule 4) — ltr keeps 'left', rtl keeps 'right' ('left' is
        // ignored). The vertical pair stays 'top' over 'bottom'.
        var root = Layout("<!DOCTYPE html><style>body{margin:0}" +
            ".c{width:300px;height:60px}" +
            ".r{position:relative;left:100px;right:100px;top:10px;bottom:10px;width:50px;height:20px}" +
            "#rtl{direction:rtl}</style>" +
            "<div class=\"c\" id=\"ltr\"><div class=\"r\" id=\"in-ltr\"></div></div>" +
            "<div class=\"c\" id=\"rtl\"><div class=\"r\" id=\"in-rtl\"></div></div>");
        var inLtr = Find(root, "in-ltr");
        var inRtl = Find(root, "in-rtl");
        True(inLtr is not null && inRtl is not null, "nodes missing");
        var ltr = BoxEngine.RelativeOffset(inLtr!);
        var rtl = BoxEngine.RelativeOffset(inRtl!);
        True(ltr.Dx == 100f, $"ltr must apply 'left' (100), got {ltr.Dx}");
        True(rtl.Dx == -100f, $"rtl must apply negative 'right' (-100), got {rtl.Dx}");
        True(ltr.Dy == 10f && rtl.Dy == 10f, $"'top' (10) must win over 'bottom', got {ltr.Dy},{rtl.Dy}");
    }

    [Test]
    public static void OverConstrainedLeftRight_InlineAncestorDirectionDoesNotCount()
    {
        // §10.1 item 2: a relatively positioned inline's containing block is its nearest BLOCK
        // container ancestor, so an intermediate inline's 'direction' must not decide the
        // over-constrained pair — the outer div's ltr keeps 'left'.
        var root = Layout("<!DOCTYPE html><style>body{margin:0}#c{width:300px;height:60px}" +
            ".r{position:relative;left:100px;right:100px;width:50px;height:20px}" +
            "#i{direction:rtl}</style>" +
            "<div id=\"c\"><span id=\"i\"><div class=\"r\" id=\"in\"></div></span></div>");
        var node = Find(root, "in");
        True(node is not null, "node missing");
        True(BoxEngine.RelativeOffset(node!).Dx == 100f,
            $"the block container's ltr must keep 'left' (100), got {BoxEngine.RelativeOffset(node!).Dx}");
    }

    [Test]
    public static void InheritedTop_TakesTheParentSComputedValue()
    {
        // dynamic-top-change-001: 'top: inherit' (§6.2 Inheritance) resolves to the parent's
        // computed value, chaining up through 'inherit' ancestors. Through the real page
        // pipeline the cascade kept the raw keyword, so every inherited offset read as
        // 'auto' — the relative #parent inherited no shift and the abs-pos #red fell back to
        // its static position, leaving the red square visible beside the green one that must
        // cover it (onload sets #grandparent's 'top' and forces a layout with offsetWidth).
        // Srcdoc/ParseChildPage-style parsing resolves the keyword, so this goes through the
        // served-page pipeline the way the conformance harness does.
        var page = "<!DOCTYPE html><html><head><style>" +
            ".testDiv { position: absolute; width: 100px; height: 100px; }" +
            "#green { top: 4em; background: green; }" +
            "#red { top: inherit; background: red; }" +
            "#parent { top: inherit; position: relative; }" +
            "#grandparent { position: relative; }" +
            "</style><script>window.onload = function() {" +
            "document.body.offsetWidth;" +
            "document.getElementById(\"grandparent\").style.top = \"2em\"; }</script>" +
            "</head><body><div id=\"grandparent\"><div id=\"parent\">" +
            "<div id=\"red\" class=\"testDiv\"></div></div>" +
            "<div id=\"green\" class=\"testDiv\"></div></div></body></html>";
        var builder = TestWebServer.CreateBuilder();
        var app = builder.Build();
        app.Run(ctx => { ctx.Response.ContentType = "text/html"; return ctx.Response.WriteAsync(page); });
        app.Start();
        try
        {
            var root = Parser.TraversePage(new NavigationRequest(app.Urls.Single() + "/"), 800, 600).Root;
            var engine = JsEngine.Instance ?? throw new InvalidOperationException("no engine");
            for (var i = 0; i < 120 && (engine.HasPendingTreeTasks || engine.HasPendingTreeRAF); i++)
            {
                engine.DrainTree();
                engine.FlushMicrotasksTree();
                engine.FlushRAFTree(i * 16);
            }
            var bitmap = Drawer.DrawToBitmap(800, 600, root, new Viewport { ViewportHeight = 600 });
            using (bitmap)
            {
                // onload moves both squares to the same spot; the green one (later in doc
                // order) covers the red one exactly — no red anywhere.
                for (var y = 0; y < 600; y += 4)
                    for (var x = 0; x < 800; x += 8)
                        True(bitmap.GetPixel(x, y) != SKColors.Red,
                            $"no red may be visible after onload's 'top' change, found red at {x},{y}");
            }
        }
        finally
        {
            app.StopAsync().Wait();
        }
    }

    private static LayoutNode? FindId(LayoutNode node, string id)
    {
        if (node.Id == id) return node;
        foreach (var c in node.Children)
            if (FindId(c, id) is { } found) return found;
        return null;
    }

    private static void True(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
