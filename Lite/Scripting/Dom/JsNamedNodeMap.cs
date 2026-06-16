using Lite.Models;

namespace Lite.Scripting.Dom;

/// <summary>An Attr node (Attr interface): a single attribute name/value pair.</summary>
public class JsAttr
{
    private readonly LayoutNode _owner;

    internal JsAttr(LayoutNode owner, string name)
    {
        _owner = owner;
        this.name = name;
    }

    public string name { get; }
    public string localName => name.Contains(':') ? name[(name.IndexOf(':') + 1)..] : name;
    public string? namespaceURI => null;
    public string? prefix => name.Contains(':') ? name[..name.IndexOf(':')] : null;
    public bool specified => true;
    public int nodeType => 2; // ATTRIBUTE_NODE
    public string nodeName => name;

    public string value
    {
        get => _owner.Attributes.GetValueOrDefault(name, string.Empty);
        set => _owner.Attributes[name] = value;
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
}
