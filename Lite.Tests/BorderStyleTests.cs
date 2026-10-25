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
}
