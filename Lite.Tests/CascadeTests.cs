using Lite;
using Lite.Extensions;
using Lite.Layout;
using Lite.Models;
using Lite.Scripting;
using Lite.Scripting.Dom;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>Item 4 — CSS2 cascade correctness: specificity, !important, inheritance, :link.</summary>
public static class CascadeTests
{
    private static (LayoutNode root, LayoutNode body, JsEngine engine) NewPage()
    {
        var sample = Parser.ParseFragment("<span></span>")[0];
        var root = new LayoutNode(null, "HTML", "", sample.Style);
        var body = new LayoutNode(null, "BODY", "", sample.Style);
        root.AddChild(body);
        return (root, body, JsEngine.Create(root, documentState: new DocumentState(Parser.Document, "http://test/", "http://test/", new List<Parser.CssRule>())));
    }

    private static IDisposable Rules(JsEngine engine, params Parser.CssRule[] rules)
    {
        var target = (List<Parser.CssRule>)engine.DocumentState.StyleRules;
        target.AddRange(rules);
        return new Cleanup(target, rules);
    }

    private sealed class Cleanup(List<Parser.CssRule> target, Parser.CssRule[] rules) : IDisposable
    {
        public void Dispose() { foreach (var r in rules) target.Remove(r); }
    }

    private static Parser.CssRule Rule(string selector, (string, string)[] props, string[]? important = null)
    {
        var dict = new Dictionary<string, string>();
        foreach (var (k, v) in props) dict[k] = v;
        return new Parser.CssRule(selector, Parser.ComputeSpecificity(selector), Parser.CssRules.Count,
            dict, new HashSet<string>(important ?? Array.Empty<string>()));
    }

    [Test]
    public static void Specificity_IdBeatsClass()
    {
        Equal(true, Parser.ComputeSpecificity("#x") > Parser.ComputeSpecificity(".x"));
        Equal(true, Parser.ComputeSpecificity(".x") > Parser.ComputeSpecificity("div"));
        Equal(true, Parser.ComputeSpecificity("div.x#y") > Parser.ComputeSpecificity("div.x"));
    }

    [Test]
    public static void Cascade_HigherSpecificityWins()
    {
        var (_, body, engine) = NewPage();
        // Lower specificity declared LATER must still lose to higher specificity.
        using (Rules(engine,
            Rule("#target", new[] { ("color", "green") }),
            Rule(".cls", new[] { ("color", "red") })))
        {
            engine.Execute("var d=document.createElement('div'); d.id='target'; d.className='cls'; document.body.appendChild(d);");
            var div = body.Children.First(c => c.TagName == "DIV");
            Equal("green", div.StyleOverrides.GetValueOrDefault("color"));
        }
    }

    [Test]
    public static void Cascade_ImportantBeatsHigherSpecificity()
    {
        var (_, body, engine) = NewPage();
        using (Rules(engine,
            Rule("#target", new[] { ("color", "green") }),
            Rule(".cls", new[] { ("color", "red") }, important: new[] { "color" })))
        {
            engine.Execute("var d=document.createElement('div'); d.id='target'; d.className='cls'; document.body.appendChild(d);");
            var div = body.Children.First(c => c.TagName == "DIV");
            // !important on the low-specificity .cls rule must beat the #target rule.
            Equal("red", div.StyleOverrides.GetValueOrDefault("color"));
        }
    }

    [Test]
    public static void Cascade_InlineBeatsNormalButLosesToImportant()
    {
        var (_, body, engine) = NewPage();
        using (Rules(engine, Rule(".cls", new[] { ("color", "red") })))
        {
            engine.Execute("var d=document.createElement('div'); d.className='cls'; d.style.color='blue'; document.body.appendChild(d);");
            var div = body.Children.First(c => c.TagName == "DIV");
            Equal("blue", div.StyleOverrides.GetValueOrDefault("color")); // inline beats normal author rule
        }

        var (_, body2, engine2) = NewPage();
        using (Rules(engine2, Rule(".cls", new[] { ("color", "red") }, important: new[] { "color" })))
        {
            engine2.Execute("var d=document.createElement('div'); d.className='cls'; d.style.color='blue'; document.body.appendChild(d);");
            var div = body2.Children.First(c => c.TagName == "DIV");
            Equal("red", div.StyleOverrides.GetValueOrDefault("color")); // !important beats inline
        }
    }

    [Test]
    public static void Inheritance_ColorFlowsToCreatedChild()
    {
        var (_, body, engine) = NewPage();
        using (Rules(engine, Rule(".parent", new[] { ("color", "purple") })))
        {
            engine.Execute(@"
                var p = document.createElement('div'); p.className = 'parent';
                var c = document.createElement('span');
                p.appendChild(c);
                document.body.appendChild(p);
            ");
            var parent = body.Children.First(c => c.TagName == "DIV");
            var child = parent.Children.First(c => c.TagName == "SPAN");
            Equal("purple", child.StyleOverrides.GetValueOrDefault("color"));
        }
    }

    [Test]
    public static void Inherit_MarginTakesParentComputedLengthNotRawEm()
    {
        // margin-em-inherit-001 (§6.2.1 + §8.3): what 'margin: inherit' passes on is the
        // parent's COMPUTED value — '2em' at the parent's 28px font is 56px — not the raw
        // '2em' re-resolved at the child's own 40px font (which would be 80px). A percentage
        // stays in the computed value verbatim: the child re-resolves it against its own
        // containing block (15% of the parent's 200px content width is 30, not the parent's
        // 15% of 800 = 120, and not 15% of the child's font size).
        var emChild = Block(new() { ["width"] = "100px", ["height"] = "5px",
                                    ["font-size"] = "40px", ["margin-top"] = "inherit" });
        var emParent = Block(new() { ["width"] = "200px", ["height"] = "10px",
                                     ["font-size"] = "28px", ["margin-top"] = "2em" }, emChild);
        var pctChild = Block(new() { ["width"] = "100px", ["height"] = "5px",
                                     ["margin-top"] = "inherit" });
        var pctParent = Block(new() { ["width"] = "200px", ["height"] = "10px",
                                      ["margin-top"] = "15%" }, pctChild);
        LayoutTree(Block(new(), emParent, pctParent));
        True(Math.Abs(emParent.Box.Margin.Top - 56f) < 0.5f,
            $"the parent's 2em computes to 56px at its 28px font, got {emParent.Box.Margin.Top}");
        True(Math.Abs(emChild.Box.Margin.Top - 56f) < 0.5f,
            $"the child's 'margin-top: inherit' takes the parent's computed 56px, got {emChild.Box.Margin.Top}");
        True(Math.Abs(pctParent.Box.Margin.Top - 120f) < 0.5f,
            $"the parent's 15% resolves against the 800px containing block (120), got {pctParent.Box.Margin.Top}");
        True(Math.Abs(pctChild.Box.Margin.Top - 30f) < 0.5f,
            $"the inherited percentage re-resolves at the child's containing block (15% of 200 = 30), got {pctChild.Box.Margin.Top}");
    }

    // Mirror of LayoutTests.Block: CascadeTests has no block helper of its own.
    private static readonly ICssStyleDeclarationCache _styleCache = new();
    private sealed class ICssStyleDeclarationCache
    {
        public AngleSharp.Css.Dom.ICssStyleDeclaration Style { get; } =
            Parser.ParseFragment("<div></div>")[0].Style;
    }
    private static LayoutNode Block(Dictionary<string, string> styles, params LayoutNode[] children)
    {
        var node = new LayoutNode(null, "DIV", "", _styleCache.Style);
        node.StyleOverrides["display"] = "block";
        foreach (var side in new[] { "top", "right", "bottom", "left" })
        {
            node.StyleOverrides[$"margin-{side}"] = "0";
            node.StyleOverrides[$"padding-{side}"] = "0";
            node.StyleOverrides[$"border-{side}-width"] = "0";
        }
        foreach (var (k, v) in styles) node.StyleOverrides[k] = v;
        foreach (var c in children) node.AddChild(c);
        return node;
    }
    private static LayoutNode LayoutTree(LayoutNode content, int vw = 800, int vh = 600)
    {
        var root = new LayoutNode(null, "HTML", "", _styleCache.Style);
        var body = new LayoutNode(null, "BODY", "", _styleCache.Style);
        root.StyleOverrides["display"] = "block";
        body.StyleOverrides["display"] = "block";
        root.AddChild(body);
        body.AddChild(content);
        BoxEngine.Layout(root, vw, vh);
        return root;
    }

    [Test]
    public static void LinkPseudoClass_MatchesAnchorWithHref()
    {
        var a = Parser.ParseFragment("<a href=\"/x\">link</a>")[0];
        var plain = Parser.ParseFragment("<a>no href</a>")[0];
        True(SelectorEngine.Matches(a, ":link"), "anchor with href should match :link");
        True(!SelectorEngine.Matches(plain, ":link"), "anchor without href should not match :link");
        True(!SelectorEngine.Matches(a, ":visited"), ":visited should never match (no history)");
    }

    [Test]
    public static void BackgroundShorthand_ExpandsInAnyOrderAndResetsWhatItOmits()
    {
        // The shorthand is expanded from the declared text, classifying tokens by shape so their
        // order does not matter, and resets the components it does not mention — AngleSharp
        // reports an unset longhand of a shorthand as the literal "initial", which the painter
        // then read as "no repeat at all".
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><head><style>" +
            "#b { background: green url('x.png') repeat-x; }" +
            "#c { background: green; }" +
            "</style></head><body><div id='b'></div><div id='c'></div></body></html>",
            isSrcdoc: true, "http://test/", 800, 600);

        LayoutNode? F(LayoutNode n, string id) => n.Id == id ? n : n.Children.Select(c => F(c, id)).FirstOrDefault(r => r != null);
        var b = F(page.Root, "b")!;
        var c = F(page.Root, "c")!;

        Equal("repeat-x", b.GetBackgroundRepeat());
        True(b.GetBackgroundColor().Green > 100 && b.GetBackgroundColor().Red < 100,
            $"the colour survives alongside an image and a repeat, got {b.GetBackgroundColor()}");

        // Nothing else named, so the omitted components take their initial values.
        Equal("repeat", c.GetBackgroundRepeat());
        Equal("0%", c.GetBackgroundPosition().X);
        Equal("0%", c.GetBackgroundPosition().Y);
    }

    [Test]
    public static void BackgroundShorthand_WithKeywordPositionSurvivesAngleSharpDroppingIt()
    {
        // AngleSharp.Css refuses a `background` whose position is a bare keyword and drops the
        // whole declaration, leaving the element with a lower-specificity rule's colour. The
        // shorthand is read back out of the stylesheet source for exactly those rules — but only
        // when AngleSharp kept nothing from the background family, so a longhand written AFTER the
        // shorthand still wins (§14.2 ordering).
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><head><style>" +
            "div { background: yellow; }" +
            "#one { background: red url('x.png') right repeat-y; }" +
            "#two { background: bottom green; }" +
            "#three { background: repeat-x; background-image: url('y.png'); }" +
            "</style></head><body>" +
            "<div id='one'></div><div id='two'></div><div id='three'></div>" +
            "</body></html>",
            isSrcdoc: true, "http://test/", 800, 600);

        LayoutNode? F(LayoutNode n, string id) =>
            n.Id == id ? n : n.Children.Select(c => F(c, id)).FirstOrDefault(r => r != null);

        var one = F(page.Root, "one")!;
        Console.WriteLine($"[dbg] one style bg: '{one.Style.GetPropertyValueSafe("background-color")}'");
        True(one.GetBackgroundColor().Red > 100 && one.GetBackgroundColor().Green < 100,
            $"`background: red url(...) right repeat-y` should win over `div {{ background: yellow }}`, " +
            $"got {one.GetBackgroundColor()}");
        Equal("repeat-y", one.GetBackgroundRepeat());
        Equal("right", one.GetBackgroundPosition().X);

        var two = F(page.Root, "two")!;
        True(two.GetBackgroundColor().Green > 100 && two.GetBackgroundColor().Red < 100,
            $"`background: bottom green` should set green, got {two.GetBackgroundColor()}");

        // AngleSharp parses this one, so its longhand ordering must be left alone: the explicit
        // background-image comes after the shorthand and survives it.
        var three = F(page.Root, "three")!;
        var backgroundImage = three.GetBackgroundImage();
        True(backgroundImage?.Contains("y.png", StringComparison.Ordinal) == true,
            $"a longhand after the shorthand must win, got '{backgroundImage}'");
    }

    [Test]
    public static void BackgroundRescue_IgnoresMalformedAndAmbiguousSource()
    {
        // The source scan is a mini-parser, so it has to make the same calls CSS 2.1 §4.2 does:
        //  * a declaration containing a BLOCK is malformed and dropped whole, so the `background`
        //    nested inside one is not a declaration of the outer rule;
        //  * `\{` is an escaped character, not the start of a block, so the rule that follows is
        //    swallowed by the malformed selector rather than standing on its own;
        //  * a selector that declares a background more than once is left to AngleSharp — source
        //    order between them is not recoverable from a CSSOM rule on its own.
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><head><style>" +
            "#a { background: green; }" +
            "#a { nested { background: red; }: not-a-declaration; }" +
            "#b { background: green; }" +
            "#b \\{ background: red; \\}" +
            "#b { background: red; }" +
            "</style></head><body><div id='a'></div><div id='b'></div></body></html>",
            isSrcdoc: true, "http://test/", 800, 600);

        LayoutNode? F(LayoutNode n, string id) =>
            n.Id == id ? n : n.Children.Select(c => F(c, id)).FirstOrDefault(r => r != null);

        foreach (var id in new[] { "a", "b" })
        {
            var node = F(page.Root, id)!;
            var bg = node.GetBackgroundColor();
            True(bg.Green > 100 && bg.Red < 100,
                $"#{id} should keep its green background — the red one is malformed, got {bg}");
        }
    }
}
