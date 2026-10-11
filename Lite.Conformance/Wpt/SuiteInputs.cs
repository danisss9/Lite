using System.Text.RegularExpressions;
using Lite.Conformance.Css21;
using Lite.Conformance.Harness;

namespace Lite.Conformance.Wpt;

/// <summary>Suite input integrity: a case whose page pulls in a render-relevant resource
/// that is absent from the vendored tree cannot be executed meaningfully — unless the suite
/// references the absence deliberately (broken-image and cursor fallback, invalid url()
/// tokens), which the reviewed table Css21/css21-intentional-missing-inputs.json records;
/// those cases execute instead of reporting a missing-input blocker. The 2011 official
/// snapshot's support/ subtree was restored from pre-snapshot csswg-test history
/// (scripts/restore-css21-support.py), so an absent resource today is either deliberate
/// or a genuine vendoring gap — both must stay visible, never silent failures.
/// Scanning follows the browser, not the raw document text: stylesheet links, src
/// attributes, and url()/@import inside style blocks, style attributes, and linked local
/// stylesheets (recursively, with comments stripped) — resource names are URL-decoded the
/// way a fetch resolves them, so url(ruler-h-50%25.png) checks for "ruler-h-50%.png".
/// rel=reference/help/author links are metadata the reference graph resolves from the
/// suite's own manifest, not render inputs.</summary>
internal static class SuiteInputs
{
    private static readonly Regex LinkTag = new(@"<link\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TagAttr = new(@"\b(rel|href|src)\s*=\s*""([^""]*)""", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Src = new(@"\bsrc\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex StyleBlock = new(@"<style\b[^>]*>(.*?)</style>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex StyleAttr = new(@"\bstyle\s*=\s*""([^""]*)""", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CssComment = new(@"/\*.*?\*/", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex UrlValue = new(@"\burl\(\s*(['""]?)(?<ref>[^)""']+)\1\s*\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Import = new(
        @"@import\s+(?:url\(\s*(['""]?)(?<ref1>[^)""']+)\1\s*\)|(['""])(?<ref2>[^""]+)\2)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Lazy<Dictionary<string, string[]>> IntentionalByStem = new(LoadIntentional);

    internal static string? FindMissing(string path)
    {
        var (root, relative) = Split(path);
        if (root is null) return null;
        var file = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(file)) return path;
        return ScanDocument(file, root, relative, stem: Path.GetFileNameWithoutExtension(relative),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    private static string? ScanDocument(string file, string root, string display, string stem,
        HashSet<string> visited)
    {
        if (!visited.Add(file)) return null;
        string source;
        try { source = File.ReadAllText(file); }
        catch (IOException) { return null; }
        var baseDir = Path.GetDirectoryName(file)!;

        // Stylesheet links: the import itself and, when present, the linked sheet.
        foreach (var tag in LinkTag.Matches(source).OfType<Match>())
        {
            var attrs = TagAttr.Matches(tag.Value);
            var rel = attrs.FirstOrDefault(a => a.Groups[1].Value.Equals("rel", StringComparison.OrdinalIgnoreCase))?.Groups[2].Value ?? "";
            var href = attrs.FirstOrDefault(a => a.Groups[1].Value.Equals("href", StringComparison.OrdinalIgnoreCase))?.Groups[2].Value;
            if (href is null) continue;
            if (!rel.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Any(t => t.Equals("stylesheet", StringComparison.OrdinalIgnoreCase))) continue;
            if (Missing(baseDir, root, href, stem) is { } missing) return $"{display} -> {missing}";
            if (FollowLocalSheet(baseDir, root, href, visited) is { } nested) return nested;
        }
        // Resource attributes (img/script/object/embed/input src).
        foreach (Match src in Src.Matches(source))
            if (Missing(baseDir, root, src.Groups[1].Value, stem) is { } missing) return $"{display} -> {missing}";
        // CSS text lives in style blocks and style attributes only — never in meta
        // content or prose, whose url(...) mentions are not render inputs.
        var cssChunks = StyleBlock.Matches(source).OfType<Match>().Select(m => m.Groups[1].Value).ToList();
        cssChunks.AddRange(StyleAttr.Matches(source).OfType<Match>().Select(m => m.Groups[1].Value));
        foreach (var chunk in cssChunks)
        {
            var cleaned = CssComment.Replace(chunk, " ");
            foreach (Match url in UrlValue.Matches(cleaned))
                if (Missing(baseDir, root, url.Groups["ref"].Value, stem) is { } missing) return $"{display} -> {missing}";
            foreach (Match import in Import.Matches(cleaned))
            {
                var reference = import.Groups["ref1"].Success ? import.Groups["ref1"].Value : import.Groups["ref2"].Value;
                if (Missing(baseDir, root, reference, stem) is { } missing) return $"{display} -> {missing}";
                if (FollowLocalSheet(baseDir, root, reference, visited) is { } nested) return nested;
            }
        }
        return null;
    }

    /// <summary>Scans a linked or imported local stylesheet with its own directory as the
    /// base (the CSS resolution rule), so url()/@import references one level down are
    /// checked too.</summary>
    private static string? FollowLocalSheet(string baseDir, string root, string raw,
        HashSet<string> visited)
    {
        var value = Decode(raw).Trim();
        if (value.Length == 0 || !value.EndsWith(".css", StringComparison.OrdinalIgnoreCase)) return null;
        var resolved = Resolve(baseDir, root, value);
        if (resolved is null || !File.Exists(resolved)) return null;
        var display = Path.GetFullPath(resolved);
        var vendorIndex = display.IndexOf(ConformancePaths.Vendor, StringComparison.OrdinalIgnoreCase);
        if (vendorIndex >= 0) display = display[(vendorIndex + ConformancePaths.Vendor.Length + 1)..].Replace('\\', '/');
        return ScanDocument(resolved, root, display, stem: "", visited);
    }

    private static string? Missing(string baseDir, string root, string raw, string stem)
    {
        var value = Decode(raw).Trim();
        if (value.Length == 0 ||
            value.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("http:", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("https:", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith('#') || value.StartsWith('/')) return null;
        if (stem.Length > 0 && IsIntentional(stem, value)) return null;
        var resolved = Resolve(baseDir, root, value);
        if (resolved is null) return null;
        return File.Exists(resolved) ? null : value;
    }

    private static bool IsIntentional(string stem, string value)
    {
        if (!IntentionalByStem.Value.TryGetValue(stem, out var resources)) return false;
        // A bare stem entry allowlists every resource the test requests: the whole test
        // is about invalid url() tokens, so any extracted reference is a phantom.
        return resources.Length == 0 || resources[0] == "*" ||
            resources.Contains(value, StringComparer.Ordinal);
    }

    private static string? Resolve(string baseDir, string root, string value)
    {
        var query = value.Split('?', '#')[0];
        if (query.Length == 0) return null;
        var resolved = Path.GetFullPath(Path.Combine(baseDir, query.Replace('/', Path.DirectorySeparatorChar)));
        if (!resolved.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase)) return null;
        return resolved;
    }

    /// <summary>Percent-decodes a resource reference the way a fetch resolves it; invalid
    /// escapes are left in place, matching lenient URL resolution.</summary>
    private static string Decode(string raw)
    {
        try { return Uri.UnescapeDataString(raw); }
        catch (UriFormatException) { return raw; }
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

    private static Dictionary<string, string[]> LoadIntentional()
    {
        var table = new Dictionary<string, string[]>(StringComparer.Ordinal);
        try
        {
            var path = Path.Combine(ConformancePaths.ProjectRoot, "Css21", "css21-intentional-missing-inputs.json");
            var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path));
            foreach (var stem in node?["byStem"]?.AsArray() ?? new System.Text.Json.Nodes.JsonArray())
                table[stem!.GetValue<string>()] = ["*"];
            foreach (var entry in node?["entries"]?.AsArray() ?? new System.Text.Json.Nodes.JsonArray())
            {
                var stem = entry!["stem"]!.GetValue<string>();
                table[stem] = entry["resources"]!.AsArray().Select(r => r!.GetValue<string>()).ToArray();
            }
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
        {
            // A missing or unreadable table leaves the scanner fail-closed: deliberate
            // absences then report as missing-input blockers, never as false executions.
        }
        return table;
    }
}
