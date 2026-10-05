using System.Text.Json.Nodes;
using Lite.Conformance.Harness;
using Lite.Conformance.Wpt;

namespace Lite.Conformance.Css21;

/// <summary>Executes the complete CSS 2.1 candidate catalogs — the pinned WPT css/CSS2
/// checkout and the vendored official 23 March 2011 suite. Applicability review remains a
/// separate readiness gate: this runner reports executed outcomes, never conformance.</summary>
internal static class Css21FullRunner
{
    internal sealed record Selection(string Suite, string Path, string Kind, string Context,
        JsonObject? Options, bool Xhtml, int ViewportWidth, int ViewportHeight);

    internal static int Run(string media, string? filter, ShardSpec shard, string? reportPath, string? catalog = null)
    {
        if (catalog is not null and not ("all" or "wpt" or "official"))
        { Console.Error.WriteLine("--catalog must be all, wpt, or official."); return 2; }
        var includeWpt = catalog is null or "all" or "wpt";
        var includeOfficial = catalog is null or "all" or "official";
        var reviewFile = Css21Inventory.Read(Css21Inventory.ApplicabilityFile);
        Css21Inventory.ValidateReviews(Css21Inventory.Read(Css21Inventory.RequirementsFile), reviewFile);
        var reviews = reviewFile["tests"]!.AsArray().OfType<JsonObject>()
            .ToDictionary(t => Css21Inventory.Text(t, "path"));

        bool Selected(string path) =>
            filter is null || path.Contains(filter, StringComparison.Ordinal);
        bool ReviewAllows(string path)
        {
            if (!reviews.TryGetValue(path, out var review)) return true;
            var classification = Css21Inventory.Text(review, "classification");
            return classification == "unreviewed" ||
                classification == "applicable" && Css21Inventory.Strings(review, "media").Contains(media);
        }

        var selections = new List<Selection>();
        if (includeWpt)
        {
            if (!File.Exists(WptCatalog.ManifestPath))
            {
                if (!includeOfficial) { Console.Error.WriteLine("Missing WPT catalog. Run scripts/build-wpt-manifest.ps1."); return 2; }
                Console.WriteLine("css21-full: WPT catalog missing; continuing with the official suite only.");
            }
            else
            {
                foreach (var candidate in Css21Inventory.Candidates().Where(c => Selected(c.Path) && ReviewAllows(c.Path)))
                {
                    var (width, height) = Viewport(candidate.Options, candidate.Path);
                    selections.Add(new("css21-wpt", candidate.Path, candidate.Kind, candidate.Context, candidate.Options,
                        Path.GetExtension(candidate.Source) is ".xht" or ".xhtml" or ".xml", width, height));
                }
            }
        }
        if (includeOfficial)
        {
            switch (OfficialCatalog.Ensure())
            {
                case null when includeWpt:
                    Console.WriteLine("css21-full: official suite not vendored; continuing with the WPT catalog only.");
                    break;
                case null:
                    Console.Error.WriteLine("css21-full: the official suite is not vendored under Lite.Conformance/vendor.");
                    return 2;
                case { } official:
                    foreach (var candidate in official.Cases.Where(c => Selected(c.Path) && ReviewAllows(c.Path)))
                        selections.Add(new("css21-official", candidate.Path, candidate.Kind, "window", null, false, 800, 600));
                    break;
            }
        }
        var selected = shard.Apply(selections.OrderBy(s => s.Suite, StringComparer.Ordinal)
            .ThenBy(s => s.Path, StringComparer.Ordinal)).ToArray();
        if (selected.Length == 0) { Console.Error.WriteLine("css21-full: empty selection."); return 2; }

        var identity = ExecutionEvidence.CaptureIdentity();
        var inventoryHash = Css21Inventory.InventoryHash();
        var started = DateTime.UtcNow;
        var outcomes = new List<TestEvidence>();
        ConformanceServer.Start(cssRegressionMode: false);
        foreach (var test in selected)
        {
            WptRunner.RunResult result;
            if (media == "print") result = new(WptRunner.Cat.Unsupported, "Paginated execution is not implemented", 0, 0);
            else result = WptRunner.RunOne(test.Path);
            var environment = test.Suite == "css21-wpt" && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("LITE_WPT_BASE_URL"))
                ? "upstream-wpt" : "local";
            outcomes.Add(new(test.Suite, test.Path, result.Cat.ToString().ToLowerInvariant(), result.Detail,
                result.Subtests ?? [], result.HarnessStatus, environment, ConformanceServer.TestUrl(test.Path),
                test.Context, test.Kind, result.Artifacts, Css: new(media, test.Xhtml ? "xhtml" : "html", inventoryHash,
                test.ViewportWidth, test.ViewportHeight, shard.Index, shard.Count, filter)));
            Console.WriteLine($"  {result.Cat.ToString().ToUpperInvariant(),-11} [{media}] {test.Path} ({result.Detail})");
        }
        var suffix = catalog is null or "all" ? "" : "-" + catalog;
        var destination = reportPath ?? Path.Combine(ConformancePaths.EnsureArtifacts(),
            $"css21-full-{media}{suffix}-{shard.Index}-of-{shard.Count}.json");
        ExecutionEvidence.Write(destination, identity, started, outcomes);
        foreach (var group in outcomes.GroupBy(t => t.Suite).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var passed = group.Count(t => t.Outcome == "pass");
            Console.WriteLine($"css21-full {group.Key} [{media}]: {passed}/{group.Count()} candidates passed.");
        }
        Console.WriteLine("Reviewed obligation coverage and the official catalog verification are required for readiness.");
        return outcomes.All(t => t.Outcome == "pass") ? 0 : 1;
    }

    private static (int Width, int Height) Viewport(JsonObject? options, string path)
    {
        var width = 800;
        var height = 600;
        if (options?["viewport_size"]?.GetValue<string>() is { } viewport)
        {
            var size = viewport.Split('x');
            if (size.Length != 2 || !int.TryParse(size[0], out width) || !int.TryParse(size[1], out height) || width <= 0 || height <= 0)
                throw new InvalidDataException($"Invalid CSS test viewport: {path}");
        }
        return (width, height);
    }
}
