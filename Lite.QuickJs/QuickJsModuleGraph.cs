using System.Collections.Concurrent;
using Acornima;
using Acornima.Ast;
using System.Text.Json;

namespace Lite.QuickJs;

/// <summary>Fetches a static browser module graph before QuickJS links it. The fetch callback
/// receives the requested URL, the importing module's URL (null for a root fetch — the referrer
/// for the Referer header), and the credentials mode ("omit", "same-origin", or "include").</summary>
internal sealed class QuickJsModuleGraph(
    Func<Uri, string?, string, CancellationToken, Task<(string Source, string ResponseUrl)>> fetchSource,
    string documentUrl) : IDisposable
{
    private readonly ConcurrentDictionary<string, string> _sources = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _responseUrls = new(StringComparer.Ordinal);
    // The HTML module map records a null (fetch failure) entry: once a URL fails, later imports
    // of it reject with the same outcome without refetching.
    private readonly ConcurrentDictionary<string, Exception> _failures = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _lifetime = new();

    internal void RegisterInline(string url, string source)
    {
        _sources[url] = source;
        _responseUrls[url] = url;
    }

    internal void RegisterResponseUrl(string url, string responseUrl) => _responseUrls[url] = responseUrl;

    internal string ResponseUrl(string url) => _responseUrls.GetValueOrDefault(url, url);

    internal string Normalize(string referrer, string specifier)
    {
        var basis = _responseUrls.GetValueOrDefault(referrer, referrer);
        if (!Uri.TryCreate(basis, UriKind.Absolute, out _)) basis = documentUrl;
        Uri? resolved = null;
        if (specifier.StartsWith('/') || specifier.StartsWith("./", StringComparison.Ordinal) ||
            specifier.StartsWith("../", StringComparison.Ordinal))
        {
            if (Uri.TryCreate(basis, UriKind.Absolute, out var baseUri))
                Uri.TryCreate(baseUri, specifier, out resolved);
        }
        else if (Uri.TryCreate(specifier, UriKind.Absolute, out var absolute)) resolved = absolute;
        if (resolved is null || resolved.Scheme is not ("http" or "https" or "data"))
            throw new IOException($"Cannot resolve browser module specifier '{specifier}'");
        return _responseUrls.GetValueOrDefault(resolved.AbsoluteUri, resolved.AbsoluteUri);
    }

    internal string? Source(string url) => _sources.GetValueOrDefault(url);

    internal async Task<string> PrefetchAsync(string entryUrl, string? referrer = null,
        string credentialsMode = "same-origin", CancellationToken cancellation = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellation);
        var pending = new Queue<(string Url, string? Referrer)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        pending.Enqueue((entryUrl, referrer));
        while (pending.Count > 0)
        {
            linked.Token.ThrowIfCancellationRequested();
            var (requested, parentReferrer) = pending.Dequeue();
            if (!seen.Add(requested)) continue;
            string code;
            string responseUrl;
            if (_failures.TryGetValue(requested, out var cachedFailure)) throw cachedFailure;
            if (_sources.TryGetValue(requested, out var inline))
                (code, responseUrl) = (inline, _responseUrls.GetValueOrDefault(requested, requested));
            else
            {
                try
                {
                    (code, responseUrl) = await fetchSource(new Uri(requested),
                        // The entry fetch's credentials mode is inherited by every descendant; each
                        // descendant's referrer is the URL of the module that imported it.
                        parentReferrer, credentialsMode, linked.Token).ConfigureAwait(false);
                }
                catch (Exception error)
                {
                    _failures[requested] = error;
                    throw;
                }
            }
            _responseUrls[requested] = responseUrl;
            _responseUrls[responseUrl] = responseUrl;
            _sources[responseUrl] = code;

            var module = new Acornima.Parser().ParseModule(code, responseUrl);
            foreach (var statement in module.Body)
            {
                var specifier = statement switch
                {
                    ImportDeclaration import => import.Source.Value,
                    ExportNamedDeclaration named when named.Source is not null => named.Source.Value,
                    ExportAllDeclaration all => all.Source.Value,
                    _ => null,
                };
                if (specifier is not null) pending.Enqueue((Normalize(responseUrl, specifier), responseUrl));
            }
        }
        return _responseUrls.GetValueOrDefault(entryUrl, entryUrl);
    }

    internal void Attach(QuickJsRealm realm) => realm.Runtime.SetModuleProvider(realm, Normalize, Source);

    /// <summary>Loads a dynamic import graph off-thread and settles its host promise on the realm thread.</summary>
    internal QuickJsValue ImportAsync(QuickJsRealm realm, string referrer, string specifier,
        Action<Action> enqueue, CancellationToken cancellation = default)
    {
        realm.Runtime.CheckThread();
        var capability = realm.NewPromise();
        var exposed = capability.Promise.Clone();
        _ = LoadAndScheduleAsync();
        return exposed;

        async Task LoadAndScheduleAsync()
        {
            string? url = null;
            Exception? failure = null;
            try
            {
                // A dynamic import always uses the "same-origin" credentials mode, and its
                // referrer is the module (or document) that evaluated the import call.
                url = await PrefetchAsync(Normalize(referrer, specifier), referrer, "same-origin", cancellation)
                    .ConfigureAwait(false);
            }
            catch (Exception error) { failure = error; }

            enqueue(() =>
            {
                using (capability)
                {
                    if (realm.IsDisposed) return;
                    try
                    {
                        if (failure is not null) throw failure;
                        // QuickJS's synchronous callback now sees only prepared sources. Its
                        // import promise is assimilated by the host promise returned to script.
                        using var imported = realm.Eval($"import({JsonSerializer.Serialize(url)})", referrer);
                        capability.Resolve(imported);
                    }
                    catch (QuickJsException error)
                    {
                        if (error.ErrorValue is { } value) capability.Reject(value);
                        else RejectTypeError(error.Message);
                        error.Dispose();
                    }
                    catch (Exception error) { RejectError(error.Message,
                        error is Acornima.SyntaxErrorException ? "SyntaxError" : "TypeError"); }
                }

                void RejectTypeError(string message) => RejectError(message, "TypeError");

                void RejectError(string message, string name)
                {
                    using var constructor = realm.Eval(name);
                    using var text = realm.String(message);
                    using var reason = constructor.Construct(text);
                    capability.Reject(reason);
                }
            });
        }
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
