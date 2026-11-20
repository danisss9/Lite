using Lite.Extensions;
using Lite.Models;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>CSS 2.1 §12.1 generated boxes: a ::before/::after is an inline box of its own, so the
/// non-inherited properties of the originating element (here a table cell's UA
/// 'vertical-align: middle') must not leak into it (§10.8.1: the initial value is 'baseline').</summary>
public static class GeneratedContentTests
{
    [Test]
    public static void BeforeInATableCell_IsBaselineAlignedNotMiddle()
    {
        // content-037: the green "PASS PASS" of `td:before` sat 2px below the reference cell's
        // text because the pseudo read the cell's 'middle' as its own vertical-align.
        var (pseudo, cell) = BeforeInCell("");
        True(pseudo.GetVerticalAlign() == VerticalAlignType.Baseline,
            $"pseudo aligns {pseudo.GetVerticalAlign()}");
        True(cell.Box.ContentBox.Height < 18f,
            $"the cell's line box grew to {cell.Box.ContentBox.Height}");
    }

    [Test]
    public static void BeforeDeclaringVerticalAlign_KeepsItsOwnValue()
    {
        // The default only applies when the pseudo rule says nothing: an authored
        // 'vertical-align' on ::before still wins (§12.1: the pseudo-element's own declarations).
        var (pseudo, _) = BeforeInCell("vertical-align: top;");
        True(pseudo.GetVerticalAlign() == VerticalAlignType.Top,
            $"pseudo aligns {pseudo.GetVerticalAlign()}");
    }

    private static (LayoutNode Pseudo, LayoutNode Cell) BeforeInCell(string pseudoDeclarations)
    {
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><head><style>td { vertical-align: middle; } " +
            $"td:before {{ content: 'x'; {pseudoDeclarations} }}</style></head>" +
            "<body><table><tr><td></td></tr></table></body></html>",
            isSrcdoc: true, "http://test/", 800, 600);
        LayoutNode? cell = null, pseudo = null;
        void Walk(LayoutNode node)
        {
            if (node.TagName == "TD") cell = node;
            if (node.TagName == "#pseudo-before") pseudo = node;
            foreach (var child in node.Children) Walk(child);
        }
        Walk(page.Root);
        True(cell is not null && pseudo is not null, "table cell or ::before missing");
        return (pseudo!, cell!);
    }
}
