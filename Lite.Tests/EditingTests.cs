using Lite;
using Lite.Scripting;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>Regression coverage for the editing-and-selection package: the Range boundary
/// model (setStart/setEnd, collapse, selectNodeContents, toString), the Selection facade
/// (anchor/focus, isCollapsed, selectAllChildren, addRange), the contentEditable/isContentEditable
/// inherited resolution, and the execCommand surface — selectall, delete, inserttext,
/// insertparagraph, bold — editing the tree through the projection so the results serialize,
/// and unknown commands never throwing (the never-throw contract).</summary>
public static class EditingTests
{
    private static JsEngine Parsed(string html) =>
        Parser.ParseChildPage(html, isSrcdoc: true, "http://test/", 800, 600).Engine!;

    private static void Run(JsEngine engine, string js) => engine.RawEngine.Execute(js);

    private static string Eval(JsEngine engine, string expr) => engine.RawEngine.Evaluate(expr).ToString() ?? "";

    [Test]
    public static void Range_BoundaryModelAndToString()
    {
        var engine = Parsed("<!doctype html><body><div id=d>hello <b>bold</b> world</div></body></html>");
        Run(engine, """
            var range = document.createRange();
            var div = document.getElementById('d');
            range.selectNodeContents(div);
            var out = [];
            out.push(range.collapsed);
            out.push(range.startContainer === div);
            out.push(range.endOffset === 3);
            range.setEnd(div.firstChild, 5);
            out.push(range.toString() === 'hello');
            range.collapse(true);
            out.push(range.collapsed && range.toString() === '');
            globalThis.out = out.join('|');
            """);
        Equal("false|true|true|true|true", Eval(engine, "out"));
    }

    [Test]
    public static void Selection_FacadeAndSelectAllChildren()
    {
        var engine = Parsed("<!doctype html><body><div id=d><p>a</p><p>b</p></div></body></html>");
        Run(engine, """
            var sel = window.getSelection();
            var out = [];
            out.push(sel.isCollapsed);
            sel.selectAllChildren(document.getElementById('d'));
            out.push(sel.isCollapsed);
            out.push(sel.anchorNode === document.getElementById('d'));
            out.push(sel.focusOffset === 2);
            out.push(sel.toString().indexOf('a') === 0 && sel.toString().indexOf('b') === 1);
            globalThis.out = out.join('|');
            """);
        Equal("true|false|true|true|true", Eval(engine, "out"));
    }

    [Test]
    public static void ContentEditable_ReflectionAndInheritance()
    {
        var engine = Parsed(
            "<!doctype html><body><div id=host contenteditable=true><p id=inner>text</p><p id=plain>x</p></div></body></html>");
        Run(engine, """
            var out = [];
            out.push(document.getElementById('host').contentEditable);
            out.push(document.getElementById('host').isContentEditable);
            out.push(document.getElementById('inner').isContentEditable);
            out.push(document.getElementById('plain').isContentEditable);
            document.getElementById('host').contentEditable = 'false';
            out.push(document.getElementById('inner').isContentEditable);
            globalThis.out = out.join('|');
            """);
        // The inherited resolution flips with the setter: inner reads true while the host
        // allows editing and false once the host sets contentEditable = 'false'.
        Equal("true|true|true|true|false", Eval(engine, "out"));
    }

    [Test]
    public static void ExecCommand_SelectAllDeleteAndInsertText()
    {
        var engine = Parsed(
            "<!doctype html><body><div id=host contenteditable=true><p>one</p><p>two</p></div></body></html>");
        Run(engine, """
            var out = [];
            out.push(document.execCommand('selectall'));
            var host2 = document.getElementById('host');
            out.push(getSelection().anchorNode === host2 && getSelection().focusOffset === 2);
            out.push(document.execCommand('delete'));
            out.push(host2.childNodes.length);
            out.push(document.execCommand('inserttext', false, 'new text'));
            globalThis.out = out.join('|');
            """);
        // Select-all selects the host's children (anchorNode is the host, focusOffset the
        // child count); delete empties them; inserttext writes at the caret through the
        // projection.
        Equal("true|true|true|0|true", Eval(engine, "out"));
    }

    [Test]
    public static void ExecCommand_UnknownCommandsNeverThrow()
    {
        var engine = Parsed("<!doctype html><body></body></html>");
        Run(engine, """
            var out = [];
            out.push(document.execCommand('nosuchcommand'));
            out.push(document.queryCommandSupported('bold'));
            out.push(document.queryCommandSupported('nosuchcommand'));
            out.push(document.queryCommandEnabled('bold'));
            try { document.execCommand('bold'); out.push('bold-ok'); } catch (e) { out.push('threw'); }
            globalThis.out = out.join('|');
            """);
        Equal("false|true|false|true|bold-ok", Eval(engine, "out"));
    }

    [Test]
    public static void ExecCommand_BoldWrapsSelection()
    {
        var engine = Parsed(
            "<!doctype html><body><div id=host contenteditable=true><p>target text</p></div></body></html>");
        Run(engine, """
            var host = document.getElementById('host');
            var range = document.createRange();
            range.selectNodeContents(host);
            getSelection().addRange(range);
            document.execCommand('bold');
            """);
        // The command edits the projected tree; the observation runs on the settled state.
        // querySelectorAll reads the AngleSharp mirror (the documented non-authoritative-DOM
        // residual), so the observation goes through childNodes, which reads the projection.
        Run(engine, """
            var host = document.getElementById('host');
            var out = [];
            out.push(host.childNodes.length);
            out.push(String(host.childNodes[0].tagName));
            out.push(String(host.childNodes[0].textContent.indexOf('target text') >= 0));
            globalThis.out = out.join('|');
            """);
        Equal("1|B|true", Eval(engine, "out"));
    }
}
