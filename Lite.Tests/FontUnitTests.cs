using Lite.Extensions;
using Lite.Layout;
using Lite.Models;
using SkiaSharp;
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

    [Test]
    public static void FontWeight_BolderAndLighterStepFromTheParentsComputedWeight()
    {
        // §15.6 / CSS Fonts table (font-weight-bolder-001, -lighter-001).
        Equal(400, TextMeasure.ResolveFontWeight("bolder", 100));
        Equal(700, TextMeasure.ResolveFontWeight("bolder", 400));
        Equal(900, TextMeasure.ResolveFontWeight("bolder", 600));
        Equal(100, TextMeasure.ResolveFontWeight("lighter", 500));
        Equal(400, TextMeasure.ResolveFontWeight("lighter", 700));
        Equal(700, TextMeasure.ResolveFontWeight("lighter", 900));
        Equal(300, TextMeasure.ResolveFontWeight(null, 300));
        Equal(300, TextMeasure.ResolveFontWeight("inherit", 300));
        True(!TextMeasure.IsValidFontWeight("9000"), "9000 is out of range (font-matching-rule-009)");
    }

    [Test]
    public static void FontWeight_ComputesOnceAndDescendantsInheritTheNumber()
    {
        // AngleSharp hands descendants the specified 'bolder'; re-reading it stepped again at
        // every level. The Parser's computed weight resolves it once, against the parent.
        var page = Parse("<div style=\"font-weight: 100\"><span id=\"s\" style=\"font-weight: bolder\"><em id=\"e\">x</em></span></div>" +
                         "<table><tr><th id=\"th\">a</th><th id=\"thn\" style=\"font-weight: normal\">b</th></tr></table>" +
                         "<div style=\"font-weight: bold\"><div id=\"f\" style=\"font: 20px serif\">c</div><p id=\"p6\" style=\"font-weight: 600\">d</p></div>");
        Equal(400, TextMeasure.ComputedFontWeight(Find(page, "s")!));
        Equal(400, TextMeasure.ComputedFontWeight(Find(page, "e")!));
        Equal(700, TextMeasure.ComputedFontWeight(Find(page, "th")!));
        Equal(400, TextMeasure.ComputedFontWeight(Find(page, "thn")!));
        // §15.8: the 'font' shorthand resets the weight it omits to 'normal'.
        Equal(400, TextMeasure.ComputedFontWeight(Find(page, "f")!));
        True(Find(page, "p6")!.GetFontBold(), "600 renders bold (font-weight-010)");
    }

    [Test]
    public static void FontRegistry_MatchesWeightsByTheCssFontsOrderAndSynthesizesBold()
    {
        // CSS Fonts 3 §5.2: 400 tries 500 before lighter weights; 700 takes the heavier faces
        // first, then the lighter ones descending. A face lighter than 600 asked for bold is
        // emboldened (font-weight-002's bold Ahem box, the "b" cells of font-weight-bolder-001).
        using var stream = SKTypeface.Default.OpenStream();
        var bytes = new byte[stream.Length];
        stream.Read(bytes, bytes.Length);
        const string family = "Lite Unit Weights";
        FontRegistry.Register(family, 100, false, bytes);
        FontRegistry.Register(family, 500, false, bytes);
        FontRegistry.Register(family, 800, false, bytes);
        var w100 = FontRegistry.Resolve(family, 100, false)!;
        var w500 = FontRegistry.Resolve(family, 500, false)!;
        var w800 = FontRegistry.Resolve(family, 800, false)!;
        True(ReferenceEquals(FontRegistry.Resolve(family, 400, false), w500), "400 → 500");
        True(ReferenceEquals(FontRegistry.Resolve(family, 300, false), w100), "300 → 100");
        True(ReferenceEquals(FontRegistry.Resolve(family, 600, false), w800), "600 → 800");
        True(ReferenceEquals(FontRegistry.Resolve(family, 900, false), w800), "900 → 800");
        True(ReferenceEquals(FontRegistry.Resolve(family, 700, true), w800), "no italic face: the upright one");
        True(TextMeasure.NeedsSyntheticBold(700, w500), "a 400-class face asked for 700 is emboldened");
        True(!TextMeasure.NeedsSyntheticBold(500, w500), "500 is not bold");
        FontRegistry.Clear();
        True(FontRegistry.Resolve(family, 400, false) is null, "@font-face faces do not survive Clear()");
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
