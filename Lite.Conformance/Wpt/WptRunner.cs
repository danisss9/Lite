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
            var outcome = RunOne(entry.Path, out var detail);
            Record(result, entry, outcome, detail);
        }

        return result.Report("wpt");
    }

    private static bool RunOne(string testPath, out string detail)
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
            detail = $"page load crashed: {ex.Message}";
            return false;
        }
        finally
        {
            JsEngine.OnCreated -= Hook;
        }

        if (reportJson is null)
        {
            detail = "timed out without reporting results";
            return false;
        }

        return ParseReport(reportJson, out detail);
    }

    private static bool ParseReport(string json, out string detail)
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

            detail = failures.Count == 0
                ? $"{total} subtests"
                : $"{failures.Count}/{total} subtests failed: {string.Join("; ", failures.Take(5))}";
            return failures.Count == 0 && total > 0;
        }
        catch (Exception ex)
        {
            detail = $"unparseable report: {ex.Message}";
            return false;
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
