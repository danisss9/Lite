using Lite;
using Lite.Models;
using Lite.Scripting;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>Phase 4 — attribute API completeness: attributes NamedNodeMap, Attr nodes,
/// getAttributeNames, hasAttributes, toggleAttribute, getElementsByName.</summary>
public static class AttributeApiTests
{
    private static JsEngine NewEngine()
    {
        var sample = Parser.ParseFragment("<span></span>")[0];
        var root = new LayoutNode(null, "HTML", "", sample.Style);
        root.AddChild(new LayoutNode(null, "BODY", "", sample.Style));
        return JsEngine.Create(root);
    }

    private static object? Global(JsEngine e, string name) => e.RawEngine.GetValue(name).ToObject();

    [Test]
    public static void AttributesCollection_LengthAndItems()
    {
        var e = NewEngine();
        e.Execute(@"
            var d = document.createElement('div');
            d.setAttribute('id', 'x');
            d.setAttribute('data-y', '2');
            globalThis.__len = d.attributes.length;
            globalThis.__name0 = d.attributes[0].name;
            globalThis.__namedVal = d.attributes.getNamedItem('data-y').value;
        ");
        Equal(2, Convert.ToInt32(Global(e, "__len")));
        Equal("id", Global(e, "__name0")?.ToString());
        Equal("2", Global(e, "__namedVal")?.ToString());
    }

    [Test]
    public static void GetAttributeNode_AndNames()
    {
        var e = NewEngine();
        e.Execute(@"
            var d = document.createElement('div');
            d.setAttribute('title', 'hello');
            globalThis.__nodeVal = d.getAttributeNode('title').value;
            globalThis.__names = d.getAttributeNames().join(',');
            globalThis.__has = d.hasAttributes();
        ");
        Equal("hello", Global(e, "__nodeVal")?.ToString());
        Equal("title", Global(e, "__names")?.ToString());
        Equal(true, Convert.ToBoolean(Global(e, "__has")));
    }

    [Test]
    public static void ToggleAttribute_FlipsAndForces()
    {
        var e = NewEngine();
        e.Execute(@"
            var d = document.createElement('div');
            globalThis.__a = d.toggleAttribute('hidden');     // add -> true
            globalThis.__b = d.toggleAttribute('hidden');     // remove -> false
            globalThis.__c = d.toggleAttribute('hidden', true);  // force on -> true
            globalThis.__has = d.hasAttribute('hidden');
        ");
        Equal(true, Convert.ToBoolean(Global(e, "__a")));
        Equal(false, Convert.ToBoolean(Global(e, "__b")));
        Equal(true, Convert.ToBoolean(Global(e, "__c")));
        Equal(true, Convert.ToBoolean(Global(e, "__has")));
    }

    [Test]
    public static void GetElementsByName_FindsMatches()
    {
        var e = NewEngine();
        e.Execute(@"
            var a = document.createElement('input'); a.setAttribute('name', 'q');
            var b = document.createElement('input'); b.setAttribute('name', 'q');
            var c = document.createElement('input'); c.setAttribute('name', 'other');
            document.body.appendChild(a); document.body.appendChild(b); document.body.appendChild(c);
            globalThis.__n = document.getElementsByName('q').length;
        ");
        Equal(2, Convert.ToInt32(Global(e, "__n")));
    }
}
