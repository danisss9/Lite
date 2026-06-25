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

    /// <summary>Asserts a JS-sourced numeric value equals <paramref name="expected"/> within 1e-6.</summary>
    private static void Near(double expected, object? actual, string what)
    {
        var got = Convert.ToDouble(actual, System.Globalization.CultureInfo.InvariantCulture);
        True(Math.Abs(got - expected) < 1e-6, $"{what}: expected {expected}, got {got}");
    }

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

    private static LayoutNode? FindDescendant(LayoutNode root, Func<LayoutNode, bool> pred)
    {
        foreach (var c in root.Children)
        {
            if (pred(c)) return c;
            if (FindDescendant(c, pred) is { } found) return found;
        }
        return null;
    }

    [Test]
    public static void Multipart_EncodesTextAndFileParts()
    {
        var form = Parser.ParseFragment(
            "<form method=\"post\" enctype=\"multipart/form-data\">" +
            "<input name=\"title\" value=\"Hi\">" +
            "<input type=\"file\" name=\"doc\">" +
            "</form>")[0];
        var fileInput = FindDescendant(form, c => c.Attributes.GetValueOrDefault("type") == "file")!;
        FormState.Files[fileInput.NodeKey] = [new SelectedFile("a.txt", 5, "text/plain", "hello", 0)];

        var body = FormSubmitter.BuildMultipartBody(form, "BOUND");
        Contains("Content-Disposition: form-data; name=\"title\"", body);
        Contains("Hi", body);
        Contains("name=\"doc\"; filename=\"a.txt\"", body);
        Contains("Content-Type: text/plain", body);
        Contains("hello", body);
        Contains("--BOUND--", body);

        FormState.Files.Remove(fileInput.NodeKey); // don't leak into other tests
    }

    [Test]
    public static void BuildSubmission_GetPostUrlencodedMultipart()
    {
        var get = Parser.ParseFragment("<form action=\"/s\" method=\"get\"><input name=\"q\" value=\"x\"></form>")[0];
        var s1 = FormSubmitter.BuildSubmission(get, "http://h/p");
        Equal("GET", s1.Method);
        Contains("q=x", s1.Url);
        True(s1.Body is null, "GET submission carries no body");

        var post = Parser.ParseFragment("<form action=\"/s\" method=\"post\"><input name=\"q\" value=\"x\"></form>")[0];
        var s2 = FormSubmitter.BuildSubmission(post, "http://h/p");
        Equal("POST", s2.Method);
        Equal("q=x", s2.Body);
        Contains("application/x-www-form-urlencoded", s2.ContentType!);

        var mp = Parser.ParseFragment("<form action=\"/s\" method=\"post\" enctype=\"multipart/form-data\"><input name=\"q\" value=\"x\"></form>")[0];
        var s3 = FormSubmitter.BuildSubmission(mp, "http://h/p");
        Contains("multipart/form-data; boundary=", s3.ContentType!);
        Contains("name=\"q\"", s3.Body!);
    }

    [Test]
    public static void FileList_ReflectsSelectedFiles()
    {
        var e = NewEngine(out var body);
        e.Execute("document.body.innerHTML = '<input type=\"file\" id=\"f\">';");
        var input = FindDescendant(body, c => c.TagName == "INPUT")!;
        FormState.Files[input.NodeKey] = [new SelectedFile("p.png", 12, "image/png", "", 0)];
        e.Execute(@"
            var inp = document.getElementById('f');
            var n = inp.files.length;
            var name = inp.files[0].name;
            var type = inp.files[0].type;
            var notFile = document.createElement('div').files;");
        Equal(1, Convert.ToInt32(Global(e, "n")));
        Equal("p.png", (string?)Global(e, "name"));
        Equal("image/png", (string?)Global(e, "type"));
        True(Global(e, "notFile") is null, "files is null on non-file elements");

        FormState.Files.Remove(input.NodeKey);
    }

    [Test]
    public static void RequestSubmit_FiresSubmit_PlainSubmitDoesNot()
    {
        var e = NewEngine(out _);
        e.Execute(@"
            document.body.innerHTML = '<form action=""/x""><input name=""q"" value=""1""></form>';
            var form = document.querySelector('form');
            globalThis.fired = 0;
            form.addEventListener('submit', function (ev) { globalThis.fired++; ev.preventDefault(); });
            form.requestSubmit();
            var afterRequest = globalThis.fired;
            form.submit();
            var afterSubmit = globalThis.fired;");
        Equal(1, Convert.ToInt32(Global(e, "afterRequest")));   // requestSubmit fires the event
        Equal(1, Convert.ToInt32(Global(e, "afterSubmit")));    // submit() does NOT fire it
    }

    [Test]
    public static void Progress_ValueMaxPositionReflect()
    {
        var e = NewEngine(out _);
        e.Execute(@"
            document.body.innerHTML = '<progress value=""0.25"" max=""1""></progress>';
            var p = document.querySelector('progress');
            var v = p.value, m = p.max, pos = p.position, vType = typeof p.value;
            p.value = 0.75;
            var v2 = p.value, attr = p.getAttribute('value');
            document.body.innerHTML = '<progress></progress>';
            var indet = document.querySelector('progress').position;");
        Near(0.25, Global(e, "v"), "progress.value");
        Near(1.0, Global(e, "m"), "progress.max");
        Near(0.25, Global(e, "pos"), "progress.position");
        Equal("number", (string?)Global(e, "vType"));
        Near(0.75, Global(e, "v2"), "progress.value after set");
        Equal("0.75", (string?)Global(e, "attr"));
        Near(-1.0, Global(e, "indet"), "indeterminate progress.position");   // no value attr
    }

    [Test]
    public static void Meter_ValueAndBoundsReflectAndClamp()
    {
        var e = NewEngine(out _);
        e.Execute(@"
            document.body.innerHTML = '<meter min=""0"" max=""10"" low=""2"" high=""8"" optimum=""9"" value=""5""></meter>';
            var m = document.querySelector('meter');
            var v=m.value, mn=m.min, mx=m.max, lo=m.low, hi=m.high, op=m.optimum;
            m.value = 20; var clamped = m.value;");
        Near(5.0, Global(e, "v"), "meter.value");
        Near(0.0, Global(e, "mn"), "meter.min");
        Near(10.0, Global(e, "mx"), "meter.max");
        Near(2.0, Global(e, "lo"), "meter.low");
        Near(8.0, Global(e, "hi"), "meter.high");
        Near(9.0, Global(e, "op"), "meter.optimum");
        Near(10.0, Global(e, "clamped"), "meter.value clamped to max");   // value clamps into [min,max]
    }

    [Test]
    public static void Output_ValueTypeAndFor()
    {
        var e = NewEngine(out _);
        e.Execute(@"
            document.body.innerHTML = '<form><output for=""a b"">42</output></form>';
            var o = document.querySelector('output');
            var v = o.value, t = o.type, f = o.htmlFor;
            o.value = 'hi';
            var v2 = o.value, txt = o.textContent;");
        Equal("42", (string?)Global(e, "v"));         // value mirrors text content
        Equal("output", (string?)Global(e, "t"));
        Equal("a b", (string?)Global(e, "f"));
        Equal("hi", (string?)Global(e, "v2"));
        Equal("hi", (string?)Global(e, "txt"));        // setting value rewrites text content
    }

    [Test]
    public static void Datalist_ExposesOptions()
    {
        var e = NewEngine(out _);
        e.Execute(@"
            document.body.innerHTML = '<datalist id=""d""><option value=""x""></option><option value=""y""></option></datalist>';
            var dl = document.getElementById('d');
            var n = dl.options.length;
            var first = dl.options[0].value;");
        Equal(2, Convert.ToInt32(Global(e, "n")));
        Equal("x", (string?)Global(e, "first"));
    }
}
