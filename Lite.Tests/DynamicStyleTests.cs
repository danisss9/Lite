using Lite;
using Lite.Extensions;
using Lite.Layout;
using Lite.Models;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>Computed styles of script-built and restyled elements, and the flow-root keyword.</summary>
public static class DynamicStyleTests
{
    private static LayoutNode Layout(string html)
    {
        var page = Parser.ParseChildPage(html, isSrcdoc: true, "http://test/", 800, 600);
        BoxEngine.Layout(page.Root, 800, 600);
        return page.Root;
    }

    private static LayoutNode? FindClass(LayoutNode node, string cls)
    {
        if (node.Attributes.GetValueOrDefault("class") == cls) return node;
        foreach (var c in node.Children)
            if (FindClass(c, cls) is { } found) return found;
        return null;
    }

    [Test]
    public static void LayeredBackground_WithAnOmittedComponent_Computes()
    {
        // line-height-201: the second layer ('red') has no size; computing the size list must
        // not throw, and the element keeps its other declarations.
        var root = Layout("<!DOCTYPE html><style>div{width:50px;height:100px;" +
            "background:url(x.png) 0 0 / 50px 100px no-repeat, red}</style><div id=\"d\">x</div>");
        LayoutNode? Find(LayoutNode n) => n.Id == "d" ? n : n.Children.Select(Find).FirstOrDefault(f => f is not null);
        var d = Find(root);
        True(d is not null && d.Box.BorderBox.Width == 50f, $"the div lays out at its declared width, got {d?.Box.BorderBox.Width}");
    }

    [Test]
    public static void FlowRoot_EstablishesABfcBesideAFloat()
    {
        var root = Layout("<!DOCTYPE html><style>body{margin:0} .f{float:left;width:7px;height:8px;margin:0 3px 0 2px}" +
            " .b{display:flow-root;height:15px}</style><div><div class=\"f\"></div><div class=\"b\"></div></div>");
        var bfc = FindClass(root, "b");
        True(bfc is not null, "bfc missing");
        True(Math.Abs(bfc!.Box.BorderBox.Left - 12f) < 0.01f, $"the BFC must start after the float's margin box (12), got {bfc.Box.BorderBox.Left}");
    }

    [Test]
    public static void ScriptBuiltElement_TakesTheRulesItsClassMatches()
    {
        // The element's computed style was taken at createElement (detached, no class): its
        // border and background stayed empty after the class was set and it was inserted.
        var root = Layout("<!DOCTYPE html><style>.x{border:3px solid blue;background:aqua;height:10px}</style>" +
            "<body onload=\"var d=document.createElement('div');d.className='x';document.body.appendChild(d)\"></body>");
        var x = FindClass(root, "x");
        True(x is not null, "created element missing");
        True(x!.GetBorderWidth().Top == 3f, $"border must apply, got {x.GetBorderWidth().Top}");
        True(x.GetBackgroundColor() == new SkiaSharp.SKColor(0, 255, 255), $"background must apply, got {x.GetBackgroundColor()}");
    }

    [Test]
    public static void ScriptBuiltBfc_NegativeMarginWrapsBelowFloat()
    {
        // floats-wrap-bfc-with-margin's shape, built by script exactly as the test does: the
        // class rules must reach the created elements and the inline 'margin-left' must feed
        // §10.3.3's prop-up, so the auto-width flow-root goes below the 14px float.
        var root = Layout("<!DOCTYPE html><style>body{margin:0}" +
            ".container{display:inline-block;width:30px;height:40px;border:1px solid gray}" +
            ".float{float:left;width:7px;height:8px;border:1px solid purple;margin:1px 3px 1px 2px}" +
            ".bfc{display:flow-root;height:15px;border:1px solid blue;background:aqua}</style>" +
            "<body onload=\"var c=document.createElement('div');c.className='container';" +
            "document.body.appendChild(c);" +
            "var f=document.createElement('div');f.className='float';c.appendChild(f);" +
            "var b=document.createElement('div');b.className='bfc';b.style['margin-left']='-17px';" +
            "c.appendChild(b);\"></body>");
        var bfc = FindClass(root, "bfc");
        True(bfc is not null, "bfc missing");
        True(bfc!.Box.BorderBox.Top >= 12f - 0.5f,
            $"the propped-up BFC goes below the float (12), got {bfc.Box.BorderBox.Top}");
        True(Math.Abs(bfc.Box.BorderBox.Width - 47f) < 0.5f,
            $"expected the §10.3.3 border box 30 - (-17) = 47, got {bfc.Box.BorderBox.Width}");
    }

    [Test]
    public static void ScriptStyleIndexer_ClearsTheGeneratedTestWaitState()
    {
        // The full floats-wrap-bfc-with-margin script (test and reference shapes): a thrown
        // exception in any style write leaves the html.reftest-wait class in place and the
        // runner reports a timeout.
        var root = Layout("<!DOCTYPE html><style>.g{width:500px}.c{display:inline-block;width:30px;height:40px}" +
            ".f{width:7px;height:8px}.b{display:flow-root;height:15px}</style>" +
            "<body onload=\"" +
            "const M=[-30,-20,-17,0,5,10,14];const S=['left','right'];const D=['ltr','rtl'];" +
            "function add(c,p){let e=document.createElement('div');if(c)e.classList.add(c);p.appendChild(e);return e}" +
            "for(let d of D)for(let f of S)for(let s of S){" +
            "let g=add('g',document.body);g.style.direction=d;let prop='margin-'+s;" +
            "for(let v of M){let c=add('c',g);let fl=add('f',c);fl.style.cssFloat=f;" +
            "let b=add('b',c);b.style[prop]=v+'px';" +
            "if(v<0){c.style.flexWrap='wrap';if((f=='right')!=(d=='rtl'))c.style.flexDirection='row-reverse';" +
            "b.style.width=(30-2-v)+'px';}}}" +
            "var m=document.createElement('div');m.id='done';document.body.appendChild(m);\"></body>");
        var done = FindTagOrId(root, "done");
        True(done is not null,
            "the script must reach its last statement (a style write threw or the loop never ended)");
    }

    private static LayoutNode? FindTagOrId(LayoutNode node, string id)
    {
        if (node.Id == id) return node;
        foreach (var c in node.Children)
            if (FindTagOrId(c, id) is { } found) return found;
        return null;
    }

    private static LayoutNode? FindTag(LayoutNode node, string tag)
    {
        if (node.TagName == tag) return node;
        foreach (var c in node.Children)
            if (FindTag(c, tag) is { } found) return found;
        return null;
    }

    [Test]
    public static void MaxHeight_ClampsInlineBlocksFloatsAndCaptions()
    {
        // §10.7: 'height: 200px; max-height: 100px' is 100px tall on an inline-block
        // (max-height-applies-to-012), a float (018) and a table caption (015); a 100% child of
        // the clamped inline-block is 100px too (017).
        var root = Layout("<!DOCTYPE html><style>body{margin:0}" +
            ".c{height:200px;max-height:100px;width:50px}" +
            "#ib{display:inline-block} #f{float:left} #cap{display:table-caption}" +
            "#kid{display:inline-block;width:10px;height:100%}</style>" +
            "<div><span id=\"ib\" class=\"c\"><span id=\"kid\"></span></span></div>" +
            "<div id=\"f\" class=\"c\"></div>" +
            "<div style=\"display:table\"><div id=\"cap\" class=\"c\"></div><div style=\"display:table-row\"><div style=\"display:table-cell\">x</div></div></div>");
        LayoutNode? Find(LayoutNode n, string id) => n.Id == id ? n : n.Children.Select(c => Find(c, id)).FirstOrDefault(f => f is not null);
        foreach (var id in new[] { "ib", "f", "cap", "kid" })
        {
            var node = Find(root, id);
            True(node is not null && MathF.Abs(node.Box.ContentBox.Height - 100f) < 0.5f,
                $"#{id} is clamped to 100px, got {node?.Box.ContentBox.Height}");
        }
    }

    private static void True(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
