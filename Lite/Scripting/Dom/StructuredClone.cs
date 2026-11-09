using Lite.QuickJs;
using Lite.Scripting.Runtime;

namespace Lite.Scripting.Dom;

/// <summary>HTML §8.3 structured clone: the postMessage/MessagePort data path. The value is
/// serialized to a flat descriptor table in the source realm (primitives, plain objects and
/// arrays with index back-references for cycles, Date, RegExp, Map, Set, the Error types,
/// ArrayBuffer and the typed-array views) and materialized in the target realm. Anything the
/// algorithm cannot clone — functions, symbols, DOM wrappers, instances of page classes —
/// raises DataCloneError instead of silently flattening: the previous JSON route dropped
/// functions and turned cyclic references into source-realm handles that crossed engines.</summary>
internal static class StructuredClone
{
    /// <summary>Clones <paramref name="value"/> from the <paramref name="source"/> realm into the
    /// <paramref name="target"/> realm.</summary>
    internal static JsValue Clone(Engine source, JsValue value, Engine target)
    {
        string json;
        try
        {
            using var serialize = source.Realm.Global().Get("__liteCloneSerialize");
            using var serialized = serialize.Call(arguments: [value.Native.Clone()]);
            json = serialized.AsString();
        }
        catch (QuickJsException)
        {
            // The source walker raises its sentinel exactly for the non-clonable shapes.
            throw JsErrors.Dom(target, "DataCloneError",
                "Failed to execute 'postMessage': the message could not be cloned.");
        }
        // The descriptor table crosses realms as a string, carried by a one-shot host function,
        // and the target realm's materializer rebuilds the value from it.
        using var materialize = target.Realm.Global().Get("__liteCloneMaterialize");
        using var jsonString = target.Realm.String(json);
        using var result = materialize.Call(arguments: [jsonString]);
        return new JsValue(target, result.Clone());
    }
}
