using Lite.Scripting.Runtime;

namespace Lite.Scripting.Dom;

/// <summary>A DOM NodeList. The supplier is evaluated on access for live lists; callers
/// can pass a captured array for static query and mutation-record lists.</summary>
public class JsNodeList(Func<IReadOnlyList<JsElement>> items)
{
    internal IReadOnlyList<JsElement> Snapshot() => items();
    public int length => items().Count;
    public JsElement? this[int index] => index >= 0 && index < items().Count ? items()[index] : null;
    public JsElement? item(int index) => this[index];

    public void forEach(JsValue callback, JsValue? thisArg = null)
    {
        var snapshot = items();
        var engine = callback.AsObject().Engine;
        if (engine is null) return;
        for (var i = 0; i < snapshot.Count; i++)
            engine.Invoke(callback, snapshot[i], i, this);
    }
}

internal interface IJsNamedCollection
{
    object? Named(string name);
}

/// <summary>HTMLCollection with live length, indexed access and named lookup.</summary>
public class JsHtmlCollection(Func<IReadOnlyList<JsElement>> items) : JsNodeList(items), IJsNamedCollection
{
    public JsElement? namedItem(string name) => Named(name) as JsElement;

    object? IJsNamedCollection.Named(string name) => Named(name);

    internal object? Named(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        var snapshot = Snapshot();
        return snapshot.FirstOrDefault(element => element.id == name)
            ?? snapshot.FirstOrDefault(element => element.getAttribute("name") == name);
    }
}
