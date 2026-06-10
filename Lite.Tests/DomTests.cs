using Lite;
using Lite.Models;
using Lite.Rendering;
using Lite.Scripting;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>Item 1 — live DOM: innerHTML parse/serialize, StyleResolver, mutation methods.</summary>
public static class DomTests
{
    /// <summary>Builds a minimal HTML/BODY LayoutNode tree and a JsEngine over it.</summary>
    private static (LayoutNode root, LayoutNode body, JsEngine engine) NewPage()
    {
        var sample = Parser.ParseFragment("<span></span>")[0];
        var style = sample.Style;
        var root = new LayoutNode(null, "HTML", "", style);
        var body = new LayoutNode(null, "BODY", "", style);
        root.AddChild(body);
        var engine = JsEngine.Create(root);
        return (root, body, engine);
    }

    [Test]
    public static void ParseFragment_BuildsElementTree()
    {
        var nodes = Parser.ParseFragment("<div class=\"a\"><p>hello</p><p>world</p></div>");
        Equal(1, nodes.Count);
        Equal("DIV", nodes[0].TagName);
        Equal("a", nodes[0].Attributes.GetValueOrDefault("class"));
        Equal(2, nodes[0].Children.Count);
        Equal("P", nodes[0].Children[0].TagName);
    }

    [Test]
    public static void Serialize_RoundTripsBasicMarkup()
    {
        var nodes = Parser.ParseFragment("<div id=\"x\"><p>hi</p></div>");
        var html = HtmlSerializer.SerializeOuter(nodes[0]);
        Contains("<div", html);
        Contains("id=\"x\"", html);
        Contains("<p>hi</p>", html);
        Contains("</div>", html);
    }

    [Test]
    public static void Serialize_VoidElementsHaveNoClosingTag()
    {
        var nodes = Parser.ParseFragment("<div><br><img src=\"a.png\"></div>");
        var html = HtmlSerializer.SerializeOuter(nodes[0]);
        Contains("<br>", html);
        Contains("<img", html);
        True(!html.Contains("</br>"), "void <br> must not have a closing tag");
        True(!html.Contains("</img>"), "void <img> must not have a closing tag");
    }

    [Test]
    public static void InnerHTML_SetterParsesChildren()
    {
        var (_, body, engine) = NewPage();
        engine.Execute("document.body.innerHTML = '<p class=\"x\">hi <strong>bold</strong></p>';");
        Equal(1, body.Children.Count);
        Equal("P", body.Children[0].TagName);
        var p = body.Children[0];
        // text node "hi " + <strong>
        True(p.Children.Any(c => c.TagName == "STRONG"), "expected a <strong> child");
    }

    [Test]
    public static void InnerHTML_GetterSerializes()
    {
        var (_, body, engine) = NewPage();
        engine.Execute("document.body.innerHTML = '<span>abc</span>';");
        engine.Execute("var __out = document.body.innerHTML;");
        var outVal = engine.RawEngine.GetValue("__out").ToString();
        Contains("<span>abc</span>", outVal);
    }

    [Test]
    public static void StyleResolver_AppliesStylesheetRuleToCreatedElement()
    {
        var (_, body, engine) = NewPage();
        Parser.CssRules.Add(new Parser.CssRule(".badge", Parser.ComputeSpecificity(".badge"), 0,
            new Dictionary<string, string> { { "color", "red" } }, new HashSet<string>()));
        try
        {
            engine.Execute("var d = document.createElement('div'); d.className = 'badge'; document.body.appendChild(d);");
            var div = body.Children.First(c => c.TagName == "DIV");
            Equal("red", div.StyleOverrides.GetValueOrDefault("color"));
        }
        finally
        {
            Parser.CssRules.RemoveAll(r => r.Selector == ".badge");
        }
    }

    [Test]
    public static void StyleResolver_DoesNotClobberInlineStyle()
    {
        var (_, body, engine) = NewPage();
        Parser.CssRules.Add(new Parser.CssRule(".badge", Parser.ComputeSpecificity(".badge"), 0,
            new Dictionary<string, string> { { "color", "red" } }, new HashSet<string>()));
        try
        {
            engine.Execute("var d = document.createElement('div'); d.className = 'badge'; d.style.color = 'green'; document.body.appendChild(d);");
            var div = body.Children.First(c => c.TagName == "DIV");
            Equal("green", div.StyleOverrides.GetValueOrDefault("color"));
        }
        finally
        {
            Parser.CssRules.RemoveAll(r => r.Selector == ".badge");
        }
    }

    [Test]
    public static void InsertAdjacentHTML_BeforeEndAppends()
    {
        var (_, body, engine) = NewPage();
        engine.Execute("document.body.innerHTML = '<ul></ul>';");
        engine.Execute("document.body.firstElementChild.insertAdjacentHTML('beforeend', '<li>one</li><li>two</li>');");
        var ul = body.Children.First(c => c.TagName == "UL");
        Equal(2, ul.Children.Count(c => c.TagName == "LI"));
    }

    [Test]
    public static void Remove_DetachesElement()
    {
        var (_, body, engine) = NewPage();
        engine.Execute("document.body.innerHTML = '<p id=\"gone\">x</p>';");
        engine.Execute("document.getElementById('gone').remove();");
        True(!body.Children.Any(c => c.TagName == "P"), "removed element should be gone");
    }
}
