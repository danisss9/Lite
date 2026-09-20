using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lite.Conformance.Harness;

namespace Lite.Conformance.Test262;

internal sealed record Es2020FeatureStatus(string Feature, int RequiredExecutions, int Passed, int Failed, int Missing,
    IReadOnlyList<string> FailingTests);
internal sealed record Es2020Status(bool Ready, IReadOnlyList<string> Blockers, int RequiredExecutions, int PassedExecutions,
    int RequiredHostTests, int PassedHostTests, IReadOnlyList<Es2020FeatureStatus> Features);

internal static class Es2020Readiness
{
    private static JsonArray HostObligations() => JsonNode.Parse(File.ReadAllText(
        ConformancePaths.Manifest("Test262/es2020-host-obligations.json")))!["obligations"]!.AsArray();

    internal static Es2020Status Evaluate(IReadOnlyList<TestEvidence> evidence, Test262Inventory? inventory = null)
    {
        inventory ??= Test262Catalog.Read();
        var blockers = inventory.Blockers.ToList();
        var sections = JsonNode.Parse(File.ReadAllText(ConformancePaths.Manifest(Test262Catalog.SectionsFile)))!["sections"]!.AsArray();
        var unreviewed = sections.Count(s => s!["reviewed"]?.GetValue<bool>() != true);
        if (unreviewed > 0) blockers.Add($"es2020-unreviewed-spec-sections:{unreviewed}");
        var language = evidence.Where(t => t.Suite == "test262" && t.JavaScript is not null).ToArray();
        var byPath = inventory.Tests.ToDictionary(t => t.Path, StringComparer.Ordinal);
        var positions = inventory.Tests.Where(t => t.Classification != "fixture")
            .Select((t, position) => (t.Path, position)).ToDictionary(t => t.Path, t => t.position, StringComparer.Ordinal);
        var lookup = language.GroupBy(t => (t.Path, t.JavaScript!.Mode)).ToDictionary(g => g.Key, g => g.ToArray());
        var runs = language.Select(t => t.JavaScript!).ToArray();
        if (runs.Any(r => r.InventorySha256 != inventory.Sha256 || r.Selection != "full" || r.Filter is not null ||
            r.ShardCount < 1 || r.ShardIndex < 0 || r.ShardIndex >= r.ShardCount)) blockers.Add("es2020-invalid-run-selection");
        if (language.Any(t => t.Context != "javascript" || t.Kind != "language" ||
            !positions.TryGetValue(t.Path, out var position) || t.JavaScript!.ShardCount < 1 ||
            position % t.JavaScript.ShardCount != t.JavaScript.ShardIndex ||
            !byPath.TryGetValue(t.Path, out var candidate) || candidate.Classification != "included" || !candidate.Metadata.Modes.Contains(t.JavaScript.Mode)))
            blockers.Add("es2020-invalid-execution-identity");
        var shardCounts = runs.Select(r => r.ShardCount).Distinct().ToArray();
        if (shardCounts.Length != 1 || runs.Select(r => r.ShardIndex).Distinct().Count() != shardCounts[0]) blockers.Add("es2020-incomplete-or-conflicting-shards");
        int required = 0, passed = 0;
        var featureCases = new Dictionary<string, List<(string Path, string State)>>(StringComparer.Ordinal);
        foreach (var test in inventory.Tests.Where(t => t.Classification == "included"))
        {
            foreach (var mode in test.Metadata.Modes)
            {
                required++;
                var matches = lookup.GetValueOrDefault((test.Path, mode)) ?? [];
                var valid = matches.Length == 1 && matches[0].Outcome == "pass" && matches[0].Subtests.Count > 0 &&
                    matches[0].Subtests.All(s => s.Status == 0) && matches[0].JavaScript is { } js &&
                    js.InventorySha256 == inventory.Sha256 && js.Selection == "full" && js.Filter is null &&
                    matches[0].Context == "javascript" && matches[0].Kind == "language" &&
                    js.ShardCount > 0 && positions[test.Path] % js.ShardCount == js.ShardIndex &&
                    js.ExpectedPhase == test.Metadata.NegativePhase && js.ExpectedType == test.Metadata.NegativeType &&
                    (test.Metadata.NegativePhase is null || js.ObservedPhase == js.ExpectedPhase && js.ObservedType == js.ExpectedType);
                if (valid) passed++;
                var state = valid ? "pass" : matches.Length == 0 ? "missing" : "fail";
                foreach (var feature in test.Metadata.Features.Length > 0 ? test.Metadata.Features : [Family(test.Path)])
                {
                    if (!featureCases.TryGetValue(feature, out var items)) featureCases.Add(feature, items = []);
                    items.Add(($"{test.Path} [{mode}]", state));
                }
            }
        }
        if (required != passed) blockers.Add($"es2020-missing-or-failing-executions:{required - passed}");
        foreach (var section in sections.OfType<JsonObject>().Where(s => s["reviewed"]?.GetValue<bool>() == true))
        {
            var id = section["id"]!.GetValue<string>();
            var tests = inventory.Tests.Where(t => t.Metadata.Esid == id && t.Classification == "included").Select(t => t.Path)
                .Concat(section["additionalTests"]!.AsArray().Select(t => t!.GetValue<string>())).Distinct().ToArray();
            if (tests.Length == 0 && string.IsNullOrWhiteSpace(section["nonExecutableReason"]?.GetValue<string>())) blockers.Add($"es2020-section-without-evidence:{id}");
            foreach (var path in tests)
                if (!inventory.Tests.Any(t => t.Path == path && t.Classification == "included")) blockers.Add($"es2020-invalid-section-test:{id}:{path}");
        }
        var hostPassed = 0;
        foreach (var obligation in HostObligations().OfType<JsonObject>())
        {
            var id = obligation["id"]!.GetValue<string>();
            if (obligation["reviewed"]?.GetValue<bool>() != true) blockers.Add($"es2020-unreviewed-host-obligation:{id}");
            if (obligation["tests"] is not JsonArray { Count: > 0 } mappings ||
                mappings.Any(t => !Es2020HostRunner.TestNames.Contains(t!.GetValue<string>())))
                blockers.Add($"es2020-unmapped-host-obligation:{id}");
        }
        foreach (var name in Es2020HostRunner.TestNames)
        {
            var matches = evidence.Where(t => t.Suite == "es2020-host" && t.Path == name).ToArray();
            if (matches.Length == 1 && matches[0].Outcome == "pass" && matches[0].Context == "window" && matches[0].Kind == "javascript-host" &&
                matches[0].Subtests.Count > 0 && matches[0].Subtests.All(s => s.Status == 0)) hostPassed++;
            else blockers.Add($"es2020-host-missing-or-failing:{name}");
        }
        var features = featureCases.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new Es2020FeatureStatus(p.Key, p.Value.Count,
            p.Value.Count(t => t.State == "pass"), p.Value.Count(t => t.State == "fail"), p.Value.Count(t => t.State == "missing"),
            p.Value.Where(t => t.State == "fail").Select(t => t.Path).ToArray())).ToArray();
        return new(blockers.Count == 0, blockers, required, passed, Es2020HostRunner.TestNames.Length, hostPassed, features);
    }

    internal static void WriteBacklog(string path, Test262Inventory inventory, IReadOnlyList<TestEvidence> evidence, IReadOnlyList<string> inputBlockers)
    {
        var status = Evaluate(evidence, inventory);
        var sections = JsonNode.Parse(File.ReadAllText(ConformancePaths.Manifest(Test262Catalog.SectionsFile)))!["sections"]!.AsArray();
        var report = new { status, inputBlockers, inventorySha256 = inventory.Sha256,
            remainingHostObligations = HostObligations().Where(o => o!["reviewed"]?.GetValue<bool>() != true).ToArray(),
            failingExecutions = evidence.Where(t => t.Suite is "test262" or "es2020-host" && t.Outcome is not ("pass" or "excluded" or "unreviewed")).ToArray(),
            unreviewedTests = inventory.Tests.Where(t => t.RequiresReview || t.Classification is "invalid" or "unreviewed").ToArray(),
            remainingSections = sections.Where(s => s!["reviewed"]?.GetValue<bool>() != true).ToArray() };
        File.WriteAllText(path, JsonSerializer.Serialize(report, ExecutionEvidence.JsonOptions));
        var md = new StringBuilder("# ES2020 remaining work\n\nGenerated from the pinned inventory and current accepted evidence. Failures require diagnosis; missing evidence does not establish a missing implementation.\n\n");
        md.AppendLine($"Required language executions: {status.RequiredExecutions}; passing: {status.PassedExecutions}. Host checks: {status.PassedHostTests}/{status.RequiredHostTests}.");
        md.AppendLine("\n## Language feature coverage\n\n| Feature | Required | Passed | Failed/conflicting | Missing |\n|---|---:|---:|---:|---:|");
        foreach (var feature in status.Features.Where(f => f.Failed > 0 || f.Missing > 0))
            md.AppendLine($"| {feature.Feature} | {feature.RequiredExecutions} | {feature.Passed} | {feature.Failed} | {feature.Missing} |");
        md.AppendLine("\n## Host and inventory blockers\n");
        foreach (var blocker in status.Blockers.Concat(inputBlockers)) md.AppendLine($"- {blocker}");
        md.AppendLine("\n## Host features requiring implementation or review\n");
        foreach (var obligation in report.remainingHostObligations)
            md.AppendLine($"- **{obligation!["id"]}**: {obligation["requirement"]}. Remaining: {obligation["remaining"]}");
        md.AppendLine("\n## Failing executions\n");
        foreach (var test in report.failingExecutions) md.AppendLine($"- `{test.Path}` ({test.JavaScript?.Mode ?? test.Suite}): {test.Detail.Replace('\n', ' ').Replace('\r', ' ')}");
        md.AppendLine("\n## Specification sections awaiting review\n\nEach linked section must be reviewed and mapped to evidence; these are coverage obligations, not assertions that the feature is absent.\n");
        foreach (var section in report.remainingSections) md.AppendLine($"- [{section!["title"]}]({section["url"]}) — `{section["id"]}`");
        File.WriteAllText(Path.ChangeExtension(path, ".md"), md.ToString());
    }

    private static string Family(string path)
    {
        var parts = path.Split('/');
        return parts.Length >= 3 ? string.Join('/', parts.Take(3)) : path;
    }
}
