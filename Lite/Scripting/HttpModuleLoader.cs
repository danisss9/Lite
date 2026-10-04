using Lite.QuickJs;
using Lite.Scripting.Runtime;

namespace Lite.Scripting;

/// <summary>Document-owned QuickJS module graph, backed by the browser session.</summary>
internal sealed class HttpModuleLoader : IDisposable
{
    private readonly QuickJsModuleGraph _graph;
    private readonly string _documentUrl;
    private readonly Action<Action> _post;
    private readonly CancellationTokenSource _lifetime = new();
    private Engine? _engine;
    private int _pending;
    private bool _disposed;

    internal HttpModuleLoader(string baseUrl, string documentUrl, Action<Action> post,
        Lite.Network.BrowserSession? session = null)
    {
        _post = post;
        _documentUrl = documentUrl;
        var fetcher = new BrowserModuleFetcher(session ?? new(), documentUrl);
        _graph = new QuickJsModuleGraph(
            (uri, referrer, credentialsMode, cancellation) => fetcher.FetchSource(uri,
                referrer is null && credentialsMode == "same-origin" ? null
                    : new ModuleFetchOptions(referrer, credentialsMode), cancellation),
            documentUrl);
    }

    internal bool HasPendingLoads => Volatile.Read(ref _pending) != 0;
    internal void Bind(Engine engine)
    {
        _engine = engine;
        engine.SourceTransformer = (source, filename, module) =>
            QuickJsDynamicImportRewriter.Rewrite(source,
                Uri.TryCreate(filename, UriKind.Absolute, out _) ? filename : _documentUrl,
                module);
        using var hostImport = engine.Realm.HostFunction("__liteImport", 1, (owner, args) =>
        {
            var specifier = args.Length > 0 ? args[0].AsString() : "undefined";
            var referrer = args.Length > 0 ? args[^1].AsString() : "";
            return _graph.ImportAsync(owner, referrer, specifier, _post, _lifetime.Token);
        });
        using var global = engine.Realm.Global();
        global.Set("__liteImport", hostImport);
        engine.Runtime.SetModuleProvider(engine.Realm, _graph.Normalize, url =>
        {
            if (_graph.Source(url) is null)
                _graph.PrefetchAsync(url, null, "same-origin", _lifetime.Token).GetAwaiter().GetResult();
            var code = _graph.Source(url);
            return code is null ? null : QuickJsDynamicImportRewriter.Rewrite(code, url, module: true);
        });
    }

    internal void Add(string url, string code) => _graph.RegisterInline(url, code);
    internal void RegisterSourceUrl(string key, string url) => _graph.RegisterResponseUrl(key, url);
    internal string ResponseUrl(string url) => _graph.ResponseUrl(url);

    internal ModuleImportOperation StartImport(string url, ModuleFetchOptions? options = null)
    {
        if (_engine is null) throw new InvalidOperationException("Module loader is not bound");
        var operation = new ModuleImportOperation();
        if (_graph.Source(url) is { } ready)
        {
            try { _engine.Realm.Execute(QuickJsDynamicImportRewriter.Rewrite(ready,
                _graph.ResponseUrl(url), module: true), _graph.ResponseUrl(url), module: true); operation.Complete(); }
            catch (Exception error) { operation.Fail(error); }
            return operation;
        }
        Interlocked.Increment(ref _pending);
        _ = Prefetch();
        return operation;

        async Task Prefetch()
        {
            string? resolved = null;
            Exception? error = null;
            try { resolved = await _graph.PrefetchAsync(url, options?.Referrer,
                options?.CredentialsMode ?? "same-origin", _lifetime.Token).ConfigureAwait(false); }
            catch (Exception failure) { error = failure; }
            _post(() =>
            {
                try
                {
                    if (error is not null) throw error;
                    if (_lifetime.IsCancellationRequested) throw new OperationCanceledException();
                    var source = _graph.Source(resolved!) ?? throw new IOException("Module source is missing");
                    _engine!.Realm.Execute(QuickJsDynamicImportRewriter.Rewrite(source,
                        resolved!, module: true), resolved!, module: true);
                    operation.Complete();
                }
                catch (Exception failure) { operation.Fail(failure); }
                finally { Interlocked.Decrement(ref _pending); }
            });
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _graph.Dispose();
        _lifetime.Dispose();
    }
}

internal sealed class ModuleImportOperation
{
    internal bool IsCompleted { get; private set; }
    internal bool IsFaulted => Error is not null;
    internal Exception? Error { get; private set; }
    internal void Complete() => IsCompleted = true;
    internal void Fail(Exception error) { Error = error; IsCompleted = true; }
}
