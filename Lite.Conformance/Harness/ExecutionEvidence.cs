using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Lite.Conformance.Harness;

internal sealed record EvidenceIdentity(string SourceRevision, string SourceSha256,
    string ProfileSha256, string SuiteLockSha256, string DependenciesSha256, string EngineSha256,
    string HarnessSha256, string SuiteInputsSha256, string Platform);
internal sealed record SubtestEvidence(string Name, int Status, string? Message);
internal sealed record TestEvidence(string Suite, string Path, string Outcome, string Detail,
    IReadOnlyList<SubtestEvidence> Subtests, int? HarnessStatus = null, string Environment = "local", string? Url = null);
internal sealed record EvidenceReport(int FormatVersion, EvidenceIdentity Identity,
    string StartedUtc, string FinishedUtc, bool Completed, IReadOnlyList<TestEvidence> Tests);

/// <summary>Executed outcomes are useful only for the source, binaries and inputs that produced them.</summary>
internal static class ExecutionEvidence
{
    internal const string ProfileFile = "Profile/lite-html53-css21-es2020-profile.json";
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
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    internal static void Write(string path, EvidenceIdentity identity, DateTime started,
        IReadOnlyList<TestEvidence> tests)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        // Changing sources during a run must not produce reusable evidence.
        var report = new EvidenceReport(2, identity, started.ToUniversalTime().ToString("O"),
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
                var report = JsonSerializer.Deserialize<EvidenceReport>(File.ReadAllText(path), JsonOptions);
                if (report is null || report.FormatVersion != 2 || !report.Completed || report.Identity != identity)
                {
                    blockers.Add($"stale-or-incomplete-evidence:{path}");
                    continue;
                }
                if (report.Tests is null || report.Tests.Any(t => t is null || t.Subtests is null))
                    throw new InvalidDataException("Missing test outcomes or assertions.");
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
        bool requireUpstream = false)
    {
        var matches = tests.Where(t => t.Suite == suite && t.Path == path).ToArray();
        // Conflicting runs are blockers; ordering the input files cannot conceal a failure.
        return matches.Length > 0 && matches.All(t => t.Outcome == "pass" &&
            (suite != "wpt" || t.HarnessStatus == 0) && t.Subtests.Count > 0 &&
            (!requireUpstream || t.Environment == "upstream-wpt") &&
            t.Subtests.All(s => s.Status == 0) &&
            (string.IsNullOrEmpty(assertion) || t.Subtests.Any(s => s.Name == assertion)));
    }

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
