using System.Text.RegularExpressions;
using Lite.Conformance.Css21;
using Lite.Conformance.Harness;

namespace Lite.Conformance.Wpt;

/// <summary>Suite input integrity: a case whose page references a relative resource that is
/// absent from the vendored tree cannot be executed meaningfully. The 2011-03-23 official
/// snapshot was recovered from an archive with an incomplete support/ subtree (~295 files of
/// 327 referenced), so those cases must report as unsupported execution blockers rather than
/// engine failures — missing inputs are blockers per the CSS 2.1 plan, never silent failures.
/// Only page-level references are scanned; resources referenced from linked CSS files are a
/// known residual gap until the support subtree is recovered from the archive.</summary>
internal static class SuiteInputs
{
    private static readonly Regex Reference = new(
        """(?:\bhref|\bsrc)\s*=\s*"(?<ref>[^"]+)"|\(\s*(?<ref>'[^']+'|"[^"]+"|[^)\s]+)\s*\)""",
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
        foreach (Match match in Reference.Matches(source))
        {
            var raw = match.Groups["ref"].Value.Trim().Trim('\'', '"');
            if (raw.Length == 0) continue;
            if (raw.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
                raw.StartsWith("http:", StringComparison.OrdinalIgnoreCase) ||
                raw.StartsWith("https:", StringComparison.OrdinalIgnoreCase) ||
                raw.StartsWith('#') || raw.StartsWith('/')) continue;
            var query = raw.Split('?', '#')[0];
            if (query.Length == 0) continue;
            var resolved = Path.GetFullPath(Path.Combine(baseDir, query.Replace('/', Path.DirectorySeparatorChar)));
            if (!resolved.StartsWith(Path.GetFullPath(root!), StringComparison.OrdinalIgnoreCase)) continue;
            if (!File.Exists(resolved)) return $"{relative} -> {raw}";
        }
        return null;
    }

    private static (string? Root, string Relative) Split(string path)
    {
        if (path.StartsWith(OfficialCatalog.UrlPrefix, StringComparison.Ordinal))
            return (OfficialCatalog.Root, path[OfficialCatalog.UrlPrefix.Length..]);
        if (!path.StartsWith("css/", StringComparison.Ordinal)) return (null, path);
        return (Path.Combine(ConformancePaths.Vendor, "wpt"), path);
    }
}
