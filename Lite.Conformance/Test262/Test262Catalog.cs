using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lite.Conformance.Harness;

namespace Lite.Conformance.Test262;

internal sealed record Test262Case(string Path, string SourceSha256, Test262Metadata Metadata,
    string Classification, string Reason, bool RequiresReview);
internal sealed record Test262Inventory(string Revision, string Sha256, bool CheckoutComplete,
    IReadOnlyList<string> Blockers, IReadOnlyList<Test262Case> Tests);

internal static class Test262Catalog
{
    internal const string ApplicabilityFile = "Test262/es2020-applicability.json";
    internal const string SectionsFile = "Test262/es2020-sections.json";
    internal static string Root => Path.Combine(ConformancePaths.Vendor, "test262");
    // Upstream blob is pure LF; any CR byte means a checkout-time rewrite (git core.autocrlf).
    internal const string LineEndingCanary = "test/built-ins/Function/prototype/toString/line-terminator-normalisation-LF.js";
    private static readonly Lazy<string[]> SmokeRoots = new(() => JsonNode.Parse(File.ReadAllText(ConformancePaths.Manifest(ApplicabilityFile)))!["smokeRoots"]!.AsArray().Select(p => p!.GetValue<string>()).ToArray());

    internal static Test262Inventory Read()
    {
        var manifest = JsonNode.Parse(File.ReadAllText(ConformancePaths.Manifest(ApplicabilityFile)))!.AsObject();
        var included = manifest["includedFeatures"]!.AsArray().Select(x => x!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
        var excluded = manifest["excludedFeatures"]!.AsArray().ToDictionary(x => x!["feature"]!.GetValue<string>(), x => x!["introduced"]!.GetValue<string>(), StringComparer.Ordinal);
        if (included.Overlaps(excluded.Keys)) throw new InvalidDataException("Conflicting Test262 feature classifications");
        var sections = JsonNode.Parse(File.ReadAllText(ConformancePaths.Manifest(SectionsFile)))!["sections"]!.AsArray();
        var esids = sections.Select(x => x!["id"]!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
        var overrides = manifest["testOverrides"]!.AsArray().ToDictionary(x => x!["path"]!.GetValue<string>(), x => x!.AsObject(), StringComparer.Ordinal);
        var suite = JsonNode.Parse(File.ReadAllText(ConformancePaths.Manifest("test-suites.lock.json")))!["suites"]!
            .AsArray().Single(x => x!["id"]!.GetValue<string>() == "test262")!;
        var revision = Git("rev-parse", "HEAD").Trim();
        var blockers = new List<string>();
        if (revision != suite["revision"]!.GetValue<string>()) blockers.Add("test262-revision-mismatch");
        if (!ExecutionEvidence.IsPristineSuite(Root)) blockers.Add("test262-checkout-modified");
        var expected = Git("ls-tree", "-r", "--name-only", "HEAD", "test/", "harness/")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(x => x.TrimEnd('\r')).ToArray();
        var missing = expected.Where(p => !File.Exists(Path.Combine(Root, p))).ToArray();
        if (missing.Length > 0) blockers.Add($"test262-checkout-incomplete:{missing.Length}");
        // Git's core.autocrlf=true (the Windows system default) rewrites LF sources to CRLF at
        // checkout while git status and git diff still report the tree as clean, so the check
        // below cannot rely on git. test262 asserts verbatim source text (Function.prototype
        // toString line-terminator tests) and the inventory hashes test bytes, so a rewritten
        // tree would yield wrong verdicts silently; scripts/fetch-tests.ps1 pins the setting.
        var canary = Path.Combine(Root, LineEndingCanary);
        var lineEndingsClean = !File.Exists(canary) || !File.ReadAllBytes(canary).Contains((byte)'\r');
        if (!lineEndingsClean)
            blockers.Add($"test262-checkout-line-endings:{LineEndingCanary} contains CR; re-run scripts/fetch-tests.ps1 (it pins core.autocrlf=false)");
        var tests = new List<Test262Case>();
        var supplementalRoot = ConformancePaths.Manifest("Test262/supplemental");
        var sources = expected.Where(p => p.StartsWith("test/", StringComparison.Ordinal) && p.EndsWith(".js", StringComparison.Ordinal))
            .Select(p => (Path: p, File: Path.Combine(Root, p)))
            .Concat(Directory.Exists(supplementalRoot) ? Directory.GetFiles(supplementalRoot, "*.js", SearchOption.AllDirectories)
                .Select(p => (Path: "supplemental/" + Path.GetRelativePath(supplementalRoot, p).Replace('\\', '/'), File: p)) : []);
        foreach (var (path, file) in sources.OrderBy(p => p.Path, StringComparer.Ordinal))
        {
            if (!File.Exists(file)) continue;
            var bytes = File.ReadAllBytes(file);
            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            if (path.Contains("_FIXTURE", StringComparison.Ordinal))
            { tests.Add(new(path, hash, new([], [], [], null, null, null, []), "fixture", "Imported support file", false)); continue; }
            var meta = Test262Metadata.Parse(Encoding.UTF8.GetString(bytes));
            var unknown = meta.Features.Where(f => !included.Contains(f) && !excluded.ContainsKey(f)).ToArray();
            var later = meta.Features.Where(excluded.ContainsKey).ToArray();
            string classification, reason;
            bool review = false;
            if (meta.Errors.Length > 0) { classification = "invalid"; reason = string.Join("; ", meta.Errors); }
            else if (path.StartsWith("test/intl402/", StringComparison.Ordinal) || path.StartsWith("test/staging/intl402/", StringComparison.Ordinal))
            { classification = "out-of-scope"; reason = "ECMA-402 is a separate workstream"; }
            else if (unknown.Length > 0) { classification = "unreviewed"; reason = "Unknown features: " + string.Join(", ", unknown); }
            else if (later.Length > 0)
            {
                classification = "post-target"; reason = string.Join(", ", later.Select(f => f + " (" + excluded[f] + ")"));
                // A later test referring to an existing algorithm may also contain target assertions.
                review = meta.Esid is not null && esids.Contains(meta.Esid);
            }
            else if (path.StartsWith("test/staging/", StringComparison.Ordinal))
            { classification = "unreviewed"; reason = "Staging tests require individual edition/host review"; }
            else { classification = "included"; reason = "ECMA-262 through ES2020 and browser Annex B"; }
            if (overrides.TryGetValue(path, out var rule))
            {
                classification = rule["classification"]!.GetValue<string>();
                reason = rule["reason"]!.GetValue<string>();
                if (string.IsNullOrWhiteSpace(reason) || classification is not ("included" or "post-target" or "out-of-scope"))
                    throw new InvalidDataException($"Invalid applicability override: {path}");
                // Mixed exclusions need target replacements, checked again against included paths below.
                review = review && rule["replacements"] is not JsonArray { Count: > 0 };
            }
            tests.Add(new(path, hash, meta, classification, reason, review));
        }
        foreach (var item in overrides)
        {
            if (!tests.Any(t => t.Path == item.Key)) blockers.Add($"unknown-test-override:{item.Key}");
            foreach (var replacement in item.Value["replacements"]?.AsArray() ?? [])
                if (!tests.Any(t => t.Path == replacement!.GetValue<string>() && t.Classification == "included"))
                    blockers.Add($"invalid-mixed-test-replacement:{item.Key}:{replacement}");
        }
        if (manifest["semanticReviewComplete"]?.GetValue<bool>() != true) blockers.Add("es2020-semantic-review-incomplete");
        foreach (var group in tests.Where(t => t.Classification is "invalid" or "unreviewed" || t.RequiresReview).GroupBy(t => t.Classification))
            blockers.Add($"es2020-unreviewed-{group.Key}:{group.Count()}");
        var signature = new StringBuilder(revision).Append(ExecutionEvidence.HashFile(ConformancePaths.Manifest(ApplicabilityFile)))
            .Append(ExecutionEvidence.HashFile(ConformancePaths.Manifest(SectionsFile)));
        foreach (var test in tests) signature.Append('\n').Append(test.Path).Append(':').Append(test.SourceSha256);
        return new(revision, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(signature.ToString()))).ToLowerInvariant(),
            missing.Length == 0 && revision == suite["revision"]!.GetValue<string>() && lineEndingsClean, blockers, tests);
    }

    internal static bool IsSmoke(string path)
    {
        return SmokeRoots.Value.Any(p => path.StartsWith(p, StringComparison.Ordinal));
    }

    internal static int Run(string? reportPath, IEnumerable<string>? evidencePaths = null)
    {
        var inventory = Read();
        var sections = JsonNode.Parse(File.ReadAllText(ConformancePaths.Manifest(SectionsFile)))!["sections"]!.AsArray();
        var mapped = inventory.Tests.Where(t => t.Classification == "included" && t.Metadata.Esid is not null)
            .GroupBy(t => t.Metadata.Esid!).ToDictionary(g => g.Key, g => g.Select(t => t.Path).ToArray());
        foreach (var section in sections)
            section!["mappedTests"] = JsonSerializer.SerializeToNode(mapped.GetValueOrDefault(section["id"]!.GetValue<string>()) ?? []);
        var report = new { target = "ECMA-262 11th edition", inventory, sections,
            counts = inventory.Tests.GroupBy(t => t.Classification).ToDictionary(g => g.Key, g => g.Count()),
            requiredExecutions = inventory.Tests.Where(t => t.Classification == "included").Sum(t => t.Metadata.Modes.Length) };
        var path = reportPath ?? Path.Combine(ConformancePaths.EnsureArtifacts(), "es2020-inventory.json");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(report, ExecutionEvidence.JsonOptions));
        var evidenceBlockers = new List<string>();
        var evidence = ExecutionEvidence.ReadCurrent(evidencePaths ?? [], ExecutionEvidence.CaptureIdentity(), evidenceBlockers);
        Es2020Readiness.WriteBacklog(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, "es2020-backlog.json"), inventory, evidence, evidenceBlockers);
        Console.WriteLine($"ES2020 inventory: {inventory.Tests.Count} files, {report.requiredExecutions} required executions; {inventory.Blockers.Count} review/checkout blockers. {path}");
        return inventory.CheckoutComplete ? 0 : 1;
    }

    private static string Git(params string[] args)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = Root, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30_000)) { process.Kill(true); throw new IOException("Test262 catalog git timeout"); }
        if (process.ExitCode != 0) throw new IOException(error.Result);
        return output.Result;
    }
}
