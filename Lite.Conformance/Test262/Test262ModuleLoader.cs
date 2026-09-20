using Jint;
using Jint.Runtime.Modules;

namespace Lite.Conformance.Test262;

/// <summary>
/// Test262 host loader. Every specifier is resolved by the host, including bare and invalid-looking
/// strings: ECMAScript requires dynamic import failures to reject the returned promise rather than
/// letting a CLR <see cref="NotSupportedException"/> escape synchronously.
/// </summary>
internal sealed class Test262ModuleLoader(string basePath, string testRoot) : IModuleLoader
{
    private readonly string _basePath = Path.GetFullPath(basePath);
    private readonly string _testRoot = Path.GetFullPath(testRoot);
    private readonly Dictionary<string, Module> _modules = new(StringComparer.Ordinal);

    public ResolvedSpecifier Resolve(string? referencingModuleLocation, ModuleRequest moduleRequest)
    {
        var directory = _basePath;
        if (referencingModuleLocation is not null && Uri.TryCreate(referencingModuleLocation, UriKind.Absolute, out var parent) && parent.IsFile)
            directory = Path.GetDirectoryName(parent.LocalPath)!;
        var specifier = moduleRequest.Specifier;
        var path = Uri.TryCreate(specifier, UriKind.Absolute, out var absolute) && absolute.IsFile
            ? absolute.LocalPath : Path.GetFullPath(Path.Combine(directory, specifier.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(_testRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ModuleResolutionException("Module path escapes Test262 root", specifier, referencingModuleLocation, path);
        var uri = new Uri(path);
        return new(moduleRequest, uri.AbsoluteUri, uri, SpecifierType.RelativeOrAbsolute);
    }

    public Module LoadModule(Engine engine, ResolvedSpecifier resolved)
    {
        try
        {
            if (_modules.TryGetValue(resolved.Key, out var existing)) return existing;
            var source = File.ReadAllText(resolved.Uri!.LocalPath);
            var module = ModuleFactory.BuildSourceTextModule(engine, resolved, source, new ModuleParsingOptions());
            _modules.Add(resolved.Key, module);
            return module;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        { throw new Jint.Runtime.JavaScriptException(engine.Intrinsics.TypeError.Construct(error.Message)); }
    }
}
