using AngleSharp.Dom;

namespace Lite.Models;

/// <summary>Rendering-side attribute cache. Existing consumers can update it while the
/// owning AngleSharp element remains the authoritative attribute store.</summary>
public sealed class DomAttributeDictionary(LayoutNode owner) : Dictionary<string, string>
{
    public new string this[string name]
    {
        get => base[name];
        set
        {
            if (owner.DomNode is IElement element && !name.StartsWith('_'))
                element.SetAttribute(name, value);
            base[name] = value;
        }
    }

    public new bool Remove(string name)
    {
        if (owner.DomNode is IElement element && !name.StartsWith('_'))
            element.RemoveAttribute(name);
        return base.Remove(name);
    }

    public new void Add(string name, string value)
    {
        if (owner.DomNode is IElement element && !name.StartsWith('_'))
            element.SetAttribute(name, value);
        base.Add(name, value);
    }

    public new bool TryAdd(string name, string value)
    {
        if (ContainsKey(name)) return false;
        Add(name, value);
        return true;
    }

    public new void Clear()
    {
        if (owner.DomNode is IElement element)
            foreach (var name in Keys.Where(name => !name.StartsWith('_')).ToArray())
                element.RemoveAttribute(name);
        base.Clear();
    }
}
