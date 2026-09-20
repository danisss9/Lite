using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Lite.Conformance.Profile;

/// <summary>The Recommendation's section index tracks review coverage, not a count of obligations.</summary>
internal static class HtmlSectionInventory
{
    internal const string FileName = "Profile/html5-sections.json";
    internal const string Target = "https://www.w3.org/TR/2014/REC-html5-20141028/";
    // Generated from the exact numbered table of contents by scripts/import-html5-sections.py.
    private const string ExpectedIndex = "c0575d831dcf2e12b974da2ab12e803a3ee09424199b586b60e141e8526de610";
    private static readonly HashSet<string> Classifications =
        ["unreviewed", "user-agent", "authoring", "informative", "optional", "mixed", "profile-excluded"];

    internal static List<string> Evaluate(JsonObject inventory, JsonObject profile, ICollection<string> errors)
    {
        var blockers = new List<string>();
        if (inventory["schemaVersion"]?.GetValue<int>() != 1 || inventory["target"]?.GetValue<string>() != Target)
            errors.Add("Section inventory must identify the pinned HTML 5.0 Recommendation.");
        if (inventory["sections"] is not JsonArray sections)
        {
            errors.Add("Section inventory must contain the complete section index.");
            return blockers;
        }
        var index = new StringBuilder();
        var requirements = profile["requirements"]!.AsArray().OfType<JsonObject>()
            .GroupBy(r => r["id"]!.GetValue<string>()).ToDictionary(g => g.Key, g => g.First());
        foreach (var node in sections)
        {
            if (node is not JsonObject section) { errors.Add("Every section must be an object."); continue; }
            var clause = section["clause"]?.GetValue<string>() ?? "";
            index.Append(clause).Append('\t').Append(section["url"]?.GetValue<string>()).Append('\t')
                .Append(section["title"]?.GetValue<string>()).Append('\n');
            var classification = section["classification"]?.GetValue<string>() ?? "";
            if (!Classifications.Contains(classification)) errors.Add($"Invalid section classification: {clause}");
            if (classification == "unreviewed") continue;
            if (string.IsNullOrWhiteSpace(section["rationale"]?.GetValue<string>()))
                errors.Add($"Reviewed section needs a rationale: {clause}");
            if (section["requirementIds"] is not JsonArray ids)
            {
                errors.Add($"Section needs requirementIds: {clause}");
                continue;
            }
            if (classification is "user-agent" or "mixed" or "profile-excluded" && ids.Count == 0)
                errors.Add($"Section needs mapped requirements: {clause}");
            foreach (var id in ids)
            {
                if (!requirements.TryGetValue(id!.GetValue<string>(), out var requirement))
                    errors.Add($"Unknown requirement in section {clause}: {id}");
                else if (classification == "profile-excluded" && requirement["applicability"]?.GetValue<string>() != "excluded")
                    errors.Add($"Section exclusion must reference an explicit profile boundary: {clause}");
            }
        }
        var actual = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(index.ToString()))).ToLowerInvariant();
        if (actual != ExpectedIndex || inventory["sectionIndexSha256"]?.GetValue<string>() != ExpectedIndex)
            errors.Add("HTML section index differs from the pinned Recommendation; reimport the complete index.");
        foreach (var chapter in sections.OfType<JsonObject>()
                     .Where(s => s["classification"]?.GetValue<string>() == "unreviewed")
                     .GroupBy(s => s["clause"]!.GetValue<string>().Split('.')[0]))
            blockers.Add($"html5-unreviewed-sections:{chapter.Key}:{chapter.Count()}");
        if (inventory["reviewComplete"]?.GetValue<bool>() != true)
            blockers.Add("html5-section-review-incomplete");
        return blockers;
    }
}
