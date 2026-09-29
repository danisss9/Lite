using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Lite.Conformance.Harness;
using Lite.Conformance.Wpt;

namespace Lite.Conformance.Css21;

internal sealed record OfficialCase(string Path, string Kind, string Variant, string Title,
    string Assert, string[] Flags, string[] Helps, IReadOnlyList<WptReference> References)
{
    public bool OptionalBehavior => Flags.Contains("may", StringComparer.Ordinal);
    public bool Interactive => Flags.Contains("interact", StringComparer.Ordinal) || Flags.Contains("animated", StringComparer.Ordinal);
}

internal sealed record OfficialCounts(int TotalTests, int RequiredBehaviorTests, int OptionalTests,
    int Reftests, int SelfTests, int ExpectedTotalTests, int ExpectedRequiredBehaviorTests,
    int UnavailableTests, IReadOnlyList<string> UnavailableVariants);

/// <summary>
/// Builds and verifies the catalog of the vendored official 23 March 2011 CSS 2.1 suite.
/// The catalog derives test/reference structure from the suite's own reftest.list manifest,
/// per-test metadata (flags, assertions, spec anchors) from each test's head, and pins the
/// vendored tree with a whole-tree SHA-256 that the suite lock must publish. Execution uses
/// the tree-verified catalog; readiness additionally requires the lock's pinned tree hash.
/// </summary>
internal static class OfficialCatalog
{
    internal const string LockId = "css21-official-20110323";
    internal const string UrlPrefix = "css21-official/";
    // Bump whenever the catalog-building logic changes meaningfully, so Ensure() rebuilds
    // catalogs produced by an older builder even when the vendored tree and lock are stable.
    private const int BuilderVersion = 2;
    internal const string ScreenHtml4Variant = "html4-screen";
    internal const string OtherFormatsVariant = "other-formats-screen";
    internal static string Root => Path.Combine(ConformancePaths.Vendor, "css21-official-20110323");
    internal static string CatalogPath => Path.Combine(ConformancePaths.EnsureArtifacts(), "css21-official-catalog.json");

    // The canonical host is offline; only these variants are recoverable from archives today.
    private static readonly string[] ArchivableVariants = [ScreenHtml4Variant, OtherFormatsVariant];

    private static readonly Regex LinkTag = new(@"<link\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex MetaTag = new(@"<meta\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TitleTag = new(@"<title>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex AttributeValue = new(@"(\w+)\s*=\s*""([^""]*)""", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Rebuilds the catalog when missing, out of date with the lock, or inconsistent
    /// with the vendored tree. Returns null when no vendored tree exists. An unpinned lock is
    /// not an error here: readiness blocks on the pin, execution proceeds on the verified tree.
    /// This verifies the whole vendored tree and is meant for once-per-process runner startup,
    /// never per-case lookups — <see cref="Case"/> uses the cheap cached read.</summary>
    internal static (OfficialCase[] Cases, OfficialCounts Counts)? Ensure()
    {
        if (!Directory.Exists(Root)) return null;
        var lockSha256 = ExecutionEvidence.HashFile(ConformancePaths.Manifest("test-suites.lock.json"));
        var existing = ReadCatalogFile();
        var needsBuild = existing is null ||
            existing["builderVersion"]?.GetValue<int>() != BuilderVersion ||
            existing["suiteLockSha256"]?.GetValue<string>() != lockSha256 ||
            existing["treeSha256"]?.GetValue<string>() != ComputeTreeSha256();
        if (needsBuild) Build();
        return TryRead() is { } result ? (result.Cases, result.Counts) : null;
    }

    /// <summary>Reads the catalog and verifies it against the vendored tree and the lock.
    /// Returns null when the catalog is missing, unreadable, stale, or tree-mismatched.</summary>
    private static (OfficialCase[] Cases, OfficialCounts Counts, bool Pinned)? TryRead()
    {
        var catalog = ReadCatalogFile();
        if (catalog is null) return null;
        var lockEntry = LockEntry();
        if (lockEntry is null) return null;
        if (lockEntry["treeSha256"]?.GetValue<string>() is { Length: 64 } pinned &&
            catalog["treeSha256"]?.GetValue<string>() != pinned) return null;
        var counts = ReadCounts(catalog);
        var cases = catalog["cases"]!.AsArray().Select(ReadCase).ToArray();
        if (cases.Length != counts.TotalTests) return null;
        return (cases, counts, lockEntry["treeSha256"] is { });
    }

    /// <summary>Strict read for readiness: the lock pin is mandatory and the vendored tree
    /// must match it byte for byte. Throws with the readiness blocker reason otherwise.</summary>
    internal static (OfficialCase[] Cases, OfficialCounts Counts) Read()
    {
        if (!Directory.Exists(Root) || !File.Exists(CatalogPath))
            throw new InvalidDataException("css21-official-catalog-unavailable");
        var lockEntry = LockEntry() ?? throw new InvalidDataException("css21-official-catalog-unavailable");
        if (lockEntry["treeSha256"]?.GetValue<string>() is not { Length: 64 })
            throw new InvalidDataException("css21-official-tree-unpinned");
        var catalog = ReadCatalogFile() ?? throw new InvalidDataException("css21-official-catalog-unavailable");
        if (catalog["suiteLockSha256"]?.GetValue<string>() != ExecutionEvidence.HashFile(ConformancePaths.Manifest("test-suites.lock.json")))
            throw new InvalidDataException("css21-official-catalog-stale");
        if (catalog["treeSha256"]?.GetValue<string>() != lockEntry["treeSha256"]!.GetValue<string>())
            throw new InvalidDataException("css21-official-catalog-stale");
        if (ComputeTreeSha256() != lockEntry["treeSha256"]!.GetValue<string>())
            throw new InvalidDataException("css21-official-tree-mismatch");
        return TryRead() is { } result ? (result.Cases, result.Counts)
            : throw new InvalidDataException("css21-official-catalog-stale");
    }

    /// <summary>Appends official-catalog readiness blockers without throwing, so partial
    /// local state reports as blocked rather than crashing the inventory report.</summary>
    internal static void Verify(ICollection<string> blockers)
    {
        OfficialCounts counts;
        try
        {
            (_, counts) = Read();
        }
        catch (InvalidDataException ex)
        {
            blockers.Add(ex.Message);
            return;
        }
        foreach (var variant in counts.UnavailableVariants)
            blockers.Add($"css21-official-variant-unavailable:{variant}");
        if (!CountsReconcile(counts))
            blockers.Add("css21-official-count-mismatch");
    }

    /// <summary>Vendored counts must fit the published suite totals exactly: vendored plus
    /// unavailable tests equal the published total, and neither the required-behavior nor
    /// the optional-behavior count may exceed its published ceiling.</summary>
    internal static bool CountsReconcile(OfficialCounts counts) =>
        counts.TotalTests <= counts.ExpectedTotalTests &&
        counts.RequiredBehaviorTests <= counts.ExpectedRequiredBehaviorTests &&
        counts.OptionalTests <= counts.ExpectedTotalTests - counts.ExpectedRequiredBehaviorTests &&
        counts.TotalTests + counts.UnavailableTests == counts.ExpectedTotalTests;

    internal static OfficialCase? Case(string path) => CachedRead()?.Cases.FirstOrDefault(c => c.Path == path);

    /// <summary>Cheap memoized catalog read for per-case lookups: keyed on the catalog
    /// file's stamp and the suite-lock hash, so a rebuild invalidates it without ever
    /// re-hashing the vendored tree.</summary>
    private static (OfficialCase[] Cases, OfficialCounts Counts)? _cache;
    private static DateTime _cacheStamp;
    private static long _cacheLength;
    private static string? _cacheLockSha;

    private static (OfficialCase[] Cases, OfficialCounts Counts)? CachedRead()
    {
        var lockSha256 = ExecutionEvidence.HashFile(ConformancePaths.Manifest("test-suites.lock.json"));
        var info = new FileInfo(CatalogPath);
        if (_cache is not null && _cacheStamp == (info.Exists ? info.LastWriteTimeUtc : DateTime.MinValue) &&
            _cacheLength == (info.Exists ? info.Length : -1) && _cacheLockSha == lockSha256) return _cache;
        _cache = TryRead() is { } read ? (read.Cases, read.Counts) : null;
        _cacheStamp = info.Exists ? info.LastWriteTimeUtc : DateTime.MinValue;
        _cacheLength = info.Exists ? info.Length : -1;
        _cacheLockSha = lockSha256;
        return _cache;
    }

    internal static WptCase ToWptCase(OfficialCase Official) => new(
        Official.Path, Official.Path, Official.Kind, "window", LongTimeout: false, TestDriver: false,
        Official.References);

    internal static JsonObject? LockEntry() => JsonNode.Parse(File.ReadAllText(ConformancePaths.Manifest("test-suites.lock.json")))
        ?["suites"]!.AsArray().OfType<JsonObject>().SingleOrDefault(s => s["id"]?.GetValue<string>() == LockId);

    /// <summary>Canonical tree digest over every vendored file (the fetch log and partial
    /// downloads excluded), matching scripts\fetch-tests.ps1: lowercase forward-slash relative
    /// path, NUL, file SHA-256, LF — files ordered by path. <see cref="CanonicalTree"/> exposes
    /// the exact hashed bytes so the PowerShell fetcher and this verifier can be diffed.</summary>
    internal static string ComputeTreeSha256() =>
        Convert.ToHexString(SHA256.HashData(CanonicalTree())).ToLowerInvariant();

    internal static byte[] CanonicalTree()
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var stream = new MemoryStream();
        foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)
                     .Where(f => Path.GetFileName(f) != "fetch-failures.log" && Path.GetExtension(f) != ".download")
                     .Select(f => new FileInfo(f))
                     .OrderBy(f => Path.GetRelativePath(Root, f.FullName).Replace('\\', '/').ToLowerInvariant(), StringComparer.OrdinalIgnoreCase)
                     .ThenBy(f => Path.GetRelativePath(Root, f.FullName).Replace('\\', '/').ToLowerInvariant(), StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(Root, file.FullName).Replace('\\', '/').ToLowerInvariant();
            var entry = Encoding.UTF8.GetBytes(relative + "\0" + ExecutionEvidence.HashFile(file.FullName) + "\n");
            stream.Write(entry);
            hash.AppendData(entry);
        }
        _ = hash.GetHashAndReset();
        return stream.ToArray();
    }

    private static OfficialCounts Build()
    {
        if (!Directory.Exists(Root)) throw new InvalidDataException("css21-official-catalog-unavailable");
        var lockEntry = LockEntry() ?? throw new InvalidDataException("css21-official-catalog-unavailable");
        var expectedTotal = lockEntry["totalTests"]?.GetValue<int>() ?? throw new InvalidDataException("css21-official-catalog-unavailable");
        var expectedRequired = lockEntry["requiredBehaviorTests"]?.GetValue<int>() ?? throw new InvalidDataException("css21-official-catalog-unavailable");
        var variants = lockEntry["variants"]!.AsArray().Select(v => v!.GetValue<string>()).ToArray();

        var cases = new List<OfficialCase>();
        cases.AddRange(BuildVariant(ScreenHtml4Variant, "html4"));
        cases.AddRange(BuildVariant(OtherFormatsVariant, "other"));
        cases.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));

        var reftests = cases.Count(c => c.Kind == "reftest");
        var optional = cases.Count(c => c.OptionalBehavior);
        // The lock's variantAvailability is authoritative for what could be recovered; the
        // archivable-variants constant is the fallback for locks that predate the field.
        string[] unavailableVariants;
        if (lockEntry["variantAvailability"] is JsonObject availability)
            unavailableVariants = variants.Where(v => availability[v]?.GetValue<string>() == "unavailable").ToArray();
        else
            unavailableVariants = variants.Where(v => !ArchivableVariants.Contains(v, StringComparer.Ordinal)).ToArray();
        var counts = new OfficialCounts(cases.Count, cases.Count - optional, optional, reftests, cases.Count - reftests,
            expectedTotal, expectedRequired, Math.Max(0, expectedTotal - cases.Count), unavailableVariants);

        var catalog = new JsonObject
        {
            ["builderVersion"] = BuilderVersion,
            ["formatVersion"] = 1,
            ["suiteId"] = "css21-official",
            ["suiteUrl"] = lockEntry["repository"]!.GetValue<string>(),
            ["suiteLockSha256"] = ExecutionEvidence.HashFile(ConformancePaths.Manifest("test-suites.lock.json")),
            ["treeSha256"] = ComputeTreeSha256(),
            ["counts"] = SerializeCounts(counts),
            ["cases"] = new JsonArray(cases.Select(SerializeCase).ToArray()),
        };
        Directory.CreateDirectory(Path.GetDirectoryName(CatalogPath)!);
        File.WriteAllText(CatalogPath, catalog.ToJsonString(ExecutionEvidence.JsonOptions) + Environment.NewLine);
        return counts;
    }

    private static IEnumerable<OfficialCase> BuildVariant(string variant, string directory)
    {
        var variantRoot = Path.Combine(Root, directory);
        if (!Directory.Exists(variantRoot)) yield break;
        // The suite's own machine-checkable reference pairs; self-describing tests are the rest.
        var reftestList = Path.Combine(Root, "html4", "reftest.list");
        var references = new Dictionary<string, List<WptReference>>(StringComparer.Ordinal);
        var refFiles = new HashSet<string>(StringComparer.Ordinal);
        if (File.Exists(reftestList))
        {
            foreach (var raw in File.ReadAllLines(reftestList))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;
                var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 3 || parts[0] is not ("==" or "!="))
                    throw new InvalidDataException($"Invalid html4/reftest.list entry: {line}");
                var test = $"{UrlPrefix}html4/{parts[1]}";
                // Root-absolute: the reftest runner resolves reference URLs against the
                // test document's URL, so a bare relative path would double the directory.
                var reference = $"/{UrlPrefix}html4/{parts[2]}";
                if (!references.TryGetValue(test, out var list)) references[test] = list = [];
                list.Add(new WptReference(reference, parts[0]));
                refFiles.Add(parts[2]);
            }
        }
        foreach (var file in Directory.EnumerateFiles(variantRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(variantRoot, file).Replace('\\', '/');
            if (relative.StartsWith("support/", StringComparison.Ordinal)) continue;
            var name = Path.GetFileName(file);
            if (!relative.EndsWith(".htm", StringComparison.OrdinalIgnoreCase) &&
                !relative.EndsWith(".html", StringComparison.OrdinalIgnoreCase)) continue;
            // Chapter indexes and the table of contents are not tests.
            if (name.Equals("toc.html", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("reftest-toc.html", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("chapter-", StringComparison.OrdinalIgnoreCase)) continue;
            // Reference pages: listed in reftest.list's right column or using its -ref naming.
            if (refFiles.Contains(relative) || (variant == ScreenHtml4Variant &&
                relative.EndsWith("-ref.htm", StringComparison.OrdinalIgnoreCase))) continue;
            var path = $"{UrlPrefix}{directory}/{relative}";
            var (title, assert, flags, helps) = ParseHead(file);
            references.TryGetValue(path, out var refs);
            yield return new OfficialCase(path, refs is { Count: > 0 } ? "reftest" : "selftest", variant,
                title, assert, flags, helps, refs ?? []);
        }
    }

    private static (string Title, string Assert, string[] Flags, string[] Helps) ParseHead(string file)
    {
        string text;
        try { text = File.ReadAllText(file); }
        catch (IOException) { return ("", "", [], []); }
        var title = TitleTag.Match(text) is { Success: true } t ? Decode(t.Groups[1].Value) : "";
        var assert = "";
        string[] flags = [];
        for (var meta = MetaTag.Match(text); meta.Success; meta = meta.NextMatch())
        {
            var attributes = Attributes(meta.Value);
            var name = attributes.TryGetValue("name", out var metaName) ? metaName.ToLowerInvariant() : "";
            if (name == "assert" && attributes.TryGetValue("content", out var metaAssert)) assert = Decode(metaAssert);
            if (name == "flags" && attributes.TryGetValue("content", out var metaFlags) && flags.Length == 0)
                flags = metaFlags.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        }
        var helps = new List<string>();
        for (var link = LinkTag.Match(text); link.Success; link = link.NextMatch())
        {
            var attributes = Attributes(link.Value);
            if (attributes.TryGetValue("rel", out var rel) && rel.Equals("help", StringComparison.OrdinalIgnoreCase) &&
                attributes.TryGetValue("href", out var href) && href.Length > 0)
                helps.Add(href);
        }
        return (title, assert, flags, helps.ToArray());
    }

    private static Dictionary<string, string> Attributes(string tag)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match attribute in AttributeValue.Matches(tag))
            attributes[attribute.Groups[1].Value.ToLowerInvariant()] = Decode(attribute.Groups[2].Value);
        return attributes;
    }

    private static string Decode(string value) => System.Net.WebUtility.HtmlDecode(value.Trim());

    private static JsonObject? ReadCatalogFile()
    {
        if (!File.Exists(CatalogPath)) return null;
        try
        {
            var catalog = JsonNode.Parse(File.ReadAllText(CatalogPath))?.AsObject();
            return catalog?["formatVersion"]?.GetValue<int>() == 1 &&
                catalog["cases"] is JsonArray && catalog["counts"] is JsonObject ? catalog : null;
        }
        catch (JsonException) { return null; }
    }

    private static JsonObject SerializeCounts(OfficialCounts counts) => new()
    {
        ["totalTests"] = counts.TotalTests,
        ["requiredBehaviorTests"] = counts.RequiredBehaviorTests,
        ["optionalTests"] = counts.OptionalTests,
        ["reftests"] = counts.Reftests,
        ["selfTests"] = counts.SelfTests,
        ["expectedTotalTests"] = counts.ExpectedTotalTests,
        ["expectedRequiredBehaviorTests"] = counts.ExpectedRequiredBehaviorTests,
        ["unavailableTests"] = counts.UnavailableTests,
        ["unavailableVariants"] = new JsonArray(counts.UnavailableVariants.Select(v => (JsonNode)v).ToArray()),
    };

    private static OfficialCounts ReadCounts(JsonObject catalog)
    {
        var counts = catalog["counts"]!.AsObject();
        return new OfficialCounts(counts["totalTests"]!.GetValue<int>(), counts["requiredBehaviorTests"]!.GetValue<int>(),
            counts["optionalTests"]!.GetValue<int>(), counts["reftests"]!.GetValue<int>(), counts["selfTests"]!.GetValue<int>(),
            counts["expectedTotalTests"]!.GetValue<int>(), counts["expectedRequiredBehaviorTests"]!.GetValue<int>(),
            counts["unavailableTests"]!.GetValue<int>(),
            counts["unavailableVariants"]!.AsArray().Select(v => v!.GetValue<string>()).ToArray());
    }

    private static JsonObject SerializeCase(OfficialCase Official) => new()
    {
        ["path"] = Official.Path,
        ["kind"] = Official.Kind,
        ["variant"] = Official.Variant,
        ["title"] = Official.Title,
        ["assert"] = Official.Assert,
        ["flags"] = new JsonArray(Official.Flags.Select(f => (JsonNode)f).ToArray()),
        ["helps"] = new JsonArray(Official.Helps.Select(h => (JsonNode)h).ToArray()),
        ["references"] = new JsonArray(Official.References.Select(r => (JsonObject)new()
        {
            ["url"] = r.Url,
            ["relation"] = r.Relation,
        }).ToArray()),
    };

    private static OfficialCase ReadCase(JsonNode? node)
    {
        var record = node!.AsObject();
        var references = record["references"]!.AsArray().Select(r => new WptReference(
            r!["url"]!.GetValue<string>(), r!["relation"]!.GetValue<string>())).ToArray();
        return new OfficialCase(
            record["path"]!.GetValue<string>(), record["kind"]!.GetValue<string>(), record["variant"]!.GetValue<string>(),
            record["title"]!.GetValue<string>(), record["assert"]!.GetValue<string>(),
            record["flags"]!.AsArray().Select(f => f!.GetValue<string>()).ToArray(),
            record["helps"]!.AsArray().Select(h => h!.GetValue<string>()).ToArray(), references);
    }
}
