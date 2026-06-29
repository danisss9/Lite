using System.Text.Json;
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
    private const int TestTimeoutMs = 10_000;

    // testharness.js status codes
    private const int SubtestPass = 0;

    /// <summary>How a single test run turned out, independent of whether the manifest
    /// expected it. <see cref="Cat.Pass"/> means every subtest passed and there was at
    /// least one subtest.</summary>
    private enum Cat { Pass, Fail, Crash, Timeout, Empty }

    private readonly record struct RunResult(Cat Cat, string Detail, int Total, int Failures)
    {
        public bool Passed => Cat == Cat.Pass;
    }

    public static int Run(string? filter)
    {
        var entries = Manifest.Filter(Manifest.Load(ConformancePaths.Manifest(Path.Combine("Wpt", "wpt-manifest.txt"))), filter);
        if (entries.Count == 0)
        {
            Console.WriteLine("wpt: no manifest entries match.");
            return 0;
        }

        ConformanceServer.Start();
        var result = new SuiteResult();

        foreach (var entry in entries)
        {
            var outcome = RunOne(entry.Path);
            Record(result, entry, outcome.Passed, outcome.Detail);
        }

        return result.Report("wpt");
    }

    /// <summary>
    /// Surveys every testharness test under a vendored directory (e.g. <c>dom/nodes</c>):
    /// runs each through the real Lite pipeline and reports a pass/fail/crash/timeout
    /// breakdown. Informational only — always returns 0 so it never gates CI. Use it to
    /// baseline a subtree and pick cleanly-passing tests to promote into wpt-manifest.txt.
    /// </summary>
    public static int Survey(string relDir, int limit = 0)
    {
        var wptRoot = Path.Combine(ConformancePaths.Vendor, "wpt");
        var root = Path.Combine(wptRoot, relDir.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(root))
        {
            Console.WriteLine($"survey: directory not found: {root}");
            return 0;
        }

        ConformanceServer.Start();

        var skip = LoadSurveySkips();

        var tests = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(IsCandidateTest)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
        if (limit > 0) tests = tests.Take(limit).ToList();

        int pass = 0, fail = 0, crash = 0, timeout = 0, empty = 0, skipped = 0;

        foreach (var file in tests)
        {
            var urlPath = Path.GetRelativePath(wptRoot, file).Replace('\\', '/');
            // Skip is a struct, so an unmatched FirstOrDefault yields default(Skip) (Path == null);
            // a real match always has a non-null Path.
            var match = skip.FirstOrDefault(s => urlPath.Contains(s.Path, StringComparison.OrdinalIgnoreCase));
            if (match.Path is not null)
            {
                skipped++;
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
        return 0;
    }

    private readonly record struct Skip(string Path, string Reason);

    /// <summary>Loads Wpt\survey-skip.txt: substring path-matches the survey must not run,
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
    private static bool IsCandidateTest(string path)
    {
        var name = Path.GetFileName(path);
        var lower = name.ToLowerInvariant();

        // Support docs and non-window variants Lite's headless window can't run.
        if (path.Replace('\\', '/').Contains("/support/")) return false;
        if (lower.Contains("-manual.")) return false;
        if (lower.EndsWith(".worker.js") || lower.EndsWith(".serviceworker.js") ||
            lower.EndsWith(".window.js") || lower.EndsWith(".sharedworker.js")) return false;

        if (lower.EndsWith(".any.js")) return true;

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
        // .any.js tests are addressed via their generated .any.html wrapper.
        var urlPath = testPath.EndsWith(".any.js", StringComparison.OrdinalIgnoreCase)
            ? testPath[..^".js".Length] + ".html"
            : testPath;
        var url = $"{ConformanceServer.BaseUrl}/{urlPath.TrimStart('/')}";

        string? reportJson = null;
        void Hook(JsEngine engine) =>
            engine.RawEngine.SetValue("__lite_report", new Action<string>(json => reportJson = json));

        JsEngine.OnCreated += Hook;
        try
        {
            var (_, engine) = HeadlessPage.Load(url);
            HeadlessPage.PumpUntil(engine, () => reportJson is not null, TestTimeoutMs);
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

    private static RunResult ParseReport(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var rootEl = doc.RootElement;
            var harnessStatus = rootEl.GetProperty("status").GetInt32();
            var failures = new List<string>();
            int total = 0;

            foreach (var test in rootEl.GetProperty("tests").EnumerateArray())
            {
                total++;
                var status = test.GetProperty("status").GetInt32();
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
            return new RunResult(failures.Count == 0 ? Cat.Pass : Cat.Fail, detail, total, failures.Count);
        }
        catch (Exception ex)
        {
            return new RunResult(Cat.Crash, $"unparseable report: {ex.Message}", 0, 0);
        }
    }

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
