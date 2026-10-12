using Lite;
using Lite.Models;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>Void-returning operations exposed to script must produce undefined, not null
/// (WebIDL void). testharness rejects add_cleanup callbacks returning any value, so a null
/// return cascaded whole url-runner files to NOTRUN.</summary>
public static class QuickJsVoidReturnTests
{
    private static LayoutNode? Find(LayoutNode node, Func<LayoutNode, bool> match)
    {
        if (match(node)) return node;
        foreach (var child in node.Children)
            if (Find(child, match) is { } hit) return hit;
        return null;
    }

    private static string Probe(string script)
    {
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><head></head><body><div id=\"host\"></div>" +
            "<script>document.getElementById('host').textContent = String(" + script + ");</script></body></html>",
            isSrcdoc: true, "http://test/", 800, 600);
        var host = Find(page.Root, n => n.Attributes.GetValueOrDefault("id") == "host");
        True(host != null, "host div missing");
        return host!.DisplayText ?? string.Empty;
    }

    [Test]
    public static void VoidDelegate_ReturnsUndefined()
    {
        // URL.revokeObjectURL is a bound Action<string> (the delegate path).
        var html = Probe("(function(){ var url = URL.createObjectURL(new Blob(['x']));" +
            "return URL.revokeObjectURL(url); })()");
        Equal("undefined", html);
    }

    [Test]
    public static void VoidMethod_ReturnsUndefined()
    {
        // removeAttribute is a void wrapper method (the member-invocation path).
        var html = Probe("(function(){ var e = document.createElement('div');" +
            "e.setAttribute('a', 'b'); return e.removeAttribute('a'); })()");
        Equal("undefined", html);
    }
}
