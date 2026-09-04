using Jint;
using Jint.Runtime.Modules;

namespace Lite.Conformance.Test262;

/// <summary>
/// Test262 host loader. Every specifier is resolved by the host, including bare and invalid-looking
/// strings: ECMAScript requires dynamic import failures to reject the returned promise rather than
/// letting a CLR <see cref="NotSupportedException"/> escape synchronously.
/// </summary>
internal sealed class Test262ModuleLoader(string basePath, string testRoot) : ModuleLoader
{
    private readonly string _basePath = Path.GetFullPath(basePath);
    private readonly string _testRoot = Path.GetFullPath(testRoot);

    public override ResolvedSpecifier Resolve(string? referencingModuleLocation, ModuleRequest moduleRequest) =>
        new(moduleRequest, moduleRequest.Specifier, Uri: null, SpecifierType.Bare);

    protected override string LoadModuleContents(Engine engine, ResolvedSpecifier resolved)
    {
        var path = ResolvePath(resolved.Key);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Module not found: {resolved.ModuleRequest.Specifier}", path);
        return File.ReadAllText(path);
    }

    protected override byte[] LoadModuleContentsAsBytes(Engine engine, ResolvedSpecifier resolved)
    {
        var path = ResolvePath(resolved.Key);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Module not found: {resolved.ModuleRequest.Specifier}", path);
        return File.ReadAllBytes(path);
    }

    private string ResolvePath(string specifier)
    {
        var relative = specifier.Replace('/', Path.DirectorySeparatorChar);
        var path = Path.GetFullPath(Path.Combine(_basePath, relative));
        var prefix = _testRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new IOException($"Module path escapes the Test262 test root: {specifier}");
        return path;
    }
}
