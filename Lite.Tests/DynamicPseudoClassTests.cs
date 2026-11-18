using Lite.Extensions;
using Lite.Interaction;
using Lite.Layout;
using Lite.Models;
using SkiaSharp;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>CSS 2.1 §5.11.3 dynamic pseudo-classes: a :hover/:active/:focus rule keeps its
/// whole selector and re-matches the live interaction state, so the state may sit on any
/// compound of the selector — not only on the element the rule finally styles.</summary>
public static class DynamicPseudoClassTests
{
    private static LayoutNode Load(string css, string body)
    {
        var page = Parser.ParseChildPage(
            $"<!DOCTYPE html><html><head><style>body {{ margin: 0 }} {css}</style></head><body>{body}</body></html>",
            isSrcdoc: true, "http://test/", 800, 600);
        Drawer.Draw(800, 600, page.Root, new Viewport { ViewportHeight = 600 });
        PseudoClassState.ClearHover();
        PseudoClassState.ClearActive();
        PseudoClassState.SetFocus(null);
        return page.Root;
    }

    private static LayoutNode ById(LayoutNode node, string id)
    {
        if (node.Attributes.GetValueOrDefault("id") == id) return node;
        foreach (var child in node.Children)
            if (FindById(child, id) is { } hit) return hit;
        throw new InvalidOperationException($"#{id} not found");
    }

    private static LayoutNode? FindById(LayoutNode node, string id)
    {
        if (node.Attributes.GetValueOrDefault("id") == id) return node;
        foreach (var child in node.Children)
            if (FindById(child, id) is { } hit) return hit;
        return null;
    }

    private static readonly SKColor Green = new(0, 0x80, 0);
    private static readonly SKColor White = SKColors.White;
    private static readonly SKColor Blue = new(0, 0, 0xff);

    private static SKColor Color(LayoutNode node) => node.GetColor();

    private static void HoverOver(LayoutNode root, LayoutNode node)
    {
        var box = node.Box.BorderBox;
        PseudoClassState.UpdateHover(root, box.Left + 2, box.Top + 2);
    }

    [Test]
    public static void Hover_AppliesWhileThePointerIsOverTheElementOnly()
    {
        var root = Load("#d:hover { color: green }", "<div id='d' style='height:50px'>Filler Text</div><div id='o' style='height:50px'>x</div>");
        var d = ById(root, "d");
        True(Color(d) != Green, "no hover state: the :hover rule does not apply");
        HoverOver(root, d);
        True(Color(d) == Green, $"expected green, got {Color(d)}");
        HoverOver(root, ById(root, "o"));
        True(Color(d) != Green, "the pointer left: the :hover rule stops applying");
    }

    [Test]
    public static void Focus_OnAnEarlierSiblingStylesTheCombinatorSubject()
    {
        // focus-pseudo-class-001: 'textarea:focus + span' turns the span green while the
        // textarea (not the span) has focus.
        var root = Load("span { color: white } textarea:focus + span { color: green }",
            "<div><textarea id='t'></textarea> <span id='s'>Focus received</span></div>");
        var s = ById(root, "s");
        True(Color(s) == White, $"expected white, got {Color(s)}");
        PseudoClassState.SetFocus(ById(root, "t"));
        True(Color(s) == Green, $"expected green, got {Color(s)}");
        PseudoClassState.SetFocus(null);
        True(Color(s) == White, $"expected white, got {Color(s)}");
    }

    [Test]
    public static void Hover_OnAnAncestorStylesTheDescendantSubject()
    {
        var root = Load("#outer:hover p { color: green }", "<div id='outer' style='padding:20px'><p id='p'>Filler Text</p></div>");
        var outer = ById(root, "outer");
        var p = ById(root, "p");
        True(Color(p) != Green, "not hovered");
        // The padding, not the paragraph: the ancestor's :hover still styles the descendant.
        PseudoClassState.UpdateHover(root, outer.Box.BorderBox.Left + 5, outer.Box.BorderBox.Top + 5);
        True(Color(p) == Green, $"expected green, got {Color(p)}");
    }

    [Test]
    public static void LinkHover_IsDynamicNotAPermanentLinkStyle()
    {
        var root = Load("a:link { color: blue } a:link:hover { color: green }",
            "<p><a id='a' href='http://test/x'>link text</a></p>");
        var a = ById(root, "a");
        True(Color(a) == Blue, $"expected blue, got {Color(a)}");
        HoverOver(root, a);
        True(Color(a) == Green, $"expected green, got {Color(a)}");
    }

    [Test]
    public static void VisitedParts_NeverApplyButTheirListSiblingsDo()
    {
        var root = Load("a:visited:hover, #p:hover { color: green }",
            "<p id='p'>Filler <a id='a' href='http://test/x'>link</a></p>");
        HoverOver(root, ById(root, "a"));
        True(Color(ById(root, "a")) != Green, "every link is unvisited: the :visited part never matches");
        True(Color(ById(root, "p")) == Green, $"expected green, got {Color(ById(root, "p"))}");
    }

    [Test]
    public static void Active_FollowsThePressAndRelease()
    {
        var root = Load("div:active { color: green }", "<div id='d' style='height:40px'>Filler Text</div>");
        var d = ById(root, "d");
        var box = d.Box.BorderBox;
        PseudoClassState.SetActive(root, box.Left + 2, box.Top + 2);
        True(Color(d) == Green, $"expected green, got {Color(d)}");
        PseudoClassState.ClearActive();
        True(Color(d) != Green, "released: :active stops applying");
    }

    [Test]
    public static void DynamicShorthands_ReachTheLonghandsThePainterReads()
    {
        // outline-pseudo-selector-002: 'span:hover { outline: 10px solid black }'.
        var root = Load("span:hover { outline: 10px solid black }", "<div><span id='s'>Filler Text</span></div>");
        var s = ById(root, "s");
        HoverOver(root, s);
        True(s.TryResolveStyle("outline-style", out var style) && style.Trim() == "solid",
            $"the hovered span's outline-style must resolve to solid, got '{style}'");
        True(s.GetOutlineStyle() == BorderStyle.Solid, $"outline style {s.GetOutlineStyle()}");
    }

    [Test]
    public static void FocusedFormControl_TakesItsFocusColor()
    {
        // focus-selector-001: 'textarea:focus { color: green }'.
        var root = Load("textarea:focus { color: green }", "<div><textarea id='t'>Filler Text</textarea></div>");
        var t = ById(root, "t");
        PseudoClassState.SetFocus(t);
        True(Color(t) == Green, $"the focused textarea's colour must be green, got {Color(t)}");
    }

    [Test]
    public static void Cascade_ALaterStaticRuleOfEqualSpecificityBeatsTheDynamicAndLinkRules()
    {
        // c21-pseu-cls-000: every a:<state> rule says red, the later 'a.test' (equal
        // specificity 0,1,1) says green, so the link stays green whatever is done to it.
        var root = Load("a:link {color: red;} a:visited {color: red;} a:hover {color: red;} a:focus {color: red;} " +
                        "a:active {color: red;} a.test {color: green;}",
            "<p><a id='a' href='' class='test' style='display:inline-block;height:30px'>link</a></p>");
        var a = ById(root, "a");
        True(Color(a) == Green, $"at rest: expected green, got {Color(a)}");
        HoverOver(root, a);
        True(Color(a) == Green, $"hovered: expected green, got {Color(a)}");
        PseudoClassState.SetFocus(a);
        True(Color(a) == Green, $"focused: expected green, got {Color(a)}");
        var box = a.Box.BorderBox;
        PseudoClassState.SetActive(root, box.Left + 2, box.Top + 2);
        True(Color(a) == Green, $"active: expected green, got {Color(a)}");
    }

    [Test]
    public static void InlineLink_PaintsItsBackground()
    {
        // dynamic-pseudo-classes-000: 'a:hover { background-color: green; color: white }'.
        var root = Load("a:hover { background-color: green; color: white } a { padding: 0 6px }",
            "<p><a id='a' href='http://test/x'>Hover me</a></p>");
        var a = ById(root, "a");
        HoverOver(root, a);
        using var bitmap = Drawer.DrawToBitmap(800, 600, root, new Viewport { ViewportHeight = 600 });
        var box = a.Box.PaddingBox;
        // The left padding strip holds no glyph: only the background paints there.
        var pixel = bitmap.GetPixel((int)box.Left + 2, (int)((box.Top + box.Bottom) / 2));
        True(pixel == Green, $"the hovered link's background must paint green, got {pixel}");
    }

    [Test]
    public static void VisitedOnlyRules_NeverApply()
    {
        // Every link is unvisited, so 'a:visited' matches nothing; it must not style every
        // link as if it were visited.
        var root = Load("a:visited { color: red }", "<p><a id='a' href='http://test/x'>link</a></p>");
        True(Color(ById(root, "a")) != new SKColor(0xff, 0, 0), $"a:visited applied: {Color(ById(root, "a"))}");
    }

    [Test]
    public static void DynamicRules_MergeBySpecificityNotSourceOrder()
    {
        var root = Load("a.test:hover { color: green } a:hover { color: red }",
            "<p><a id='a' class='test' href='http://test/x' style='display:inline-block;height:30px'>link</a></p>");
        var a = ById(root, "a");
        HoverOver(root, a);
        True(Color(a) == Green, $"the more specific a.test:hover must win, got {Color(a)}");
    }

    [Test]
    public static void StyleAttribute_BeatsANormalDynamicRule()
    {
        var root = Load("a:hover { color: green } b:hover { color: green !important }",
            "<p><a id='a' href='http://test/x' style='color: blue; display:inline-block; height:30px'>link</a> " +
            "<b id='b' style='color: blue; display:inline-block; height:30px'>bold</b></p>");
        var a = ById(root, "a");
        HoverOver(root, a);
        True(Color(a) == Blue, $"the style attribute outranks a normal :hover declaration, got {Color(a)}");
        var b = ById(root, "b");
        HoverOver(root, b);
        True(Color(b) == Green, $"an !important :hover declaration outranks a normal style attribute, got {Color(b)}");
    }

    [Test]
    public static void Lang_IgnoresANoNamespaceXmlLangInHtml()
    {
        // xml-lang-selectors-002: in an HTML document 'xml:lang' is an ordinary attribute in
        // no namespace and sets no language, so ':lang(ch)' must not match; 'lang' does.
        var root = Load(":lang(ch) { background: red }",
            "<p id='x' xml:lang='ch'>no red</p><p id='l' lang='ch'>red</p>");
        True(!(ById(root, "x").TryResolveStyle("background-color", out var x) && IsRed(x)),
            $"xml:lang in HTML set a language: background-color '{x}'");
        True(ById(root, "l").TryResolveStyle("background-color", out var l) && IsRed(l),
            $"lang='ch' must match :lang(ch): background-color '{l}'");
    }

    private static bool IsRed(string value) =>
        value.Trim() == "red" || value.Replace(" ", "").StartsWith("rgba(255,0,0,", StringComparison.Ordinal) ||
        value.Replace(" ", "") == "rgb(255,0,0)";

    [Test]
    public static void Tab_FollowsTheSequentialFocusNavigationOrder()
    {
        // HTML 6.6.3: positive tabindex ascending, then tabindex 0 and natively focusable
        // elements in tree order; negative tabindex, disabled controls and unrendered
        // elements are skipped.
        var root = Load("", "<a id='l' href='http://test/x'>link</a> <input id='i'> <span id='z' tabindex='0'>z</span> " +
            "<button id='off' disabled>off</button> <div id='neg' tabindex='-1'>neg</div> " +
            "<span id='two' tabindex='2'>2</span> <span id='one' tabindex='1'>1</span> <a id='hidden' href='x' style='display:none'>h</a>");
        var order = BrowserWindow.SequentialFocusOrder(root).Select(n => n.Attributes.GetValueOrDefault("id")).ToList();
        Equal("one,two,l,i,z", string.Join(",", order));
    }
}
