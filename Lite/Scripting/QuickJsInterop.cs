using System.Collections;
using System.Globalization;
using System.Reflection;
using Lite.QuickJs;
using Lite.Scripting.Dom;

namespace Lite.Scripting.Runtime;

/// <summary>The browser's managed boundary to a QuickJS realm.</summary>
public sealed class Engine : IDisposable
{
    private readonly QuickJsRuntime _runtime = new();
    private readonly QuickJsRealm _realm;
    private readonly Dictionary<object, QuickJsValue> _indexProxies = new(ReferenceEqualityComparer.Instance);
    private bool _disposed;

    public Engine()
    {
        _realm = _runtime.CreateRealm();
        _runtime.SetRejectionHandler((realm, promise, reason, handled) =>
        {
            if (ReferenceEquals(realm, _realm))
                PromiseRejection?.Invoke(new JsValue(this, promise.Clone()),
                    new JsValue(this, reason.Clone()), handled);
        });
    }

    public event Action<JsValue, JsValue, bool>? PromiseRejection;

    internal QuickJsRuntime Runtime => _runtime;
    internal QuickJsRealm Realm => _realm;
    internal Func<string, string, bool, string>? SourceTransformer { get; set; }

    public JsValue Evaluate(string source, string filename = "<eval>") =>
        new(this, _realm.Eval(SourceTransformer?.Invoke(source, filename, false) ?? source, filename));

    public void Execute(string source, string filename = "<eval>")
    {
        using var result = _realm.Eval(SourceTransformer?.Invoke(source, filename, false) ?? source, filename);
        ProcessTasks();
    }

    public void ProcessTasks() => _runtime.PumpJobs();

    /// <summary>Re-binds the engine to the calling thread; see QuickJsRuntime.TransferToCurrentThread.</summary>
    internal void TransferToCurrentThread() => _runtime.TransferToCurrentThread();

    public JsValue GetValue(string name)
    {
        using var global = _realm.Global();
        return new(this, global.Get(name));
    }

    public void SetValue(string name, object? value)
    {
        using var global = _realm.Global();
        using var converted = ConvertToNative(value);
        global.Set(name, converted);
    }

    public JsValue Invoke(JsValue function, params object?[] args)
    {
        using var target = ConvertToNative(function);
        var nativeArgs = args.Select(ConvertToNative).ToArray();
        try { return new(this, target.Call(arguments: nativeArgs)); }
        finally { foreach (var arg in nativeArgs) arg.Dispose(); }
    }

    public JsValue Call(JsValue function, JsValue thisValue, params object?[] args)
    {
        using var target = ConvertToNative(function);
        using var receiver = ConvertToNative(thisValue);
        var nativeArgs = args.Select(ConvertToNative).ToArray();
        try { return new(this, target.Call(receiver, nativeArgs)); }
        finally { foreach (var arg in nativeArgs) arg.Dispose(); }
    }

    public JsValue Construct(JsValue function, params object?[] args)
    {
        using var target = ConvertToNative(function);
        var nativeArgs = args.Select(ConvertToNative).ToArray();
        try { return new(this, target.Construct(nativeArgs)); }
        finally { foreach (var arg in nativeArgs) arg.Dispose(); }
    }

    internal QuickJsValue ConvertToNative(object? value)
    {
        if (value is JsValue js)
        {
            if (js.Native is { } native)
            {
                if (!ReferenceEquals(js.Engine, this))
                    throw new InvalidOperationException("JavaScript values cannot cross page runtimes");
                return native.Clone();
            }
            value = js.Scalar;
        }
        if (value is JsValueObject nativeObject) return nativeObject.Native.Clone();
        if (value is null) return _realm.Null();
        if (ReferenceEquals(value, JsValue.UndefinedMarker)) return _realm.Undefined();
        if (value is bool flag) return _realm.Bool(flag);
        if (value is string text) return _realm.String(text);
        if (value is char character) return _realm.String(character.ToString());
        if (value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal)
            return _realm.Number(System.Convert.ToDouble(value, CultureInfo.InvariantCulture));
        if (value is Type type) return BindConstructor(type);
        if (value is Delegate callback) return BindDelegate(callback);
        if (value is IDictionary dictionary)
        {
            var result = _realm.Object();
            foreach (DictionaryEntry entry in dictionary)
            {
                using var item = ConvertToNative(entry.Value);
                result.Set(System.Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? "", item);
            }
            return result;
        }
        if (value is IEnumerable enumerable)
        {
            using var array = _realm.Eval("[]");
            using var push = array.Get("push");
            foreach (var item in enumerable)
            {
                using var element = ConvertToNative(item);
                using var ignored = push.Call(array, element);
            }
            return array.Clone();
        }
        var hostType = value.GetType();
        var indexer = hostType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(p => p.Name == "Item" && p.GetIndexParameters() is [{ ParameterType: var key }]
                && key == typeof(int) && p.GetMethod is not null);
        if (indexer is null) return _realm.HostObjects.Wrap(value, binding => BindMembers(hostType, binding));
        if (_indexProxies.TryGetValue(value, out var cached)) return cached.Clone();
        using var target = _realm.HostObjects.Wrap(value, binding => BindMembers(hostType, binding));
        using var index = _realm.HostFunction("item", 1, (_, args) =>
            ConvertToNative(indexer.GetValue(value, [(int)args[0].AsNumber()])));
        using var named = _realm.HostFunction("namedItem", 1, (_, args) =>
            ConvertToNative((value as IJsNamedCollection)?.Named(args[0].AsString())));
        using var makeProxy = _realm.Eval("""
            (target, item, named, hasNames) => new Proxy(target, {
              get(t,p,r) {
                if (typeof p === 'string' && /^(0|[1-9][0-9]*)$/.test(p)) return item(+p) ?? undefined;
                if (p === Symbol.iterator) return function* () { for (let i = 0; i < t.length; i++) yield item(i); };
                if (typeof p === 'string' && hasNames && !Reflect.has(t,p)) return named(p) ?? undefined;
                return Reflect.get(t,p,r);
              },
              ownKeys(t) {
                const keys = Reflect.ownKeys(t);
                for (let i = 0; i < t.length; i++) keys.unshift(String(i));
                return keys;
              },
              getOwnPropertyDescriptor(t,p) {
                if (typeof p === 'string' && /^(0|[1-9][0-9]*)$/.test(p) && +p < t.length)
                  return { value: item(+p), writable: false, enumerable: true, configurable: true };
                return Reflect.getOwnPropertyDescriptor(t,p);
              }
            })
            """);
        using var hasNames = _realm.Bool(value is IJsNamedCollection);
        var proxy = makeProxy.Call(arguments: [target, index, named, hasNames]);
        _indexProxies.Add(value, proxy);
        return proxy.Clone();
    }

    private QuickJsValue BindDelegate(Delegate callback) => _realm.HostFunction(callback.Method.Name,
        callback.Method.GetParameters().Length, (_, args) =>
        {
            var converted = ConvertParameters(callback.Method.GetParameters(), args);
            try { return ConvertToNative(callback.DynamicInvoke(converted)); }
            catch (TargetInvocationException error) { throw error.InnerException ?? error; }
        });

    private QuickJsValue BindConstructor(Type type)
    {
        using var factory = _realm.HostFunction("create " + type.Name, 0, (_, args) =>
        {
            var candidates = type.GetConstructors();
            foreach (var constructor in candidates.OrderBy(c => c.GetParameters().Length))
            {
                if (!CanBind(constructor.GetParameters(), args.Length)) continue;
                try { return ConvertToNative(constructor.Invoke(ConvertParameters(constructor.GetParameters(), args))); }
                catch (TargetInvocationException error) { throw error.InnerException ?? error; }
            }
            throw new MissingMethodException($"No constructor for {type.Name} with {args.Length} arguments");
        });
        using var makeConstructor = _realm.Eval("(factory => function(...args){ return factory(...args); })");
        // Ordinary JavaScript functions can be called and used with new; returning the host
        // object supplies the constructed value in either form.
        return makeConstructor.Call(arguments: [factory]);
    }

    private void BindMembers(Type type, QuickJsHostBinding<object> binding)
    {
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length != 0 || property.GetMethod is null) continue;
            binding.Property(property.Name,
                target => ConvertToNative(property.GetValue(target)),
                property.SetMethod is null ? null : (target, next) =>
                    property.SetValue(target, ConvertArgument(property.PropertyType, next)));
        }
        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            binding.Property(field.Name, target => ConvertToNative(field.GetValue(target)),
                field.IsInitOnly ? null : (target, next) => field.SetValue(target, ConvertArgument(field.FieldType, next)));
        }
        foreach (var group in type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => !m.IsSpecialName && m.DeclaringType != typeof(object))
            .GroupBy(m => m.Name))
        {
            var methods = group.ToArray();
            binding.Method(group.Key, methods.Min(m => m.GetParameters().Length), (target, _, args) =>
            {
                var method = methods.FirstOrDefault(m => CanBind(m.GetParameters(), args.Length))
                    ?? throw new MissingMethodException($"No overload of {group.Key} accepts {args.Length} arguments");
                try { return ConvertToNative(method.Invoke(target, ConvertParameters(method.GetParameters(), args))); }
                catch (TargetInvocationException error) { throw error.InnerException ?? error; }
            });
        }
    }

    private static bool CanBind(ParameterInfo[] parameters, int supplied)
    {
        var required = parameters.Count(p => !p.IsOptional && p.GetCustomAttribute<ParamArrayAttribute>() is null);
        return supplied >= required && (supplied <= parameters.Length ||
            parameters.LastOrDefault()?.GetCustomAttribute<ParamArrayAttribute>() is not null);
    }

    private object?[] ConvertParameters(ParameterInfo[] parameters, QuickJsValue[] args)
    {
        var result = new object?[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].GetCustomAttribute<ParamArrayAttribute>() is not null)
            {
                var elementType = parameters[i].ParameterType.GetElementType()!;
                var array = Array.CreateInstance(elementType, Math.Max(0, args.Length - i));
                for (var j = i; j < args.Length; j++) array.SetValue(ConvertArgument(elementType, args[j]), j - i);
                result[i] = array;
            }
            else if (i < args.Length) result[i] = ConvertArgument(parameters[i].ParameterType, args[i]);
            else if (parameters[i].IsOptional) result[i] = parameters[i].DefaultValue;
            else result[i] = ConvertArgument(parameters[i].ParameterType, _realm.Undefined());
        }
        return result;
    }

    private object? ConvertArgument(Type type, QuickJsValue native)
    {
        var nullable = Nullable.GetUnderlyingType(type);
        type = nullable ?? type;
        if (type == typeof(JsValue)) return new JsValue(this, native.Clone());
        if (type == typeof(object)) return new JsValue(this, native.Clone()).ToObject();
        if (native.Kind is QuickJsKind.Null or QuickJsKind.Undefined)
            return type.IsValueType && nullable is null ? Activator.CreateInstance(type) : null;
        var host = _realm.HostObjects.Unwrap<object>(native);
        if (host is null && native.Identity != 0)
            host = _indexProxies.FirstOrDefault(p => p.Value.Identity == native.Identity).Key;
        if (host is not null && type.IsInstanceOfType(host)) return host;
        if (type == typeof(string)) return native.AsString();
        if (type == typeof(bool)) return native.AsBoolean();
        if (type.IsEnum) return Enum.ToObject(type, (int)native.AsNumber());
        if (type == typeof(double)) return native.AsNumber();
        if (type == typeof(float)) return (float)native.AsNumber();
        if (type == typeof(int)) return (int)native.AsNumber();
        if (type == typeof(long)) return (long)native.AsNumber();
        if (type == typeof(uint)) return (uint)native.AsNumber();
        if (type.IsArray)
        {
            using var length = native.Get("length");
            var count = Math.Clamp((int)length.AsNumber(), 0, 1_000_000);
            var elementType = type.GetElementType()!;
            var array = Array.CreateInstance(elementType, count);
            for (var i = 0; i < count; i++)
            {
                using var item = native.Get(i.ToString(CultureInfo.InvariantCulture));
                array.SetValue(ConvertArgument(elementType, item), i);
            }
            return array;
        }
        if (host is not null) return host;
        throw new InvalidCastException($"Cannot bind a JavaScript value to {type.Name}");
    }

    internal object? ToObject(QuickJsValue value)
    {
        var host = _realm.HostObjects.Unwrap<object>(value);
        if (host is null && value.Identity != 0)
            host = _indexProxies.FirstOrDefault(p => p.Value.Identity == value.Identity).Key;
        if (host is not null) return host;
        return value.Kind switch
        {
            QuickJsKind.Null or QuickJsKind.Undefined => null,
            QuickJsKind.Boolean => value.AsBoolean(),
            QuickJsKind.Number => value.AsNumber(),
            QuickJsKind.String => value.AsString(),
            _ => ConvertObject(value)
        };
    }

    private object? ConvertObject(QuickJsValue value)
    {
        // Used for structured clone and test inspection. Cyclic or accessor-heavy page
        // objects stay as managed handles instead of recursively walking arbitrary script.
        using var stringify = _realm.Eval("JSON.stringify");
        try
        {
            using var json = stringify.Call(arguments: [value]);
            if (json.Kind == QuickJsKind.Undefined) return new JsValue(this, value.Clone());
            using var document = System.Text.Json.JsonDocument.Parse(json.AsString());
            return ReadJson(document.RootElement);
        }
        catch (QuickJsException) { return new JsValue(this, value.Clone()); }
    }

    private static object? ReadJson(System.Text.Json.JsonElement element) => element.ValueKind switch
    {
        System.Text.Json.JsonValueKind.Null => null,
        System.Text.Json.JsonValueKind.True => true,
        System.Text.Json.JsonValueKind.False => false,
        System.Text.Json.JsonValueKind.String => element.GetString(),
        System.Text.Json.JsonValueKind.Number => element.GetDouble(),
        System.Text.Json.JsonValueKind.Array => element.EnumerateArray().Select(ReadJson).ToArray(),
        System.Text.Json.JsonValueKind.Object => element.EnumerateObject()
            .ToDictionary(p => p.Name, p => ReadJson(p.Value), StringComparer.Ordinal),
        _ => null
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _runtime.Dispose();
    }
}

public sealed class JsValue
{
    internal static readonly object UndefinedMarker = new();
    internal Engine? Engine { get; }
    internal QuickJsValue? Native { get; }
    internal object? Scalar { get; }
    internal JsValue(Engine engine, QuickJsValue native) { Engine = engine; Native = native; }
    private JsValue(object? scalar) { Scalar = scalar; }
    public static JsValue Undefined { get; } = new(UndefinedMarker);
    public static JsValue Null { get; } = new(null);
    public static JsValue FromObject(Engine engine, object? value) =>
        new(engine, engine.ConvertToNative(value));
    public static implicit operator JsValue(string value) => new(value);
    public static implicit operator JsValue(bool value) => new(value);
    public static implicit operator JsValue(int value) => new(value);
    public static implicit operator JsValue(double value) => new(value);
    public bool IsUndefined() => Native?.Kind == QuickJsKind.Undefined || ReferenceEquals(Scalar, UndefinedMarker);
    public bool IsNull() => Native?.Kind == QuickJsKind.Null || (Native is null && Scalar is null);
    public bool IsBoolean() => Native?.Kind == QuickJsKind.Boolean || Scalar is bool;
    public bool IsNumber() => Native?.Kind == QuickJsKind.Number || Scalar is int or long or double or float;
    public bool IsString() => Native?.Kind == QuickJsKind.String || Scalar is string;
    public bool IsObject() => Native?.Kind is QuickJsKind.Object or QuickJsKind.Function;
    public bool IsCallable() => Native?.Kind == QuickJsKind.Function;
    public bool IsError() => Native?.IsError == true;
    public bool AsBoolean() => Native?.AsBoolean() ?? System.Convert.ToBoolean(Scalar, CultureInfo.InvariantCulture);
    public double AsNumber() => Native?.AsNumber() ?? System.Convert.ToDouble(Scalar, CultureInfo.InvariantCulture);
    public string AsString() => Native?.AsString() ?? (IsUndefined() ? "undefined" :
        System.Convert.ToString(Scalar, CultureInfo.InvariantCulture) ?? "null");
    public JsValue AsObject() => this;
    public JsValue Get(string name)
    {
        if (Native is null || Engine is null) return Undefined;
        return new JsValue(Engine, Native.Get(name));
    }
    public object? ToObject() => Native is not null && Engine is not null ? Engine.ToObject(Native) :
        ReferenceEquals(Scalar, UndefinedMarker) ? null : Scalar;
    public override string ToString() => AsString();
    public override bool Equals(object? obj)
    {
        if (obj is not JsValue other) return false;
        if (Native is { } left && other.Native is { } right) return left.SameValue(right);
        if (Native is null && other.Native is null) return Equals(Scalar, other.Scalar);
        return false;
    }
    public override int GetHashCode() => Native is { } native && native.Identity != 0
        ? native.Identity.GetHashCode() : Scalar?.GetHashCode() ?? 0;
    public static bool operator ==(JsValue? left, JsValue? right) =>
        ReferenceEquals(left, right) || left is not null && left.Equals(right);
    public static bool operator !=(JsValue? left, JsValue? right) => !(left == right);
}

public static class JsNumber
{
    public static JsValue Create(double number) => number;
}

public static class TypeConverter
{
    public static string ToString(JsValue value) => value.AsString();
    public static double ToNumber(JsValue value) => value.AsNumber();
    public static bool ToBoolean(JsValue value) => value.AsBoolean();
}

public sealed class JsObject : JsValueObject
{
    public JsObject(Engine engine) : base(engine) { }
}

public class JsValueObject
{
    private readonly Engine _engine;
    internal readonly QuickJsValue Native;
    public JsValueObject(Engine engine) { _engine = engine; Native = engine.Realm.Object(); }
    public void Set(string name, object? value)
    {
        using var native = _engine.ConvertToNative(value);
        Native.Set(name, native);
    }
}

internal sealed class JavaScriptException(JsValue error)
    : QuickJsException(error.ToString(), null, error.Native?.Clone())
{
    public JsValue Error => error;
}
