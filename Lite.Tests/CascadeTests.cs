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
    public static void LinkedSheetKeepsItsSourceOrderBeforeALaterStyleElement()
    {
        // §6.4.1 orders declarations of one origin by source order. The eagerly fetched
        // <link rel=stylesheet> must stay at the link's position: appended to the end of <head>
        // its `.c { color: red }` beat the LATER inline `.c { color: green }` (c32-cascading-000).
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><head>" +
            "<link rel='stylesheet' href='data:text/css,.c%7Bcolor%3Ared%7D'>" +
            "<style>.c { color: green }</style>" +
            "</head><body><p class='c' id='c'>x</p></body></html>",
            isSrcdoc: true, "http://test/", 800, 600);
        LayoutNode? F(LayoutNode n, string id) =>
            n.Id == id ? n : n.Children.Select(c => F(c, id)).FirstOrDefault(r => r != null);
        True(F(page.Root, "c")!.GetColor() == new SkiaSharp.SKColor(0, 128, 0),
            $"the later inline sheet wins at equal specificity, got {F(page.Root, "c")!.GetColor()}");
    }

    [Test]
    public static void Lang_MatchesRangesDocumentLanguageAndReachesText()
    {
        // §5.11.4: ':lang(C)' takes the nearest lang/xml:lang; with none in scope the document
        // language (meta http-equiv="content-language" here) applies. The argument is a language
        // RANGE: ':lang(en)' matches en-US but ':lang(en-GB)' does not match a plain 'en'.
        // §6.2.1: the rule reaches the element through Lite's override channel, so its text and
        // its child elements must still see the computed color (the XML-served
        // lang-selector-004/lang-pseudoclass-002 twins painted the text and the <em> black).
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><head><meta http-equiv='content-language' content='fr'>" +
            "<style>div:lang(fr) { color: green } p:lang(en) { color: green } " +
            "span:lang(en-GB) { color: lime } p:lang(es) { color: white; background-color: green }" +
            "</style></head><body>" +
            "<div id='doc'>Document language<span id='docspan'>x</span></div>" +
            "<p id='widen' lang='en-US'>English range</p>" +
            "<p><span id='narrow' lang='en'>not en-GB</span></p>" +
            "<p id='es' lang='es'>Spanish <em id='em'>emphasis</em></p>" +
            "</body></html>", isSrcdoc: true, "http://test/", 800, 600);
        LayoutNode? F(LayoutNode n, string id) =>
            n.Id == id ? n : n.Children.Select(c => F(c, id)).FirstOrDefault(r => r != null);
        var green = new SkiaSharp.SKColor(0, 128, 0);
        var doc = F(page.Root, "doc")!;
        var docText = doc.Children.FirstOrDefault(c => c.TagName == "#text");
        True(doc.GetColor() == green, $"meta content-language 'fr' makes div:lang(fr) green, got {doc.GetColor()}");
        True(docText is not null && docText.GetColor() == green,
            $"the div's own text inherits the green override, got {docText?.GetColor()}");
        True(F(page.Root, "widen")!.GetColor() == green, $":lang(en) matches lang=en-US, got {F(page.Root, "widen")!.GetColor()}");
        True(F(page.Root, "narrow")!.GetColor() == SkiaSharp.SKColors.Black,
            $":lang(en-GB) must not match lang=en, got {F(page.Root, "narrow")!.GetColor()}");
        var es = F(page.Root, "es")!;
        True(es.GetColor() == SkiaSharp.SKColors.White, $"p:lang(es) sets white, got {es.GetColor()}");
        True(F(page.Root, "em")!.GetColor() == SkiaSharp.SKColors.White,
            $"a child element inherits the override-channel white, got {F(page.Root, "em")!.GetColor()}");
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
    public static void Inherit_ExTakesDeclaringElementXHeight()
    {
        // word-spacing-101 (§6.2.1, §4.3.2): an inherited 'ex' length computes at the
        // DECLARING element's x-height — Ahem's is 0.8em, so 12ex at 16px is 153.6px. The
        // conversion ran unscoped and fell back to 0.5em (96px); the child must read exactly
        // what the parent resolves its own 12ex to.
        var dir = Directory.GetCurrentDirectory();
        while (dir is not null && !File.Exists(Path.Combine(dir, "Lite.sln"))) dir = Path.GetDirectoryName(dir);
        FontRegistry.RegisterFile(Path.Combine(dir!, "Lite.Conformance", "vendor", "wpt", "fonts", "Ahem.ttf"));
        var child = Block(new() { ["width"] = "100px", ["height"] = "5px", ["font-family"] = "Ahem",
                                  ["font-size"] = "16px", ["margin-top"] = "inherit" });
        var parent = Block(new() { ["width"] = "200px", ["height"] = "10px", ["font-family"] = "Ahem",
                                   ["font-size"] = "16px", ["margin-top"] = "12ex" }, child);
        LayoutTree(Block(new(), parent));
        True(Math.Abs(parent.Box.Margin.Top - 153.6f) < 0.5f,
            $"the parent's 12ex in Ahem resolves to 153.6px, got {parent.Box.Margin.Top}");
        True(Math.Abs(child.Box.Margin.Top - parent.Box.Margin.Top) < 0.5f,
            $"the inherited 12ex takes the parent's computed {parent.Box.Margin.Top}px, got {child.Box.Margin.Top}");
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

    [Test]
    public static void Inherit_StylesheetMarginAndPaddingTakeParentComputedEm()
    {
        // §6.2.1 through the real cascade (margin-em-inherit-001 / padding-em-inherit-001):
        // AngleSharp replaces the child's 'inherit' with the parent's specified "2em", which the
        // child's own 40px font then scaled to 80px. The computed value is the parent's 56px.
        // A child that authors its own em margin keeps resolving it at its own font.
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><head><style>" +
            "body { margin: 0 } #parent { font-size: 28px; margin: 2em 3em 1em 4em; padding: 1em }" +
            "#child { font-size: 40px; margin: inherit; padding: inherit; height: 10px }" +
            "#own { font-size: 40px; margin: 1em 0 0 0; height: 10px }" +
            "</style></head><body><div id='parent'><div id='child'></div><div id='own'></div></div></body></html>",
            isSrcdoc: true, "http://test/", 800, 600);
        Drawer.Draw(800, 600, page.Root, new Viewport { ViewportHeight = 600 });

        LayoutNode? F(LayoutNode n, string id) =>
            n.Id == id ? n : n.Children.Select(c => F(c, id)).FirstOrDefault(r => r != null);
        var child = F(page.Root, "child")!;
        var own = F(page.Root, "own")!;
        True(Math.Abs(child.Box.Margin.Top - 56f) < 0.5f, $"margin-top inherits 2em at 28px = 56, got {child.Box.Margin.Top}");
        True(Math.Abs(child.Box.Margin.Right - 84f) < 0.5f, $"margin-right inherits 3em at 28px = 84, got {child.Box.Margin.Right}");
        True(Math.Abs(child.Box.Margin.Left - 112f) < 0.5f, $"margin-left inherits 4em at 28px = 112, got {child.Box.Margin.Left}");
        True(Math.Abs(child.Box.Padding.Top - 28f) < 0.5f, $"padding-top inherits 1em at 28px = 28, got {child.Box.Padding.Top}");
        True(Math.Abs(own.Box.Margin.Top - 40f) < 0.5f, $"an authored 1em resolves at the element's own 40px, got {own.Box.Margin.Top}");
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

    [Test]
    public static void PseudoElementCascade_RespectsSpecificityAndDescendantUniversal()
    {
        // CSS 2.1 §6.4.3: single-colon pseudo-elements count in c (not b), and pseudo-element
        // rules cascade by specificity rather than last-rule-wins (specificity-005/008/009/011).
        True(Parser.ComputeSpecificity("div:first-line") > Parser.ComputeSpecificity(":first-line"),
            "0,0,2 > 0,0,1");
        True(Parser.ComputeSpecificity("div:first-child:first-line") > Parser.ComputeSpecificity("div:first-line"),
            "0,1,2 > 0,0,2");
        True(Parser.ComputeSpecificity("html > body > *:first-line") > Parser.ComputeSpecificity("div:first-line"),
            "0,0,3 > 0,0,2");
        Equal(Parser.ComputeSpecificity("p:after"), Parser.ComputeSpecificity("div :after"));
        Equal("div *", Parser.ExtractPseudoElementBaseSelector("div :after"));
        Equal("body div *", Parser.ExtractPseudoElementBaseSelector("body div :after"));

        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><head><style>" +
            "div:first-line { color: green; }" +
            ":first-line { color: red; }" +
            "p:after { content: 'FAILED'; }" +
            "div :after { content: 'PASSED'; }" +
            "</style></head><body><div><p id='p'>Hi</p></div></body></html>",
            isSrcdoc: true, "http://test/", 800, 600);

        LayoutNode? F(LayoutNode n, string tag, string? id = null) =>
            n.TagName == tag && (id is null || n.Id == id) ? n : n.Children.Select(c => F(c, tag, id)).FirstOrDefault(r => r != null);

        var div = F(page.Root, "DIV")!;
        var p = F(page.Root, "P", "p")!;
        Equal("rgba(0, 128, 0, 1)", div.FirstLineStyles!["color"]);
        True(div.AfterStyles is null, "div :after must not match div itself");
        Equal("\"PASSED\"", p.AfterStyles!["content"]);
    }
}
