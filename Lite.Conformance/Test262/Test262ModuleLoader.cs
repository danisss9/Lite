using Lite.QuickJs;

namespace Lite.Conformance.Test262;

/// <summary>Local, root-bounded source provider for QuickJS Test262 modules.</summary>
internal sealed class Test262ModuleLoader(string basePath, string testRoot)
{
    private readonly string _basePath = Path.GetFullPath(basePath);
    private readonly string _testRoot = Path.GetFullPath(testRoot).TrimEnd(Path.DirectorySeparatorChar)
        + Path.DirectorySeparatorChar;

    /// <summary>Set once the root module's imports are resolved; the root itself is compiled
    /// from source text, so any syntax error raised after this point comes from the graph.</summary>
    internal bool GraphLoadStarted { get; private set; }

    internal void Bind(QuickJsRuntime runtime, QuickJsRealm realm) =>
        runtime.SetModuleProvider(realm, Normalize, Source);

    internal string Normalize(string? referrer, string specifier)
    {
        GraphLoadStarted = true;
        var directory = _basePath;
        if (referrer is not null && Uri.TryCreate(referrer, UriKind.Absolute, out var parent) && parent.IsFile)
            directory = Path.GetDirectoryName(parent.LocalPath)!;
        var path = Uri.TryCreate(specifier, UriKind.Absolute, out var absolute) && absolute.IsFile
            ? absolute.LocalPath : Path.GetFullPath(Path.Combine(directory,
                specifier.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(_testRoot, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Module path escapes Test262 root");
        return new Uri(path).AbsoluteUri;
    }

    private string? Source(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !uri.IsFile) return null;
        var path = Path.GetFullPath(uri.LocalPath);
        return path.StartsWith(_testRoot, StringComparison.OrdinalIgnoreCase) && File.Exists(path)
            ? File.ReadAllText(path) : null;
    }
}
