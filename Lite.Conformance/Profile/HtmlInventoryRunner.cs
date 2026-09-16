using System.Text.Json;
using System.Text.Json.Nodes;
using Lite.Conformance.Harness;
using Lite.Conformance.Wpt;

namespace Lite.Conformance.Profile;

/// <summary>Exports the review backlog without assigning conformance from filenames or headings.</summary>
internal static class HtmlInventoryRunner
{
    internal static int Run(string? reportPath)
    {
        if (!File.Exists(WptCatalog.ManifestPath))
        {
            Console.Error.WriteLine("Missing upstream WPT manifest. Run scripts/build-wpt-manifest.ps1 first.");
            return 2;
        }
        var catalog = WptCatalog.Read(WptCatalog.ManifestPath).Where(WptCatalog.IsHtmlCandidate).ToArray();
        var applicability = HtmlApplicability.Read();
        var reviews = applicability["tests"]!.AsArray().OfType<JsonObject>().ToArray();
        var sections = JsonNode.Parse(File.ReadAllText(ConformancePaths.Manifest(HtmlSectionInventory.FileName)))!.AsObject();
        var profile = JsonNode.Parse(File.ReadAllText(ConformancePaths.Manifest(ExecutionEvidence.ProfileFile)))!.AsObject();
        var report = new
        {
            formatVersion = 1,
            target = HtmlSectionInventory.Target,
            manifestSha256 = ExecutionEvidence.HashFile(WptCatalog.ManifestPath),
            sectionCount = sections["sections"]!.AsArray().Count,
            unreviewedSections = sections["sections"]!.AsArray().OfType<JsonObject>()
                .Where(s => s["classification"]!.GetValue<string>() == "unreviewed").Select(s => s.DeepClone()).ToArray(),
            requirementsWithoutTests = profile["requirements"]!.AsArray().OfType<JsonObject>()
                .Where(r => r["specification"]!.GetValue<string>() == "html53" &&
                    r["applicability"]!.GetValue<string>() == "included" && r["tests"]!.AsArray().Count == 0)
                .Select(r => r["id"]!.GetValue<string>()).ToArray(),
            tests = catalog.Select(c => new
            {
                c.Source, c.Path, c.Kind, c.Context, c.LongTimeout, c.TestDriver, c.References,
                classification = HtmlApplicability.FindReview(reviews, c.Path, c.Source)?["classification"]?.GetValue<string>() ?? "unreviewed",
            }).ToArray(),
        };
        var destination = reportPath ?? Path.Combine(ConformancePaths.EnsureArtifacts(), "html53-inventory.json");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        File.WriteAllText(destination, JsonSerializer.Serialize(report, ExecutionEvidence.JsonOptions) + Environment.NewLine);
        Console.WriteLine($"HTML inventory: {report.sectionCount} sections; {catalog.Length} upstream test cases; {destination}");
        return 0;
    }
}
