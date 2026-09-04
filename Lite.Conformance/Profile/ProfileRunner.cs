using System.Security.Cryptography;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lite.Conformance.Harness;

namespace Lite.Conformance.Profile;

/// <summary>
/// Validates the compatibility contract and emits a deterministic, machine-readable status report.
/// A valid contract is not the same as a conforming engine: incomplete, failing, excluded, and
/// dependency-exception entries are carried into the report and make releaseReady false.
/// </summary>
internal static class ProfileRunner
{
    private const string ProfileFile = "Profile/lite-html52-css21-es2020-profile.json";
    private const string SuiteLockFile = "test-suites.lock.json";

    private static readonly HashSet<string> Statuses = new(StringComparer.Ordinal)
    {
        "implemented", "failing", "untested", "profile-excluded", "dependency-exception",
    };

    private static readonly HashSet<string> SpecificationIds = new(StringComparer.Ordinal)
    {
        "html52", "css21", "es2020",
    };

    public static int Run(string? reportPath, bool requireReady)
    {
        var profilePath = ConformancePaths.Manifest(ProfileFile);
        var lockPath = ConformancePaths.Manifest(SuiteLockFile);
        var errors = new List<string>();

        var profile = ReadObject(profilePath, "profile", errors);
        var suiteLock = ReadObject(lockPath, "suite lock", errors);
        if (profile is null || suiteLock is null)
            return ReportValidationErrors(errors);

        ValidateProfile(profile, errors);
        ValidateSuiteLock(suiteLock, errors);
        ValidateExceptionManifests(profile, errors);
        if (errors.Count > 0)
            return ReportValidationErrors(errors);

        var requirements = profile["requirements"]!.AsArray()
            .Select(node => node!.AsObject())
            .ToArray();
        var counts = BuildCounts(requirements);
        var coverage = profile["coverage"]!.AsObject();
        var inventoryComplete = coverage["normativeClauseInventoryComplete"]!.GetValue<bool>();
        var blockerStatuses = new[] { "failing", "untested", "profile-excluded", "dependency-exception" };
        var blockers = new JsonArray();

        if (!inventoryComplete)
            blockers.Add("normative-clause-inventory-incomplete");
        foreach (var status in blockerStatuses)
        {
            var count = counts["total"]!.AsObject()[status]!.GetValue<int>();
            if (count > 0) blockers.Add($"{status}:{count}");
        }
        foreach (var suite in suiteLock["suites"]!.AsArray().OfType<JsonObject>())
        {
            if (suite["vendored"]?.GetValue<bool>() == false)
                blockers.Add($"suite-not-vendored:{Text(suite, "id")}");
            if (Text(suite, "scope") == "sparse")
                blockers.Add($"incomplete-suite-scope:{Text(suite, "id")}");
        }

        var claim = profile["claim"]!.GetValue<string>();
        var releaseReady = inventoryComplete && blockers.Count == 0 && claim == "conforming";
        var report = new JsonObject
        {
            ["reportFormatVersion"] = 1,
            ["profileName"] = profile["name"]!.GetValue<string>(),
            ["claim"] = claim,
            ["profileSha256"] = Sha256(profilePath),
            ["suiteLockSha256"] = Sha256(lockPath),
            ["platform"] = profile["platform"]!.DeepClone(),
            ["coverage"] = coverage.DeepClone(),
            ["counts"] = counts,
            ["releaseReady"] = releaseReady,
            ["blockers"] = blockers,
            ["dependencyExceptions"] = new JsonArray(requirements
                .Where(r => Text(r, "status") == "dependency-exception")
                .Select(r => JsonValue.Create(Text(r, "id")))
                .ToArray()),
            ["suiteLock"] = suiteLock.DeepClone(),
        };

        var destination = string.IsNullOrWhiteSpace(reportPath)
            ? Path.Combine(ConformancePaths.EnsureArtifacts(), "compatibility-report.json")
            : Path.GetFullPath(reportPath);
        var parent = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
        var jsonOptions = new JsonSerializerOptions(JsonSerializerOptions.Default) { WriteIndented = true };
        File.WriteAllText(destination, report.ToJsonString(jsonOptions) + Environment.NewLine,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        Console.WriteLine($"  PASS  compatibility contract ({requirements.Length} classified entries)");
        foreach (var specification in SpecificationIds.Order())
        {
            var c = counts[specification]!.AsObject();
            Console.WriteLine($"        {specification}: implemented={c["implemented"]}, failing={c["failing"]}, " +
                              $"untested={c["untested"]}, excluded={c["profile-excluded"]}, dependencies={c["dependency-exception"]}");
        }
        Console.WriteLine($"        releaseReady={releaseReady.ToString().ToLowerInvariant()} " +
                          $"(normative inventory complete={inventoryComplete.ToString().ToLowerInvariant()})");
        Console.WriteLine($"        report: {destination}");
        if (requireReady && !releaseReady)
        {
            Console.WriteLine("  FAIL  compatibility profile is not release-ready.");
            return 1;
        }
        return 0;
    }

    private static JsonObject? ReadObject(string path, string label, List<string> errors)
    {
        if (!File.Exists(path))
        {
            errors.Add($"Missing {label}: {path}");
            return null;
        }

        try
        {
            return JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Disallow,
                AllowTrailingCommas = false,
            })?.AsObject();
        }
        catch (Exception ex)
        {
            errors.Add($"Invalid {label} JSON ({path}): {ex.Message}");
            return null;
        }
    }

    private static void ValidateProfile(JsonObject profile, List<string> errors)
    {
        RequireText(profile, "name", errors);
        RequireText(profile, "claim", errors);
        var coverage = profile["coverage"] as JsonObject;
        if (coverage is null)
            errors.Add("profile.coverage must be an object.");
        else
        {
            if (coverage["normativeClauseInventoryComplete"] is not JsonValue)
                errors.Add("profile.coverage.normativeClauseInventoryComplete must be present.");
            if (Text(coverage, "unmappedApplicableClauseStatus") != "untested")
                errors.Add("Unmapped applicable clauses must be classified as untested.");
        }

        if (profile["targets"] is not JsonArray targets)
            errors.Add("profile.targets must be an array.");
        else
        {
            var ids = targets.OfType<JsonObject>().Select(t => Text(t, "id")).ToHashSet(StringComparer.Ordinal);
            foreach (var id in SpecificationIds)
                if (!ids.Contains(id)) errors.Add($"Missing specification target '{id}'.");
        }

        if (profile["requirements"] is not JsonArray requirements || requirements.Count == 0)
        {
            errors.Add("profile.requirements must be a non-empty array.");
            return;
        }

        var idsSeen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in requirements)
        {
            if (item is not JsonObject requirement)
            {
                errors.Add("Every profile requirement must be an object.");
                continue;
            }

            var id = RequireText(requirement, "id", errors);
            var specification = RequireText(requirement, "specification", errors);
            var status = RequireText(requirement, "status", errors);
            var applicability = RequireText(requirement, "applicability", errors);
            RequireText(requirement, "clause", errors);
            RequireUri(requirement, "url", errors);
            RequireText(requirement, "title", errors);

            if (!string.IsNullOrEmpty(id) && !idsSeen.Add(id))
                errors.Add($"Duplicate requirement id '{id}'.");
            if (!SpecificationIds.Contains(specification))
                errors.Add($"{id}: unknown specification '{specification}'.");
            if (!Statuses.Contains(status))
                errors.Add($"{id}: unknown status '{status}'.");
            if (applicability is not ("included" or "excluded"))
                errors.Add($"{id}: applicability must be included or excluded.");
            if (status == "profile-excluded")
            {
                if (applicability != "excluded") errors.Add($"{id}: profile-excluded entries must be excluded.");
                RequireText(requirement, "exclusionReason", errors);
            }
            else if (applicability == "excluded")
                errors.Add($"{id}: excluded entries must have profile-excluded status.");

            if (status == "dependency-exception")
            {
                if (requirement["upstreamIssues"] is not JsonArray issues || issues.Count == 0)
                    errors.Add($"{id}: dependency exceptions require at least one upstream issue.");
                else foreach (var issue in issues)
                    if (!Uri.TryCreate(issue?.GetValue<string>(), UriKind.Absolute, out _))
                        errors.Add($"{id}: invalid upstream issue URI '{issue}'.");
                if (requirement["tests"] is not JsonArray dependencyTests || dependencyTests.Count == 0)
                    errors.Add($"{id}: dependency exceptions must name at least one exact test.");
            }

            if (requirement["implementationAreas"] is not JsonArray)
                errors.Add($"{id}: implementationAreas must be an array.");
            ValidateTests(id, requirement["tests"], errors);
        }

        var claim = Text(profile, "claim");
        var hasNonConformingStatus = requirements.OfType<JsonObject>()
            .Any(r => Text(r, "status") != "implemented");
        var inventoryComplete = coverage?["normativeClauseInventoryComplete"]?.GetValue<bool>() ?? false;
        if (claim == "conforming" && (hasNonConformingStatus || !inventoryComplete))
            errors.Add("The profile cannot claim conformance while the inventory is incomplete or non-implemented statuses remain.");
    }

    private static void ValidateTests(string id, JsonNode? node, List<string> errors)
    {
        if (node is not JsonArray tests)
        {
            errors.Add($"{id}: tests must be an array.");
            return;
        }

        foreach (var item in tests)
        {
            if (item is not JsonObject test)
            {
                errors.Add($"{id}: every test reference must be an object.");
                continue;
            }
            var suite = RequireText(test, "suite", errors);
            var path = RequireText(test, "path", errors).Replace('\\', '/');
            if (path.Contains('*') || path.EndsWith('/'))
                errors.Add($"{id}: test references must identify exact tests, not patterns or directories: '{path}'.");
            if (suite == "unit" && !path.Contains('#'))
                errors.Add($"{id}: unit evidence must include the exact test name after '#': '{path}'.");
            if (path.Contains("..", StringComparison.Ordinal))
                errors.Add($"{id}: test reference escapes its suite root: '{path}'.");
            if (!string.IsNullOrEmpty(path) && !EvidenceExists(suite, path))
                errors.Add($"{id}: referenced {suite} evidence does not exist: '{path}'.");
        }
    }

    private static bool EvidenceExists(string suite, string path)
    {
        var filePath = path.Split('#', 2)[0].Replace('/', Path.DirectorySeparatorChar);
        return suite switch
        {
            "unit" => File.Exists(Path.Combine(ConformancePaths.ProjectRoot, "..", filePath)),
            "wpt" => File.Exists(Path.Combine(ConformancePaths.Vendor, "wpt", filePath)) ||
                     File.Exists(Path.Combine(ConformancePaths.Overrides, filePath)),
            "test262" => File.Exists(Path.Combine(ConformancePaths.Vendor, "test262", filePath)),
            "acid" => File.Exists(Path.Combine(ConformancePaths.Vendor, filePath)),
            "css21-curated" => File.Exists(Path.Combine(ConformancePaths.Overrides, filePath)) ||
                               File.Exists(Path.Combine(ConformancePaths.Vendor, "wpt", filePath)),
            "css21-official" => File.Exists(Path.Combine(ConformancePaths.Vendor, "css21-official-20110323", filePath)),
            "manual" => true,
            _ => false,
        };
    }

    private static void ValidateSuiteLock(JsonObject suiteLock, List<string> errors)
    {
        if (suiteLock["schemaVersion"]?.GetValue<int>() != 1)
            errors.Add("suite lock schemaVersion must be 1.");
        if (suiteLock["suites"] is not JsonArray suites)
        {
            errors.Add("suite lock suites must be an array.");
            return;
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in suites.OfType<JsonObject>())
        {
            var id = RequireText(item, "id", errors);
            if (!ids.Add(id)) errors.Add($"Duplicate suite id '{id}'.");
            RequireUri(item, "repository", errors);
            var revision = RequireText(item, "revision", errors);
            var destination = RequireText(item, "destination", errors);
            var destinationPath = Path.GetFullPath(Path.Combine(ConformancePaths.ProjectRoot,
                destination.Replace('/', Path.DirectorySeparatorChar)));
            var vendorRoot = Path.GetFullPath(ConformancePaths.Vendor).TrimEnd(Path.DirectorySeparatorChar) +
                             Path.DirectorySeparatorChar;
            if (!destinationPath.StartsWith(vendorRoot, StringComparison.OrdinalIgnoreCase))
                errors.Add($"{id}: destination must remain under Lite.Conformance/vendor: '{destination}'.");
            if (Text(item, "source") == "git" &&
                (revision.Length != 40 || revision.Any(c => !Uri.IsHexDigit(c))))
                errors.Add($"{id}: git revision must be a full 40-character SHA.");
            if (Text(item, "source") == "git")
            {
                var actual = ReadGitHead(destinationPath);
                if (actual is null)
                    errors.Add($"{id}: vendored git checkout is missing or unreadable at '{destination}'.");
                else if (!actual.Equals(revision, StringComparison.OrdinalIgnoreCase))
                    errors.Add($"{id}: vendored revision {actual} does not match lock {revision}.");
            }
            else if (item["vendored"]?.GetValue<bool>() == true && !Directory.Exists(destinationPath))
                errors.Add($"{id}: suite is marked vendored but its directory is missing: '{destination}'.");
            if (item["sparseCheckout"] is JsonArray directories)
            {
                foreach (var directory in directories)
                {
                    var value = directory?.GetValue<string>() ?? "";
                    if (value.Contains('*') || value.Contains("..", StringComparison.Ordinal))
                        errors.Add($"{id}: unsafe sparse-checkout entry '{value}'.");
                }
            }
        }
        foreach (var id in new[] { "wpt", "test262", "css21-official-20110323" })
            if (!ids.Contains(id)) errors.Add($"Missing locked suite '{id}'.");
    }

    private static void ValidateExceptionManifests(JsonObject profile, List<string> errors)
    {
        var requirements = profile["requirements"]?.AsArray().OfType<JsonObject>().ToArray() ?? [];
        var published = new HashSet<string>(StringComparer.Ordinal);
        var publishedDependencies = new HashSet<string>(StringComparer.Ordinal);
        foreach (var requirement in requirements)
        {
            var status = Text(requirement, "status");
            if (status is not ("failing" or "dependency-exception")) continue;
            foreach (var test in requirement["tests"]?.AsArray().OfType<JsonObject>() ?? [])
            {
                var key = $"{Text(test, "suite")}:{Text(test, "path")}";
                published.Add(key);
                if (status == "dependency-exception") publishedDependencies.Add(key);
            }
        }

        RequirePublishedExpectedFailures("wpt",
            Path.Combine("Wpt", "wpt-manifest.txt"), published, errors);
        RequirePublishedExpectedFailures("css21-curated",
            Path.Combine("Css21", "css21-manifest.txt"), published, errors);
        RequirePublishedSkipEntries("test262",
            Path.Combine("Test262", "skip-list.txt"), publishedDependencies, errors);
        RequirePublishedSkipEntries("wpt",
            Path.Combine("Wpt", "survey-skip.txt"), publishedDependencies, errors);
    }

    private static void RequirePublishedExpectedFailures(
        string suite,
        string manifestPath,
        HashSet<string> published,
        List<string> errors)
    {
        foreach (var entry in Manifest.Load(ConformancePaths.Manifest(manifestPath)).Where(e => e.ExpectedFail))
        {
            if (!published.Contains($"{suite}:{entry.Path}"))
                errors.Add($"{manifestPath}: expected failure is absent from the compatibility profile: '{entry.Path}'.");
        }
    }

    private static void RequirePublishedSkipEntries(
        string suite,
        string manifestPath,
        HashSet<string> publishedDependencies,
        List<string> errors)
    {
        foreach (var entry in Manifest.Load(ConformancePaths.Manifest(manifestPath)))
        {
            if (entry.Path.Contains('*') || entry.Path.EndsWith('/') || entry.Path.Contains("..", StringComparison.Ordinal))
                errors.Add($"{manifestPath}: skip must be an exact safe test path: '{entry.Path}'.");
            if (!publishedDependencies.Contains($"{suite}:{entry.Path}"))
                errors.Add($"{manifestPath}: dependency skip is absent from the compatibility profile: '{entry.Path}'.");
        }
    }

    private static JsonObject BuildCounts(IEnumerable<JsonObject> requirements)
    {
        var result = new JsonObject();
        foreach (var specification in SpecificationIds.Append("total"))
        {
            var source = specification == "total"
                ? requirements
                : requirements.Where(r => Text(r, "specification") == specification);
            var entries = source.ToArray();
            var counts = new JsonObject();
            foreach (var status in Statuses.Order())
                counts[status] = entries.Count(r => Text(r, "status") == status);
            counts["entries"] = entries.Length;
            result[specification] = counts;
        }
        return result;
    }

    private static string RequireText(JsonObject obj, string property, List<string> errors)
    {
        var value = Text(obj, property);
        if (string.IsNullOrWhiteSpace(value)) errors.Add($"Missing or empty string property '{property}'.");
        return value;
    }

    private static void RequireUri(JsonObject obj, string property, List<string> errors)
    {
        var value = RequireText(obj, property, errors);
        if (!Uri.TryCreate(value, UriKind.Absolute, out _))
            errors.Add($"Property '{property}' is not an absolute URI: '{value}'.");
    }

    private static string Text(JsonObject obj, string property) =>
        obj[property]?.GetValue<string>() ?? "";

    private static string Sha256(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private static string? ReadGitHead(string directory)
    {
        if (!Directory.Exists(directory)) return null;
        try
        {
            var start = new ProcessStartInfo("git")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            start.ArgumentList.Add("-C");
            start.ArgumentList.Add(directory);
            start.ArgumentList.Add("rev-parse");
            start.ArgumentList.Add("HEAD");
            using var process = Process.Start(start);
            if (process is null) return null;
            if (!process.WaitForExit(5_000))
            {
                process.Kill(entireProcessTree: true);
                return null;
            }
            if (process.ExitCode != 0) return null;
            return process.StandardOutput.ReadToEnd().Trim();
        }
        catch
        {
            return null;
        }
    }

    private static int ReportValidationErrors(IEnumerable<string> errors)
    {
        foreach (var error in errors) Console.WriteLine($"  FAIL  {error}");
        return 1;
    }
}
