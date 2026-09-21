using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lite.Conformance.Harness;
using Lite.Conformance.Wpt;

namespace Lite.Conformance.Css21;

internal sealed record Css21Readiness(bool Ready, IReadOnlyList<string> Blockers, int Requirements, int Tests);

/// <summary>CSS readiness is derived from reviewed inventories and executed evidence, never a sample pass rate.</summary>
internal static class Css21Inventory
{
    internal const string Target = "https://www.w3.org/TR/2011/REC-CSS2-20110607/";
    internal const string RequirementsFile = "Profile/css21-requirements.json";
    internal const string ApplicabilityFile = "Css21/css21-applicability.json";
    internal const string ArchiveSha256 = "ef072758dbf8618d2f6fe2fc60afd7cf4a73dc3b566ca39f06f4a6a297dbea60";
    private const string SectionIndex = "9b700634ebe4e6f2a7b2f0c61e8ef5b0fb70ba8134b9d3e32975137978f27ea1";
    private const string PropertyIndex = "07c417ff194c6999477932340d0521560b9ab067c8bc2462d39f3744881ca7b9";

    internal static JsonObject Read(string file) => JsonNode.Parse(File.ReadAllText(ConformancePaths.Manifest(file)))?.AsObject()
        ?? throw new InvalidDataException($"Missing CSS inventory: {file}");

    internal static WptCase[] Candidates() => WptCatalog.Read(WptCatalog.ManifestPath)
        .Where(c => c.Source.StartsWith("css/CSS2/", StringComparison.Ordinal)).ToArray();

    internal static string InventoryHash()
    {
        var inputs = new[] { "Profile/css21-sections.json", "Profile/css21-properties.json", RequirementsFile, ApplicabilityFile };
        return Hash(string.Join('\n', inputs.Select(f => f + "\0" + ExecutionEvidence.HashFile(ConformancePaths.Manifest(f)))));
    }

    internal static void ValidateIndex(JsonObject inventory, bool properties)
    {
        var field = properties ? "properties" : "sections";
        string[] keys = properties ? ["name", "url", "values", "initial", "appliesTo", "inherited", "percentages", "mediaGroups"]
            : ["clause", "title", "url"];
        var expected = properties ? PropertyIndex : SectionIndex;
        if (inventory["schemaVersion"]?.GetValue<int>() != 1 || Text(inventory, "target") != Target ||
            Text(inventory, "archiveSha256") != ArchiveSha256 || inventory[field] is not JsonArray records)
            throw new InvalidDataException($"Invalid CSS {field} inventory header.");
        var canonical = new StringBuilder();
        foreach (var node in records)
        {
            var record = node?.AsObject() ?? throw new InvalidDataException($"Invalid CSS {field} record.");
            canonical.AppendJoin('\t', keys.Select(k => k == "inherited" ?
                record[k]!.GetValue<bool>().ToString().ToLowerInvariant() : Text(record, k))).Append('\n');
        }
        if (Text(inventory, "indexSha256") != expected || Hash(canonical.ToString()) != expected)
            throw new InvalidDataException($"CSS {field} index differs from the pinned Recommendation; reimport it.");
    }

    internal static Css21Readiness Evaluate(string media, IReadOnlyList<TestEvidence> evidence)
    {
        if (media is not ("screen" or "print")) throw new ArgumentException("CSS media must be screen or print.", nameof(media));
        var blockers = new List<string>();
        var requirements = Read(RequirementsFile);
        var applicability = Read(ApplicabilityFile);
        ValidateReviews(requirements, applicability);
        var requirementsById = requirements["requirements"]!.AsArray().OfType<JsonObject>()
            .ToDictionary(r => Text(r, "id"), StringComparer.Ordinal);
        foreach (var properties in new[] { false, true })
        {
            var inventory = Read(properties ? "Profile/css21-properties.json" : "Profile/css21-sections.json");
            ValidateIndex(inventory, properties);
            var field = properties ? "properties" : "sections";
            var unreviewed = 0;
            foreach (var row in inventory[field]!.AsArray().OfType<JsonObject>())
            {
                var classification = Text(row, "classification");
                if (classification == "unreviewed") { unreviewed++; continue; }
                if (classification is not ("user-agent" or "mixed" or "informative" or "authoring" or "optional") ||
                    string.IsNullOrWhiteSpace(Text(row, "rationale")))
                    throw new InvalidDataException($"Invalid reviewed CSS {field} record.");
                var ids = Strings(row, "requirementIds");
                if ((classification is "user-agent" or "mixed") && ids.Length == 0)
                    blockers.Add($"css21-{field}-without-obligations:{Text(row, properties ? "name" : "clause")}");
                foreach (var id in ids)
                    if (!requirementsById.ContainsKey(id)) throw new InvalidDataException($"Unknown CSS obligation: {id}");
            }
            if (unreviewed > 0) blockers.Add($"css21-unreviewed-{field}:{unreviewed}");
            if (inventory["reviewComplete"]?.GetValue<bool>() != true) blockers.Add($"css21-{field}-review-incomplete");
        }
        if (requirements["reviewComplete"]?.GetValue<bool>() != true) blockers.Add("css21-obligation-inventory-incomplete");
        if (applicability["reviewComplete"]?.GetValue<bool>() != true) blockers.Add("css21-test-review-incomplete");
        var hash = InventoryHash();
        var applicableRequirements = requirementsById.Values.Where(r => Strings(r, "media").Contains(media)).ToArray();
        if (applicableRequirements.Length == 0) blockers.Add($"css21-no-obligations:{media}");
        foreach (var requirement in applicableRequirements)
        {
            var id = Text(requirement, "id");
            if (Text(requirement, "status") != "implemented") blockers.Add($"css21-unimplemented:{id}");
            var tests = requirement["tests"]!.AsArray().OfType<JsonObject>().ToArray();
            if (tests.Length == 0) blockers.Add($"css21-untested:{id}");
            foreach (var test in tests)
                if (!HasEvidence(evidence, Text(test, "suite"), Text(test, "path"), media, hash, Text(test, "assertion")))
                    blockers.Add($"css21-missing-evidence:{media}:{id}:{Text(test, "path")}");
        }
        var reviews = applicability["tests"]!.AsArray().OfType<JsonObject>().ToDictionary(r => Text(r, "path"));
        var count = 0;
        if (!File.Exists(WptCatalog.ManifestPath)) blockers.Add("css21-missing-wpt-catalog");
        else
        {
            var candidates = Candidates();
            if (candidates.Length == 0) blockers.Add("css21-empty-wpt-catalog");
            var unreviewed = 0;
            foreach (var test in candidates)
            {
                if (!reviews.TryGetValue(test.Path, out var review) || Text(review, "classification") == "unreviewed")
                { unreviewed++; continue; }
                if (Text(review, "classification") != "applicable" || !Strings(review, "media").Contains(media)) continue;
                count++;
                foreach (var id in Strings(review, "requirementIds"))
                    if (!requirementsById.TryGetValue(id, out var requirement) || !Strings(requirement, "media").Contains(media))
                        blockers.Add($"css21-invalid-test-obligation:{test.Path}:{id}:{media}");
                if (!HasEvidence(evidence, "css21-wpt", test.Path, media, hash))
                    blockers.Add($"css21-missing-case:{media}:{test.Path}");
            }
            if (unreviewed > 0) blockers.Add($"css21-unclassified-wpt-cases:{unreviewed}");
            var paths = candidates.Select(c => c.Path).ToHashSet(StringComparer.Ordinal);
            foreach (var path in reviews.Keys.Where(p => !paths.Contains(p))) blockers.Add($"css21-orphan-test-review:{path}");
        }
        // A directory or a hand-edited 'vendored' flag alone cannot prove the historical suite is complete.
        // The official catalog importer must be implemented before this blocker can be removed.
        blockers.Add("css21-official-catalog-unavailable");
        return new(blockers.Count == 0, blockers.Distinct().ToArray(), applicableRequirements.Length, count);
    }

    internal static bool HasEvidence(IReadOnlyList<TestEvidence> evidence, string suite, string path,
        string media, string inventoryHash, string? assertion = null)
    {
        if (suite == "unit") return ExecutionEvidence.HasPassingEvidence(evidence, suite, path, assertion);
        var matches = evidence.Where(t => t.Suite == suite && t.Path == path && t.Css?.Media == media).ToArray();
        var expectedMode = Path.GetExtension(path.Split(['?', '#'])[0]) is ".xht" or ".xhtml" or ".xml" ? "xhtml" : "html";
        return matches.Length > 0 && matches.All(t => t.Css is { } css && css.InventorySha256 == inventoryHash &&
            css.DocumentMode == expectedMode && css.ViewportWidth > 0 && css.ViewportHeight > 0 &&
            (media != "print" || css.PageCount > 0 && css.PageWidthPoints > 0 && css.PageHeightPoints > 0 &&
                t.Artifacts?.Any(a => a.Kind == "pdf") == true) &&
            t.Outcome == "pass" && t.HarnessStatus is null or 0 && t.Subtests.Count > 0 && t.Subtests.All(s => s.Status == 0) &&
            (string.IsNullOrEmpty(assertion) || t.Subtests.Any(s => s.Name == assertion)) &&
            (suite != "css21-wpt" || t.Environment == "upstream-wpt"));
    }

    internal static void ValidateReviews(JsonObject requirements, JsonObject applicability)
    {
        foreach (var data in new[] { requirements, applicability })
            if (data["schemaVersion"]?.GetValue<int>() != 1 || Text(data, "target") != Target ||
                data["reviewComplete"] is not JsonValue complete || !complete.TryGetValue<bool>(out _))
                throw new InvalidDataException("Invalid CSS review header.");
        if (requirements["requirements"] is not JsonArray rows || applicability["tests"] is not JsonArray tests)
            throw new InvalidDataException("Missing CSS obligations or test reviews.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows.OfType<JsonObject>())
        {
            if (string.IsNullOrWhiteSpace(Text(row, "id")) || !ids.Add(Text(row, "id")) ||
                !Text(row, "url").StartsWith(Target, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(Text(row, "title")) ||
                Text(row, "status") is not ("untested" or "implemented" or "failing") || row["tests"] is not JsonArray)
                throw new InvalidDataException("Invalid or duplicate CSS obligation.");
            ValidateMedia(row, allowEmpty: false);
            foreach (var test in row["tests"]!.AsArray())
                if (test is not JsonObject mapping || Text(mapping, "suite") is not ("unit" or "css21-wpt" or "css21-official" or "manual") ||
                    !WptCatalog.ValidPath(Text(mapping, "path")) || string.IsNullOrWhiteSpace(Text(mapping, "assertion")))
                    throw new InvalidDataException("CSS obligation mappings need an executable path, suite and exact assertion.");
        }
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in tests.OfType<JsonObject>())
        {
            var classification = Text(row, "classification");
            if (!WptCatalog.ValidPath(Text(row, "path")) || !paths.Add(Text(row, "path")) ||
                classification is not ("unreviewed" or "applicable" or "later-feature" or "informative" or "optional" or "defective"))
                throw new InvalidDataException("Invalid or duplicate CSS test review.");
            if (classification != "unreviewed" && string.IsNullOrWhiteSpace(Text(row, "rationale")))
                throw new InvalidDataException("Reviewed CSS tests need a rationale.");
            ValidateMedia(row, allowEmpty: classification != "applicable");
            if (classification == "applicable" && Strings(row, "requirementIds").Length == 0)
                throw new InvalidDataException("Applicable CSS tests need obligation mappings.");
            // Defects remain blockers until an independently reviewed equivalent is represented.
            if (classification == "defective") throw new InvalidDataException("A defective CSS test needs an equivalent replacement before exclusion.");
        }
        if (rows.Any(n => n is not JsonObject) || tests.Any(n => n is not JsonObject))
            throw new InvalidDataException("CSS inventory rows must be objects.");
    }

    private static void ValidateMedia(JsonObject row, bool allowEmpty)
    {
        var media = Strings(row, "media");
        if ((!allowEmpty && media.Length == 0) || media.Any(m => m is not ("screen" or "print")) || media.Distinct().Count() != media.Length)
            throw new InvalidDataException("CSS review media must explicitly identify screen and/or print.");
    }

    internal static int Run(string? reportPath)
    {
        var screen = Evaluate("screen", []);
        var print = Evaluate("print", []);
        var candidates = File.Exists(WptCatalog.ManifestPath) ? Candidates() : [];
        var reviews = Read(ApplicabilityFile)["tests"]!.AsArray().OfType<JsonObject>().ToDictionary(t => Text(t, "path"));
        var destination = reportPath ?? Path.Combine(ConformancePaths.EnsureArtifacts(), "css21-inventory.json");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        var report = new { formatVersion = 1, target = Target, inventorySha256 = InventoryHash(), screen, print,
            cases = candidates.Select(c => new { c.Source, c.Path, c.Kind, c.Context, c.References,
                classification = reviews.TryGetValue(c.Path, out var review) ? Text(review, "classification") : "unreviewed" }) };
        File.WriteAllText(destination, JsonSerializer.Serialize(report, ExecutionEvidence.JsonOptions) + Environment.NewLine);
        Console.WriteLine($"CSS inventory: {candidates.Length} WPT cases; screen blockers={screen.Blockers.Count}; print blockers={print.Blockers.Count}; {destination}");
        return 0;
    }

    internal static string[] Strings(JsonObject obj, string key) => obj[key] is JsonArray items
        ? items.Select(i => i?.GetValue<string>() ?? "").ToArray() : [];
    internal static string Text(JsonObject obj, string key) => obj[key]?.GetValue<string>() ?? "";
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}
