namespace Lite.QuickJs;

/// <summary>Only explicitly registered members of a host object become visible to scripts.</summary>
internal sealed class QuickJsHostObjects(QuickJsRealm realm)
{
    private readonly Dictionary<object, QuickJsValue> _wrappers = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<int, object> _instances = new();
    private int _nextId;

    internal QuickJsValue Wrap<T>(T instance, Action<QuickJsHostBinding<T>> configure) where T : class
    {
        if (_wrappers.TryGetValue(instance, out var existing)) return existing.Clone();
        var id = ++_nextId;
        var wrapper = realm.HostObject(id);
        _instances.Add(id, instance);
        _wrappers.Add(instance, wrapper);
        try { configure(new QuickJsHostBinding<T>(realm, wrapper, instance)); }
        catch
        {
            _instances.Remove(id);
            _wrappers.Remove(instance);
            wrapper.Dispose();
            throw;
        }
        return wrapper.Clone();
    }

    internal T? Unwrap<T>(QuickJsValue value) where T : class
    {
        if (!ReferenceEquals(value.Realm, realm)) return null;
        var id = Native.lite_host_object_id(value.Handle);
        return id != 0 && _instances.TryGetValue(id, out var instance) ? instance as T : null;
    }
}

internal sealed class QuickJsHostBinding<T>(QuickJsRealm realm, QuickJsValue wrapper, T instance)
    where T : class
{
    internal QuickJsHostBinding<T> Property(string name, Func<T, QuickJsValue> get,
        Action<T, QuickJsValue>? set = null)
    {
        using var getter = realm.HostFunction($"get {name}", 0, (_, _) => get(instance));
        using var setter = set is null ? null : realm.HostFunction($"set {name}", 1, (owner, args) =>
        {
            set(instance, args.Length > 0 ? args[0] : owner.Undefined());
            return owner.Undefined();
        });
        wrapper.DefineAccessor(name, getter, setter);
        return this;
    }

    internal QuickJsHostBinding<T> Method(string name, int arity,
        Func<T, QuickJsRealm, QuickJsValue[], QuickJsValue> invoke)
    {
        using var function = realm.HostFunction(name, arity, (owner, args) => invoke(instance, owner, args));
        wrapper.Set(name, function);
        return this;
    }
}
