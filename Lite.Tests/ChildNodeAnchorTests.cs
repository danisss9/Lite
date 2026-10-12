using Lite;
using Lite.Models;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>ChildNode before/after/replaceWith anchoring (DOM §4.2.8) over the engine's folded
/// text representation. Each case mirrors the shape of the WPT dom/nodes/ChildNode-*.html
/// subtests: a CharacterData anchor is appended into a fresh parent and a relative insertion
/// must land beside it — including when the projection folds the anchor into its parent's own
/// text and exposes no LayoutNode tree position for it.</summary>
public static class ChildNodeAnchorTests
{
    private static LayoutNode? Find(LayoutNode node, Func<LayoutNode, bool> match)
    {
        if (match(node)) return node;
        foreach (var child in node.Children)
            if (Find(child, match) is { } hit) return hit;
        return null;
    }

    private static LayoutNode? FindById(LayoutNode node, string id)
        => Find(node, n => n.Attributes.GetValueOrDefault("id") == id);

    /// <summary>Runs a script scenario and relays an expression's value through a host div so
    /// the assertion reads the engine's own innerHTML serialization.</summary>
    private static string Probe(string script)
    {
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><head></head><body><div id=\"host\"></div>" +
            "<script>document.getElementById('host').textContent = String(" + script + ");</script></body></html>",
            isSrcdoc: true, "http://test/", 800, 600);
        var host = FindById(page.Root, "host");
        True(host != null, "host div missing");
        return host!.DisplayText ?? string.Empty;
    }

    [Test]
    public static void TextAfter_FoldedAnchor_InsertsAfterSelf()
    {
        var html = Probe("(function(){ var p = document.createElement('div');" +
            "var t = document.createTextNode('test'); p.appendChild(t); t.after(null);" +
            "return p.innerHTML; })()");
        Equal("testnull", html);
    }

    [Test]
    public static void TextBefore_ElementAndText_InsertsBeforeSelfInOrder()
    {
        var html = Probe("(function(){ var p = document.createElement('div');" +
            "var t = document.createTextNode('test'); p.appendChild(t);" +
            "t.before(document.createElement('x'), 'text');" +
            "return p.innerHTML; })()");
        Equal("<x></x>texttest", html);
    }

    [Test]
    public static void TextBefore_ContextObjectItself_ReinsertsSelfAfterArgs()
    {
        var html = Probe("(function(){ var p = document.createElement('div');" +
            "var t = document.createTextNode('test'); p.appendChild(t);" +
            "t.before('text', t);" +
            "return p.innerHTML; })()");
        Equal("texttest", html);
    }

    [Test]
    public static void TextReplaceWith_ParentlessChild_IsANoOp()
    {
        var html = Probe("(function(){ var p = document.createElement('div');" +
            "var x = document.createElement('x'); var y = document.createElement('y');" +
            "p.appendChild(x); p.appendChild(y);" +
            "document.createTextNode('test').replaceWith(x, y);" +
            "return p.innerHTML; })()");
        Equal("<x></x><y></y>", html);
    }

    [Test]
    public static void CommentAfter_FoldedAnchor_InsertsAfterSelf()
    {
        var html = Probe("(function(){ var p = document.createElement('div');" +
            "var c = document.createComment('test'); p.appendChild(c); c.after('x');" +
            "return p.innerHTML; })()");
        Equal("<!--test-->x", html);
    }
}
