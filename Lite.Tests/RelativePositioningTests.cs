using Lite;
using Lite.Layout;
using Lite.Models;
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

    private static void True(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
