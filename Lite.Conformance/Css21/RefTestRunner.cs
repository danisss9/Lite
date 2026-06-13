using Lite.Conformance.Harness;
using Lite.Layout;
using SkiaSharp;

namespace Lite.Conformance.Css21;

/// <summary>
/// CSS reftest runner: renders a test page and its reference page through the real
/// Lite pipeline and pixel-compares the two. Both sides are rendered by Lite, so font
/// rasterization differences cancel out — only layout/paint differences register.
/// </summary>
internal static class RefTestRunner
{
    // WPT reftest convention viewport
    public const int Width = 600;
    public const int Height = 600;

    public static int Run(string? filter)
    {
        var entries = Manifest.Filter(Manifest.Load(ConformancePaths.Manifest(Path.Combine("Css21", "css21-manifest.txt"))), filter);
        if (entries.Count == 0)
        {
            Console.WriteLine("css21: no manifest entries match.");
            return 0;
        }

        ConformanceServer.Start();
        var result = new SuiteResult();

        foreach (var entry in entries)
        {
            // Explicit reference from the manifest, else the WPT <link rel="match"/"mismatch"> in the test file.
            var (reference, mismatch) = entry.Reference is not null
                ? (entry.Reference, false)
                : ResolveReferenceLink(entry.Path);

            if (reference is null)
            {
                result.Failed++;
                result.Problems.Add($"{entry.Path}: no reference (manifest 'test | ref' or a <link rel=match> in the test)");
                continue;
            }

            bool passed;
            string detail;
            try
            {
                using var testBitmap = Render(entry.Path);
                using var refBitmap = Render(reference);
                var diff = PixelDiff.Compare(refBitmap, testBitmap);
                // rel="mismatch": the renderings must DIFFER for the test to pass.
                passed = mismatch ? !diff.Match : diff.Match;
                detail = (mismatch ? "[mismatch] " : "") +
                    (diff.Detail ?? $"{diff.DiffPixels} differing pixels ({diff.DiffRatio.ToString("P3", System.Globalization.CultureInfo.InvariantCulture)})");
                if (!passed)
                    PixelDiff.WriteFailureArtifacts(SafeName(entry.Path), refBitmap, testBitmap);
            }
            catch (Exception ex)
            {
                passed = false;
                detail = $"render crashed: {ex.Message}";
            }

            Record(result, entry, passed, detail);
        }

        return result.Report("css21");
    }

    /// <summary>Reads a test file and returns the path of its WPT reference
    /// (&lt;link rel="match"&gt; or "mismatch"), resolved relative to the test, plus whether
    /// it is a mismatch reference. Returns (null, false) if none is found.</summary>
    private static (string? Reference, bool Mismatch) ResolveReferenceLink(string testPath)
    {
        var file = ConformanceServer.ResolveFile("/" + testPath.TrimStart('/'));
        if (file is null || !File.Exists(file)) return (null, false);

        var html = File.ReadAllText(file);
        var m = System.Text.RegularExpressions.Regex.Match(html,
            "<link\\s+[^>]*rel\\s*=\\s*[\"']?(match|mismatch)[\"']?[^>]*href\\s*=\\s*[\"']([^\"']+)[\"']",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!m.Success)
        {
            // rel and href can appear in the other order.
            m = System.Text.RegularExpressions.Regex.Match(html,
                "<link\\s+[^>]*href\\s*=\\s*[\"']([^\"']+)[\"'][^>]*rel\\s*=\\s*[\"']?(match|mismatch)[\"']?",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!m.Success) return (null, false);
            var href2 = m.Groups[1].Value;
            var rel2 = m.Groups[2].Value;
            return (ResolveRelative(testPath, href2), rel2.Equals("mismatch", StringComparison.OrdinalIgnoreCase));
        }

        var rel = m.Groups[1].Value;
        var href = m.Groups[2].Value;
        return (ResolveRelative(testPath, href), rel.Equals("mismatch", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Resolves a reference href relative to the test's URL path.</summary>
    private static string ResolveRelative(string testPath, string href)
    {
        if (href.StartsWith('/')) return href.TrimStart('/');
        var dir = testPath.Contains('/') ? testPath[..testPath.LastIndexOf('/')] : "";
        var combined = string.IsNullOrEmpty(dir) ? href : $"{dir}/{href}";
        // Normalize ../ and ./ segments.
        var parts = new List<string>();
        foreach (var seg in combined.Split('/'))
        {
            if (seg == "." || seg.Length == 0) continue;
            if (seg == "..") { if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); }
            else parts.Add(seg);
        }
        return string.Join('/', parts);
    }

    /// <summary>
    /// Surveys every reftest under a vendored directory (e.g. css/CSS2/normal-flow): auto-detects
    /// each test's &lt;link rel=match&gt; reference, renders both, and reports a pass-rate.
    /// Informational only — always returns 0 so it never gates CI. Use it to baseline progress
    /// and pick failing tests to promote into the curated manifest.
    /// </summary>
    public static int Survey(string relDir, int limit = 0)
    {
        var root = Path.Combine(ConformancePaths.Vendor, "wpt", relDir.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(root))
        {
            Console.WriteLine($"survey: directory not found: {root}");
            return 0;
        }

        ConformanceServer.Start();

        var tests = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".xht", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".xhtml", StringComparison.OrdinalIgnoreCase))
            .Where(f => !IsReferenceFile(f))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
        if (limit > 0) tests = tests.Take(limit).ToList();

        int pass = 0, fail = 0, noref = 0, crash = 0;
        var failed = new List<string>();

        foreach (var file in tests)
        {
            var urlPath = "css/" + Path.GetRelativePath(Path.Combine(ConformancePaths.Vendor, "wpt", "css"), file)
                .Replace('\\', '/');
            var (reference, mismatch) = ResolveReferenceLink(urlPath);
            if (reference is null) { noref++; continue; }

            try
            {
                using var testBmp = Render(urlPath);
                using var refBmp = Render(reference);
                var diff = PixelDiff.Compare(refBmp, testBmp);
                var ok = mismatch ? !diff.Match : diff.Match;
                if (ok) pass++;
                else { fail++; if (failed.Count < 40) failed.Add(urlPath); }
            }
            catch (Exception ex) { crash++; if (failed.Count < 40) failed.Add($"{urlPath} (crash: {ex.GetType().Name})"); }
        }

        int total = pass + fail + crash;
        Console.WriteLine();
        Console.WriteLine($"=== survey {relDir}: {pass}/{total} passed " +
                          $"({(total == 0 ? 0 : 100.0 * pass / total):F1}%), {crash} crashed, {noref} skipped (no ref) ===");
        foreach (var f in failed) Console.WriteLine($"  fail: {f}");
        return 0;
    }

    private static bool IsReferenceFile(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        return name.EndsWith("-ref", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith("-notref", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("-ref.", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("ref-", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Renders a served page to a bitmap at the reftest viewport size.</summary>
    internal static SKBitmap Render(string path, int width = Width, int height = Height, float scrollY = 0)
    {
        var url = $"{ConformanceServer.BaseUrl}/{path.TrimStart('/')}";
        var (root, engine) = HeadlessPage.Load(url, width, height);
        HeadlessPage.PumpUntilIdle(engine, 2_000);
        var viewport = new Viewport { ViewportHeight = height };
        if (scrollY > 0)
        {
            // First layout establishes ContentHeight so the scroll clamp works.
            Drawer.DrawToBitmap(width, height, root, viewport).Dispose();
            viewport.ScrollTo(scrollY);
        }
        return Drawer.DrawToBitmap(width, height, root, viewport);
    }

    private static string SafeName(string path) =>
        path.Replace('/', '_').Replace('\\', '_').Replace('.', '_');

    private static void Record(SuiteResult result, ManifestEntry entry, bool passed, string detail)
    {
        if (passed && !entry.ExpectedFail)
        {
            result.Passed++;
            Console.WriteLine($"  PASS  {entry.Path}");
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
