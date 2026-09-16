using System.Text.Json.Nodes;
using Lite.Conformance.Harness;

namespace Lite.Conformance.Wpt;

internal sealed record WptReference(string Url, string Relation);
internal sealed record WptCase(string Source, string Path, string Kind, string Context,
    bool LongTimeout, bool TestDriver, IReadOnlyList<WptReference> References, JsonObject? Options = null);

/// <summary>Reads the pinned upstream manifest, including tests Lite cannot execute yet.</summary>
internal static class WptCatalog
{
    internal static string ManifestPath => Path.Combine(ConformancePaths.EnsureArtifacts(), "wpt-manifest.json");

    internal static IReadOnlyList<WptCase> Read(string path)
    {
        if (Path.GetFullPath(path) == Path.GetFullPath(ManifestPath))
        {
            var provenance = JsonNode.Parse(File.ReadAllText(path + ".meta.json"))!.AsObject();
            var suiteLock = ConformancePaths.Manifest("test-suites.lock.json");
            var expected = JsonNode.Parse(File.ReadAllText(suiteLock))!["suites"]!.AsArray().OfType<JsonObject>()
                .Single(s => s["id"]?.GetValue<string>() == "wpt")["revision"]!.GetValue<string>();
            if (provenance["revision"]?.GetValue<string>() != expected ||
                provenance["suiteLockSha256"]?.GetValue<string>() != ExecutionEvidence.HashFile(suiteLock) ||
                provenance["manifestSha256"]?.GetValue<string>() != ExecutionEvidence.HashFile(path))
                throw new InvalidDataException("WPT manifest provenance differs from its contents or suite lock; regenerate it.");
        }
        return Parse(JsonNode.Parse(File.ReadAllText(path))!.AsObject());
    }

    internal static IReadOnlyList<WptCase> Parse(JsonObject manifest)
    {
        if (manifest["version"]?.GetValue<int>() != 9 || manifest["url_base"]?.GetValue<string>() != "/" ||
            manifest["items"] is not JsonObject kinds)
            throw new InvalidDataException("Expected the pinned WPT version 9 manifest with url_base '/'.");
        var cases = new List<WptCase>();
        foreach (var (kind, tree) in kinds)
        {
            if (kind is "support" or "spec") continue;
            if (tree is not JsonObject directory) throw new InvalidDataException($"Invalid WPT manifest tree: {kind}");
            Walk(directory, "", kind, cases);
        }
        if (cases.GroupBy(c => (c.Kind, c.Path)).Any(g => g.Count() != 1))
            throw new InvalidDataException("Duplicate WPT test URLs.");
        return cases.OrderBy(c => c.Path, StringComparer.Ordinal).ThenBy(c => c.Kind, StringComparer.Ordinal).ToArray();
    }

    private static void Walk(JsonObject tree, string prefix, string kind, List<WptCase> cases)
    {
        foreach (var (name, node) in tree)
        {
            if (name.Length == 0 || name is "." or ".." || name.IndexOfAny(['/', '\\', '?', '#', ':']) >= 0)
                throw new InvalidDataException("Invalid WPT manifest source path.");
            var source = prefix + name;
            if (node is JsonObject directory) { Walk(directory, source + "/", kind, cases); continue; }
            if (node is not JsonArray entries || entries.Count < 2 || entries[0] is not JsonValue)
                throw new InvalidDataException($"Invalid WPT source record: {source}");
            foreach (var entry in entries.Skip(1))
            {
                if (entry is not JsonArray item || item.Count < 2 || item[^1] is not JsonObject metadata)
                    throw new InvalidDataException($"Invalid WPT test record: {source}");
                var path = (item[0]?.GetValue<string>() ?? source).TrimStart('/');
                if (!ValidPath(path)) throw new InvalidDataException($"Invalid WPT test URL: {path}");
                var references = new List<WptReference>();
                if (kind is "reftest" or "print-reftest")
                {
                    if (item.Count != 3 || item[1] is not JsonArray refs || refs.Count == 0)
                        throw new InvalidDataException($"Missing WPT reference relations: {path}");
                    foreach (var reference in refs)
                    {
                        if (reference is not JsonArray pair || pair.Count != 2 ||
                            pair[1]?.GetValue<string>() is not ("==" or "!="))
                            throw new InvalidDataException($"Invalid WPT reference relation: {path}");
                        var url = pair[0]!.GetValue<string>();
                        references.Add(new(url, pair[1]!.GetValue<string>()));
                    }
                }
                cases.Add(new(source, path, kind, Context(path), metadata["timeout"]?.GetValue<string>() == "long",
                    metadata["testdriver"]?.GetValue<bool>() == true, references, (JsonObject)metadata.DeepClone()));
            }
        }
    }

    internal static bool ValidPath(string path) => !string.IsNullOrWhiteSpace(path) && !path.StartsWith('/') &&
        !path.Contains('\\') && !path.Contains(':') &&
        !Uri.UnescapeDataString(path.Split(['?', '#'])[0]).Split('/').Any(s => s is "." or "..");

    internal static string Context(string path)
    {
        var file = path.Split(['?', '#'])[0];
        if (file.Contains(".serviceworker.", StringComparison.Ordinal)) return "serviceworker";
        if (file.Contains(".sharedworker.", StringComparison.Ordinal)) return "sharedworker";
        if (file.Contains(".worker.", StringComparison.Ordinal)) return "dedicatedworker";
        if (file.Contains(".shadowrealm.", StringComparison.Ordinal)) return "shadowrealm";
        return "window";
    }

    internal static bool IsHtmlCandidate(WptCase test) => HtmlApplicability.CandidateRoots.Any(root =>
        test.Source.StartsWith(root + "/", StringComparison.Ordinal));
}
