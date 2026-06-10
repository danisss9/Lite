using Lite;
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
        return (root, body, JsEngine.Create(root));
    }

    private static IDisposable Rules(params Parser.CssRule[] rules)
    {
        foreach (var r in rules) Parser.CssRules.Add(r);
        return new Cleanup(rules);
    }

    private sealed class Cleanup(Parser.CssRule[] rules) : IDisposable
    {
        public void Dispose() { foreach (var r in rules) Parser.CssRules.Remove(r); }
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
        using (Rules(
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
        using (Rules(
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
        using (Rules(Rule(".cls", new[] { ("color", "red") })))
        {
            engine.Execute("var d=document.createElement('div'); d.className='cls'; d.style.color='blue'; document.body.appendChild(d);");
            var div = body.Children.First(c => c.TagName == "DIV");
            Equal("blue", div.StyleOverrides.GetValueOrDefault("color")); // inline beats normal author rule
        }

        var (_, body2, engine2) = NewPage();
        using (Rules(Rule(".cls", new[] { ("color", "red") }, important: new[] { "color" })))
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
        using (Rules(Rule(".parent", new[] { ("color", "purple") })))
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
    public static void LinkPseudoClass_MatchesAnchorWithHref()
    {
        var a = Parser.ParseFragment("<a href=\"/x\">link</a>")[0];
        var plain = Parser.ParseFragment("<a>no href</a>")[0];
        True(SelectorEngine.Matches(a, ":link"), "anchor with href should match :link");
        True(!SelectorEngine.Matches(plain, ":link"), "anchor without href should not match :link");
        True(!SelectorEngine.Matches(a, ":visited"), ":visited should never match (no history)");
    }
}
