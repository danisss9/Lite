using Lite;
using Lite.Extensions;
using Lite.Layout;
using Lite.Models;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>CSS 2.1 §10.6.6 / §10.8.1 inline-block heights and baselines.</summary>
public static class InlineBlockTests
{
    private static LayoutNode? FindId(LayoutNode node, string id)
    {
        if (node.Id == id) return node;
        foreach (var c in node.Children)
            if (FindId(c, id) is { } found) return found;
        return null;
    }

    [Test]
    public static void ZeroHeightInlineBlock_BottomAlignsWithAnEmptySibling()
    {
        // border-top-width-006: 'height: 0' plus a 96px top border and an empty 96px-tall box
        // both put their bottom margin edge on the baseline, so their tops line up.
        var page = Parser.ParseChildPage("<!DOCTYPE html><style>body{margin:0} div{display:inline-block;width:50px}" +
            " #a{border-top:96px solid black;height:0} #b{height:96px;background:black}</style>" +
            "<div id=\"a\"></div><div id=\"b\"></div>", isSrcdoc: true, "http://test/", 800, 600);
        BoxEngine.Layout(page.Root, 800, 600);
        var a = FindId(page.Root, "a");
        var b = FindId(page.Root, "b");
        True(a is not null && b is not null, "nodes missing");
        True(a!.Box.BorderBox.Height == 96f, $"height:0 box must be 96px (border only), got {a.Box.BorderBox.Height}");
        True(Math.Abs(a.Box.BorderBox.Top - b!.Box.BorderBox.Top) < 0.01f,
            $"tops must align, got {a.Box.BorderBox.Top} vs {b.Box.BorderBox.Top}");
    }

    [Test]
    public static void InlineLeafKeepsItsTrailingSpaceBeforeAnInlineNeighbour()
    {
        // word-spacing-007: "<span>x </span><span>y</span>" — the space is mid-line, not at a
        // line edge, so §16.6.1 keeps it.
        var page = Parser.ParseChildPage("<!DOCTYPE html><div><span id=\"a\">x </span><span>y</span></div>",
            isSrcdoc: true, "http://test/", 800, 600);
        var a = FindId(page.Root, "a");
        True(a is not null, "span missing");
        True(a!.DisplayText == "x ", $"the boundary space must survive, got '{a.DisplayText}'");
    }

    private static void True(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
