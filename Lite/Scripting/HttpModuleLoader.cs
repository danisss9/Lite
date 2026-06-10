using Jint;
using Jint.Runtime.Modules;
using Lite.Network;

namespace Lite.Scripting;

/// <summary>
/// Resolves and loads ES modules over http(s), relative to the page's base URL.
/// Used to support &lt;script type="module"&gt; and import/export.
/// </summary>
internal sealed class HttpModuleLoader : IModuleLoader
{
    private readonly string _baseUrl;

    public HttpModuleLoader(string baseUrl) => _baseUrl = baseUrl;

    public ResolvedSpecifier Resolve(string? referencingModuleLocation, ModuleRequest moduleRequest)
    {
        var specifier = moduleRequest.Specifier;

        Uri? baseUri = null;
        if (!string.IsNullOrEmpty(referencingModuleLocation))
            Uri.TryCreate(referencingModuleLocation, UriKind.Absolute, out baseUri);
        if (baseUri is null)
            Uri.TryCreate(_baseUrl, UriKind.Absolute, out baseUri);

        if (Uri.TryCreate(specifier, UriKind.Absolute, out var absolute))
            return new ResolvedSpecifier(moduleRequest, absolute.AbsoluteUri, absolute, SpecifierType.RelativeOrAbsolute);

        if (baseUri is not null && Uri.TryCreate(baseUri, specifier, out var relative))
            return new ResolvedSpecifier(moduleRequest, relative.AbsoluteUri, relative, SpecifierType.RelativeOrAbsolute);

        // Bare specifier (e.g. "lodash") — not resolvable without a registry.
        return new ResolvedSpecifier(moduleRequest, specifier, null, SpecifierType.Bare);
    }

    public Module LoadModule(Engine engine, ResolvedSpecifier resolved)
    {
        var url = resolved.Uri?.AbsoluteUri ?? resolved.Key;
        var code = ResourceLoader.FetchText(url, _baseUrl)
            ?? throw new ModuleResolutionException("Module not found", resolved.Key, _baseUrl, url);
        return ModuleFactory.BuildSourceTextModule(engine, resolved, code, new ModuleParsingOptions());
    }
}
