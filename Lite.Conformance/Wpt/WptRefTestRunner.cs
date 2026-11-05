using System.Text.Json.Nodes;
using Lite.Conformance.Harness;
using SkiaSharp;

namespace Lite.Conformance.Wpt;

/// <summary>Implements the locked wptrunner reference graph algorithm. Fuzzy limits are
/// honored only from the pinned manifest's per-test metadata, never a global budget; DPI
/// options scale the render instead of refusing execution, and the root test's browser
/// configuration (viewport, device scale) governs the entire graph, matching the pinned
/// runner; testdriver-flagged pages drive the headless input pipeline before their
/// snapshot.</summary>
internal static class WptRefTestRunner
{
    internal static WptRunner.RunResult Run(WptCase test, Func<string, WptCase?> lookup)
    {
        var images = new Dictionary<string, SKBitmap>(StringComparer.Ordinal);
        var artifacts = new List<EvidenceArtifact>();
        var pending = new Stack<WptCase>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        // A passing path may END at a node with no references (a leaf) or CLOSE a cycle: the
        // block-in-inline-insert family's references chain in 2-cycles (nosplit-ref ↔ ref), so
        // every edge comparison matches and the walk previously fell off the end into
        // "No reference path passed" although all renders were identical. onPath holds the
        // nodes whose references are being explored (a back-edge closes their cycle);
        // allMatched holds nodes whose every comparison matched (a re-encounter replays a
        // proven-equal chain).
        var onPath = new HashSet<string>(StringComparer.Ordinal);
        var allMatched = new HashSet<string>(StringComparer.Ordinal);
        var (scale, scaleNote) = WptVisualPage.DeviceScale(test.Options);
        pending.Push(test);
        try
        {
            while (pending.TryPop(out var current))
            {
                if (!visited.Add(current.Path)) continue;
                onPath.Add(current.Path);
                var anyMismatch = false;
                foreach (var reference in current.References)
                {
                    var relative = new Uri(new Uri("http://wpt.invalid/" + current.Path), reference.Url);
                    if (relative.GetLeftPart(UriPartial.Authority) != "http://wpt.invalid")
                        return new(WptRunner.Cat.Unsupported, "External reference origin requires explicit serving configuration", 0, 0);
                    var targetPath = (relative.PathAndQuery + relative.Fragment).TrimStart('/');
                    var target = lookup(targetPath);
                    // The root test's device scale applies to every page in the graph (as the
                    // viewport_size does); a target carrying its own DPI option is superseded,
                    // and the evidence note says so rather than silently dropping it.
                    var targetScale = WptVisualPage.DeviceScale(target?.Options);
                    var superseded = targetScale.Scale != scale
                        ? "; reference DPI option superseded by the test's configuration" : "";
                    // A testdriver-flagged page (test or reference) drives the input pipeline
                    // before its snapshot through the headless BrowserWindow path.
                    var left = Render(current.Path, testDriver: current.TestDriver);
                    var right = Render(targetPath, testDriver: target?.TestDriver == true);
                    var fuzzy = FuzzyLimit(current, target, targetPath);
                    var comparison = PixelDiff.Compare(right, left,
                        tolerance: (byte)Math.Clamp(fuzzy?.MaxDifference ?? 0, 0, 255),
                        pixelBudget: fuzzy?.MaxPixels ?? 0);
                    var equal = comparison.Match;
                    var note = (fuzzy is { } limit
                        ? $" (fuzzy maxDifference={limit.MaxDifference} pixels={limit.MaxPixels})"
                        : "") + scaleNote + superseded;
                    if (reference.Relation == "==" ? equal : !equal)
                    {
                        // A self-reference is not a cycle that closes a passing path: comparing a
                        // page with itself is vacuous (the M3 reviewed contract rejects vacuous
                        // matches), so only a back-edge to a DIFFERENT node on the path — the
                        // nosplit-ref ↔ ref two-cycle shape — terminates as a passing chain.
                        if (target is not { References.Count: > 0 } ||
                            allMatched.Contains(targetPath) ||
                            (targetPath != current.Path && onPath.Contains(targetPath)))
                            return Result(true, "Reference graph has a passing path" + note);
                        pending.Push(target);
                    }
                    else
                    {
                        anyMismatch = true;
                        var name = $"wpt-reference-{Path.GetFileNameWithoutExtension(test.Path)}-{Guid.NewGuid().ToString("N")[..8]}";
                        PixelDiff.WriteFailureArtifacts(name, right, left, tolerance: 0);
                        foreach (var suffix in new[] { "expected", "actual", "diff" })
                        {
                            var file = Path.Combine(ConformancePaths.EnsureArtifacts(), $"{name}-{suffix}.png");
                            if (File.Exists(file)) artifacts.Add(ExecutionEvidence.Artifact(file, suffix));
                        }
                    }
                }
                if (!anyMismatch) allMatched.Add(current.Path);
                onPath.Remove(current.Path);
            }
            return Result(false, "No reference path passed");
        }
        catch (TimeoutException ex) { return new(WptRunner.Cat.Timeout, ex.Message, 0, 0, Artifacts: artifacts); }
        catch (Exception ex) { return new(WptRunner.Cat.Crash, WptVisualPage.CrashDetail(ex), 0, 0, Artifacts: artifacts); }
        finally { foreach (var image in images.Values) image.Dispose(); }

        WptRunner.RunResult Result(bool pass, string detail) => new(pass ? WptRunner.Cat.Pass : WptRunner.Cat.Fail, detail, 1,
            pass ? 0 : 1, [new("Reference graph has a passing path", pass ? 0 : 1, detail)], 0, artifacts);

        SKBitmap Render(string path, bool testDriver = false)
        {
            if (images.TryGetValue(path, out var bitmap)) return bitmap;
            (bitmap, _, _, _, _) = WptVisualPage.Render(path, test.Options?["viewport_size"]?.GetValue<string>(),
                testDriver: testDriver, scale: scale);
            images.Add(path, bitmap);
            return bitmap;
        }
    }

    /// <summary>Diagnostic: renders two served documents exactly as a reference pair is
    /// rendered and reports the exact pixel diff, writing expected/actual/diff artifacts.</summary>
    internal static int CompareDocuments(string left, string right)
    {
        ConformanceServer.Start(cssRegressionMode: false);
        ConformanceServer.EnsureTestFontsProvisioned();
        var (test, _, _, _, _) = WptVisualPage.Render(left.TrimStart('/'));
        var (reference, _, _, _, _) = WptVisualPage.Render(right.TrimStart('/'));
        using (test)
        using (reference)
        {
            var comparison = PixelDiff.Compare(reference, test, tolerance: 0, pixelBudget: 0);
            var name = "compare-" + Path.GetFileNameWithoutExtension(left);
            PixelDiff.WriteFailureArtifacts(name, reference, test, tolerance: 0);
            Console.WriteLine($"{(comparison.Match ? "MATCH" : "DIFFER")} {comparison.DiffPixels} px; artifacts {name}-*.png");
            return comparison.Match ? 0 : 1;
        }
    }

    /// <summary>WPT manifest fuzzy metadata: <c>"fuzzy": [[ref|null, [[min,max],[min,max]],
    /// ...]]</c> — a maxDifference range and a totalPixels range, optionally addressed to one
    /// reference URL. Where test-side and reference-side metadata both apply, the larger
    /// limit wins, matching wptrunner's combination rule.</summary>
    internal static (int MaxDifference, int MaxPixels)? FuzzyLimit(WptCase current, WptCase? target, string targetPath)
    {
        (int, int)? combined = null;
        foreach (var node in new[] { current, target })
        {
            if (node?.Options?["fuzzy"] is not JsonArray entries) continue;
            foreach (var entry in entries.OfType<JsonArray>())
            {
                if (entry.Count < 2 || entry[1] is not JsonArray ranges || ranges.Count < 2) continue;
                if (entry[0]?.GetValue<string>() is { Length: > 0 } appliesTo &&
                    !targetPath.EndsWith(appliesTo.TrimStart('/'), StringComparison.Ordinal)) continue;
                var maxDifference = RangeMax(ranges[0]);
                var maxPixels = RangeMax(ranges[1]);
                combined = (Math.Max(combined?.Item1 ?? 0, maxDifference), Math.Max(combined?.Item2 ?? 0, maxPixels));
            }
        }
        return combined;
    }

    private static int RangeMax(JsonNode? range) => range is JsonArray pair && pair.Count == 2 &&
        pair[0] is { } first && pair[1] is { } second
            ? Math.Max(first.GetValue<int>(), second.GetValue<int>())
            : 0;
}
