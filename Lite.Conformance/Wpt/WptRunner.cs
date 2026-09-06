using System.Text.Json;
using System.Diagnostics;
using Lite.Conformance.Harness;
using Lite.Scripting;

namespace Lite.Conformance.Wpt;

/// <summary>
/// Runs Web Platform Tests through the real Lite pipeline. Each test page loads the
/// vendored testharness.js plus a custom testharnessreport.js (overrides\resources\)
/// whose completion callback calls the host function <c>__lite_report(json)</c>.
/// </summary>
internal static class WptRunner
{
    public static int RunHtml(string? filter, ShardSpec shard, string? reportPath)
    {
        var applicability = HtmlApplicability.Read();
        var errors = new List<string>();
        HtmlApplicability.Validate(applicability, errors);
        if (errors.Count > 0) { foreach (var error in errors) Console.WriteLine(error); return 2; }
        var paths = applicability["tests"]!.AsArray().OfType<System.Text.Json.Nodes.JsonObject>()
            .Where(t => t["classification"]!.GetValue<string>() == "included")
            .Select(t => t["path"]!.GetValue<string>())
            .Where(p => filter is null || p.Contains(filter, StringComparison.Ordinal)).Order(StringComparer.Ordinal);
        var entries = shard.Apply(paths.SelectMany(Expand)).ToArray();
        if (entries.Length == 0) { Console.WriteLine("html53: no reviewed applicable tests match."); return 2; }
        var identity = ExecutionEvidence.CaptureIdentity();
        var started = DateTime.UtcNow;
        var outcomes = new List<TestEvidence>();
        ConformanceServer.Start(cssRegressionMode: false);
        foreach (var path in entries)
        {
            var result = RunOne(path);
            outcomes.Add(ToEvidence(path, result));
            Console.WriteLine($"  {result.Cat.ToString().ToUpperInvariant(),-7} {path} ({result.Detail})");
        }
        ExecutionEvidence.Write(reportPath ?? DefaultReport("html53", shard), identity, started, outcomes);
        Console.WriteLine($"html53: {outcomes.Count(t => t.Outcome == "pass")}/{outcomes.Count} reviewed tests passed; this is not a profile-readiness claim.");
        return outcomes.All(t => t.Outcome == "pass") ? 0 : 1;
    }

    private const int TestTimeoutMs = 10_000;

    // testharness.js status codes
    private const int SubtestPass = 0;

    /// <summary>How a single test run turned out, independent of whether the manifest
    /// expected it. <see cref="Cat.Pass"/> means every subtest passed and there was at
    /// least one subtest.</summary>
    internal enum Cat { Pass, Fail, Crash, Timeout, Empty }

    internal readonly record struct RunResult(Cat Cat, string Detail, int Total, int Failures,
        IReadOnlyList<SubtestEvidence>? Subtests = null, int? HarnessStatus = null)
    {
        public bool Passed => Cat == Cat.Pass;
    }

    public static int Run(string? filter, ShardSpec shard, string? reportPath = null)
    {
        var entries = shard.Apply(Manifest.Filter(Manifest.Load(ConformancePaths.Manifest(Path.Combine("Wpt", "wpt-manifest.txt"))), filter, ShardSpec.All)
            .SelectMany(e => Expand(e.Path).Select(p => e with { Path = p })).OrderBy(e => e.Path, StringComparer.Ordinal)).ToList();
        if (entries.Count == 0)
        {
            Console.WriteLine("wpt: no manifest entries match.");
            return 2;
        }

        ConformanceServer.Start(cssRegressionMode: false);
        var result = new SuiteResult();
        var identity = ExecutionEvidence.CaptureIdentity();
        var started = DateTime.UtcNow;
        var outcomes = new List<TestEvidence>();
        if (shard.Count > 1) Console.WriteLine($"  shard {shard}");

        foreach (var entry in entries)
        {
            var outcome = RunOne(entry.Path);
            outcomes.Add(ToEvidence(entry.Path, outcome));
            Record(result, entry, outcome.Passed, outcome.Detail);
        }

        ExecutionEvidence.Write(reportPath ?? DefaultReport("results", shard), identity, started, outcomes);
        return result.Report("wpt");
    }

    /// <summary>
    /// Surveys every testharness test under a vendored directory (e.g. <c>dom/nodes</c>):
    /// runs each through the real Lite pipeline and reports a pass/fail/crash/timeout
    /// breakdown. Any failure, timeout, crash, empty result or skip returns a failing exit
    /// code; an empty selection returns 2. Outcomes remain available in the evidence report.
    /// </summary>
    public static int Survey(string relDir, int limit = 0, string? reportPath = null, ShardSpec? shard = null)
    {
        var wptRoot = Path.Combine(ConformancePaths.Vendor, "wpt");
        var root = Path.GetFullPath(Path.Combine(wptRoot, relDir.Replace('/', Path.DirectorySeparatorChar)));
        if (!root.StartsWith(wptRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(root))
        {
            Console.WriteLine($"survey: directory not found: {root}");
            return 2;
        }

        ConformanceServer.Start(cssRegressionMode: false);

        var skip = LoadSurveySkips();
        var selectedShard = shard ?? ShardSpec.All;
        var identity = ExecutionEvidence.CaptureIdentity();
        var started = DateTime.UtcNow;
        var outcomes = new List<TestEvidence>();

        var tests = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(IsCandidateTest)
            .Select(f => Path.GetRelativePath(wptRoot, f).Replace('\\', '/'))
            .SelectMany(Expand)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
        if (limit > 0) tests = tests.Take(limit).ToList();
        tests = selectedShard.Apply(tests).ToList();

        int pass = 0, fail = 0, crash = 0, timeout = 0, empty = 0, skipped = 0;

        foreach (var file in tests)
        {
            var urlPath = file;
            // Skip is a struct, so an unmatched FirstOrDefault yields default(Skip) (Path == null);
            // a real match always has a non-null Path.
            var match = skip.FirstOrDefault(s => urlPath.Equals(s.Path, StringComparison.Ordinal));
            if (match.Path is not null)
            {
                skipped++;
                outcomes.Add(new TestEvidence("wpt", urlPath, "skipped", match.Reason, []));
                Console.WriteLine($"  SKIP    {urlPath} — {match.Reason}");
                continue;
            }
            // Trace each test before running it: a test that triggers an uncatchable CLR
            // StackOverflow (deep JS recursion — a Jint limitation, like the test262 tco-* skips)
            // terminates the whole process. The result line is streamed (not buffered), so a
            // crash never loses the passing tests gathered so far; the last [run] line on stderr
            // names the culprit to add to survey-skip.txt.
            Console.Error.WriteLine($"[run] {urlPath}");
            Console.Error.Flush();
            var r = RunOne(urlPath);
            outcomes.Add(ToEvidence(urlPath, r));
            switch (r.Cat)
            {
                case Cat.Pass: pass++; Console.WriteLine($"  PASS    {urlPath}"); break;
                case Cat.Fail: fail++; Console.WriteLine($"  FAIL    {urlPath} — {r.Detail}"); break;
                case Cat.Crash: crash++; Console.WriteLine($"  CRASH   {urlPath} — {r.Detail}"); break;
                case Cat.Timeout: timeout++; Console.WriteLine($"  TIMEOUT {urlPath}"); break;
                case Cat.Empty: empty++; Console.WriteLine($"  EMPTY   {urlPath} — {r.Detail}"); break;
            }
            Console.Out.Flush();
        }

        int total = tests.Count;
        Console.WriteLine();
        Console.WriteLine($"=== survey {relDir}: {pass}/{total} fully passed " +
                          $"({(total == 0 ? 0 : 100.0 * pass / total):F1}%) — " +
                          $"{fail} partial-fail, {crash} crash, {timeout} timeout, {empty} no-subtests, {skipped} skipped ===");
        Console.WriteLine("  (grep '  PASS    ' for the fully-passing tests to promote into wpt-manifest.txt)");
        ExecutionEvidence.Write(reportPath ?? DefaultReport("survey", selectedShard), identity, started, outcomes);
        return total == 0 ? 2 : fail + crash + timeout + empty + skipped > 0 ? 1 : 0;
    }

    private readonly record struct Skip(string Path, string Reason);

    /// <summary>Loads Wpt\survey-skip.txt: exact paths the survey must not run,
    /// each with a reason. These are tests that crash the whole process (uncatchable CLR
    /// StackOverflow from deep JS recursion — a Jint limitation), so they can't simply fail.</summary>
    private static List<Skip> LoadSurveySkips()
    {
        var file = ConformancePaths.Manifest(Path.Combine("Wpt", "survey-skip.txt"));
        var skips = new List<Skip>();
        if (!File.Exists(file)) return skips;
        foreach (var raw in File.ReadAllLines(file))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var bar = line.IndexOf('|');
            skips.Add(bar >= 0
                ? new Skip(line[..bar].Trim(), line[(bar + 1)..].Trim())
                : new Skip(line, "process-crasher"));
        }
        return skips;
    }

    /// <summary>True for files that look like runnable window-context testharness tests:
    /// a <c>.html</c> that pulls in testharness.js, or a <c>.any.js</c> (served via its
    /// generated <c>.any.html</c> wrapper). Excludes references, manual tests, support
    /// files, and worker-only variants Lite can't host.</summary>
    internal static bool IsCandidateTest(string path)
    {
        var name = Path.GetFileName(path);
        var lower = name.ToLowerInvariant();

        // Support docs and non-window variants Lite's headless window can't run.
        if (path.Replace('\\', '/').Contains("/support/")) return false;
        if (lower.Contains("-manual.")) return false;
        if (lower.EndsWith(".worker.js") || lower.EndsWith(".serviceworker.js") ||
            lower.EndsWith(".sharedworker.js")) return false;

        if (lower.EndsWith(".any.js") || lower.EndsWith(".window.js")) return true;

        if (lower.EndsWith(".html") || lower.EndsWith(".xht") || lower.EndsWith(".xhtml"))
        {
            // Skip reference/companion files and anything that isn't a testharness test.
            var stem = Path.GetFileNameWithoutExtension(name);
            if (stem.EndsWith("-ref", StringComparison.OrdinalIgnoreCase) ||
                stem.EndsWith("-notref", StringComparison.OrdinalIgnoreCase) ||
                stem.StartsWith("ref-", StringComparison.OrdinalIgnoreCase)) return false;
            try
            {
                return File.ReadAllText(path).Contains("testharness.js", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }
        return false;
    }

    private static RunResult RunOne(string testPath)
    {
        var file = ResolveSource(testPath.Split(['?', '#'])[0], UsesUpstream(testPath));
        var longTimeout = file is not null && WptMetadata.Parse(File.ReadAllText(file)).LongTimeout;
        return RunIsolated(testPath, ConformanceServer.TestUrl(WptMetadata.UrlPath(testPath)), longTimeout ? 70_000 : 20_000);
    }

    internal static RunResult RunIsolated(string path, string url, int timeoutMs)
    {
        var output = Path.Combine(ConformancePaths.EnsureArtifacts(), $"worker-{Guid.NewGuid():N}.json");
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var arg in new[] { typeof(WptRunner).Assembly.Location, "--wpt-worker", path, url, output }) start.ArgumentList.Add(arg);
        try
        {
            using var process = Process.Start(start) ?? throw new IOException("Cannot start WPT worker.");
            var stdout = Drain(process.StandardOutput);
            var stderr = Drain(process.StandardError);
            if (!process.WaitForExit(timeoutMs))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
                Task.WaitAll(stdout, stderr);
                return new(Cat.Timeout, $"Loading/execution exceeded {timeoutMs} ms", 0, 0);
            }
            Task.WaitAll(stdout, stderr);
            if (process.ExitCode != 0 || !File.Exists(output))
                return new(Cat.Crash, $"Worker exited {process.ExitCode}: {stderr.Result}", 0, 0);
            return JsonSerializer.Deserialize<RunResult>(File.ReadAllText(output), ExecutionEvidence.JsonOptions);
        }
        catch (Exception ex) when (ex is IOException or JsonException or System.ComponentModel.Win32Exception)
        { return new(Cat.Crash, ex.Message, 0, 0); }
        finally { if (File.Exists(output)) File.Delete(output); }
    }

    private static async Task<string> Drain(StreamReader reader)
    {
        var tail = new System.Text.StringBuilder();
        var buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer)) > 0)
        {
            tail.Append(buffer, 0, count);
            if (tail.Length > 8192) tail.Remove(0, tail.Length - 8192);
        }
        return tail.ToString();
    }

    internal static int Worker(string path, string url, string output)
    {
        var result = RunInProcess(url);
        File.WriteAllText(output, JsonSerializer.Serialize(result, ExecutionEvidence.JsonOptions));
        return 0;
    }

    private static RunResult RunInProcess(string url)
    {
        string? reportJson = null;
        var reports = new Dictionary<JsEngine, string>();
        var reporter = File.ReadAllText(Path.Combine(ConformancePaths.Overrides, "resources", "testharnessreport.js"));
        void Hook(JsEngine engine)
        {
            engine.RawEngine.SetValue("__lite_report", new Action<string>(json => reports.TryAdd(engine, json)));
            void Attach(JsEngine owner)
            {
                if (owner.RawEngine.Evaluate("typeof add_completion_callback === 'function'") == Jint.Native.JsBoolean.True)
                {
                    owner.ScriptExecuted -= Attach;
                    owner.RawEngine.Execute(reporter);
                }
            }
            engine.ScriptExecuted += Attach;
        }

        JsEngine.OnCreated += Hook;
        try
        {
            var (_, engine) = HeadlessPage.Load(url);
            HeadlessPage.PumpUntil(engine, () => reports.ContainsKey(engine), TestTimeoutMs * 6);
            reports.TryGetValue(engine, out reportJson);
        }
        catch (Exception ex)
        {
            return new RunResult(Cat.Crash, $"page load crashed: {ex.Message}", 0, 0);
        }
        finally
        {
            JsEngine.OnCreated -= Hook;
        }

        if (reportJson is null)
            return new RunResult(Cat.Timeout, "timed out without reporting results", 0, 0);

        return ParseReport(reportJson);
    }

    internal static RunResult ParseReport(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var rootEl = doc.RootElement;
            var harnessStatus = rootEl.GetProperty("status").GetInt32();
            var failures = new List<string>();
            var subtests = new List<SubtestEvidence>();
            int total = 0;

            foreach (var test in rootEl.GetProperty("tests").EnumerateArray())
            {
                total++;
                var status = test.GetProperty("status").GetInt32();
                subtests.Add(new(test.GetProperty("name").GetString() ?? "", status,
                    test.TryGetProperty("message", out var subtestMessage) ? subtestMessage.GetString() : null));
                if (status != SubtestPass)
                {
                    var name = test.GetProperty("name").GetString();
                    var message = test.TryGetProperty("message", out var m) ? m.GetString() : null;
                    failures.Add($"{name}: status {status}{(message is null ? "" : $" — {message}")}");
                }
            }

            if (harnessStatus != 0)
                failures.Insert(0, $"harness status {harnessStatus}");

            if (total == 0)
                return new RunResult(Cat.Empty, "0 subtests reported", 0, 0);

            var detail = failures.Count == 0
                ? $"{total} subtests"
                : $"{failures.Count}/{total} subtests failed: {string.Join("; ", failures.Take(5))}";
            return new RunResult(failures.Count == 0 ? Cat.Pass : Cat.Fail, detail, total, failures.Count, subtests, harnessStatus);
        }
        catch (Exception ex)
        {
            return new RunResult(Cat.Crash, $"unparseable report: {ex.Message}", 0, 0);
        }
    }

    private static bool UsesUpstream(string path) => !path.StartsWith("lite/", StringComparison.Ordinal) &&
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("LITE_WPT_BASE_URL"));

    private static string? ResolveSource(string path, bool upstream)
    {
        if (!upstream) return ConformanceServer.ResolveFile(path);
        var root = Path.GetFullPath(Path.Combine(ConformancePaths.Vendor, "wpt")) + Path.DirectorySeparatorChar;
        var file = Path.GetFullPath(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)));
        return file.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(file) ? file : null;
    }

    internal static IEnumerable<string> Expand(string path) => Expand(path, UsesUpstream(path));

    internal static IEnumerable<string> Expand(string path, bool upstream)
    {
        if (path.Contains('?') || path.Contains('#')) return [path];
        var file = ResolveSource(path, upstream);
        if (file is null) return [path];
        var metadata = WptMetadata.Parse(File.ReadAllText(file));
        if (path.EndsWith(".any.js", StringComparison.Ordinal) && !metadata.Window) return [];
        return metadata.Variants.Count == 0 ? [path] : metadata.Variants.Select(v => path + v);
    }

    private static TestEvidence ToEvidence(string path, RunResult result) => new("wpt", path,
        result.Cat.ToString().ToLowerInvariant(), result.Detail, result.Subtests ?? [], result.HarnessStatus,
        !path.StartsWith("lite/", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("LITE_WPT_BASE_URL"))
            ? "upstream-wpt" : "local", ConformanceServer.TestUrl(WptMetadata.UrlPath(path)));

    private static string DefaultReport(string kind, ShardSpec shard) => Path.Combine(ConformancePaths.EnsureArtifacts(),
        $"wpt-{kind}{(shard.Count > 1 ? $"-{shard.Index}-of-{shard.Count}" : "")}.json");

    private static void Record(SuiteResult result, ManifestEntry entry, bool passed, string detail)
    {
        if (passed && !entry.ExpectedFail)
        {
            result.Passed++;
            Console.WriteLine($"  PASS  {entry.Path} ({detail})");
        }
        else if (!passed && entry.ExpectedFail)
        {
            result.ExpectedFailures++;
            Console.WriteLine($"  XFAIL {entry.Path} ({entry.Reason ?? "expected"})");
        }
        else if (passed && entry.ExpectedFail)
        {
            result.UnexpectedPasses++;
            result.Problems.Add($"{entry.Path} passes but is annotated expected-fail — update the manifest");
            Console.WriteLine($"  XPASS {entry.Path}");
        }
        else
        {
            result.Failed++;
            result.Problems.Add($"{entry.Path}: {detail}");
            Console.WriteLine($"  FAIL  {entry.Path}: {detail}");
        }
    }
}
