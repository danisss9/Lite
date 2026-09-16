using System.Text.Json.Nodes;
using Lite.Conformance.Harness;

namespace Lite.Conformance.Wpt;

internal static class HtmlApplicability
{
    internal const string FileName = "Wpt/html53-applicability.json";
    internal static readonly string[] CandidateRoots = ["html", "dom", "custom-elements", "shadow-dom", "selection",
        "uievents", "url", "encoding", "mimesniff", "fetch", "cors", "cookies", "webstorage", "webmessaging", "FileAPI"];
    internal static readonly HashSet<string> Classifications = new(StringComparer.Ordinal)
        { "included", "mixed", "unreviewed", "post-target", "regression-only", "profile-excluded" };

    internal static JsonObject Read() => JsonNode.Parse(File.ReadAllText(ConformancePaths.Manifest(FileName)))!.AsObject();

    internal static void Validate(JsonObject inventory, ICollection<string> errors)
    {
        if (inventory["schemaVersion"]?.GetValue<int>() != 2 ||
            inventory["target"]?.GetValue<string>() != "https://www.w3.org/TR/2018/WD-html53-20181018/" ||
            inventory["defaultClassification"]?.GetValue<string>() != "unreviewed")
            errors.Add("HTML applicability must identify the pinned HTML 5.3 draft and default to unreviewed.");
        if (inventory["inventoryComplete"] is not JsonValue complete || !complete.TryGetValue<bool>(out _))
            errors.Add("HTML applicability inventoryComplete must be a boolean.");
        if (inventory["tests"] is not JsonArray tests) { errors.Add("HTML applicability tests must be an array."); return; }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in tests)
        {
            if (node is not JsonObject test) { errors.Add("Every HTML applicability review must be an object."); continue; }
            var path = test["path"]?.GetValue<string>() ?? "";
            if (!seen.Add(path) || !WptCatalog.ValidPath(path) || path.Contains('*'))
                errors.Add($"Invalid or duplicate exact HTML applicability path: {path}");
            if (!Classifications.Contains(test["classification"]?.GetValue<string>() ?? "") ||
                string.IsNullOrWhiteSpace(test["reason"]?.GetValue<string>()))
                errors.Add($"HTML applicability requires a classification and reason: {path}");
            if (test["classification"]?.GetValue<string>() == "mixed")
            {
                if (test["assertionInventoryComplete"]?.GetValue<bool>() != true || test["assertions"] is not JsonArray assertions || assertions.Count == 0)
                { errors.Add($"Mixed HTML test needs a complete assertion inventory: {path}"); continue; }
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var assertion in assertions)
                {
                    if (assertion is not JsonObject review || string.IsNullOrWhiteSpace(review["name"]?.GetValue<string>()) ||
                        !names.Add(review["name"]!.GetValue<string>()) ||
                        review["classification"]?.GetValue<string>() is not ("included" or "post-target" or "regression-only" or "profile-excluded") ||
                        string.IsNullOrWhiteSpace(review["reason"]?.GetValue<string>()))
                        errors.Add($"Invalid, duplicate or unreviewed assertion in {path}");
                }
                if (!assertions.OfType<JsonObject>().Any(a => a["classification"]?.GetValue<string>() == "included"))
                    errors.Add($"Mixed HTML test must include a target assertion: {path}");
            }
            else if (test.ContainsKey("assertions") || test.ContainsKey("assertionInventoryComplete"))
                errors.Add($"Assertion review fields require mixed classification: {path}");
        }
    }

    internal static bool IsIncluded(JsonObject test) => test["classification"]?.GetValue<string>() is "included" or "mixed";

    internal static JsonObject? FindReview(IEnumerable<JsonObject> reviews, string path, string? source = null)
    {
        var all = reviews.ToArray();
        return all.FirstOrDefault(r => r["path"]?.GetValue<string>() == path) ??
            all.FirstOrDefault(r => r["path"]?.GetValue<string>() == source);
    }

    internal static bool HasPassingAssertions(TestEvidence test, JsonObject review, string? requiredAssertion)
    {
        if (review["classification"]?.GetValue<string>() != "mixed")
            return test.Outcome == "pass" && test.Subtests.Count > 0 && test.Subtests.All(s => s.Status == 0) &&
                (string.IsNullOrEmpty(requiredAssertion) || test.Subtests.Any(s => s.Name == requiredAssertion));
        // A mixed test may fail ONLY in explicitly reviewed non-target assertions. Missing,
        // duplicate, new, or unreviewed assertions never disappear from the gate.
        if (test.Outcome is not ("pass" or "fail") || review["assertionInventoryComplete"]?.GetValue<bool>() != true ||
            review["assertions"] is not JsonArray assertions || assertions.Count == 0) return false;
        var expected = assertions.OfType<JsonObject>().ToArray();
        if (expected.Length != assertions.Count || test.Subtests.Count != expected.Length ||
            test.Subtests.GroupBy(s => s.Name).Any(g => g.Count() != 1) ||
            expected.GroupBy(a => a["name"]?.GetValue<string>()).Any(g => g.Count() != 1)) return false;
        if (!expected.Any(a => a["classification"]?.GetValue<string>() == "included")) return false;
        foreach (var assertion in expected)
        {
            var actual = test.Subtests.SingleOrDefault(s => s.Name == assertion["name"]?.GetValue<string>());
            var classification = assertion["classification"]?.GetValue<string>();
            if (actual is null || string.IsNullOrWhiteSpace(assertion["reason"]?.GetValue<string>()) ||
                classification is not ("included" or "post-target" or "regression-only" or "profile-excluded") ||
                actual.Status is not (0 or 1) ||
                (classification == "included" && actual.Status != 0)) return false;
        }
        return string.IsNullOrEmpty(requiredAssertion) || expected.Any(a =>
            a["name"]?.GetValue<string>() == requiredAssertion && a["classification"]?.GetValue<string>() == "included");
    }
}
