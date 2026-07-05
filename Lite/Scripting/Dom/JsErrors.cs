using Jint;
using Jint.Native;
using Jint.Runtime;
using Jint.Runtime.Descriptors;

namespace Lite.Scripting.Dom;

/// <summary>
/// Builds JS-catchable error objects (DOMException + native Error subtypes like TypeError) from
/// host code <em>without re-entering the engine</em> — calling Invoke/Evaluate/Construct from inside
/// a host method hangs Jint. The object shape mirrors what testharness.js inspects:
/// own <c>name</c> / <c>message</c> / <c>constructor</c> (+ <c>code</c> for DOMException), which is
/// all <c>assert_throws_dom</c> / <c>assert_throws_js</c> compare (they use <c>constructor ===</c>
/// and <c>name</c>, never <c>instanceof</c>). These propagate intact because
/// <c>CatchClrExceptions</c> skips <see cref="JavaScriptException"/>.
/// </summary>
internal static class JsErrors
{
    // WebIDL error-name → legacy DOMException code (https://webidl.spec.whatwg.org/#dfn-error-names-table).
    private static readonly Dictionary<string, int> DomCodes = new()
    {
        ["IndexSizeError"] = 1, ["HierarchyRequestError"] = 3, ["WrongDocumentError"] = 4,
        ["InvalidCharacterError"] = 5, ["NoModificationAllowedError"] = 7, ["NotFoundError"] = 8,
        ["NotSupportedError"] = 9, ["InvalidStateError"] = 11, ["SyntaxError"] = 12,
        ["NamespaceError"] = 14, ["InvalidNodeTypeError"] = 24,
    };

    /// <summary>Builds (does not throw) a DOMException with the given <paramref name="name"/>.</summary>
    internal static JavaScriptException Dom(string name, string message)
    {
        var eng = JsEngine.Instance?.RawEngine;
        if (eng is null) return new JavaScriptException((JsValue)message);
        var err = new JsObject(eng);
        Set(err, "name", name);
        Set(err, "message", message);
        Set(err, "code", JsNumber.Create(DomCodes.GetValueOrDefault(name)));
        var ctor = eng.GetValue("DOMException");
        if (ctor.IsObject()) Set(err, "constructor", ctor);
        return new JavaScriptException(err);
    }

    /// <summary>Builds (does not throw) a native error, e.g. <c>Native("TypeError", msg)</c>.</summary>
    internal static JavaScriptException Native(string ctorName, string message)
    {
        var eng = JsEngine.Instance?.RawEngine;
        if (eng is null) return new JavaScriptException((JsValue)message);
        var err = new JsObject(eng);
        Set(err, "name", ctorName);
        Set(err, "message", message);
        var ctor = eng.GetValue(ctorName);
        if (ctor.IsObject()) Set(err, "constructor", ctor);
        return new JavaScriptException(err);
    }

    private static void Set(JsObject o, string key, JsValue value) =>
        o.FastSetProperty(key, new PropertyDescriptor(value, writable: true, enumerable: false, configurable: true));
}
