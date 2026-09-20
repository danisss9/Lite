using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lite.Conformance.Wpt;

namespace Lite.Conformance.Harness;

internal sealed record EvidenceIdentity(string SourceRevision, string SourceSha256,
    string ProfileSha256, string SuiteLockSha256, string DependenciesSha256, string EngineSha256,
    string HarnessSha256, string SuiteInputsSha256, string Platform);
internal sealed record SubtestEvidence(string Name, int Status, string? Message);
internal sealed record EvidenceArtifact(string Path, string Sha256, string Kind);
internal sealed record ManualEvidence(string Operator, string Procedure, string Environment, string ObservedUtc);
internal sealed record JavaScriptEvidence(string Mode, string InventorySha256, string Selection,
    int ShardIndex, int ShardCount, string? Filter, string? ExpectedPhase, string? ObservedPhase,
    string? ExpectedType, string? ObservedType, long DurationMs);
internal sealed record CssEvidence(string Media, string DocumentMode, string InventorySha256,
    int ViewportWidth, int ViewportHeight, int ShardIndex, int ShardCount, string? Filter,
    int? PageCount = null, double? PageWidthPoints = null, double? PageHeightPoints = null);
internal sealed record TestEvidence(string Suite, string Path, string Outcome, string Detail,
    IReadOnlyList<SubtestEvidence> Subtests, int? HarnessStatus = null, string Environment = "local", string? Url = null,
    string Context = "window", string Kind = "testharness", IReadOnlyList<EvidenceArtifact>? Artifacts = null,
    ManualEvidence? Manual = null, JavaScriptEvidence? JavaScript = null, CssEvidence? Css = null);
internal sealed record EvidenceReport(int FormatVersion, EvidenceIdentity Identity,
    string StartedUtc, string FinishedUtc, bool Completed, IReadOnlyList<TestEvidence> Tests);

/// <summary>Executed outcomes are useful only for the source, binaries and inputs that produced them.</summary>
internal static class ExecutionEvidence
{
    internal const int FormatVersion = 5;
    internal const string ProfileFile = "Profile/lite-html5-css21-es2020-profile.json";
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    internal static EvidenceIdentity CaptureIdentity()
    {
        var root = Path.GetFullPath(Path.Combine(ConformancePaths.ProjectRoot, ".."));
        var names = Git(root, "ls-files", "-z", "--cached", "--others", "--exclude-standard")
            .Split('\0', StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var name in names)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(name + "\0"));
            var file = Path.Combine(root, name);
            hash.AppendData(Encoding.UTF8.GetBytes(File.Exists(file) ? HashFile(file) : "deleted"));
        }
        return new EvidenceIdentity(Git(root, "rev-parse", "HEAD").Trim(),
            Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(),
            HashFile(ConformancePaths.Manifest(ProfileFile)),
            HashFile(ConformancePaths.Manifest("test-suites.lock.json")),
            DependencyHash(root),
            HashFile(typeof(Lite.BrowserWindow).Assembly.Location),
            HashFile(typeof(ExecutionEvidence).Assembly.Location),
            SuiteInputsHash(),
            $"{System.Runtime.InteropServices.RuntimeInformation.OSDescription};{System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");
    }

    internal static string HashFile(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    internal static bool IsPristineSuite(string directory) =>
        string.IsNullOrWhiteSpace(Git(directory, "status", "--porcelain", "--untracked-files=normal"));

    private static string DependencyHash(string root)
    {
        var directory = Path.GetDirectoryName(typeof(Lite.BrowserWindow).Assembly.Location)!;
        var input = new StringBuilder(HashFile(Path.Combine(root, "Directory.Packages.props")));
        foreach (var name in new[] { "AngleSharp.dll", "AngleSharp.Css.dll", "Jint.dll", "Acornima.dll", "SkiaSharp.dll" })
            input.Append('\n').Append(name).Append(':').Append(HashFile(Path.Combine(directory, name)));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input.ToString()))).ToLowerInvariant();
    }

    private static string SuiteInputsHash()
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var suite in new[] { "wpt", "test262" })
        {
            var root = Path.Combine(ConformancePaths.Vendor, suite);
            hash.AppendData(Encoding.UTF8.GetBytes(suite + "\0"));
            if (!Directory.Exists(root)) { hash.AppendData("missing"u8); continue; }
            hash.AppendData(Encoding.UTF8.GetBytes(Git(root, "rev-parse", "HEAD")));
            hash.AppendData(Encoding.UTF8.GetBytes(Git(root, "diff", "HEAD", "--binary", "--no-ext-diff")));
            foreach (var name in Git(root, "ls-files", "-z", "--others", "--exclude-standard")
                         .Split('\0', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal))
                hash.AppendData(Encoding.UTF8.GetBytes(name + "\0" + HashFile(Path.Combine(root, name))));
        }
        if (File.Exists(WptCatalog.ManifestPath))
            hash.AppendData(Encoding.UTF8.GetBytes("wpt-manifest\0" + HashFile(WptCatalog.ManifestPath)));
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    internal static void Write(string path, EvidenceIdentity identity, DateTime started,
        IReadOnlyList<TestEvidence> tests)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        // Changing sources during a run must not produce reusable evidence.
        var report = new EvidenceReport(FormatVersion, identity, started.ToUniversalTime().ToString("O"),
            DateTime.UtcNow.ToString("O"), identity == CaptureIdentity(), tests);
        File.WriteAllText(fullPath, JsonSerializer.Serialize(report, JsonOptions) + Environment.NewLine);
    }

    internal static List<TestEvidence> ReadCurrent(IEnumerable<string> paths, EvidenceIdentity identity,
        ICollection<string> blockers)
    {
        var tests = new List<TestEvidence>();
        foreach (var path in paths)
        {
            try
            {
                var json = File.ReadAllText(path);
                var report = JsonSerializer.Deserialize<EvidenceReport>(json, JsonOptions);
                if (report is null || report.FormatVersion != FormatVersion || !report.Completed || report.Identity != identity)
                {
                    blockers.Add($"stale-or-incomplete-evidence:{path}");
                    continue;
                }
                if (report.Tests is null || report.Tests.Any(t => t is null || t.Subtests is null))
                    throw new InvalidDataException("Missing test outcomes or assertions.");
                // Constructor defaults are useful to producers, but v3 input must explicitly
                // record its execution context and kind instead of inheriting a window default.
                using var document = JsonDocument.Parse(json);
                foreach (var test in document.RootElement.GetProperty("tests").EnumerateArray())
                    if (!test.TryGetProperty("context", out _) || !test.TryGetProperty("kind", out _))
                        throw new InvalidDataException("Missing execution context or test kind.");
                if (!DateTimeOffset.TryParse(report.StartedUtc, out var started) ||
                    !DateTimeOffset.TryParse(report.FinishedUtc, out var finished) || finished < started)
                    throw new InvalidDataException("Invalid execution timestamps.");
                foreach (var test in report.Tests)
                {
                    if (test.Suite is "css21-wpt" or "css21-official" &&
                        (test.Css is not { } css || css.Media is not ("screen" or "print") ||
                         css.DocumentMode is not ("html" or "xhtml") || css.ViewportWidth <= 0 || css.ViewportHeight <= 0 ||
                         css.ShardCount <= 0 || css.ShardIndex < 0 || css.ShardIndex >= css.ShardCount ||
                         css.InventorySha256.Length != 64))
                        throw new InvalidDataException("CSS evidence needs media, document mode, inventory identity, geometry and shard identity.");
                    if (string.IsNullOrWhiteSpace(test.Context) || string.IsNullOrWhiteSpace(test.Kind))
                        throw new InvalidDataException("Missing execution context or test kind.");
                    foreach (var artifact in test.Artifacts ?? [])
                    {
                        var file = ResolveArtifact(artifact.Path);
                        if (!File.Exists(file) || HashFile(file) != artifact.Sha256)
                            throw new InvalidDataException($"Missing or changed evidence artifact: {artifact.Path}");
                    }
                    if ((test.Suite == "manual" || test.Kind is "manual" or "visual") && (test.Manual is not { } manual ||
                        string.IsNullOrWhiteSpace(manual.Operator) || string.IsNullOrWhiteSpace(manual.Procedure) ||
                        string.IsNullOrWhiteSpace(manual.Environment) || !DateTimeOffset.TryParse(manual.ObservedUtc, out var observed) ||
                        observed < started || observed > finished ||
                        test.Artifacts is not { Count: > 0 }))
                        throw new InvalidDataException("Manual evidence needs an operator, procedure, environment, date and artifacts.");
                }
                tests.AddRange(report.Tests);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
                blockers.Add($"unreadable-evidence:{path}:{ex.Message}");
            }
        }
        return tests;
    }

    internal static bool HasPassingEvidence(IEnumerable<TestEvidence> tests, string suite, string path, string? assertion,
        bool requireUpstream = false, JsonObject? review = null, string? context = null, string? kind = null)
    {
        var matches = tests.Where(t => t.Suite == suite && t.Path == path).ToArray();
        // Conflicting runs are blockers; ordering the input files cannot conceal a failure.
        return matches.Length > 0 && matches.All(t =>
            (suite != "wpt" || t.HarnessStatus == 0) && t.Subtests.Count > 0 &&
            (!requireUpstream || t.Environment == "upstream-wpt") &&
            (context is null || t.Context == context) &&
            (kind is null || t.Kind == kind) &&
            (review is null ? t.Outcome == "pass" && t.Subtests.All(s => s.Status == 0) &&
                (string.IsNullOrEmpty(assertion) || t.Subtests.Any(s => s.Name == assertion)) :
                HtmlApplicability.HasPassingAssertions(t, review, assertion)));
    }

    internal static string ResolveArtifact(string path)
    {
        if (!WptCatalog.ValidPath(path)) throw new InvalidDataException("Artifact path must be relative to the artifacts directory.");
        var root = Path.GetFullPath(ConformancePaths.EnsureArtifacts()) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(root, path));
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Artifact escapes evidence directory.");
        return fullPath;
    }

    internal static EvidenceArtifact Artifact(string fullPath, string kind) => new(
        Path.GetRelativePath(ConformancePaths.EnsureArtifacts(), fullPath).Replace('\\', '/'), HashFile(fullPath), kind);

    private static string Git(string root, params string[] args)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new IOException("Cannot run git for evidence identity.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30_000))
        {
            process.Kill(entireProcessTree: true);
            throw new IOException("Timed out reading source identity.");
        }
        if (process.ExitCode != 0) throw new IOException(error.GetAwaiter().GetResult());
        return output.GetAwaiter().GetResult();
    }
}
