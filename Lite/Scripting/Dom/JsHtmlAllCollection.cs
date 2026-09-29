using System.Globalization;
using Lite.Models;
using Lite.QuickJs;
using Lite.Scripting.Runtime;

namespace Lite.Scripting.Dom;

/// <summary>The document-owned, live HTMLAllCollection boundary.</summary>
internal sealed class JsHtmlAllCollection
{
    private readonly Engine _engine;
    private readonly LayoutNode _root;
    private readonly QuickJsValue _collection;

    internal JsHtmlAllCollection(Engine engine, LayoutNode root)
    {
        _engine = engine;
        _root = root;
        var realm = engine.Realm;
        using var lookup = realm.HostFunction("HTMLAllCollection lookup", 1, (_, args) =>
            engine.ConvertToNative(args.Length == 0 || args[0].Kind == QuickJsKind.Undefined
                ? null : Lookup(args[0].AsString())));
        using var named = realm.HostFunction("HTMLAllCollection namedItem", 1, (_, args) =>
            engine.ConvertToNative(args.Length == 0 ? null : Named(args[0].AsString())));
        using var length = realm.HostFunction("HTMLAllCollection length", 0, (owner, _) =>
            owner.Number(Elements().Count));
        using var create = realm.Eval("""
            (lookup, named, length) => {
              const target = function(nameOrIndex) {
                return arguments.length === 0 || nameOrIndex === undefined ? null : lookup(String(nameOrIndex));
              };
              Object.setPrototypeOf(target, null);
              Object.defineProperty(target, 'length', { configurable: true, get: length });
              Object.defineProperty(target, 'item', { configurable: true,
                value: function(nameOrIndex) { return arguments.length ? lookup(String(nameOrIndex)) : null; } });
              Object.defineProperty(target, 'namedItem', { configurable: true,
                value: function(name) { return named(String(name)); } });
              return new Proxy(target, {
                get(t, key, receiver) {
                  if (Reflect.has(t, key)) return Reflect.get(t, key, receiver);
                  if (typeof key !== 'string') return undefined;
                  const value = lookup(key);
                  return value === null ? undefined : value;
                },
                has(t, key) {
                  return Reflect.has(t, key) || typeof key === 'string' && lookup(key) !== null;
                }
              });
            }
            """);
        _collection = create.Call(arguments: [lookup, named, length]);
        _collection.SetIsHtmlDda();
    }

    internal JsValue Value => new(_engine, _collection.Clone());

    private List<LayoutNode> Elements()
    {
        var result = new List<LayoutNode>();
        var stack = new Stack<LayoutNode>();
        for (var i = _root.Children.Count - 1; i >= 0; i--) stack.Push(_root.Children[i]);
        while (stack.Count != 0)
        {
            var node = stack.Pop();
            if (!node.TagName.StartsWith('#')) result.Add(node);
            for (var i = node.Children.Count - 1; i >= 0; i--) stack.Push(node.Children[i]);
        }
        return result;
    }

    private object? Lookup(string nameOrIndex)
    {
        if (uint.TryParse(nameOrIndex, NumberStyles.None, CultureInfo.InvariantCulture, out var index) &&
            index.ToString(CultureInfo.InvariantCulture) == nameOrIndex)
        {
            var elements = Elements();
            return index < elements.Count ? JsElement.For(_engine, elements[(int)index]) : null;
        }
        return Named(nameOrIndex);
    }

    private object? Named(string name)
    {
        if (name.Length == 0) return null;
        var matches = Elements().Where(node => node.Attributes.GetValueOrDefault("id") == name ||
            AllNamedTags.Contains(node.TagName) && node.Attributes.GetValueOrDefault("name") == name).ToArray();
        if (matches.Length == 0) return null;
        if (matches.Length == 1) return JsElement.For(_engine, matches[0]);
        return new JsLiveNamedCollection(_engine, () => Elements().Where(node =>
            node.Attributes.GetValueOrDefault("id") == name ||
            AllNamedTags.Contains(node.TagName) && node.Attributes.GetValueOrDefault("name") == name).ToArray()).Value;
    }

    private static readonly HashSet<string> AllNamedTags = new(StringComparer.Ordinal)
    {
        "A", "BUTTON", "EMBED", "FORM", "FRAME", "FRAMESET", "IFRAME", "IMG", "INPUT", "MAP",
        "META", "OBJECT", "SELECT", "TEXTAREA",
    };
}

internal sealed class JsLiveNamedCollection
{
    internal JsValue Value { get; }

    internal JsLiveNamedCollection(Engine engine, Func<LayoutNode[]> current)
    {
        var realm = engine.Realm;
        using var item = realm.HostFunction("HTMLCollection item", 1, (_, args) =>
        {
            var index = args.Length == 0 ? -1 : (int)args[0].AsNumber();
            var items = current();
            return engine.ConvertToNative(index >= 0 && index < items.Length ? JsElement.For(engine, items[index]) : null);
        });
        using var length = realm.HostFunction("HTMLCollection length", 0, (owner, _) =>
            owner.Number(current().Length));
        using var create = realm.Eval("""
            (item, length) => new Proxy(Object.create(null), {
              get(_, key) {
                if (key === 'length') return length();
                if (key === 'item') return item;
                if (typeof key === 'string' && /^(0|[1-9][0-9]*)$/.test(key)) return item(Number(key));
                return undefined;
              }
            })
            """);
        Value = new JsValue(engine, create.Call(arguments: [item, length]));
    }
}
