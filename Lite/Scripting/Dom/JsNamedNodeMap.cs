using Lite.Models;

namespace Lite.Scripting.Dom;

/// <summary>An Attr node (Attr interface): a single attribute name/value pair. May be attached to an
/// owner element (a live view of one of its attributes) or detached (document.createAttribute).</summary>
public class JsAttr
{
    private readonly LayoutNode? _owner;
    private string _detachedValue = string.Empty;

    internal JsAttr(LayoutNode owner, string name)
    {
        _owner = owner;
        this.name = name;
    }

    /// <summary>Creates a detached Attr (document.createAttribute) that holds its own value until
    /// attached to an element via setAttributeNode.</summary>
    internal JsAttr(string name) => this.name = name;

    public string name { get; }
    public string localName => name.Contains(':') ? name[(name.IndexOf(':') + 1)..] : name;
    public string? namespaceURI => null;
    public string? prefix => name.Contains(':') ? name[..name.IndexOf(':')] : null;
    public bool specified => true;
    public int nodeType => 2; // ATTRIBUTE_NODE
    public string nodeName => name;

    /// <summary>The owning element, or null for a detached Attr.</summary>
    public JsElement? ownerElement =>
        _owner is not null && JsEngine.Instance is { } e ? JsElement.For(e.RawEngine, _owner) : null;

    public string value
    {
        get => _owner is null ? _detachedValue : _owner.Attributes.GetValueOrDefault(name, string.Empty);
        set
        {
            if (_owner is null) { _detachedValue = value; return; }
            var old = _owner.Attributes.GetValueOrDefault(name);
            _owner.Attributes[name] = value;
            // Mutating an Attr's value must be observable (re-cascade + MutationRecord), like setAttribute.
            JsElement.OnAttributeChanged(_owner, name, old);
        }
    }

    public string nodeValue
    {
        get => value;
        set => this.value = value;
    }
}

/// <summary>NamedNodeMap returned by element.attributes — a live view of the element's attributes.</summary>
public class JsNamedNodeMap
{
    private readonly LayoutNode _node;

    internal JsNamedNodeMap(LayoutNode node) => _node = node;

    // Internal attribute keys (prefixed with '_') are engine bookkeeping, not real DOM attributes.
    private List<string> Names => _node.Attributes.Keys.Where(k => !k.StartsWith('_')).ToList();

    public int length => Names.Count;

    public JsAttr? item(int index)
    {
        var names = Names;
        return index >= 0 && index < names.Count ? new JsAttr(_node, names[index]) : null;
    }

    /// <summary>Indexer so attributes[i] works from JS.</summary>
    public JsAttr? this[int index] => item(index);

    public JsAttr? getNamedItem(string name) =>
        _node.Attributes.ContainsKey(name) ? new JsAttr(_node, name) : null;

    /// <summary>NamedNodeMap.setNamedItem(attr) — adds/replaces the attribute the Attr describes.</summary>
    public JsAttr? setNamedItem(JsAttr attr)
    {
        var old = _node.Attributes.TryGetValue(attr.name, out var o) ? o : null;
        _node.Attributes[attr.name] = attr.value;
        JsElement.OnAttributeChanged(_node, attr.name, old);
        return old is null ? null : new JsAttr(_node, attr.name);
    }

    /// <summary>NamedNodeMap.removeNamedItem(name) — removes and returns the named attribute.</summary>
    public JsAttr? removeNamedItem(string name)
    {
        if (!_node.Attributes.TryGetValue(name, out var old)) return null;
        var detached = new JsAttr(name) { };
        detached.value = old;
        _node.Attributes.Remove(name);
        JsElement.OnAttributeChanged(_node, name, old);
        return detached;
    }
}
