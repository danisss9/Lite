using Lite.Conformance.Harness;
using Lite.Conformance.Wpt;

namespace Lite.Conformance.Css21;

/// <summary>Executes the complete CSS2 candidate catalog; applicability review remains a separate readiness gate.</summary>
internal static class Css21FullRunner
{
    internal static int Run(string media, string? filter, ShardSpec shard, string? reportPath)
    {
        if (!File.Exists(WptCatalog.ManifestPath))
        {
            Console.Error.WriteLine("Missing WPT catalog. Run scripts/build-wpt-manifest.ps1.");
            return 2;
        }
        var candidates = Css21Inventory.Candidates();
        var reviewFile = Css21Inventory.Read(Css21Inventory.ApplicabilityFile);
        Css21Inventory.ValidateReviews(Css21Inventory.Read(Css21Inventory.RequirementsFile), reviewFile);
        var reviews = reviewFile["tests"]!.AsArray().OfType<System.Text.Json.Nodes.JsonObject>()
            .ToDictionary(t => Css21Inventory.Text(t, "path"));
        var selected = shard.Apply(candidates.Where(c => filter is null || c.Path.Contains(filter, StringComparison.Ordinal))
            .Where(c => !reviews.TryGetValue(c.Path, out var r) || Css21Inventory.Text(r, "classification") == "unreviewed" ||
                Css21Inventory.Text(r, "classification") == "applicable" && Css21Inventory.Strings(r, "media").Contains(media))
            .OrderBy(c => c.Path, StringComparer.Ordinal)).ToArray();
        if (selected.Length == 0) { Console.Error.WriteLine("css21-full: empty selection."); return 2; }
        var identity = ExecutionEvidence.CaptureIdentity();
        var inventoryHash = Css21Inventory.InventoryHash();
        var started = DateTime.UtcNow;
        var outcomes = new List<TestEvidence>();
        ConformanceServer.Start(cssRegressionMode: false);
        foreach (var test in selected)
        {
            var xhtml = Path.GetExtension(test.Source) is ".xht" or ".xhtml" or ".xml";
            WptRunner.RunResult result;
            if (media == "print") result = new(WptRunner.Cat.Unsupported, "Paginated execution is not implemented", 0, 0);
            else if (xhtml) result = new(WptRunner.Cat.Unsupported, "Authentic XHTML parsing is required for this variant", 0, 0);
            else result = WptRunner.RunOne(test.Path);
            var width = 800;
            var height = 600;
            if (test.Options?["viewport_size"]?.GetValue<string>() is { } viewport)
            {
                var size = viewport.Split('x');
                if (size.Length != 2 || !int.TryParse(size[0], out width) || !int.TryParse(size[1], out height) || width <= 0 || height <= 0)
                    throw new InvalidDataException($"Invalid CSS test viewport: {test.Path}");
            }
            var environment = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("LITE_WPT_BASE_URL")) ? "local" : "upstream-wpt";
            outcomes.Add(new("css21-wpt", test.Path, result.Cat.ToString().ToLowerInvariant(), result.Detail,
                result.Subtests ?? [], result.HarnessStatus, environment, ConformanceServer.TestUrl(test.Path),
                test.Context, test.Kind, result.Artifacts, Css: new(media, xhtml ? "xhtml" : "html", inventoryHash,
                    width, height, shard.Index, shard.Count, filter)));
            Console.WriteLine($"  {result.Cat.ToString().ToUpperInvariant(),-11} [{media}] {test.Path} ({result.Detail})");
        }
        var destination = reportPath ?? Path.Combine(ConformancePaths.EnsureArtifacts(), $"css21-full-{media}-{shard.Index}-of-{shard.Count}.json");
        ExecutionEvidence.Write(destination, identity, started, outcomes);
        var passed = outcomes.Count(t => t.Outcome == "pass");
        Console.WriteLine($"css21-full {media}: {passed}/{outcomes.Count} candidates passed. Reviewed obligation coverage and the official catalog are required for readiness.");
        return passed == outcomes.Count ? 0 : 1;
    }
}
