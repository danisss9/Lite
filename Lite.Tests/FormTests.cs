using Lite;
using Lite.Interaction;
using Lite.Models;
using Lite.Scripting;
using Lite.Scripting.Dom;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>Item 3 — typed events, form submission, validation, input types.</summary>
public static class FormTests
{
    private static JsEngine NewEngine(out LayoutNode body)
    {
        var sample = Parser.ParseFragment("<span></span>")[0];
        var root = new LayoutNode(null, "HTML", "", sample.Style);
        body = new LayoutNode(null, "BODY", "", sample.Style);
        root.AddChild(body);
        return JsEngine.Create(root);
    }

    private static object? Global(JsEngine e, string name) => e.RawEngine.GetValue(name).ToObject();

    [Test]
    public static void CustomEvent_CarriesDetail()
    {
        var e = NewEngine(out var body);
        e.Execute(@"
            globalThis.__detail = null;
            var el = document.createElement('div');
            el.addEventListener('ping', function (ev) { globalThis.__detail = ev.detail; });
            el.dispatchEvent(new CustomEvent('ping', { detail: 99 }));
        ");
        Equal(99, Convert.ToInt32(Global(e, "__detail")));
    }

    [Test]
    public static void Event_ConstructorSetsBubbles()
    {
        var e = NewEngine(out var body);
        e.Execute(@"
            globalThis.__bubbled = false;
            var parent = document.createElement('div');
            var child = document.createElement('span');
            parent.appendChild(child);
            parent.addEventListener('boom', function () { globalThis.__bubbled = true; });
            child.dispatchEvent(new Event('boom', { bubbles: true }));
        ");
        True((bool)Global(e, "__bubbled")!, "bubbling Event should reach the parent listener");
    }

    [Test]
    public static void FormSubmitter_BuildsGetQuery()
    {
        var nodes = Parser.ParseFragment(
            "<form action=\"/search\" method=\"get\">" +
            "<input name=\"q\" value=\"hello world\">" +
            "<input name=\"page\" value=\"2\">" +
            "<input type=\"submit\" value=\"Go\">" +
            "</form>");
        var form = nodes[0];
        var url = FormSubmitter.BuildActionUrl(form, "http://localhost:4444/index.html");
        Contains("/search?", url);
        Contains("q=hello%20world", url);
        Contains("page=2", url);
        True(!url.Contains("Go"), "submit button value must not be serialized");
    }

    [Test]
    public static void FormSubmitter_SkipsDisabledAndUnnamed()
    {
        var nodes = Parser.ParseFragment(
            "<form action=\"/x\">" +
            "<input name=\"a\" value=\"1\">" +
            "<input value=\"2\">" +              // no name → skipped
            "<input name=\"c\" value=\"3\" disabled>" + // disabled → skipped
            "</form>");
        var query = FormSubmitter.BuildQuery(nodes[0]);
        Equal("a=1", query);
    }

    [Test]
    public static void Validation_RequiredEmptyIsInvalid()
    {
        var nodes = Parser.ParseFragment("<input name=\"x\" required>");
        var validity = FormValidation.GetValidity(nodes[0]);
        True(validity.valueMissing, "empty required input should report valueMissing");
        True(!validity.valid, "empty required input should be invalid");
    }

    [Test]
    public static void Validation_EmailTypeMismatch()
    {
        var bad = Parser.ParseFragment("<input type=\"email\" value=\"not-an-email\">")[0];
        var good = Parser.ParseFragment("<input type=\"email\" value=\"a@b.com\">")[0];
        True(FormValidation.GetValidity(bad).typeMismatch, "invalid email should mismatch");
        True(!FormValidation.GetValidity(good).typeMismatch, "valid email should not mismatch");
    }

    [Test]
    public static void CheckValidity_ExposedToJs()
    {
        var e = NewEngine(out var body);
        e.Execute(@"
            var i = document.createElement('input');
            i.setAttribute('required', '');
            document.body.appendChild(i);
            globalThis.__valid = i.checkValidity();
        ");
        True(!(bool)Global(e, "__valid")!, "required empty input should fail checkValidity");
    }

    [Test]
    public static void HiddenInput_IsNotDisplayed()
    {
        var node = Parser.ParseFragment("<input type=\"hidden\" name=\"token\" value=\"abc\">")[0];
        Equal("none", node.StyleOverrides.GetValueOrDefault("display"));
    }

    [Test]
    public static void RequiredPseudoClass_Matches()
    {
        var node = Parser.ParseFragment("<input required>")[0];
        True(SelectorEngine.Matches(node, ":required"), ":required should match an input with the attribute");
        True(SelectorEngine.Matches(node, "input:invalid"), "empty required input should match :invalid");
    }

    [Test]
    public static void SetCustomValidity_MakesControlInvalid()
    {
        var e = NewEngine(out _);
        e.Execute(@"
            var inp = document.createElement('input');
            document.body.appendChild(inp);
            var validBefore = inp.checkValidity();
            inp.setCustomValidity('bad value');
            var validAfter = inp.checkValidity();
            var msg = inp.validationMessage;
            var customErr = inp.validity.customError;
            inp.setCustomValidity('');
            var validCleared = inp.checkValidity();");
        True(Global(e, "validBefore") is true, "a fresh input is valid");
        True(Global(e, "validAfter") is false, "setCustomValidity makes the control invalid");
        Equal("bad value", (string?)Global(e, "msg"));
        True(Global(e, "customErr") is true, "validity.customError should be true");
        True(Global(e, "validCleared") is true, "clearing custom validity restores validity");
    }

    [Test]
    public static void FormData_AppendGetAndFromForm()
    {
        var e = NewEngine(out _);
        e.Execute(@"
            var fd = new FormData();
            fd.append('a', '1');
            fd.append('a', '2');
            fd.append('b', 'x');
            var aFirst = fd.get('a');
            var aAll = fd.getAll('a').join(',');
            var hasB = fd.has('b');
            fd.set('a', '9');
            var aAfterSet = fd.getAll('a').join(',');
            fd.delete('b');
            var hasBAfter = fd.has('b');

            document.body.innerHTML = '<form><input name=\""q\"" value=\""hi\""><input name=\""n\"" value=\""5\""></form>';
            var fromForm = new FormData(document.querySelector('form'));
            var q = fromForm.get('q');
            var n = fromForm.get('n');");
        Equal("1", (string?)Global(e, "aFirst"));
        Equal("1,2", (string?)Global(e, "aAll"));
        True(Global(e, "hasB") is true, "has('b') should be true");
        Equal("9", (string?)Global(e, "aAfterSet"));   // set collapses duplicates to one
        True(Global(e, "hasBAfter") is false, "delete('b') should remove it");
        Equal("hi", (string?)Global(e, "q"));
        Equal("5", (string?)Global(e, "n"));
    }
}
