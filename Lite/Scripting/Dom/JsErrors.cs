using Lite.Scripting.Runtime;

namespace Lite.Scripting.Dom;

/// <summary>
/// Builds JS-catchable error objects (DOMException + native Error subtypes like TypeError) from
/// host code <em>without re-entering the engine</em> — calling Invoke/Evaluate/Construct from inside
/// a host method cannot safely re-enter the engine. The object shape mirrors what testharness.js inspects:
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
    internal static JavaScriptException Dom(string name, string message) =>
        JsEngine.Instance?.RawEngine is { } eng ? Dom(eng, name, message)
        : new JavaScriptException((JsValue)message);

    /// <summary>The engine-parameterized form for host objects that hold their page's engine —
    /// the singleton's realm may not be the realm the exception must surface in (unit tests and
    /// nested page engines both build multi-engine processes).</summary>
    internal static JavaScriptException Dom(Engine engine, string name, string message)
    {
        var err = new JsObject(engine);
        Set(err, "name", name);
        Set(err, "message", message);
        Set(err, "code", JsNumber.Create(DomCodes.GetValueOrDefault(name)));
        var ctor = engine.GetValue("DOMException");
        if (ctor.IsObject()) Set(err, "constructor", ctor);
        return new JavaScriptException(JsValue.FromObject(engine, err));
    }

    /// <summary>Builds (does not throw) a native error, e.g. <c>Native("TypeError", msg)</c>.
    /// The engine-parameterized form is for host-binding layers that hold the raw engine
    /// directly (nested iframe engines may not own the process-wide singleton).</summary>
    internal static JavaScriptException Native(Engine engine, string ctorName, string message)
    {
        var err = new JsObject(engine);
        Set(err, "name", ctorName);
        Set(err, "message", message);
        var ctor = engine.GetValue(ctorName);
        if (ctor.IsObject()) Set(err, "constructor", ctor);
        return new JavaScriptException(JsValue.FromObject(engine, err));
    }

    internal static JavaScriptException Native(string ctorName, string message)
    {
        var eng = JsEngine.Instance?.RawEngine;
        if (eng is null) return new JavaScriptException((JsValue)message);
        return Native(eng, ctorName, message);
    }

    private static void Set(JsObject o, string key, JsValue value) => o.Set(key, value);
}
