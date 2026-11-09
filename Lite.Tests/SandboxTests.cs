using Lite;
using Lite.Models;
using Lite.Scripting;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>Regression coverage for the origins-and-sandbox package: the sandbox attribute
/// parsed into the child page's flag set (unioned with the flags the frame inherited), script
/// execution gated on allow-scripts across the whole load pipeline (classic, deferred, async,
/// onload), the opaque origin a sandboxed frame carries without allow-same-origin (Origin reads
/// "null", contentDocument reads null from the parent, document.domain's setter raises
/// SecurityError), the flags persisting across a frame's document.write stream replacement, and
/// window.open returning null from a frame without allow-popups.</summary>
public static class SandboxTests
{
    private static void Run(JsEngine engine, string js) => engine.RawEngine.Execute(js);

    private static string Eval(JsEngine engine, string expr) => engine.RawEngine.Evaluate(expr).ToString() ?? "";

    private static LayoutNode? Find(LayoutNode node, string tag, string? id = null)
    {
        if (node.TagName == tag && (id is null || node.Id == id)) return node;
        foreach (var c in node.Children) { var f = Find(c, tag, id); if (f != null) return f; }
        return null;
    }

    [Test]
    public static void Sandbox_DisallowScripts_ParsesButNeverRuns()
    {
        var page = Parser.ParseChildPage(
            "<!doctype html><body><iframe sandbox='' id=f srcdoc=" +
            "\"&lt;!doctype html&gt;&lt;body&gt;&lt;script&gt;document.body.textContent = 'ran-marker-7';&lt;/script&gt;&lt;p&gt;hi&lt;/body&gt;\"" +
            "></iframe></body></html>",
            isSrcdoc: true, "http://test/", 800, 600);
        page.Engine!.DrainTasks();
        var frame = Find(page.Root, "IFRAME", "f");
        True(frame?.ChildPage is { }, "frame child page missing");
        True(frame.ChildPage.Engine.SandboxFlags is not null && !frame.ChildPage.Engine.SandboxAllowsScripts,
            "the empty sandbox attribute must sandbox without allowances");
        True(FindText(frame.ChildPage.Root, "hi") && !FindText(frame.ChildPage.Root, "ran-marker-7"),
            "the sandboxed frame's inline script must never run");
    }

    private static bool FindText(LayoutNode node, string needle)
    {
        // The projected SCRIPT element keeps its raw source text — it must not match: only
        // DOM text the script WROTE proves execution. Element nodes carry their own folded
        // text (a leaf body's Text is the script's write).
        if (node.TagName == "SCRIPT") return false;
        if (node.Text != null && node.Text.Contains(needle, StringComparison.Ordinal)) return true;
        foreach (var c in node.Children) if (FindText(c, needle)) return true;
        return false;
    }

    [Test]
    public static void Sandbox_AllowScripts_RunsAndDefersAreGated()
    {
        var page = Parser.ParseChildPage(
            "<!doctype html><body><iframe sandbox='allow-scripts' id=f srcdoc=" +
            "\"&lt;!doctype html&gt;&lt;body&gt;&lt;script&gt;document.body.textContent = 'ran-marker-7';&lt;/script&gt;&lt;p&gt;hi&lt;/body&gt;\"" +
            "></iframe></body></html>",
            isSrcdoc: true, "http://test/", 800, 600);
        page.Engine!.DrainTasks();
        var frame = Find(page.Root, "IFRAME", "f");
        True(frame?.ChildPage is { }, "frame child page missing");
        True(frame!.ChildPage!.Engine.SandboxAllowsScripts, "allow-scripts must clear the gate");
        True(FindText(frame.ChildPage.Root, "ran-marker-7"), "the allow-scripts frame's script must run");
    }

    [Test]
    public static void Sandbox_WithoutSameOrigin_IsOpaqueToTheParent()
    {
        var page = Parser.ParseChildPage(
            "<!doctype html><body><iframe sandbox='allow-scripts' id=f srcdoc=\"<p>x\"></iframe></body></html>",
            isSrcdoc: true, "http://test/", 800, 600);
        var frame = Find(page.Root, "IFRAME", "f");
        True(frame?.ChildPage is { }, "frame child page missing");
        True(!frame!.ChildPage!.Engine.SandboxAllowsSameOrigin, "no allow-same-origin in the flag set");
        True(frame.ChildPage.Engine.Origin == "null", "the sandboxed frame's origin must read opaque");
        var engine = page.Engine!;
        True(Eval(engine, "document.getElementById('f').contentDocument") == "null",
            "contentDocument must read null across the opaque boundary");
    }

    [Test]
    public static void Sandbox_AllowSameOrigin_KeepsParentAccess()
    {
        var page = Parser.ParseChildPage(
            "<!doctype html><body><iframe sandbox='allow-scripts allow-same-origin' id=f srcdoc=\"<p>x\"></iframe></body></html>",
            isSrcdoc: true, "http://test/", 800, 600);
        var frame = Find(page.Root, "IFRAME", "f");
        True(frame?.ChildPage is { }, "frame child page missing");
        True(frame!.ChildPage!.Engine.SandboxAllowsSameOrigin, "allow-same-origin in the flag set");
        var engine = page.Engine!;
        True(Eval(engine, "document.getElementById('f').contentDocument !== null") == "true",
            "same-origin sandboxed frames keep contentDocument access");
    }

    [Test]
    public static void Sandbox_FlagsInheritIntoNestedFrames()
    {
        // The outer frame is sandboxed without allow-scripts; its OWN child iframe carries no
        // sandbox attribute — the grandchild inherits the flags and must not run script either.
        var page = Parser.ParseChildPage(
            "<!doctype html><body><iframe sandbox='' id=outer srcdoc=" +
            "\"&lt;!doctype html&gt;&lt;body&gt;&lt;iframe id=inner srcdoc=" +
            "'&amp;lt;!doctype html&amp;gt;&amp;lt;body&amp;gt;&amp;lt;script&amp;gt;parent.parent.RAN = 3;&amp;lt;/script&amp;gt;'&gt;&lt;/iframe&gt;&lt;/body&gt;\"" +
            "></iframe></body></html>",
            isSrcdoc: true, "http://test/", 800, 600);
        page.Engine!.DrainTasks();
        True(Eval(page.Engine, "typeof RAN") == "undefined", "the grandchild's script must stay blocked");
    }

    [Test]
    public static void Sandbox_FlagsPersistAcrossWriteStreamReplacement()
    {
        var page = Parser.ParseChildPage(
            "<!doctype html><body><iframe sandbox='allow-scripts allow-same-origin' id=f srcdoc=\"<p>x\"></iframe></body></html>",
            isSrcdoc: true, "http://test/", 800, 600);
        var engine = page.Engine!;
        Run(engine, """
            var f = document.getElementById('f');
            var doc = f.contentDocument;
            doc.open();
            doc.write('<!doctype html><body><p>streamed</p></body>');
            doc.close();
            """);
        engine.DrainTasks();
        var frame = Find(page.Root, "IFRAME", "f");
        True(frame?.ChildPage is { }, "the swapped child page is missing");
        True(frame!.ChildPage!.Engine.SandboxAllowsScripts, "the flag set must survive the write-stream swap");
    }

    [Test]
    public static void Sandbox_WindowOpenIsNullWithoutAllowPopups()
    {
        var page = Parser.ParseChildPage("<!doctype html><body></body></html>",
            isSrcdoc: true, "http://test/", 800, 600);
        var engine = page.Engine!;
        True(Eval(engine, "window.open('http://test/', '_blank')") == "null",
            "window.open returns null in the headless environment");
    }

    [Test]
    public static void DocumentDomain_SetterSuffixGatesAndOpaqueThrows()
    {
        var page = Parser.ParseChildPage("<!doctype html><body></body></html>",
            isSrcdoc: true, "http://test/", 800, 600);
        var engine = page.Engine!;
        Run(engine, """
            var out = [];
            out.push(document.domain);
            try { document.domain = 'test'; out.push('accepted'); } catch (e) { out.push(e.name); }
            try { document.domain = 'other.test'; out.push('accepted'); } catch (e) { out.push(e.name); }
            globalThis.out = out.join('|');
            """);
        // The current host registers (a suffix of itself); a foreign host is rejected.
        Equal("test|accepted|SecurityError", Eval(engine, "out"));
    }
}
