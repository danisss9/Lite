using Lite;
using Lite.Layout;
using Lite.Extensions;
using Lite.Models;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>
/// CSS 2.1 §10.3.5 / §10.3.2: a floated box's computed 'auto' left/right margins are USED as 0 —
/// not the in-flow block's §10.3.3 centering split (float-non-replaced-width-001/003/005,
/// float-replaced-width-001/003/005/006/011).
/// </summary>
public static class FloatSizingTests
{
    private static string FindRepoRoot()
    {
        var dir = Directory.GetCurrentDirectory();
        while (dir is not null && !File.Exists(Path.Combine(dir, "Lite.sln")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("repo root not found");
    }

    private static LayoutNode Layout(string html)
    {
        FontRegistry.RegisterFile(
            Path.Combine(FindRepoRoot(), "Lite.Conformance", "vendor", "wpt", "fonts", "Ahem.ttf"));
        var page = Parser.ParseChildPage(html, isSrcdoc: true, "http://test/", 800, 600);
        BoxEngine.Layout(page.Root, 800, 600);
        return page.Root;
    }

    private static LayoutNode? FindNode(LayoutNode n, Func<LayoutNode, bool> pred) =>
        pred(n) ? n : n.Children.Select(c => FindNode(c, pred)).FirstOrDefault(r => r is not null);

    [Test]
    public static void AutoMarginLeft_FloatUsesZeroNotCentering()
    {
        // float-non-replaced-width-001's shape: margin-left:auto on a left float must place it at
        // the containing block's left edge, not at the centering split (§10.3.5: used value 0).
        var root = Layout(
            "<!DOCTYPE html><html><body style='margin:0'>" +
            "<div style='border:solid black;height:2in;width:2in'>" +
            "<div style='background-color:blue;float:left;margin-left:auto;height:1in;width:1in'></div>" +
            "</div></body></html>");
        var f = FindNode(root, n => n.GetFloat() == FloatType.Left);
        True(f is not null, "float node not found");
        True(f!.Box.Margin.Left == 0f, $"auto margin-left must be used as 0, got {f.Box.Margin.Left}");
        // The float's border box starts at the container's content edge (the 3px border aside).
        True(Math.Abs(f.Box.ContentBox.Left - 3f) < 0.5f,
            $"float must sit at the containing block's content left edge, got {f.Box.ContentBox.Left}");
    }

    [Test]
    public static void AutoMarginRight_FloatUsesZeroFlushRight()
    {
        // float-non-replaced-width-003's shape: margin-right:auto on a right float leaves it
        // flush with the containing block's right edge (§10.3.5: used value 0).
        var root = Layout(
            "<!DOCTYPE html><html><body style='margin:0'>" +
            "<div style='border:solid black;height:2in;width:2in'>" +
            "<div style='background-color:blue;float:right;margin-right:auto;height:1in;width:1in'></div>" +
            "</div></body></html>");
        var f = FindNode(root, n => n.GetFloat() == FloatType.Right);
        True(f is not null, "float node not found");
        True(f!.Box.Margin.Right == 0f, $"auto margin-right must be used as 0, got {f.Box.Margin.Right}");
        True(Math.Abs((195f - f.Box.ContentBox.Right)) < 0.5f,
            $"right float must sit flush with the content right edge, got right {f.Box.ContentBox.Right}");
    }
}
