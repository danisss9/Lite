using Lite.Extensions;
using Lite.Layout;
using Lite.Models;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>CSS 2.1 §4.3.2 'ex' and §15.7 zero font sizes.</summary>
public static class FontUnitTests
{
    [Test]
    public static void Ex_IsTheFontsXHeightNotHalfAnEm()
    {
        // font-size-083: 'font-size: 6ex' refers to the PARENT's font; a width in ex to the
        // element's own. The 0.5em approximation made Ahem's 6ex 60px instead of 96.
        var page = Parse("<div id=\"p\" style=\"font-size: 20px\"><div id=\"c\" style=\"font-size: 6ex; width: 2ex\">x</div></div>");
        var parent = Find(page, "p")!;
        var child = Find(page, "c")!;
        var ratio = TextMeasure.XHeightRatio(parent);
        True(Math.Abs(ratio - 0.5f) > 0.001f, $"the test font's x-height must differ from 0.5em to discriminate ({ratio})");
        True(Math.Abs(child.GetFontSize() - 6 * 20 * ratio) < 0.01f, $"font-size {child.GetFontSize()} vs {6 * 20 * ratio}");
        BoxEngine.Layout(page, 800, 600);
        var expectedWidth = 2 * child.GetFontSize() * TextMeasure.XHeightRatio(child);
        True(Math.Abs(child.Box.ContentBox.Width - expectedWidth) < 0.01f, $"width {child.Box.ContentBox.Width} vs {expectedWidth}");
    }

    [Test]
    public static void ZeroFontSize_MeasuresAndPaintsNothing()
    {
        // font-size-002: text at 'font-size: 0px' must not paint or take space (the 1px floor
        // in CreateFont painted it).
        var page = Parse("<div id=\"z\" style=\"font-size: 0px; line-height: normal\">Filler Text</div>");
        var node = Find(page, "z")!;
        using var font = TextMeasure.CreateFont(node);
        True(font.MeasureText("Filler Text") == 0f, $"width {font.MeasureText("Filler Text")}");
    }

    private static LayoutNode Parse(string body) => Parser.ParseChildPage(
        $"<!DOCTYPE html><html><head></head><body>{body}</body></html>", isSrcdoc: true, "http://test/", 800, 600).Root;

    private static LayoutNode? Find(LayoutNode node, string id)
    {
        if (node.Attributes.GetValueOrDefault("id") == id) return node;
        foreach (var child in node.Children)
            if (Find(child, id) is { } hit) return hit;
        return null;
    }
}
