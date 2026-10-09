using System.Text.RegularExpressions;
using Lite.Conformance.Css21;
using Lite.Conformance.Harness;

namespace Lite.Conformance.Wpt;

/// <summary>Suite input integrity: a case whose page pulls in a render-relevant resource
/// that is absent from the vendored tree cannot be executed meaningfully. The 2011-03-23
/// official snapshot was recovered from an archive with an incomplete support/ subtree, so
/// those cases must report as unsupported execution blockers rather than engine failures —
/// missing inputs are blockers per the CSS 2.1 plan, never silent failures. Only resources
/// the render consumes are scanned (stylesheet links, src attributes, CSS url() values and
/// @import targets);
/// rel=reference/help/author links are metadata the reference graph resolves from the
/// suite's own manifest, not render inputs. Resources referenced from linked CSS files are
/// a known residual gap until the support subtree is recovered from the archive.</summary>
internal static class SuiteInputs
{
    private static readonly Regex LinkTag = new(@"<link\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Attr = new(@"\b(rel|href|src)\s*=\s*""([^""]*)""", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Src = new(@"\bsrc\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex UrlValue = new(@"\burl\(\s*(['""]?)(?<ref>[^)""']+)\1\s*\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // @import "x.css" / @import url(x.css) inside a <style> block is a render input like a
    // stylesheet link — the at-charset family imports its (missing) support files exactly this
    // way, and without the scan those cases executed and failed on pixels instead of reporting
    // the honest missing-input blocker.
    private static readonly Regex Import = new(
        @"@import\s+(?:url\(\s*(['""]?)(?<ref>[^)""']+)\1\s*\)|(['""])(?<ref>[^""]+)\2)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    internal static string? FindMissing(string path)
    {
        var (root, relative) = Split(path);
        if (root is null) return null;
        var file = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(file)) return path;
        string source;
        try { source = File.ReadAllText(file); }
        catch (IOException) { return null; }
        var baseDir = Path.GetDirectoryName(file)!;

        foreach (var tag in LinkTag.Matches(source).OfType<Match>())
        {
            var attrs = Attr.Matches(tag.Value);
            var rel = attrs.FirstOrDefault(a => a.Groups[1].Value.Equals("rel", StringComparison.OrdinalIgnoreCase))?.Groups[2].Value ?? "";
            var href = attrs.FirstOrDefault(a => a.Groups[1].Value.Equals("href", StringComparison.OrdinalIgnoreCase))?.Groups[2].Value;
            if (href is null) continue;
            if (!rel.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Any(t => t.Equals("stylesheet", StringComparison.OrdinalIgnoreCase))) continue;
            if (IsMissing(baseDir, root, href) is { } missing) return $"{relative} -> {missing}";
        }
        foreach (Match src in Src.Matches(source))
            if (IsMissing(baseDir, root, src.Groups[1].Value) is { } missing) return $"{relative} -> {missing}";
        foreach (Match url in UrlValue.Matches(source))
            if (IsMissing(baseDir, root, url.Groups["ref"].Value) is { } missing) return $"{relative} -> {missing}";
        foreach (Match import in Import.Matches(source))
            if (IsMissing(baseDir, root, import.Groups["ref"].Value) is { } missing) return $"{relative} -> {missing}";
        return null;
    }

    private static string? IsMissing(string baseDir, string root, string raw)
    {
        var value = raw.Trim();
        if (value.Length == 0 ||
            value.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("http:", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("https:", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith('#') || value.StartsWith('/')) return null;
        var query = value.Split('?', '#')[0];
        if (query.Length == 0) return null;
        var resolved = Path.GetFullPath(Path.Combine(baseDir, query.Replace('/', Path.DirectorySeparatorChar)));
        if (!resolved.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase)) return null;
        return File.Exists(resolved) ? null : value;
    }

    /// <summary>Whether a case file exists in the vendored tree (reference-target checks).</summary>
    internal static bool FileExists(string path)
    {
        var (root, relative) = Split(path);
        if (root is null) return true;
        return File.Exists(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static (string? Root, string Relative) Split(string path)
    {
        if (path.StartsWith(OfficialCatalog.UrlPrefix, StringComparison.Ordinal))
            return (OfficialCatalog.Root, path[OfficialCatalog.UrlPrefix.Length..]);
        if (!path.StartsWith("css/", StringComparison.Ordinal)) return (null, path);
        return (Path.Combine(ConformancePaths.Vendor, "wpt"), path);
    }
}
