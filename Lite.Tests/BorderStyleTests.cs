using Lite;
using Lite.Extensions;
using Lite.Layout;
using Lite.Models;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>CSS 2.1 §8.5.3 border styles: the initial style is none (used width 0), and
/// 'inherit' takes the parent's style.</summary>
public static class BorderStyleTests
{
    [Test]
    public static void UnstyledSides_HaveNoBorder_AndInheritTakesTheParentStyle()
    {
        // border-left-002: 'border-left: dashed; border-width: 5px' is a left border only.
        var page = Parser.ParseChildPage("<!DOCTYPE html><style>#a{border-left:dashed;border-width:5px}" +
            " body{border-top-style:dotted;border-top-width:0} #b{border-top-style:inherit;border-top-width:10px}</style>" +
            "<div id=\"a\">x</div><div id=\"b\">y</div>", isSrcdoc: true, "http://test/", 800, 600);
        LayoutNode? Find(LayoutNode n, string id) => n.Id == id ? n : n.Children.Select(c => Find(c, id)).FirstOrDefault(f => f is not null);
        var a = Find(page.Root, "a")!;
        var b = Find(page.Root, "b")!;
        var w = a.GetBorderWidth();
        if (w.Left != 5f || w.Top != 0f || w.Right != 0f || w.Bottom != 0f)
            throw new Exception($"only the left side may have width, got {w.Top},{w.Right},{w.Bottom},{w.Left}");
        if (b.GetBorderStyleTop() != BorderStyle.Dotted) throw new Exception($"inherit must take dotted, got {b.GetBorderStyleTop()}");
    }

    [Test]
    public static void GrooveAndRidge_WithDefaultBlackColor_DifferFromSolid()
    {
        // CSS 2.1 §8.5.3: groove and ridge borders with the default black color must shade
        // their two bevel halves differently so they do not collapse into a solid black border
        // (groove-default.html, ridge-default.html).
        static SkiaSharp.SKBitmap Render(string borderStyle)
        {
            var page = Parser.ParseChildPage(
                $"<!DOCTYPE html><style>body{{margin:0}} div{{width:60px;height:60px;border:10px {borderStyle}}}</style><div></div>",
                isSrcdoc: true, "http://test/", 120, 120);
            BoxEngine.Layout(page.Root, 120, 120);
            return Drawer.DrawToBitmap(120, 120, page.Root, new Viewport { ViewportHeight = 120 });
        }

        using var groove = Render("groove");
        using var ridge = Render("ridge");
        // Top border spans y=[0..10), outer half at y=2, inner half at y=7 (mid-x=40).
        var gOuter = groove.GetPixel(40, 2);
        var gInner = groove.GetPixel(40, 7);
        var rOuter = ridge.GetPixel(40, 2);
        var rInner = ridge.GetPixel(40, 7);
        if (gOuter == gInner)
            throw new Exception($"groove top border halves must differ on default black, both were {gOuter}");
        if (rOuter == rInner)
            throw new Exception($"ridge top border halves must differ on default black, both were {rOuter}");
        if (gOuter == rOuter && gInner == rInner)
            throw new Exception("groove and ridge must invert their light/dark bevel halves");
    }

    [Test]
    public static void ClipRect_AppliesOnlyToAbsolutelyPositionedElements()
    {
        // CSS 2.1 §11.1.2: 'clip' applies only to absolutely positioned elements
        // (position: absolute or fixed). Non-positioned and relatively positioned boxes
        // must ignore clip: rect(...) (clip-non-absolute-001).
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><style>" +
            "#s{clip:rect(10px,25px,50px,10px)}" +
            "#r{position:relative;clip:rect(10px,25px,50px,10px)}" +
            "#a{position:absolute;clip:rect(10px,25px,50px,10px)}" +
            "#f{position:fixed;clip:rect(10px,25px,50px,10px)}" +
            "</style><div id=\"s\">x</div><div id=\"r\">x</div><div id=\"a\">x</div><div id=\"f\">x</div>",
            isSrcdoc: true, "http://test/", 400, 300);
        BoxEngine.Layout(page.Root, 400, 300);
        LayoutNode? Find(LayoutNode n, string id) => n.Id == id ? n : n.Children.Select(c => Find(c, id)).FirstOrDefault(f => f is not null);
        if (Find(page.Root, "s")!.GetClipRect() is not null) throw new Exception("static element must not clip");
        if (Find(page.Root, "r")!.GetClipRect() is not null) throw new Exception("relative element must not clip");
        if (Find(page.Root, "a")!.GetClipRect() is null) throw new Exception("absolute element must clip");
        if (Find(page.Root, "f")!.GetClipRect() is null) throw new Exception("fixed element must clip");
    }

    [Test]
    public static void StyleClipProperty_OverridesStylesheetClipToAuto()
    {
        // CSS 2.1 §11.1.2: assigning element.style.clip = "auto" must override a stylesheet
        // clip: rect(...) so the element is no longer clipped (clip-001).
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><style>#p{position:absolute;width:2px;height:2px;clip:rect(0,1px,1px,0)}</style>" +
            "<div id=\"p\"></div>",
            isSrcdoc: true, "http://test/", 200, 200);
        BoxEngine.Layout(page.Root, 200, 200);
        LayoutNode? Find(LayoutNode n, string id) => n.Id == id ? n : n.Children.Select(c => Find(c, id)).FirstOrDefault(f => f is not null);
        var p = Find(page.Root, "p")!;
        if (p.GetClipRect() is null) throw new Exception("stylesheet clip: rect(...) must apply initially");
        new Lite.Scripting.Dom.JsStyle(p).clip = "auto";
        if (p.GetClipRect() is not null) throw new Exception("style.clip = 'auto' must clear the clip rect");
    }
}
