using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lite.Conformance.Harness;
using Lite.Conformance.Wpt;

namespace Lite.Conformance.Css21;

internal sealed record Css21Readiness(bool Ready, IReadOnlyList<string> Blockers, int Requirements, int Tests,
    IReadOnlyList<string>? Exclusions = null);

/// <summary>CSS readiness is derived from reviewed inventories and executed evidence, never a sample pass rate.</summary>
internal static class Css21Inventory
{
    internal const string Target = "https://www.w3.org/TR/2011/REC-CSS2-20110607/";
    internal const string RequirementsFile = "Profile/css21-requirements.json";
    internal const string ApplicabilityFile = "Css21/css21-applicability.json";
    internal const string CuratedManifestFile = "Css21/css21-manifest.txt";
    internal static readonly string[] DefectKinds = ["missing-input", "self-contradiction", "contradicts-target", "superseded-in-profile"];
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
        var reviews = applicability["tests"]!.AsArray().OfType<JsonObject>().ToDictionary(r => Text(r, "path"));
        // An obligation's evidence is every reviewed case the applicability file maps to it
        // (derived, never hand-copied), plus its explicit unit/manual/curated mappings, plus —
        // for a roll-up section — the obligations it rolls up.
        var derived = DerivedTests(reviews.Values, media);
        foreach (var requirement in applicableRequirements)
        {
            var id = Text(requirement, "id");
            var implemented = Text(requirement, "status") == "implemented";
            if (!implemented) blockers.Add($"css21-unimplemented:{id}");
            var tests = requirement["tests"]!.AsArray().OfType<JsonObject>().ToArray();
            var cases = derived.GetValueOrDefault(id) ?? [];
            var rollUp = Strings(requirement, "rollUp");
            if (tests.Length == 0 && cases.Count == 0 && rollUp.Length == 0) blockers.Add($"css21-untested:{id}");
            foreach (var test in tests)
                if (!HasEvidence(evidence, Text(test, "suite"), Text(test, "path"), media, hash, Text(test, "assertion")))
                    blockers.Add($"css21-missing-evidence:{media}:{id}:{Text(test, "path")}");
            // Each derived case already blocks on its own as a missing case; a status that
            // claims implemented over cases without passing evidence is a stale flag.
            var unproven = cases.Count(c => !HasEvidence(evidence, c.Suite, c.Path, media, hash));
            if (implemented && unproven > 0) blockers.Add($"css21-implemented-without-evidence:{media}:{id}:{unproven}");
            foreach (var child in rollUp)
                if (implemented && Text(requirementsById[child], "status") != "implemented")
                    blockers.Add($"css21-rollup-incomplete:{media}:{id}:{child}");
        }
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
            // Official-suite reviews live in the same manifest but are accounted for
            // against the official catalog below, not against the WPT candidates.
            foreach (var path in reviews.Keys.Where(p => !p.StartsWith(OfficialCatalog.UrlPrefix, StringComparison.Ordinal) &&
                     !paths.Contains(p))) blockers.Add($"css21-orphan-test-review:{path}");
        }
        // The official catalog is verified against the vendored tree and the lock's pinned
        // tree hash; its variants and counts must reconcile with the published suite.
        OfficialCatalog.Verify(blockers);
        // Official-suite cases follow the same accounting as WPT candidates: every case is
        // classified, applicable cases map to reviewed obligations, and evidence exists.
        try
        {
            var (officialCases, _) = OfficialCatalog.Read();
            var officialUnclassified = 0;
            var officialApplicable = 0;
            foreach (var officialCase in officialCases)
            {
                if (!reviews.TryGetValue(officialCase.Path, out var review) || Text(review, "classification") == "unreviewed")
                { officialUnclassified++; continue; }
                if (Text(review, "classification") != "applicable" || !Strings(review, "media").Contains(media)) continue;
                officialApplicable++;
                foreach (var id in Strings(review, "requirementIds"))
                    if (!requirementsById.TryGetValue(id, out var requirement) || !Strings(requirement, "media").Contains(media))
                        blockers.Add($"css21-invalid-test-obligation:{officialCase.Path}:{id}:{media}");
                if (!HasEvidence(evidence, "css21-official", officialCase.Path, media, hash))
                    blockers.Add($"css21-missing-case:{media}:{officialCase.Path}");
            }
            if (officialUnclassified > 0) blockers.Add($"css21-unclassified-official-cases:{officialUnclassified}");
            count += officialApplicable;
            var officialPaths = officialCases.Select(c => c.Path).ToHashSet(StringComparer.Ordinal);
            foreach (var path in reviews.Keys.Where(p => p.StartsWith(OfficialCatalog.UrlPrefix, StringComparison.Ordinal) &&
                     !officialPaths.Contains(p)))
                blockers.Add($"css21-orphan-test-review:{path}");
        }
        catch (InvalidDataException) { /* Verify already recorded the official-catalog blocker. */ }
        // A defective test is excluded only while every reviewed replacement carrying its
        // obligation passes; the exclusion itself is published, never silent.
        var exclusions = new List<string>();
        foreach (var review in reviews.Values.Where(r => Text(r, "classification") == "defective" &&
                     Strings(r, "media").Contains(media)).OrderBy(r => Text(r, "path"), StringComparer.Ordinal))
        {
            var path = Text(review, "path");
            var replacements = review["replacements"]!.AsArray().OfType<JsonObject>().ToArray();
            exclusions.Add($"{path} ({Text(review["defect"]!.AsObject(), "kind")}) -> " +
                string.Join(", ", replacements.Select(r => $"{Text(r, "suite")}:{Text(r, "path")}")));
            foreach (var replacement in replacements)
                if (!HasEvidence(evidence, Text(replacement, "suite"), Text(replacement, "path"), media, hash))
                    blockers.Add($"css21-defective-without-passing-replacement:{media}:{path}:{Text(replacement, "path")}");
        }
        return new(blockers.Count == 0, blockers.Distinct().ToArray(), applicableRequirements.Length, count, exclusions);
    }

    /// <summary>An obligation mapping whose assertion is "*" is satisfied by the whole case
    /// passing, whatever route decided it. Route-specific subtest names (the no-red scan's
    /// sentence, a twin's reftest) change when a case moves to a stricter route, which
    /// would orphan the mapping although the case still passes.</summary>
    internal const string WholeCase = "*";

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IReadOnlyList<TestEvidence>,
        ILookup<(string Suite, string Path), TestEvidence>> EvidenceIndex = new();

    internal static bool HasEvidence(IReadOnlyList<TestEvidence> evidence, string suite, string path,
        string media, string inventoryHash, string? assertion = null)
    {
        if (suite == "unit")
            return ExecutionEvidence.HasPassingEvidence(evidence, suite, path, assertion == WholeCase ? null : assertion);
        // Readiness asks once per case over the whole evidence list; index it once per list.
        var index = EvidenceIndex.GetValue(evidence, list => list.ToLookup(t => (t.Suite, t.Path)));
        var matches = index[(suite, path)].Where(t => t.Css?.Media == media).ToArray();
        var expectedMode = Path.GetExtension(path.Split(['?', '#'])[0]) is ".xht" or ".xhtml" or ".xml" ? "xhtml" : "html";
        return matches.Length > 0 && matches.All(t => t.Css is { } css && css.InventorySha256 == inventoryHash &&
            css.DocumentMode == expectedMode && css.ViewportWidth > 0 && css.ViewportHeight > 0 &&
            (media != "print" || css.PageCount > 0 && css.PageWidthPoints > 0 && css.PageHeightPoints > 0 &&
                t.Artifacts?.Any(a => a.Kind == "pdf") == true) &&
            t.Outcome == "pass" && t.HarnessStatus is null or 0 && t.Subtests.Count > 0 && t.Subtests.All(s => s.Status == 0) &&
            (string.IsNullOrEmpty(assertion) || assertion == WholeCase || t.Subtests.Any(s => s.Name == assertion)) &&
            (suite != "css21-wpt" || t.Environment == "upstream-wpt"));
    }

    /// <summary>Obligation id → the reviewed cases that carry it for one medium: applicable
    /// cases mapped to it, and the replacements standing in for its defective cases.</summary>
    internal static Dictionary<string, List<(string Suite, string Path)>> DerivedTests(IEnumerable<JsonObject> reviews, string media)
    {
        var derived = new Dictionary<string, List<(string Suite, string Path)>>(StringComparer.Ordinal);
        void Add(string id, string suite, string path)
        {
            if (!derived.TryGetValue(id, out var list)) derived[id] = list = [];
            if (!list.Contains((suite, path))) list.Add((suite, path));
        }
        foreach (var review in reviews)
        {
            if (!Strings(review, "media").Contains(media)) continue;
            var classification = Text(review, "classification");
            var path = Text(review, "path");
            if (classification == "applicable")
                foreach (var id in Strings(review, "requirementIds"))
                    Add(id, path.StartsWith(OfficialCatalog.UrlPrefix, StringComparison.Ordinal) ? "css21-official" : "css21-wpt", path);
            else if (classification == "defective")
                foreach (var id in Strings(review, "requirementIds"))
                    foreach (var replacement in review["replacements"]!.AsArray().OfType<JsonObject>())
                        Add(id, Text(replacement, "suite"), Text(replacement, "path"));
        }
        return derived;
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
                if (test is not JsonObject mapping || Text(mapping, "suite") is not ("unit" or "css21-wpt" or "css21-official" or "css21-curated" or "manual") ||
                    !WptCatalog.ValidPath(Text(mapping, "path")) || string.IsNullOrWhiteSpace(Text(mapping, "assertion")))
                    throw new InvalidDataException("CSS obligation mappings need an executable path, suite and exact assertion.");
        }
        // A roll-up names known obligations other than itself, and never loops back.
        var byId = rows.OfType<JsonObject>().ToDictionary(r => Text(r, "id"), StringComparer.Ordinal);
        foreach (var row in byId.Values.Where(r => r["rollUp"] is not null))
        {
            if (row["rollUp"] is not JsonArray { Count: > 0 } || Strings(row, "rollUp").Any(c => !byId.ContainsKey(c)) ||
                Strings(row, "rollUp").Distinct().Count() != Strings(row, "rollUp").Length)
                throw new InvalidDataException($"CSS roll-up {Text(row, "id")} must list known obligations.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Stack<string>(Strings(row, "rollUp"));
            while (pending.TryPop(out var next))
            {
                if (next == Text(row, "id")) throw new InvalidDataException($"CSS roll-up {Text(row, "id")} contains itself.");
                if (seen.Add(next)) foreach (var child in Strings(byId[next], "rollUp")) pending.Push(child);
            }
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
            if (row["permits"] is not null && (classification != "optional" || string.IsNullOrWhiteSpace(Text(row, "permits"))))
                throw new InvalidDataException($"Only optional CSS tests cite a permitting sentence: {Text(row, "path")}");
            // A defect is excluded only through a reviewed equivalent that carries the same
            // obligation; anything else stays a blocker (validated across rows below).
            if (classification != "defective")
            {
                if (row["defect"] is not null || row["replacements"] is not null)
                    throw new InvalidDataException($"Only defective CSS tests carry a defect or replacements: {Text(row, "path")}");
                continue;
            }
            if (row["defect"] is not JsonObject defect || !DefectKinds.Contains(Text(defect, "kind")) ||
                string.IsNullOrWhiteSpace(Text(defect, "citation")) || string.IsNullOrWhiteSpace(Text(defect, "detail")) ||
                Strings(row, "media").Length == 0 || Strings(row, "requirementIds").Length == 0 ||
                row["replacements"] is not JsonArray { Count: > 0 } replacements ||
                replacements.Any(r => r is not JsonObject o || Text(o, "suite") is not ("css21-wpt" or "css21-official" or "css21-curated") ||
                    !WptCatalog.ValidPath(Text(o, "path")) || Text(o, "relation") is not ("maintained-twin" or "authored-equivalent") ||
                    Text(o, "path") == Text(row, "path")))
                throw new InvalidDataException($"A defective CSS test needs a reviewed defect, its obligations and an equivalent replacement: {Text(row, "path")}");
        }
        ValidateReplacements(tests.OfType<JsonObject>().ToDictionary(r => Text(r, "path")));
        if (rows.Any(n => n is not JsonObject) || tests.Any(n => n is not JsonObject))
            throw new InvalidDataException("CSS inventory rows must be objects.");
    }

    /// <summary>A replacement stands in for a defective case only if it is itself evidence
    /// for the same obligation and media: an applicable suite case, or a curated reftest the
    /// CI gate runs without an expected-fail waiver.</summary>
    private static void ValidateReplacements(Dictionary<string, JsonObject> rows)
    {
        Dictionary<string, ManifestEntry>? curated = null;
        foreach (var row in rows.Values.Where(r => Text(r, "classification") == "defective"))
            foreach (var replacement in row["replacements"]!.AsArray().OfType<JsonObject>())
            {
                var path = Text(replacement, "path");
                if (Text(replacement, "suite") == "css21-curated")
                {
                    curated ??= Manifest.Load(ConformancePaths.Manifest(CuratedManifestFile))
                        .GroupBy(e => e.Path).ToDictionary(g => g.Key, g => g.First());
                    if (!curated.TryGetValue(path, out var entry) || entry.ExpectedFail)
                        throw new InvalidDataException($"Curated replacement {path} for {Text(row, "path")} must be a waiver-free entry of {CuratedManifestFile}.");
                    continue;
                }
                var isOfficial = path.StartsWith(OfficialCatalog.UrlPrefix, StringComparison.Ordinal);
                if (isOfficial != (Text(replacement, "suite") == "css21-official") ||
                    !rows.TryGetValue(path, out var target) || Text(target, "classification") != "applicable" ||
                    !Strings(row, "media").All(Strings(target, "media").Contains) ||
                    !Strings(row, "requirementIds").Intersect(Strings(target, "requirementIds")).Any())
                    throw new InvalidDataException($"Replacement {path} for {Text(row, "path")} must be an applicable case in its suite " +
                        "covering the same media and sharing an obligation.");
            }
    }

    private static void ValidateMedia(JsonObject row, bool allowEmpty)
    {
        var media = Strings(row, "media");
        if ((!allowEmpty && media.Length == 0) || media.Any(m => m is not ("screen" or "print")) || media.Distinct().Count() != media.Length)
            throw new InvalidDataException("CSS review media must explicitly identify screen and/or print.");
    }

    internal static int Run(string? reportPath)
    {
        // Rebuild the official catalog first so the readiness evaluation below reads the
        // current lock and tree, not whatever the previous run left on disk.
        var official = OfficialCatalog.Ensure();
        var screen = Evaluate("screen", []);
        var print = Evaluate("print", []);
        var candidates = File.Exists(WptCatalog.ManifestPath) ? Candidates() : [];
        var reviews = Read(ApplicabilityFile)["tests"]!.AsArray().OfType<JsonObject>().ToDictionary(t => Text(t, "path"));
        var destination = reportPath ?? Path.Combine(ConformancePaths.EnsureArtifacts(), "css21-inventory.json");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        var report = new
        {
            formatVersion = 1,
            target = Target,
            inventorySha256 = InventoryHash(),
            screen,
            print,
            officialCatalog = official is { } suite ? new
            {
                suite.Counts.TotalTests,
                suite.Counts.RequiredBehaviorTests,
                suite.Counts.Reftests,
                suite.Counts.SelfTests,
                suite.Counts.UnavailableTests,
                suite.Counts.UnavailableVariants
            } : null,
            cases = candidates.Select(c => new
            {
                c.Source,
                c.Path,
                c.Kind,
                c.Context,
                c.References,
                classification = reviews.TryGetValue(c.Path, out var review) ? Text(review, "classification") : "unreviewed"
            })
        };
        File.WriteAllText(destination, JsonSerializer.Serialize(report, ExecutionEvidence.JsonOptions) + Environment.NewLine);
        Console.WriteLine($"CSS inventory: {candidates.Length} WPT cases; official={official?.Cases.Length.ToString() ?? "not vendored"}; " +
            $"screen blockers={screen.Blockers.Count}; print blockers={print.Blockers.Count}; {destination}");
        return 0;
    }

    internal static string[] Strings(JsonObject obj, string key) => obj[key] is JsonArray items
        ? items.Select(i => i?.GetValue<string>() ?? "").ToArray() : [];
    internal static string Text(JsonObject obj, string key) => obj[key]?.GetValue<string>() ?? "";
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}
