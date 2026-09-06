using System.Text.Json.Nodes;
using Lite.Conformance.Harness;

namespace Lite.Conformance.Wpt;

internal static class HtmlApplicability
{
    internal const string FileName = "Wpt/html53-applicability.json";
    internal static readonly string[] CandidateRoots = ["html", "dom", "custom-elements", "shadow-dom", "selection",
        "uievents", "url", "encoding", "mimesniff", "fetch", "cors", "cookies", "webstorage", "webmessaging", "FileAPI"];
    internal static readonly HashSet<string> Classifications = new(StringComparer.Ordinal)
        { "included", "unreviewed", "post-target", "regression-only", "profile-excluded" };

    internal static JsonObject Read() => JsonNode.Parse(File.ReadAllText(ConformancePaths.Manifest(FileName)))!.AsObject();

    internal static void Validate(JsonObject inventory, ICollection<string> errors)
    {
        if (inventory["schemaVersion"]?.GetValue<int>() != 1 ||
            inventory["target"]?.GetValue<string>() != "https://www.w3.org/TR/2018/WD-html53-20181018/" ||
            inventory["defaultClassification"]?.GetValue<string>() != "unreviewed")
            errors.Add("HTML applicability must identify the pinned HTML 5.3 draft and default to unreviewed.");
        if (inventory["inventoryComplete"] is not JsonValue complete || !complete.TryGetValue<bool>(out _))
            errors.Add("HTML applicability inventoryComplete must be a boolean.");
        if (inventory["tests"] is not JsonArray tests) { errors.Add("HTML applicability tests must be an array."); return; }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var test in tests.OfType<JsonObject>())
        {
            var path = test["path"]?.GetValue<string>() ?? "";
            if (!seen.Add(path) || string.IsNullOrWhiteSpace(path) || path.StartsWith('/') || path.Contains("..") || path.Contains('*'))
                errors.Add($"Invalid or duplicate exact HTML applicability path: {path}");
            if (!Classifications.Contains(test["classification"]?.GetValue<string>() ?? "") ||
                string.IsNullOrWhiteSpace(test["reason"]?.GetValue<string>()))
                errors.Add($"HTML applicability requires a classification and reason: {path}");
        }
    }
}
