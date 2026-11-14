using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using Lite.QuickJs;
using Lite.Scripting.Dom;

namespace Lite.Scripting.Runtime;

/// <summary>A host object that resolves member reads the binder did not declare (a WindowProxy
/// answers the accessing window's globals). The binder installs a JS Proxy facade so page code
/// can read <c>win.Element</c>, <c>win.getComputedStyle</c> and friends.</summary>
internal interface IJsDynamicMembers
{
    /// <summary>Resolves a member in the <paramref name="requesting"/> realm: a cross-realm
    /// value cannot be shared, so an interface name answers a branding constructor and other
    /// globals answer primitives.</summary>
    QuickJsValue GetMember(QuickJsRealm requesting, string name);
}

/// <summary>The browser's managed boundary to a QuickJS realm.</summary>
public sealed class Engine : IDisposable
{
    private readonly QuickJsRuntime _runtime = new();
    private readonly QuickJsRealm _realm;
    private readonly Dictionary<object, QuickJsValue> _indexProxies = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<nint, object> _proxyByIdentity = new();
    private readonly HashSet<object> _branded = new(ReferenceEqualityComparer.Instance);
    private QuickJsValue? _newArray;
    private QuickJsValue? _arrayPush;
    private QuickJsValue? _defineTag;
    private QuickJsValue? _makeDatasetProxy;
    private QuickJsValue? _datasetGet;
    private QuickJsValue? _datasetSet;
    private QuickJsValue? _datasetHas;
    private QuickJsValue? _datasetDelete;
    private QuickJsValue? _datasetKeys;
    private QuickJsValue? _makeLegacyProxy;
    private QuickJsValue? _makeProxy;
    private QuickJsValue? _makeConstructor;
    private QuickJsValue? _makeDynamicProxy;
    private QuickJsValue? _stringify;
    private bool _elementProtoInstalled;
    private QuickJsValue? _elemGet;
    private QuickJsValue? _elemSet;
    private QuickJsValue? _elemCall;
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
            _newArray ??= _realm.Eval("() => []");
            _arrayPush ??= _realm.Eval("(a, v) => { a.push(v); }");
            using var array = _newArray.Call();
            foreach (var item in enumerable)
            {
                using var element = ConvertToNative(item);
                using var ignored = _arrayPush.Call(arguments: [array, element]);
            }
            return array.Clone();
        }
        if (value is JsDataset dataset)
        {
            // DOMStringMap (HTML §2.7.3): camelCase named properties over data-* attributes with
            // has/delete/enumeration. The proxy target is a bare object carrying only the
            // toStringTag — DOMStringMap exposes no methods, and its ownKeys must enumerate
            // exactly the named properties. Every string key is a candidate supported name —
            // the empty data attribute ("data-") and trailing-hyphen forms ("data-id-") are
            // supported names too, so no identifier gate may filter them out.
            if (_indexProxies.TryGetValue(value, out var cachedDataset)) return cachedDataset.Clone();
            using var datasetTarget = _realm.HostObjects.Wrap(dataset, _ => { });
            _defineTag ??= _realm.Eval("(o, tag) => Object.defineProperty(o, Symbol.toStringTag, { value: tag })");
            using var tagValue = _realm.String("DOMStringMap");
            using var ignoredTag = _defineTag.Call(arguments: [datasetTarget, tagValue]);
            _datasetGet ??= _realm.HostFunction("dataset get", 2, (_, args) =>
            {
                var ds = _realm.HostObjects.Unwrap<JsDataset>(args[0]);
                var found = ds?.get(args[1].AsString());
                return found is null ? _realm.Undefined() : ConvertToNative(found);
            });
            _datasetSet ??= _realm.HostFunction("dataset set", 3, (_, args) =>
            {
                _realm.HostObjects.Unwrap<JsDataset>(args[0])?.set(args[1].AsString(), args[2].AsString());
                return _realm.Undefined();
            });
            _datasetHas ??= _realm.HostFunction("dataset has", 2, (_, args) =>
                _realm.Bool(_realm.HostObjects.Unwrap<JsDataset>(args[0])?.has(args[1].AsString()) ?? false));
            _datasetDelete ??= _realm.HostFunction("dataset delete", 2, (_, args) =>
            {
                _realm.HostObjects.Unwrap<JsDataset>(args[0])?.remove(args[1].AsString());
                return _realm.Undefined();
            });
            _datasetKeys ??= _realm.HostFunction("dataset keys", 1, (_, args) =>
                ConvertToNative(_realm.HostObjects.Unwrap<JsDataset>(args[0])?.keys() ?? []));
            _makeDatasetProxy ??= _realm.Eval("""
                (target, get, set, has, del, keys) => new Proxy(target, {
                  get(t, p, r) {
                    if (typeof p === 'string' && has(t, p)) return get(t, p);
                    return Reflect.get(t, p, r);
                  },
                  set(t, p, v, r) {
                    if (typeof p === 'string') { set(t, p, String(v)); return true; }
                    return Reflect.set(t, p, v, r);
                  },
                  has(t, p) {
                    return (typeof p === 'string' && has(t, p)) || Reflect.has(t, p);
                  },
                  deleteProperty(t, p) {
                    if (typeof p === 'string' && has(t, p)) { del(t, p); return true; }
                    return Reflect.deleteProperty(t, p);
                  },
                  ownKeys(t) {
                    return [...keys(t), ...Reflect.ownKeys(t)];
                  },
                  getOwnPropertyDescriptor(t, p) {
                    if (typeof p === 'string' && has(t, p))
                      return { value: get(t, p), writable: true, enumerable: true, configurable: true };
                    return Reflect.getOwnPropertyDescriptor(t, p);
                  }
                })
                """);
            var proxy2 = _makeDatasetProxy.Call(arguments: [datasetTarget, _datasetGet, _datasetSet, _datasetHas, _datasetDelete, _datasetKeys]);
            CacheIndexProxy(value, proxy2);
            return proxy2.Clone();
        }
        var hostType = value.GetType();
        var typeMembers = GetTypeMembers(hostType);
        var indexer = typeMembers.IntIndexer;
        if (indexer is null)
        {
            var useElementProto = value is JsElement && EnsureElementPrototype();
            var wrapped = _realm.HostObjects.Wrap(value,
                useElementProto ? _ => { } : binding => BindMembers(typeMembers, binding));
            if (!_branded.Contains(value)) Brand(wrapped, value);
            return wrapped is null ? null! : WithDynamicMembers(value, wrapped);
        }
        if (_indexProxies.TryGetValue(value, out var cached)) return cached.Clone();
        using var target = _realm.HostObjects.Wrap(value, binding => BindMembers(typeMembers, binding));
        if (!_branded.Contains(value)) Brand(target, value);
        using var index = _realm.HostFunction("item", 1, (_, args) =>
        {
            // WebIDL unsigned long: ToUint32 wraps — item(4294967296) reads index 0, item(-2)
            // reads 2^32-2 (beyond any real length, so null). The narrowing saturates so an
            // index beyond any real length never overflows the int indexer.
            var n = args[0].AsNumber();
            var u = double.IsFinite(n) ? ((n % 4294967296.0) + 4294967296.0) % 4294967296.0 : 0d;
            return ConvertToNative(indexer.GetValue(value, [(int)Math.Min(u, int.MaxValue)]));
        });
        using var named = _realm.HostFunction("namedItem", 1, (_, args) =>
            ConvertToNative((value as IJsNamedCollection)?.Named(args[0].AsString())));
        // DOM collections (NodeList, HTMLCollection, NamedNodeMap) get the full WebIDL legacy
        // platform-object semantics; other indexed host objects keep the simpler shape.
        if (value is JsNodeList || value is IJsNamedCollection)
        {
            using var names = _realm.HostFunction("names", 0, (_, _) =>
                ConvertToNative((value as IJsNamedCollection)?.SupportedNames() ?? Array.Empty<string>()));
            using var memberNames = ConvertToNative(typeMembers.InterfaceMemberNames);
            _makeLegacyProxy ??= _realm.Eval("""
                (target, item, named, names, members, hasNames) => {
                  const INDEX = /^(0|[1-9][0-9]*)$/;
                  // A canonical array-index property name (WebIDL §3.9): ToUint32 round-trip,
                  // strictly below 2^32-1 — at and beyond, the name is a supported property
                  // name, not an index (dom/collections/HTMLCollection-supported-property-indices).
                  const isIndex = p => typeof p === 'string' && INDEX.test(p) && +p < 4294967295;
                  // Anything that is not a canonical array index is a candidate named property —
                  // including numeric names at and beyond 2^32-1.
                  const isNamed = p => hasNames && typeof p === 'string' && !isIndex(p);
                  const supported = p => named(p) != null;
                  let proxy;
                  proxy = new Proxy(target, {
                    get(t, p, r) {
                      if (typeof p === 'string') {
                        if (isIndex(p)) return +p < t.length ? (item(+p) ?? undefined) : undefined;
                        if (isNamed(p) && !Reflect.has(t, p)) return named(p) ?? undefined;
                        // Interface members throw on a foreign receiver: a getter's this must be
                        // the platform object itself (Object.create(collection).length).
                        if (r !== proxy && members.includes(p)) throw new TypeError('Illegal invocation');
                      } else if (p === Symbol.iterator) {
                        return function* () { const n = t.length; for (let i = 0; i < n; i++) yield item(i); };
                      }
                      return Reflect.get(t, p, r);
                    },
                    set(t, p, v, r) {
                      // A foreign receiver creates the own property there, exactly like ordinary
                      // [[Set]] through a prototype (Object.create(collection).named = x).
                      if (r !== proxy) return Reflect.set(t, p, v, r);
                      if (typeof p === 'string' && (isIndex(p) || (isNamed(p) && !Reflect.has(t, p) && supported(p))))
                        return false;
                      return Reflect.set(t, p, v, r);
                    },
                    has(t, p) {
                      if (p === Symbol.iterator) return true;
                      if (typeof p === 'string') {
                        if (isIndex(p)) return +p < t.length;
                        if (isNamed(p) && !Reflect.has(t, p)) return supported(p);
                      }
                      return Reflect.has(t, p);
                    },
                    deleteProperty(t, p) {
                      // Supported properties are protected even though their descriptor reads
                      // configurable — the DOM spec's [[Delete]] guards them directly. Expandos
                      // (including ones shadowing a named property) delete by ordinary rules.
                      if (Reflect.has(t, p)) return Reflect.deleteProperty(t, p);
                      if (typeof p === 'string') {
                        if (isIndex(p)) return +p >= t.length;
                        if (isNamed(p)) return !supported(p);
                      }
                      return true;
                    },
                    defineProperty(t, p, desc) {
                      if (typeof p === 'string' && (isIndex(p) || (isNamed(p) && !Reflect.has(t, p) && supported(p))))
                        return false;
                      return Reflect.defineProperty(t, p, desc);
                    },
                    ownKeys(t) {
                      const keys = [];
                      for (let i = 0; i < t.length; i++) keys.push(String(i));
                      if (hasNames) for (const n of names()) if (!keys.includes(n)) keys.push(n);
                      // Expando own properties follow; the interface members do not — they are
                      // prototype members, never own properties of the collection.
                      for (const k of Reflect.ownKeys(t))
                        if (!keys.includes(k) && !(typeof k === 'string' && members.includes(k))) keys.push(k);
                      return keys;
                    },
                    getOwnPropertyDescriptor(t, p) {
                      if (typeof p === 'string' && isIndex(p)) {
                        if (+p >= t.length) return undefined;
                        return { value: item(+p), writable: false, enumerable: true, configurable: true };
                      }
                      if (typeof p === 'string' && isNamed(p) && !Reflect.has(t, p)) {
                        const found = named(p);
                        if (found != null)
                          return { value: found, writable: false, enumerable: false, configurable: true };
                        return undefined;
                      }
                      return Reflect.getOwnPropertyDescriptor(t, p);
                    }
                  });
                  return proxy;
                }
                """);
            using var hasNamesLegacy = _realm.Bool(value is IJsNamedCollection);
            var legacyProxy = _makeLegacyProxy.Call(arguments: [target, index, named, names, memberNames,
                hasNamesLegacy]);
            CacheIndexProxy(value, legacyProxy);
            return legacyProxy.Clone();
        }
        _makeProxy ??= _realm.Eval("""
            (target, item, named, hasNames) => new Proxy(target, {
              get(t,p,r) {
                if (typeof p === 'string' && /^(0|[1-9][0-9]*)$/.test(p)) return item(+p) ?? undefined;
                if (p === Symbol.iterator) return function* () { for (let i = 0; i < t.length; i++) yield item(i); };
                if (typeof p === 'string' && hasNames && !Reflect.has(t,p)) return named(p) ?? undefined;
                return Reflect.get(t,p,r);
              },
              has(t, p) {
                if (typeof p === 'string' && /^(0|[1-9][0-9]*)$/.test(p)) return +p < t.length;
                return Reflect.has(t, p);
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
        var proxy = _makeProxy.Call(arguments: [target, index, named, hasNames]);
        CacheIndexProxy(value, proxy);
        return proxy.Clone();
    }

    private void CacheIndexProxy(object value, QuickJsValue proxy)
    {
        _indexProxies.Add(value, proxy);
        if (proxy.Identity != 0) _proxyByIdentity[proxy.Identity] = value;
    }

    private object? UnwrapHost(QuickJsValue value)
    {
        var host = _realm.HostObjects.Unwrap<object>(value);
        if (host is null && value.Identity != 0) _proxyByIdentity.TryGetValue(value.Identity, out host);
        return host;
    }

    private bool EnsureElementPrototype()
    {
        if (_elementProtoInstalled) return true;
        using var hasProtos = _realm.Eval("Boolean(globalThis.__lite_brand && globalThis.Element && globalThis.Element.prototype)");
        if (hasProtos.Kind != QuickJsKind.Boolean || !hasProtos.AsBoolean()) return false;

        var typeMembers = GetTypeMembers(typeof(JsElement));
        var props = typeMembers.Properties;
        var methods = typeMembers.Methods;

        _elemGet = _realm.HostFunction("__elemGet", 2, (_, args) =>
        {
            if (UnwrapHost(args[0]) is not JsElement target)
                throw JsErrors.Native(this, "TypeError", "Illegal invocation");
            var prop = props[(int)args[1].AsNumber()];
            return ConvertToNative(prop.Getter(target));
        });
        _elemSet = _realm.HostFunction("__elemSet", 3, (_, args) =>
        {
            if (UnwrapHost(args[0]) is not JsElement target)
                throw JsErrors.Native(this, "TypeError", "Illegal invocation");
            var prop = props[(int)args[1].AsNumber()];
            InvokePropertySetter(prop, target, args[2]);
            return _realm.Undefined();
        });
        _elemCall = _realm.HostFunction("__elemCall", 2, (_, args) =>
        {
            if (UnwrapHost(args[0]) is not JsElement target)
                throw JsErrors.Native(this, "TypeError", "Illegal invocation");
            var group = methods[(int)args[1].AsNumber()];
            var callArgs = args.Length > 2 ? args[2..] : [];
            return InvokeMethodGroup(group, target, callArgs);
        });

        using var propNames = ConvertToNative(props.Select(p => p.Name).ToArray());
        using var propWritable = ConvertToNative(props.Select(p => p.Setter is not null).ToArray());
        using var methodNames = ConvertToNative(methods.Select(m => m.Name).ToArray());
        using var methodArities = ConvertToNative(methods.Select(m => m.MinArity).ToArray());
        using var installer = _realm.Eval("""
            (get, set, call, propNames, propWritable, methodNames, methodArities) => {
              const targets = [
                globalThis.Element && globalThis.Element.prototype,
                globalThis.CharacterData && globalThis.CharacterData.prototype,
                globalThis.DocumentType && globalThis.DocumentType.prototype,
                globalThis.DocumentFragment && globalThis.DocumentFragment.prototype,
              ].filter(Boolean);
              const descs = Object.create(null);
              for (let i = 0; i < propNames.length; i++) {
                const idx = i;
                const name = propNames[i];
                const getter = function () { return get(this, idx); };
                const setter = propWritable[i] ? function (v) { set(this, idx, v); } : undefined;
                descs[name] = { get: getter, set: setter, enumerable: true, configurable: true };
              }
              for (let i = 0; i < methodNames.length; i++) {
                const idx = i;
                const name = methodNames[i];
                const fn = function (...args) { return call(this, idx, ...args); };
                Object.defineProperty(fn, 'name', { value: name, configurable: true });
                Object.defineProperty(fn, 'length', { value: methodArities[i], configurable: true });
                descs[name] = { value: fn, writable: true, enumerable: true, configurable: true };
              }
              for (const proto of targets) {
                for (const k of Object.keys(descs)) {
                  if (!Object.prototype.hasOwnProperty.call(proto, k)) {
                    Object.defineProperty(proto, k, descs[k]);
                  }
                }
              }
              if (globalThis.HTMLMarqueeElement && globalThis.HTMLMarqueeElement.prototype && descs.loop)
                Object.defineProperty(globalThis.HTMLMarqueeElement.prototype, 'loop', descs.loop);
              if (globalThis.HTMLFrameElement && globalThis.HTMLFrameElement.prototype) {
                if (descs.name) Object.defineProperty(globalThis.HTMLFrameElement.prototype, 'name', descs.name);
                if (descs.src) Object.defineProperty(globalThis.HTMLFrameElement.prototype, 'src', descs.src);
              }
            }
            """);
        using var ignored = installer.Call(arguments: [_elemGet, _elemSet, _elemCall, propNames, propWritable, methodNames, methodArities]);
        _elementProtoInstalled = true;
        return true;
    }

    private QuickJsValue? _brand;

    /// <summary>Chains a host wrapper into its Web IDL interface prototype so script-visible
    /// identity (instanceof, constructor, toString tag) matches the interface it exposes.</summary>
    private void Brand(QuickJsValue wrapped, object value)
    {
        var interfaceName = Dom.WebIdlInterfaces.For(value);
        if (interfaceName is null)
        {
            _branded.Add(value);
            return;
        }
        _brand ??= _realm.Eval("(o, n) => globalThis.__lite_brand ? (__lite_brand(o, n), true) : false");
        using var name = _realm.String(interfaceName);
        using var applied = _brand.Call(arguments: [wrapped, name]);
        if (applied.Kind == QuickJsKind.Boolean && applied.AsBoolean())
            _branded.Add(value);
    }

    private QuickJsValue BindDelegate(Delegate callback)
    {
        var methodSpec = GetMethodSpec(callback.Method);
        return _realm.HostFunction(callback.Method.Name, methodSpec.Parameters.Length, (_, args) =>
        {
            var converted = ConvertParameters(methodSpec.Parameters, args);
            try
            {
                var result = callback.DynamicInvoke(converted);
                // A void delegate DynamicInvokes to null, but WebIDL void operations must
                // produce undefined: testharness rejects add_cleanup callbacks returning any
                // value, which cascaded whole url-runner files to NOTRUN.
                return methodSpec.IsVoid ? _realm.Undefined() : ConvertToNative(result);
            }
            catch (TargetInvocationException error) { throw error.InnerException ?? error; }
        });
    }

    private QuickJsValue BindConstructor(Type type)
    {
        var candidates = GetTypeMembers(type).Constructors;
        using var factory = _realm.HostFunction("create " + type.Name, 0, (_, args) =>
        {
            ConstructorSpec? constructor = null;
            for (var i = 0; i < candidates.Length; i++)
            {
                if (CanBind(candidates[i], args.Length)) { constructor = candidates[i]; break; }
            }
            if (constructor is null)
            {
                // WebIDL constructors ignore trailing arguments beyond their declared ones.
                for (var i = candidates.Length - 1; i >= 0; i--)
                {
                    if (candidates[i].Parameters.Length <= args.Length) { constructor = candidates[i]; break; }
                }
                if (constructor is null)
                    throw JsErrors.Native(this, "TypeError",
                        $"No constructor for {type.Name} with {args.Length} arguments");
            }
            try { return ConvertToNative(constructor.Constructor.Invoke(ConvertParameters(constructor.Parameters, args))); }
            catch (TargetInvocationException error) { throw error.InnerException ?? error; }
        });
        // WebIDL interface constructors throw when called without new (the [Constructor]
        // shapes — Event, URL, MessageChannel, …) — only legacy constructors are callable.
        _makeConstructor ??= _realm.Eval("""
            (factory => function (...args) {
              if (!new.target)
                throw new TypeError("Failed to construct: Please use the 'new' operator to call the constructor.");
              return factory(...args);
            })
            """);
        // Ordinary JavaScript functions can be called and used with new; returning the host
        // object supplies the constructed value in either form.
        return _makeConstructor.Call(arguments: [factory]);
    }

    private readonly Dictionary<object, Dictionary<nint, QuickJsValue>> _dynamicProxies = new(ReferenceEqualityComparer.Instance);

    /// <summary>Wraps a host object whose type resolves dynamic members (a WindowProxy answers a
    /// window's remaining globals) in a JS Proxy: declared members win, and any other string key
    /// is resolved by <see cref="IJsDynamicMembers.GetMember"/>, which answers a value of the
    /// ACCESSING realm — a cross-realm object graph cannot be shared, so an interface name
    /// answers a constructor whose Symbol.hasInstance brand-checks the host object (what
    /// <c>elt instanceof frame.contentWindow.Element</c> observes) and other globals answer
    /// their primitive value or undefined. The proxy is cached per (value, realm) so repeated
    /// reads keep one identity: <c>event.source === frame.contentWindow</c> must hold.</summary>
    private QuickJsValue WithDynamicMembers(object value, QuickJsValue wrapped)
    {
        if (value is not IJsDynamicMembers dynamicMembers) return wrapped;
        var realmKey = _realm.Handle;
        if (!_dynamicProxies.TryGetValue(value, out var perRealm))
        {
            perRealm = [];
            _dynamicProxies[value] = perRealm;
        }
        if (perRealm.TryGetValue(realmKey, out var cached)) return cached.Clone();
        using var lookup = _realm.HostFunction("__member", 1, (_, args) =>
            dynamicMembers.GetMember(_realm, args.Length > 0 ? args[0].AsString() : ""));
        _makeDynamicProxy ??= _realm.Eval("""
            ((target, lookup) => new Proxy(target, {
              get(t, k, r) {
                if (typeof k !== 'string') return Reflect.get(t, k, r);
                if (k in t) return Reflect.get(t, k, r);
                return lookup(k);
              }
            }))
            """);
        using var proxy = _makeDynamicProxy.Call(arguments: [wrapped, lookup]);
        perRealm[realmKey] = proxy.Clone();
        return proxy.Clone();
    }

    internal QuickJsValue BrandConstructor(string interfaceName, Func<object, bool> isInstance) =>
        BrandConstructor(_realm, interfaceName, isInstance);

    /// <summary>An interface constructor of <paramref name="realm"/> that brand-checks host
    /// objects through <paramref name="isInstance"/>: the hasInstance test is all a cross-realm
    /// <c>instanceof</c> observes, and the constructor itself is never callable.</summary>
    internal static QuickJsValue BrandConstructor(QuickJsRealm realm, string interfaceName, Func<object, bool> isInstance)
    {
        using var fn = realm.HostFunction(interfaceName, 0, (_, _) =>
            throw new JavaScriptException((JsValue)"Illegal constructor"));
        using var check = realm.HostFunction("isInstance", 1, (_, args) =>
        {
            if (args.Length == 0) return realm.Bool(false);
            var host = realm.HostObjects.Unwrap<object>(args[0]);
            return realm.Bool(host is not null && isInstance(host));
        });
        using var decorate = realm.Eval("""
            ((fn, check) => {
              Object.defineProperty(fn, Symbol.hasInstance, {
                value: (v) => { try { return check(v); } catch (e) { return false; } },
                configurable: true,
              });
              return fn;
            })
            """);
        return decorate.Call(arguments: [fn, check]);
    }

    private void BindMembers(TypeMembers typeMembers, QuickJsHostBinding<object> binding)
    {
        foreach (var property in typeMembers.Properties)
        {
            binding.Property(property.Name,
                target => ConvertToNative(property.Getter(target)),
                property.Setter is null ? null : (target, next) => InvokePropertySetter(property, target, next));
        }
        foreach (var group in typeMembers.Methods)
        {
            binding.Method(group.Name, group.MinArity, (target, _, args) => InvokeMethodGroup(group, target, args));
        }
    }

    private void InvokePropertySetter(PropertySpec property, object target, QuickJsValue next)
    {
        try { property.Setter!(target, ConvertArgument(property.MemberType, next)); }
        // Reflection wraps the setter's own JavaScriptException in a
        // TargetInvocationException — unwrap it so the typed error object surfaces.
        catch (TargetInvocationException error) when (error.InnerException is JavaScriptException)
        { throw error.InnerException; }
        // A property setter has no overload fallthrough: a wrong-typed value (for
        // example TreeWalker.currentNode = nonNode) must raise a catchable TypeError.
        catch (InvalidCastException error) { throw JsErrors.Native(this, "TypeError", error.Message); }
    }

    private QuickJsValue InvokeMethodGroup(MethodGroupSpec group, object target, QuickJsValue[] args)
    {
        // WebIDL host behavior: arguments beyond an operation's declared parameters are
        // ignored (reCAPTCHA calls appendChild(node, reference)), and a match that
        // cannot convert an argument falls through to the next overload. Too few
        // arguments still raises — a browser throws TypeError there.
        var methods = group.Candidates;
        var hasDirectMatch = false;
        string? bindingFailure = null;
        for (var pass = 0; pass < 2; pass++)
        {
            for (var i = 0; i < methods.Length; i++)
            {
                var candidate = methods[i];
                if (pass == 0)
                {
                    if (!CanBind(candidate, args.Length)) continue;
                    hasDirectMatch = true;
                }
                else
                {
                    if (hasDirectMatch || candidate.Parameters.Length > args.Length) continue;
                }
                object?[] arguments;
                try { arguments = ConvertParameters(candidate.Parameters, args); }
                catch (InvalidCastException error) { bindingFailure = error.Message; continue; }
                try
                {
                    var result = candidate.Method.Invoke(target, arguments);
                    // Void operations must return undefined, not null (see BindDelegate).
                    return candidate.IsVoid ? _realm.Undefined() : ConvertToNative(result);
                }
                catch (TargetInvocationException error) { throw error.InnerException ?? error; }
            }
            if (hasDirectMatch) break;
        }
        // WebIDL raises a TypeError (not an engine-internal error) when no overload can
        // take the supplied arguments, so page code can catch it by constructor.
        throw JsErrors.Native(this, "TypeError",
            bindingFailure ?? $"No overload of {group.Name} accepts {args.Length} arguments");
    }

    private static bool CanBind(MethodSpec spec, int supplied) =>
        supplied >= spec.RequiredCount && (supplied <= spec.Parameters.Length || spec.HasParamArray);

    private static bool CanBind(ConstructorSpec spec, int supplied) =>
        supplied >= spec.RequiredCount && (supplied <= spec.Parameters.Length || spec.HasParamArray);

    private object?[] ConvertParameters(ParamSpec[] parameters, QuickJsValue[] args)
    {
        if (parameters.Length == 0) return [];
        var result = new object?[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            var param = parameters[i];
            if (param.IsParamArray)
            {
                var elementType = param.ParamArrayElementType!;
                var array = Array.CreateInstance(elementType, Math.Max(0, args.Length - i));
                for (var j = i; j < args.Length; j++) array.SetValue(ConvertArgument(elementType, args[j]), j - i);
                result[i] = array;
            }
            else if (i < args.Length) result[i] = ConvertParameter(param, args[i]);
            else if (param.IsOptional) result[i] = param.DefaultValue;
            else result[i] = ConvertParameter(param, _realm.Undefined());
        }
        return result;
    }

    /// <summary>Converts one argument for a declared parameter: a nullable string
    /// (<c>string?</c>, WebIDL's nullable DOMString) keeps null/undefined as a DOM null, while a
    /// plain DOMString converts them to "null"/"undefined".</summary>
    private object? ConvertParameter(ParamSpec parameter, QuickJsValue native)
    {
        if (parameter.IsNullableString && native.Kind is (QuickJsKind.Null or QuickJsKind.Undefined))
            return null;
        return ConvertArgument(parameter.ParameterType, native);
    }

    private static readonly ConcurrentDictionary<Type, TypeMembers> TypeMembersCache = new();
    private static readonly ConcurrentDictionary<MethodInfo, MethodSpec> DelegateMethodCache = new();

    private static TypeMembers GetTypeMembers(Type type) => TypeMembersCache.GetOrAdd(type, BuildTypeMembers);
    private static MethodSpec GetMethodSpec(MethodInfo method) => DelegateMethodCache.GetOrAdd(method, BuildMethodSpec);

    private static TypeMembers BuildTypeMembers(Type type)
    {
        var nullabilityContext = new NullabilityInfoContext();
        PropertyInfo? indexer = null;
        var properties = new List<PropertySpec>();
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var indexParams = property.GetIndexParameters();
            if (indexParams.Length > 0)
            {
                if (indexer is null && property.Name == "Item" && indexParams is [{ ParameterType: var key }]
                    && key == typeof(int) && property.GetMethod is not null)
                    indexer = property;
                continue;
            }
            if (property.GetMethod is null) continue;
            properties.Add(new PropertySpec(
                property.Name,
                property.PropertyType,
                property.GetValue,
                property.SetMethod is null ? null : property.SetValue));
        }
        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            properties.Add(new PropertySpec(
                field.Name,
                field.FieldType,
                field.GetValue,
                field.IsInitOnly ? null : field.SetValue));
        }
        var methodGroups = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => !m.IsSpecialName && m.DeclaringType != typeof(object))
            .GroupBy(m => m.Name)
            .Select(g =>
            {
                var specs = g.Select(m => BuildMethodSpec(m, nullabilityContext))
                    .OrderByDescending(m => m.Parameters.Length)
                    .ToArray();
                return new MethodGroupSpec(g.Key, specs.Min(m => m.Parameters.Length), specs);
            })
            .ToArray();
        var constructors = type.GetConstructors()
            .OrderBy(c => c.GetParameters().Length)
            .Select(c => BuildConstructorSpec(c, nullabilityContext))
            .ToArray();
        var memberNames = properties.Select(p => p.Name)
            .Concat(methodGroups.Select(m => m.Name))
            .Distinct()
            .ToArray();
        return new TypeMembers(indexer, properties.ToArray(), methodGroups, constructors, memberNames);
    }

    private static MethodSpec BuildMethodSpec(MethodInfo method) =>
        BuildMethodSpec(method, new NullabilityInfoContext());

    private static MethodSpec BuildMethodSpec(MethodInfo method, NullabilityInfoContext nullability)
    {
        var parameters = BuildParamSpecs(method.GetParameters(), nullability);
        var required = parameters.Count(p => !p.IsOptional && !p.IsParamArray);
        var hasParamArray = parameters.Length > 0 && parameters[^1].IsParamArray;
        return new MethodSpec(method, method.ReturnType == typeof(void), parameters, required, hasParamArray);
    }

    private static ConstructorSpec BuildConstructorSpec(ConstructorInfo constructor, NullabilityInfoContext nullability)
    {
        var parameters = BuildParamSpecs(constructor.GetParameters(), nullability);
        var required = parameters.Count(p => !p.IsOptional && !p.IsParamArray);
        var hasParamArray = parameters.Length > 0 && parameters[^1].IsParamArray;
        return new ConstructorSpec(constructor, parameters, required, hasParamArray);
    }

    private static ParamSpec[] BuildParamSpecs(ParameterInfo[] parameters, NullabilityInfoContext nullability)
    {
        if (parameters.Length == 0) return [];
        var specs = new ParamSpec[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            var p = parameters[i];
            var isParamArray = p.IsDefined(typeof(ParamArrayAttribute), inherit: false);
            var isNullableString = p.ParameterType == typeof(string) &&
                nullability.Create(p).WriteState == NullabilityState.Nullable;
            specs[i] = new ParamSpec(
                p.ParameterType,
                p.IsOptional,
                p.IsOptional ? p.DefaultValue : null,
                isParamArray,
                isParamArray ? p.ParameterType.GetElementType() : null,
                isNullableString);
        }
        return specs;
    }

    private sealed record PropertySpec(
        string Name,
        Type MemberType,
        Func<object, object?> Getter,
        Action<object, object?>? Setter);

    private sealed record ParamSpec(
        Type ParameterType,
        bool IsOptional,
        object? DefaultValue,
        bool IsParamArray,
        Type? ParamArrayElementType,
        bool IsNullableString);

    private sealed record MethodSpec(
        MethodInfo Method,
        bool IsVoid,
        ParamSpec[] Parameters,
        int RequiredCount,
        bool HasParamArray);

    private sealed record MethodGroupSpec(
        string Name,
        int MinArity,
        MethodSpec[] Candidates);

    private sealed record ConstructorSpec(
        ConstructorInfo Constructor,
        ParamSpec[] Parameters,
        int RequiredCount,
        bool HasParamArray);

    private sealed record TypeMembers(
        PropertyInfo? IntIndexer,
        PropertySpec[] Properties,
        MethodGroupSpec[] Methods,
        ConstructorSpec[] Constructors,
        string[] InterfaceMemberNames);

    private object? ConvertArgument(Type type, QuickJsValue native)
    {
        var nullable = Nullable.GetUnderlyingType(type);
        type = nullable ?? type;
        if (type == typeof(JsValue)) return new JsValue(this, native.Clone());
        if (type == typeof(object)) return new JsValue(this, native.Clone()).ToObject();
        // WebIDL DOMString conversion: null and undefined become "null"/"undefined" — a
        // DOMString parameter is not nullable unless the IDL says so (createElement(null)
        // creates an element named "null", setAttribute(name, undefined) stores "undefined").
        if (type == typeof(string)) return native.AsString();
        if (native.Kind is QuickJsKind.Null or QuickJsKind.Undefined)
            return type.IsValueType && nullable is null ? Activator.CreateInstance(type) : null;
        var host = UnwrapHost(native);
        if (host is not null && type.IsInstanceOfType(host)) return host;
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
        if (host is not null)
            // A wrong-typed host wrapper must never reach reflection: the ArgumentException's
            // CLR type names ("Object of type 'Lite.Scripting.Dom.JsDocument'...") used to
            // surface verbatim as an InternalError. Fail the overload conversion instead, so
            // BindMembers falls through and raises a catchable TypeError.
            throw new InvalidCastException(
                $"Failed to convert argument: the value is {WebIdlInterfaces.For(host) ?? "a host object"}, not of the expected interface");
        throw new InvalidCastException($"Cannot bind a JavaScript value to {type.Name}");
    }

    internal object? ToObject(QuickJsValue value)
    {
        var host = UnwrapHost(value);
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
        _stringify ??= _realm.Eval("JSON.stringify");
        try
        {
            using var json = _stringify.Call(arguments: [value]);
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
