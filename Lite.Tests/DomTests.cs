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

    [Test]
    public static void WrapperIdentity_SameNodeYieldsSameWrapper()
    {
        // A DOM node must always surface as the SAME JS object so === and assert_equals(node, node)
        // work (a real WPT/JS expectation that the per-call wrapper construction used to break).
        var (_, _, engine) = NewPage();
        engine.Execute("document.body.innerHTML = '<p id=\"x\">hi</p>';");
        engine.Execute(@"
            var a = document.getElementById('x');
            var b = document.getElementById('x');
            var idSame = (a === b);
            var parentSame = (a.parentElement === document.body);
            var childSame = (document.body.children[0] === a);");
        True(engine.RawEngine.GetValue("idSame").ToObject() is true, "getElementById must return the same wrapper for the same node");
        True(engine.RawEngine.GetValue("parentSame").ToObject() is true, "parentElement must be identity-equal to document.body");
        True(engine.RawEngine.GetValue("childSame").ToObject() is true, "children[] must be identity-equal to getElementById");
    }

    [Test]
    public static void DetailsOpen_ReflectsAndFiresToggle()
    {
        var (_, _, engine) = NewPage();
        engine.Execute("document.body.innerHTML = '<details><summary>S</summary><p>body</p></details>';");
        engine.Execute(@"
            var d = document.querySelector('details');
            var initial = d.open;
            var fired = 0;
            d.addEventListener('toggle', function () { fired++; });
            d.open = true;
            var afterOpen = d.open;
            var hasAttr = d.hasAttribute('open');");
        engine.DrainTasks(); // toggle is delivered on a macrotask
        True(engine.RawEngine.GetValue("initial").ToObject() is false, "details.open should start false");
        True(engine.RawEngine.GetValue("afterOpen").ToObject() is true, "details.open should be true after setting");
        True(engine.RawEngine.GetValue("hasAttr").ToObject() is true, "setting open should add the open attribute");
        True(System.Convert.ToInt32(engine.RawEngine.GetValue("fired").ToObject()) == 1, "toggle event should fire once on change");
    }

    [Test]
    public static void Dialog_ShowCloseAndReturnValue()
    {
        var (_, _, engine) = NewPage();
        engine.Execute("document.body.innerHTML = '<dialog><p>hi</p></dialog>';");
        engine.Execute(@"
            var dlg = document.querySelector('dialog');
            var open0 = dlg.open;
            dlg.show();
            var open1 = dlg.open;
            var closed = 0;
            dlg.addEventListener('close', function () { closed++; });
            dlg.close('accepted');
            var open2 = dlg.open;
            var rv = dlg.returnValue;");
        engine.DrainTasks(); // close event delivered on a macrotask
        True(engine.RawEngine.GetValue("open0").ToObject() is false, "dialog starts closed");
        True(engine.RawEngine.GetValue("open1").ToObject() is true, "show() opens the dialog");
        True(engine.RawEngine.GetValue("open2").ToObject() is false, "close() closes the dialog");
        True((string?)engine.RawEngine.GetValue("rv").ToObject() == "accepted", "close(v) sets returnValue");
        True(System.Convert.ToInt32(engine.RawEngine.GetValue("closed").ToObject()) == 1, "close() fires a close event");
    }

    [Test]
    public static void Template_ContentIsInertFragment()
    {
        var (_, _, engine) = NewPage();
        engine.Execute("document.body.innerHTML = '<template id=\"t\"><p class=\"x\">hi</p><span>yo</span></template>';");
        engine.Execute(@"
            var t = document.getElementById('t');
            var ownChildren = t.children.length;        // template's own children are empty
            var frag = t.content;
            var fragType = frag.nodeType;               // 11 = DOCUMENT_FRAGMENT_NODE
            var fragChildren = frag.children.length;
            var pText = frag.querySelector('.x').textContent;
            var bodyText = document.body.textContent;   // template content must not render");
        True(System.Convert.ToInt32(engine.RawEngine.GetValue("ownChildren").ToObject()) == 0,
            "a <template>'s own children are empty (content lives in the fragment)");
        True(System.Convert.ToInt32(engine.RawEngine.GetValue("fragType").ToObject()) == 11,
            "template.content is a DocumentFragment (nodeType 11)");
        True(System.Convert.ToInt32(engine.RawEngine.GetValue("fragChildren").ToObject()) == 2,
            "template.content holds the two parsed children");
        True((string?)engine.RawEngine.GetValue("pText").ToObject() == "hi",
            "querySelector inside template.content works");
        True(!((string?)engine.RawEngine.GetValue("bodyText").ToObject() ?? "").Contains("hi"),
            "template content must not appear in the rendered document");
    }

    [Test]
    public static void Picture_SelectsMatchingSourceForImg()
    {
        var (_, _, engine) = NewPage();
        // Picture 1: first source is skipped (non-image type), second matches → wins.
        // Picture 2: only source never matches → falls back to the <img>'s own src.
        engine.Execute(@"
            document.body.innerHTML =
                '<picture>' +
                  '<source srcset=""bad.png"" type=""application/json"">' +
                  '<source srcset=""large.png"" media=""(min-width: 1px)"">' +
                  '<img src=""fallback.png"">' +
                '</picture>' +
                '<picture>' +
                  '<source srcset=""none.png"" media=""(max-width: 1px)"">' +
                  '<img src=""fb2.png"">' +
                '</picture>';
            var imgs = document.querySelectorAll('img');
            var chosen = imgs[0].currentSrc;
            var chosenSrc = imgs[0].src;
            var fallback = imgs[1].currentSrc;");
        Equal("large.png", (string?)engine.RawEngine.GetValue("chosen").ToObject());
        Equal("large.png", (string?)engine.RawEngine.GetValue("chosenSrc").ToObject());
        Equal("fb2.png", (string?)engine.RawEngine.GetValue("fallback").ToObject());
    }

    private static object? Val(JsEngine e, string name) => e.RawEngine.GetValue(name).ToObject();

    [Test]
    public static void ResizeObserver_FiresInitialAndOnResize()
    {
        var (root, body, engine) = NewPage();
        body.StyleOverrides["display"] = "block";
        engine.Execute(@"
            var d = document.createElement('div');
            d.style.display = 'block'; d.style.width = '120px'; d.style.height = '30px';
            document.body.appendChild(d);
            globalThis.count = 0; globalThis.first = 0; globalThis.last = 0;
            var ro = new ResizeObserver(function (entries) {
                globalThis.count++;
                globalThis.last = entries[0].contentRect.width;
                if (globalThis.count === 1) globalThis.first = entries[0].contentRect.width;
            });
            ro.observe(d);
            globalThis.__d = d;");
        engine.DrainTasks();                               // initial observation
        engine.Execute("__d.style.width = '200px';");
        engine.DrainTasks();                               // size changed → re-deliver
        Equal(2, System.Convert.ToInt32(Val(engine, "count")));
        True(System.Math.Abs(System.Convert.ToDouble(Val(engine, "first")) - 120) < 1, "initial contentRect.width 120");
        True(System.Math.Abs(System.Convert.ToDouble(Val(engine, "last")) - 200) < 1, "resized contentRect.width 200");
    }

    [Test]
    public static void IntersectionObserver_ReportsViewportIntersection()
    {
        var (root, body, engine) = NewPage();
        body.StyleOverrides["display"] = "block";
        engine.Execute(@"
            var d = document.createElement('div');
            d.style.display = 'block'; d.style.width = '50px'; d.style.height = '50px';
            document.body.appendChild(d);
            globalThis.hits = 0; globalThis.intersecting = null; globalThis.ratio = -1;
            var io = new IntersectionObserver(function (entries) {
                globalThis.hits++;
                globalThis.intersecting = entries[0].isIntersecting;
                globalThis.ratio = entries[0].intersectionRatio;
            });
            io.observe(d);");
        engine.DrainTasks();
        True(System.Convert.ToInt32(Val(engine, "hits")) >= 1, "intersection callback should fire once");
        True(Val(engine, "intersecting") is true, "a 50x50 box at the top-left is intersecting the viewport");
        True(System.Math.Abs(System.Convert.ToDouble(Val(engine, "ratio")) - 1.0) < 0.01, "fully visible → ratio 1");
    }

    [Test]
    public static void WheelAndPointerEvents_ConstructWithProperties()
    {
        var (_, _, engine) = NewPage();
        engine.Execute(@"
            var w = new WheelEvent('wheel', { deltaY: 120, deltaX: 5 });
            var wType = w.type, wY = w.deltaY, wX = w.deltaX;
            var p = new PointerEvent('pointerdown', { pointerId: 7, pointerType: 'pen', isPrimary: true, clientX: 3 });
            var pType = p.type, pid = p.pointerId, pkind = p.pointerType, px = p.clientX;");
        Equal("wheel", (string?)Val(engine, "wType"));
        True(System.Math.Abs(System.Convert.ToDouble(Val(engine, "wY")) - 120) < 0.01, "deltaY round-trips");
        True(System.Math.Abs(System.Convert.ToDouble(Val(engine, "wX")) - 5) < 0.01, "deltaX round-trips");
        Equal("pointerdown", (string?)Val(engine, "pType"));
        Equal(7, System.Convert.ToInt32(Val(engine, "pid")));
        Equal("pen", (string?)Val(engine, "pkind"));
    }

    [Test]
    public static void Attr_ValueMutationIsObservableAndRecascades()
    {
        var (_, _, engine) = NewPage();
        engine.Execute(@"
            document.body.innerHTML = '<p id=""t"" class=""old"">hi</p>';
            var p = document.getElementById('t');
            var records = 0;
            var mo = new MutationObserver(function (recs) { records += recs.length; });
            mo.observe(p, { attributes: true });
            var attr = p.getAttributeNode('class');
            attr.value = 'fresh';
            var classNow = p.className;
            var attrNow = p.getAttribute('class');");
        engine.DrainTasks(); // mutation records delivered at the checkpoint
        Equal("fresh", (string?)Val(engine, "classNow"));
        Equal("fresh", (string?)Val(engine, "attrNow"));
        True(System.Convert.ToInt32(Val(engine, "records")) >= 1, "mutating Attr.value queues a MutationRecord");
    }

    [Test]
    public static void CreateAttribute_SetAttributeNode_RoundTrips()
    {
        var (_, _, engine) = NewPage();
        engine.Execute(@"
            var el = document.createElement('div');
            var a = document.createAttribute('data-x');
            a.value = '42';
            var detachedOwner = a.ownerElement;     // null while detached
            el.setAttributeNode(a);
            var got = el.getAttribute('data-x');
            el.attributes.removeNamedItem('data-x');
            var afterRemove = el.getAttribute('data-x');");
        Equal("42", (string?)Val(engine, "got"));
        True(Val(engine, "detachedOwner") is null, "a detached Attr has no ownerElement");
        True(Val(engine, "afterRemove") is null, "removeNamedItem deletes the attribute");
    }
}
