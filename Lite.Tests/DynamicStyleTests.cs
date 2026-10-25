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

    private static void True(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
