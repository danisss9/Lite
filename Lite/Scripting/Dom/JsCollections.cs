using Lite.Scripting.Runtime;

namespace Lite.Scripting.Dom;

/// <summary>WebIDL numeric conversion for collection index operations: ToUint32 wraps
/// (item(4294967296) reads index 0, item(-2) reads 2^32-2 — beyond any real length), so the
/// conversion must happen before the value narrows to a signed int.</summary>
internal static class DomCollections
{
    internal static uint ToUint32(double value)
    {
        if (!double.IsFinite(value)) return 0;
        var u = (value % 4294967296.0 + 4294967296.0) % 4294967296.0;
        return (uint)u;
    }
}

/// <summary>A DOM NodeList. The supplier is evaluated on access for live lists; callers
/// can pass a captured array for static query and mutation-record lists.</summary>
public class JsNodeList(Func<IReadOnlyList<JsElement>> items)
{
    internal IReadOnlyList<JsElement> Snapshot() => items();
    public int length => items().Count;
    public JsElement? this[int index] => index >= 0 && index < items().Count ? items()[index] : null;

    public JsElement? item(double index)
    {
        var u = DomCollections.ToUint32(index);
        var list = items();
        return u < (uint)list.Count ? list[(int)u] : null;
    }

    public void forEach(JsValue callback, JsValue? thisArg = null)
    {
        var snapshot = items();
        var engine = callback.AsObject().Engine;
        if (engine is null) return;
        for (var i = 0; i < snapshot.Count; i++)
            engine.Invoke(callback, snapshot[i], i, this);
    }
}

/// <summary>A script-facing named collection (HTMLCollection, NamedNodeMap): the script proxy's
/// named-property lookup and supported-property-name enumeration route through here, so the
/// legacy-platform-object semantics live in one place (see Runtime.QuickJsInterop).</summary>
internal interface IJsNamedCollection
{
    object? Named(string name);

    /// <summary>The supported property names in enumeration order, duplicates removed.</summary>
    IReadOnlyList<string> SupportedNames();
}

/// <summary>ASCII whitespace per the DOM/HTML spec (U+0009, U+000A, U+000C, U+000D, U+0020) —
/// the only characters that separate tokens in class attributes and getElementsByClassName
/// arguments. .NET's char.IsWhiteSpace set is wider (U+000B, U+00A0, U+2000-200A, …) and would
/// split class names the spec keeps whole.</summary>
internal static class DomWhitespace
{
    internal static readonly char[] Separator = [' ', '\t', '\n', '\f', '\r'];

    internal static string[] Split(string value) =>
        value.Split(Separator, StringSplitOptions.RemoveEmptyEntries);
}

/// <summary>HTMLCollection with live length, indexed access and named lookup. Deliberately not a
/// JsNodeList subclass: HTMLCollection carries no forEach/values/entries (the iterable surface
/// belongs to NodeList only — dom/collections/HTMLCollection-iterator).</summary>
public class JsHtmlCollection(Func<IReadOnlyList<JsElement>> items) : IJsNamedCollection
{
    internal const string HtmlNamespace = "http://www.w3.org/1999/xhtml";

    internal IReadOnlyList<JsElement> Snapshot() => items();

    public int length => items().Count;
    public JsElement? this[int index] => index >= 0 && index < items().Count ? items()[index] : null;

    // The unsigned long wrap lives on DomCollections.ToUint32 — an int overload here would
    // shadow it and narrow 2^32 to a negative index before the wrap could run.
    public JsElement? item(double index)
    {
        var u = DomCollections.ToUint32(index);
        var list = items();
        return u < (uint)list.Count ? list[(int)u] : null;
    }

    public JsElement? namedItem(string name) => Named(name) as JsElement;

    object? IJsNamedCollection.Named(string name) => Named(name);

    internal object? Named(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        // The named getter: the first element whose ID matches, or whose name content attribute
        // matches for an HTML-namespace element (DOM §4.2.10.2) — a single tree-order pass.
        return items().FirstOrDefault(element => element.id == name
            || (element.namespaceURI == HtmlNamespace && element.getAttribute("name") == name));
    }

    /// <summary>Supported property names in tree order: every non-empty id, then the non-empty
    /// name content attribute of HTML-namespace elements — so a foreign-namespace element's name
    /// attribute is not a named property (dom/collections/HTMLCollection-supported-property-names).</summary>
    IReadOnlyList<string> IJsNamedCollection.SupportedNames()
    {
        var names = new List<string>();
        foreach (var element in items())
        {
            var id = element.id;
            if (!string.IsNullOrEmpty(id) && !names.Contains(id)) names.Add(id);
            if (element.namespaceURI == HtmlNamespace)
            {
                var name = element.getAttribute("name");
                if (!string.IsNullOrEmpty(name) && !names.Contains(name)) names.Add(name);
            }
        }
        return names;
    }
}
