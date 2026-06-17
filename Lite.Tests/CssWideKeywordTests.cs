using Lite;
using Lite.Extensions;
using Lite.Layout;
using Lite.Models;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>Phase 3 (#12) — initial / inherit / unset cascade-wide keywords for non-parsed styles.</summary>
public static class CssWideKeywordTests
{
    private static readonly ICssStyleDeclarationProvider _style = new();
    private sealed class ICssStyleDeclarationProvider
    {
        public AngleSharp.Css.Dom.ICssStyleDeclaration S { get; } = Parser.ParseFragment("<div></div>")[0].Style;
    }

    private static (LayoutNode parent, LayoutNode child) Pair()
    {
        var parent = new LayoutNode(null, "DIV", "", _style.S);
        var child = new LayoutNode(null, "SPAN", "", _style.S);
        parent.AddChild(child);
        return (parent, child);
    }

    [Test]
    public static void Initial_ResolvesToPropertyInitialValue()
    {
        var (_, child) = Pair();
        child.StyleOverrides["color"] = "initial";
        StyleResolver.ResolveCssWideKeyword(child, "color");
        Equal("black", child.StyleOverrides.GetValueOrDefault("color"));
    }

    [Test]
    public static void Inherit_TakesParentValue()
    {
        var (parent, child) = Pair();
        parent.StyleOverrides["color"] = "purple";
        child.StyleOverrides["color"] = "inherit";
        StyleResolver.ResolveCssWideKeyword(child, "color");
        Equal("purple", child.StyleOverrides.GetValueOrDefault("color"));
    }

    [Test]
    public static void Unset_InheritedProperty_BehavesAsInherit()
    {
        var (parent, child) = Pair();
        parent.StyleOverrides["color"] = "green";
        child.StyleOverrides["color"] = "unset";       // color inherits → take parent
        StyleResolver.ResolveCssWideKeyword(child, "color");
        Equal("green", child.StyleOverrides.GetValueOrDefault("color"));
    }

    [Test]
    public static void Unset_NonInheritedProperty_BehavesAsInitial()
    {
        var (parent, child) = Pair();
        parent.StyleOverrides["display"] = "flex";
        child.StyleOverrides["display"] = "unset";     // display does NOT inherit → initial (inline)
        StyleResolver.ResolveCssWideKeyword(child, "display");
        Equal("inline", child.StyleOverrides.GetValueOrDefault("display"));
    }

    [Test]
    public static void ListStyleImage_ParsesUrlFromPropertyAndShorthand()
    {
        var (_, a) = Pair();
        a.StyleOverrides["list-style-image"] = "url('http://x/bullet.png')";
        Equal("http://x/bullet.png", a.GetListStyleImage());

        var (_, b) = Pair();
        b.StyleOverrides["list-style"] = "square url(\"http://x/dot.gif\") inside";
        Equal("http://x/dot.gif", b.GetListStyleImage());
    }
}
