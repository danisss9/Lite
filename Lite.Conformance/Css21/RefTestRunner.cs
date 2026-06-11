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
            if (entry.Reference is null)
            {
                result.Failed++;
                result.Problems.Add($"{entry.Path}: manifest entry has no reference (use 'test | ref')");
                continue;
            }

            bool passed;
            string detail;
            try
            {
                using var testBitmap = Render(entry.Path);
                using var refBitmap = Render(entry.Reference);
                var diff = PixelDiff.Compare(refBitmap, testBitmap);
                passed = diff.Match;
                detail = diff.Detail ?? $"{diff.DiffPixels} differing pixels ({diff.DiffRatio:P3})";
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
