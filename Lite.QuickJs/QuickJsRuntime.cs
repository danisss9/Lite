using System.Runtime.InteropServices;
using System.Reflection;
using System.Text;

namespace Lite.QuickJs;

/// <summary>Owns a native QuickJS runtime. All calls must occur on its owning thread.</summary>
internal sealed class QuickJsRuntime : IDisposable
{
    private int _threadId = Environment.CurrentManagedThreadId;
    private readonly Dictionary<nint, QuickJsRealm> _realms = new();
    private readonly Dictionary<int, Func<QuickJsRealm, QuickJsValue[], QuickJsValue>> _callbacks = new();
    private readonly Native.HostCallback _nativeCallback;
    private readonly Native.NormalizeCallback _normalizeCallback;
    private readonly Native.ModuleSourceCallback _moduleSourceCallback;
    private readonly Native.ReleaseCallback _releaseCallback;
    private readonly Native.RejectionCallback _rejectionCallback;
    private readonly Native.InterruptCallback _interruptCallback;
    private readonly Dictionary<nint, (Func<string, string, string> Normalize,
        Func<string, string?> Source)> _moduleProviders = new();
    private Action<QuickJsRealm, QuickJsValue, QuickJsValue, bool>? _onRejection;
    private Func<bool>? _shouldInterrupt;
    private int _nextCallback;
    private bool _disposed;

    internal nint Handle { get; private set; }

    internal QuickJsRuntime()
    {
        Handle = Native.lite_runtime_new();
        if (Handle == 0) throw new OutOfMemoryException("QuickJS runtime allocation failed");
        _nativeCallback = InvokeHost;
        _normalizeCallback = Normalize;
        _moduleSourceCallback = ModuleSource;
        _releaseCallback = Marshal.FreeCoTaskMem;
        _rejectionCallback = OnNativeRejection;
        _interruptCallback = OnNativeInterrupt;
        Native.lite_set_host_callback(Handle, _nativeCallback);
    }

    internal QuickJsRealm CreateRealm()
    {
        CheckThread();
        var handle = Native.lite_context_new(Handle);
        if (handle == 0) throw new OutOfMemoryException("QuickJS context allocation failed");
        var realm = new QuickJsRealm(this, handle);
        _realms.Add(handle, realm);
        return realm;
    }

    internal QuickJsValue CreateHostFunction(QuickJsRealm realm, string name, int arity,
        Func<QuickJsRealm, QuickJsValue[], QuickJsValue> callback)
    {
        CheckThread();
        var id = ++_nextCallback;
        _callbacks.Add(id, callback);
        return realm.Wrap(Native.lite_new_host_function(realm.Handle, id, name, arity));
    }

    internal void SetModuleProvider(QuickJsRealm realm, Func<string, string, string> normalize,
        Func<string, string?> source)
    {
        CheckThread();
        if (!ReferenceEquals(realm.Runtime, this)) throw new InvalidOperationException("Realm belongs to another runtime");
        _moduleProviders[realm.Handle] = (normalize, source);
        Native.lite_set_module_callbacks(Handle, _normalizeCallback, _moduleSourceCallback, _releaseCallback);
    }

    internal void SetRejectionHandler(Action<QuickJsRealm, QuickJsValue, QuickJsValue, bool> handler)
    {
        CheckThread();
        _onRejection = handler;
        Native.lite_set_rejection_callback(Handle, _rejectionCallback);
    }

    private void OnNativeRejection(nint context, nint promise, nint reason, int isHandled)
    {
        if (_onRejection is null || !_realms.TryGetValue(context, out var realm)) return;
        _onRejection(realm, realm.Borrow(promise), realm.Borrow(reason), isHandled != 0);
    }

    internal void SetInterruptHandler(Func<bool> shouldInterrupt)
    {
        CheckThread();
        _shouldInterrupt = shouldInterrupt;
        Native.lite_set_interrupt_callback(Handle, _interruptCallback);
    }

    private int OnNativeInterrupt()
    {
        try { return _shouldInterrupt?.Invoke() == true ? 1 : 0; }
        catch { return 1; }
    }

    private nint Normalize(nint context, nint baseName, nint specifier)
    {
        try
        {
            var result = _moduleProviders[context].Normalize(Marshal.PtrToStringUTF8(baseName) ?? "",
                Marshal.PtrToStringUTF8(specifier) ?? "");
            return Marshal.StringToCoTaskMemUTF8(result);
        }
        catch { return 0; }
    }

    private nint ModuleSource(nint context, nint moduleName, out nuint length)
    {
        length = 0;
        try
        {
            var result = _moduleProviders[context].Source(Marshal.PtrToStringUTF8(moduleName) ?? "");
            if (result is null) return 0;
            var bytes = Encoding.UTF8.GetBytes(result);
            var pointer = Marshal.AllocCoTaskMem(bytes.Length + 1);
            if (bytes.Length > 0) Marshal.Copy(bytes, 0, pointer, bytes.Length);
            Marshal.WriteByte(pointer, bytes.Length, 0);
            length = (nuint)bytes.Length;
            return pointer;
        }
        catch { return 0; }
    }

    private nint InvokeHost(nint context, int id, nint arguments, int count)
    {
        try
        {
            CheckThread();
            var realm = _realms[context];
            var values = new QuickJsValue[count];
            for (var i = 0; i < count; i++)
                values[i] = realm.Borrow(Native.lite_argument_at(arguments, i));
            var result = _callbacks[id](realm, values);
            if (!ReferenceEquals(result.Realm, realm))
                throw new InvalidOperationException("A host callback returned a value from another realm");
            return result.Detach();
        }
        catch (QuickJsException error)
        {
            if (error.ErrorValue is { } value && ReferenceEquals(value.Realm.Runtime, this))
                Native.lite_throw_value(context, value.Handle);
            else Native.lite_throw_error(context, error.Message);
            error.Dispose();
            return 0;
        }
        catch (Exception error)
        {
            Native.lite_throw_error(context, error.Message);
            return 0;
        }
    }

    internal void PumpJobs()
    {
        CheckThread();
        while (Native.lite_jobs_pending(Handle) != 0)
        {
            var result = Native.lite_execute_job(Handle, out var exceptionContext);
            if (result < 0)
            {
                var realm = _realms[exceptionContext];
                throw realm.Exception();
            }
            if (result == 0) break;
        }
    }

    internal void CollectGarbage()
    {
        CheckThread();
        Native.lite_gc(Handle);
    }

    internal void SetCanBlock(bool canBlock)
    {
        CheckThread();
        Native.lite_runtime_set_can_block(Handle, canBlock ? 1 : 0);
    }

    internal void Remove(QuickJsRealm realm)
    {
        _moduleProviders.Remove(realm.Handle);
        _realms.Remove(realm.Handle);
    }

    internal void CheckThread()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Environment.CurrentManagedThreadId != _threadId)
            throw new InvalidOperationException("QuickJS runtime accessed from another thread");
    }

    /// <summary>
    /// Re-binds the runtime to the calling thread so it can be handed off (e.g. a page parsed on
    /// a background thread committed to the UI thread). Only valid at quiescent points where the
    /// previous owner has finished using the runtime — QuickJS has no internal synchronization,
    /// so concurrent access from two threads remains forbidden. A no-op when the calling thread
    /// already owns the runtime. Also re-bases QuickJS's stack-overflow watermark, which is
    /// captured from the thread that created the runtime and is invalid on any other thread.
    /// </summary>
    internal void TransferToCurrentThread()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Environment.CurrentManagedThreadId == _threadId) return;
        _threadId = Environment.CurrentManagedThreadId;
        Native.lite_runtime_update_stack_top(Handle);
    }

    public void Dispose()
    {
        if (_disposed) return;
        CheckThread();
        foreach (var realm in _realms.Values.ToArray()) realm.Dispose();
        Native.lite_runtime_free(Handle);
        Handle = 0;
        _disposed = true;
        GC.KeepAlive(_nativeCallback);
        GC.KeepAlive(_normalizeCallback);
        GC.KeepAlive(_moduleSourceCallback);
        GC.KeepAlive(_releaseCallback);
        GC.KeepAlive(_rejectionCallback);
        GC.KeepAlive(_interruptCallback);
    }
}

/// <summary>CESU-8 conversion for strings crossing the QuickJS boundary. QuickJS stores strings
/// as UTF-16 code units; standard UTF-8 marshaling replaces lone surrogates with U+FFFD on both
/// sides (CharacterData-surrogates WPT). CESU-8 encodes every UTF-16 code unit — surrogates
/// included — as 1-3 bytes, which <c>JS_NewStringLen</c> stores verbatim, and
/// <c>JS_ToCStringLen2(cesu8=TRUE)</c> produces on the way back.</summary>
internal static class Cesu8
{
    internal static byte[] GetBytes(string value)
    {
        var ascii = true;
        foreach (var ch in value) if (ch >= 0x80) { ascii = false; break; }
        if (ascii) return Encoding.UTF8.GetBytes(value);
        var bytes = new byte[value.Length * 3];
        var count = 0;
        foreach (var ch in value)
        {
            if (ch < 0x80) bytes[count++] = (byte)ch;
            else if (ch < 0x800)
            {
                bytes[count++] = (byte)(0xC0 | (ch >> 6));
                bytes[count++] = (byte)(0x80 | (ch & 0x3F));
            }
            else
            {
                bytes[count++] = (byte)(0xE0 | (ch >> 12));
                bytes[count++] = (byte)(0x80 | ((ch >> 6) & 0x3F));
                bytes[count++] = (byte)(0x80 | (ch & 0x3F));
            }
        }
        return bytes[..count];
    }

    internal static string GetString(byte[] bytes)
    {
        foreach (var b in bytes) if (b >= 0x80) return Decode(bytes);
        return Encoding.UTF8.GetString(bytes);
    }

    private static string Decode(byte[] bytes)
    {
        var chars = new char[bytes.Length];
        var count = 0;
        for (var i = 0; i < bytes.Length; )
        {
            var b = bytes[i];
            if (b < 0x80) { chars[count++] = (char)b; i += 1; }
            else if ((b & 0xE0) == 0xC0 && i + 1 < bytes.Length)
            {
                chars[count++] = (char)(((b & 0x1F) << 6) | (bytes[i + 1] & 0x3F));
                i += 2;
            }
            else if ((b & 0xF0) == 0xE0 && i + 2 < bytes.Length)
            {
                chars[count++] = (char)(((b & 0x0F) << 12) | ((bytes[i + 1] & 0x3F) << 6) | (bytes[i + 2] & 0x3F));
                i += 3;
            }
            else if ((b & 0xF8) == 0xF0 && i + 3 < bytes.Length)
            {
                // Standard 4-byte UTF-8 (not produced by cesu8=TRUE, but harmless to accept).
                var cp = ((b & 0x07) << 18) | ((bytes[i + 1] & 0x3F) << 12) |
                         ((bytes[i + 2] & 0x3F) << 6) | (bytes[i + 3] & 0x3F);
                cp -= 0x10000;
                chars[count++] = (char)(0xD800 + (cp >> 10));
                chars[count++] = (char)(0xDC00 + (cp & 0x3FF));
                i += 4;
            }
            else { chars[count++] = '\uFFFD'; i += 1; }
        }
        return new string(chars, 0, count);
    }
}

/// <summary>A separate global object and value lifetime within a QuickJS runtime.</summary>
internal sealed class QuickJsRealm : IDisposable
{
    private readonly HashSet<QuickJsValue> _owned = [];
    private QuickJsHostObjects? _hostObjects;
    private bool _disposed;
    internal bool IsDisposed => _disposed;
    internal QuickJsRuntime Runtime { get; }
    internal QuickJsHostObjects HostObjects => _hostObjects ??= new(this);
    internal nint Handle { get; private set; }

    internal QuickJsRealm(QuickJsRuntime runtime, nint handle)
    {
        Runtime = runtime;
        Handle = handle;
    }

    internal QuickJsValue Wrap(nint handle)
    {
        if (handle == 0) throw Exception();
        var value = new QuickJsValue(this, handle, owned: true);
        _owned.Add(value);
        return value;
    }

    internal QuickJsValue Borrow(nint handle) => new(this, handle, owned: false);
    internal void Forget(QuickJsValue value) => _owned.Remove(value);

    internal QuickJsValue ImportFrom(QuickJsValue value)
    {
        Runtime.CheckThread();
        if (!ReferenceEquals(Runtime, value.Realm.Runtime))
            throw new InvalidOperationException("Values cannot cross QuickJS runtimes");
        return Wrap(Native.lite_value_to_context(Handle, value.Handle));
    }

    internal QuickJsException Exception()
    {
        var error = Wrap(Native.lite_get_exception(Handle));
        string? name = null;
        try
        {
            if (error.IsError)
            {
                using var value = error.Get("name");
                name = value.AsString();
            }
            else if (error.Kind == QuickJsKind.Object)
            {
                using var constructor = error.Get("constructor");
                using var value = constructor.Get("name");
                if (value.AsString() == "Test262Error") name = "Test262Error";
            }
        }
        catch { /* A hostile getter must not replace the original thrown value. */ }
        string message;
        try { message = error.AsString(); }
        catch { message = "JavaScript threw a value that cannot be converted to a string"; }
        return new QuickJsException(message, name, error);
    }

    internal QuickJsValue Eval(string source, string filename = "<eval>", bool module = false)
    {
        Runtime.CheckThread();
        var bytes = Encoding.UTF8.GetBytes(source + "\0");
        return Wrap(Native.lite_eval(Handle, bytes, (nuint)(bytes.Length - 1), filename, module ? 1 : 0));
    }

    internal void Compile(string source, string filename = "<eval>", bool module = false)
    {
        Runtime.CheckThread();
        var bytes = Encoding.UTF8.GetBytes(source + "\0");
        if (Native.lite_compile(Handle, bytes, (nuint)(bytes.Length - 1), filename, module ? 1 : 0) != 0)
            throw Exception();
    }

    internal QuickJsValue CompileValue(string source, string filename = "<eval>", bool module = false)
    {
        Runtime.CheckThread();
        var bytes = Encoding.UTF8.GetBytes(source + "\0");
        return Wrap(Native.lite_compile_value(Handle, bytes, (nuint)(bytes.Length - 1),
            filename, module ? 1 : 0));
    }

    internal void Execute(string source, string filename = "<eval>", bool module = false)
    {
        using var _ = Eval(source, filename, module);
        Runtime.PumpJobs();
    }

    internal QuickJsValue Global() => Wrap(Native.lite_global(Handle));
    internal QuickJsValue Undefined() => Wrap(Native.lite_new_undefined(Handle));
    internal QuickJsValue Null() => Wrap(Native.lite_new_null(Handle));
    internal QuickJsValue Bool(bool value) => Wrap(Native.lite_new_bool(Handle, value ? 1 : 0));
    internal QuickJsValue Number(double value) => Wrap(Native.lite_new_number(Handle, value));
    internal QuickJsValue String(string value)
    {
        var bytes = Cesu8.GetBytes(value);
        return Wrap(Native.lite_new_string(Handle, bytes, (nuint)bytes.Length));
    }
    internal QuickJsValue Object() => Wrap(Native.lite_new_object(Handle));
    internal QuickJsValue HostObject(int id) => Wrap(Native.lite_new_host_object(Handle, id));
    internal QuickJsValue HostFunction(string name, int arity,
        Func<QuickJsRealm, QuickJsValue[], QuickJsValue> callback) =>
        Runtime.CreateHostFunction(this, name, arity, callback);

    internal QuickJsPromise NewPromise()
    {
        Runtime.CheckThread();
        var promise = Wrap(Native.lite_new_promise(Handle, out var resolve, out var reject));
        return new QuickJsPromise(promise, Wrap(resolve), Wrap(reject));
    }

    public void Dispose()
    {
        if (_disposed) return;
        Runtime.CheckThread();
        foreach (var value in _owned.ToArray()) value.Dispose();
        Runtime.Remove(this);
        Native.lite_context_free(Handle);
        Handle = 0;
        _disposed = true;
    }
}

internal enum QuickJsKind { Undefined, Null, Boolean, Number, String, Object, Function, Other }

internal sealed class QuickJsPromise(QuickJsValue promise, QuickJsValue resolve, QuickJsValue reject) : IDisposable
{
    internal QuickJsValue Promise => promise;
    internal QuickJsValue ResolveFunction => resolve;
    internal QuickJsValue RejectFunction => reject;
    internal void Resolve(QuickJsValue value)
    {
        using var _ = resolve.Call(arguments: [value]);
    }

    internal void Reject(QuickJsValue reason)
    {
        using var _ = reject.Call(arguments: [reason]);
    }

    public void Dispose()
    {
        reject.Dispose();
        resolve.Dispose();
        promise.Dispose();
    }
}

/// <summary>An owned or borrowed JSValue. Borrowed callback arguments expire on return.</summary>
internal sealed class QuickJsValue : IDisposable
{
    internal QuickJsRealm Realm { get; }
    private nint _handle;
    private bool _owned;

    internal QuickJsValue(QuickJsRealm realm, nint handle, bool owned)
    {
        Realm = realm;
        _handle = handle;
        _owned = owned;
    }

    internal nint Handle => _handle != 0 ? _handle : throw new ObjectDisposedException(nameof(QuickJsValue));
    internal QuickJsKind Kind => (QuickJsKind)Native.lite_value_kind(Handle);
    internal bool IsError => Native.lite_value_is_error(Handle) != 0;
    internal bool SameValue(QuickJsValue other) => Native.lite_value_same(Handle, other.Handle) != 0;
    internal nint Identity => Native.lite_value_identity(Handle);
    internal QuickJsValue Clone() => Realm.Wrap(Native.lite_value_dup(Handle));

    internal string AsString()
    {
        var text = Native.lite_to_string(Handle, out var length);
        if (text == 0) throw new QuickJsException("JavaScript string conversion failed", "TypeError");
        try
        {
            var bytes = new byte[checked((int)length)];
            if (bytes.Length > 0) Marshal.Copy(text, bytes, 0, bytes.Length);
            return Cesu8.GetString(bytes);
        }
        finally { Native.lite_string_free(text); }
    }

    internal double AsNumber()
    {
        if (Native.lite_to_number(Handle, out var result) != 0) throw Realm.Exception();
        return result;
    }

    internal bool AsBoolean()
    {
        var result = Native.lite_to_bool(Handle);
        if (result < 0) throw Realm.Exception();
        return result != 0;
    }

    internal QuickJsValue Get(string name) => Realm.Wrap(Native.lite_get_property(Handle, name));
    internal void Set(string name, QuickJsValue value)
    {
        if (!ReferenceEquals(Realm, value.Realm)) throw new InvalidOperationException("Cross-realm value assignment");
        if (Native.lite_set_property(Handle, name, value.Handle) < 0) throw Realm.Exception();
    }

    internal QuickJsValue Call(QuickJsValue? thisValue = null, params QuickJsValue[] arguments)
    {
        foreach (var value in arguments)
            if (!ReferenceEquals(Realm, value.Realm)) throw new InvalidOperationException("Cross-realm function argument");
        return Realm.Wrap(Native.lite_call_handles(Handle, thisValue?._handle ?? 0,
            arguments.Select(value => value.Handle).ToArray(), arguments.Length));
    }

    internal QuickJsValue Construct(params QuickJsValue[] arguments)
    {
        foreach (var value in arguments)
            if (!ReferenceEquals(Realm, value.Realm)) throw new InvalidOperationException("Cross-realm constructor argument");
        return Realm.Wrap(Native.lite_construct(Handle,
            arguments.Select(value => value.Handle).ToArray(), arguments.Length));
    }

    internal void DetachArrayBuffer() => Native.lite_detach_array_buffer(Handle);
    internal void SetIsHtmlDda() => Native.lite_set_html_dda(Handle);
    internal void ResolveModule()
    {
        if (Native.lite_resolve_module(Handle) != 0) throw Realm.Exception();
    }

    internal QuickJsValue EvaluateCompiled() => Realm.Wrap(Native.lite_eval_compiled(Handle));

    internal void DefineAccessor(string name, QuickJsValue getter, QuickJsValue? setter = null)
    {
        if (Native.lite_define_accessor(Handle, name, getter.Handle, setter?.Handle ?? 0) < 0)
            throw Realm.Exception();
    }

    internal nint Detach()
    {
        if (!_owned) return Clone().Detach();
        Realm.Forget(this);
        _owned = false;
        var handle = _handle;
        _handle = 0;
        return handle;
    }

    public void Dispose()
    {
        if (_handle == 0) return;
        if (_owned)
        {
            Realm.Forget(this);
            Native.lite_value_free(_handle);
        }
        _handle = 0;
    }
}

internal class QuickJsException(string message, string? errorName, QuickJsValue? errorValue = null)
    : Exception(message), IDisposable
{
    internal string? ErrorName => errorName;
    internal QuickJsValue? ErrorValue => errorValue;
    public void Dispose() => errorValue?.Dispose();
}

internal static class Native
{
    private const string Library = "litequickjs";
    static Native()
    {
        NativeLibrary.SetDllImportResolver(typeof(Native).Assembly, Resolve);
    }

    private static nint Resolve(string name, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (name != Library) return 0;
        var basePath = Path.GetDirectoryName(assembly.Location)!;
        foreach (var asset in new[]
        {
            Path.Combine(basePath, "runtimes", "win-x64", "native", "litequickjs.dll"),
            Path.Combine(basePath, "litequickjs.dll"),
        })
            if (File.Exists(asset)) return NativeLibrary.Load(asset);
        throw new DllNotFoundException($"The Windows x64 QuickJS native asset was not found beside {assembly.Location}");
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate nint HostCallback(nint context, int id, nint arguments, int count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate nint NormalizeCallback(nint context, nint baseName, nint specifier);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate nint ModuleSourceCallback(nint context, nint moduleName, out nuint length);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void ReleaseCallback(nint pointer);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void RejectionCallback(nint context, nint promise, nint reason, int isHandled);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int InterruptCallback();

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern nint lite_runtime_new();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void lite_runtime_free(nint runtime);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void lite_runtime_set_can_block(nint runtime, int canBlock);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void lite_runtime_update_stack_top(nint runtime);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern nint lite_context_new(nint runtime);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void lite_context_free(nint context);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern nint lite_value_dup(nint value);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern nint lite_value_to_context(nint context, nint value);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void lite_value_free(nint value);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int lite_value_kind(nint value);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int lite_value_same(nint left, nint right);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern nint lite_value_identity(nint value);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern nint lite_new_undefined(nint context);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern nint lite_new_null(nint context);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern nint lite_new_bool(nint context, int value);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern nint lite_new_number(nint context, double value);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern nint lite_new_string(nint context, byte[] value, nuint length);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern nint lite_new_object(nint context);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern nint lite_new_host_object(nint context, int id);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int lite_host_object_id(nint value);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int lite_value_is_error(nint value);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void lite_gc(nint runtime);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void lite_detach_array_buffer(nint value);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void lite_set_html_dda(nint value);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern nint lite_global(nint context);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern nint lite_to_string(nint value, out nuint length);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void lite_string_free(nint value);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int lite_to_number(nint value, out double result);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int lite_to_bool(nint value);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern nint lite_eval(nint context,
        byte[] source, nuint length, [MarshalAs(UnmanagedType.LPUTF8Str)] string filename, int flags);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int lite_compile(nint context,
        byte[] source, nuint length, [MarshalAs(UnmanagedType.LPUTF8Str)] string filename, int flags);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern nint lite_compile_value(nint context,
        byte[] source, nuint length, [MarshalAs(UnmanagedType.LPUTF8Str)] string filename, int flags);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int lite_resolve_module(nint compiled);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern nint lite_eval_compiled(nint compiled);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern nint lite_get_exception(nint context);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern nint lite_get_property(nint target, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int lite_set_property(nint target, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, nint value);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int lite_define_accessor(nint target, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, nint getter, nint setter);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern nint lite_call_handles(nint function, nint thisValue, nint[] arguments, int count);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern nint lite_construct(nint function, nint[] arguments, int count);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern nint lite_new_promise(nint context, out nint resolve, out nint reject);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int lite_execute_job(nint runtime, out nint exceptionContext);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int lite_jobs_pending(nint runtime);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void lite_set_host_callback(nint runtime, HostCallback callback);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void lite_set_module_callbacks(nint runtime, NormalizeCallback normalize, ModuleSourceCallback source, ReleaseCallback release);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void lite_set_rejection_callback(nint runtime, RejectionCallback callback);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void lite_set_interrupt_callback(nint runtime, InterruptCallback callback);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern nint lite_argument_at(nint arguments, int index);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern nint lite_new_host_function(nint context, int id, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int arity);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void lite_throw_error(nint context, [MarshalAs(UnmanagedType.LPUTF8Str)] string message);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void lite_throw_value(nint context, nint value);
}
