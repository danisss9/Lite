using Lite.Conformance.Css21;
using Lite.Conformance.Harness;
using SkiaSharp;

namespace Lite.Conformance.Acid;

/// <summary>
/// Renders the Acid1/Acid2 tests at 800x600 and compares against approved baselines in
/// baselines\. With --update-baselines the current render becomes the new baseline
/// (only do this after visually signing off the render in artifacts\).
/// </summary>
internal static class AcidRunner
{
    private const int Width = 800;
    private const int Height = 600;

    private sealed record AcidCase(string Name, string Path, float ScrollY = 0, string? Anchor = null);

    private static readonly AcidCase[] Cases =
    [
        new("acid1", "acid/acid1/test5526c.htm"),
        // Acid2's face is assembled ~2600px down the page; it only appears once you follow the
        // "Take The Acid2 Test" link to #top, which scrolls #top to the top of the viewport.
        new("acid2", "acid/acid2/test.html", Anchor: "top"),
        // position:fixed check — the fixed scalp/backgrounds must not move when the page scrolls
        // 50px past #top while the rest of the face shifts up.
        new("acid2-scrolled", "acid/acid2/test.html", ScrollY: 50, Anchor: "top"),
    ];

    public static int Run(string? filter, bool updateBaselines)
    {
        ConformanceServer.Start();
        var result = new SuiteResult();

        foreach (var acidCase in Cases)
        {
            if (!string.IsNullOrEmpty(filter) &&
                !acidCase.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                continue;

            var sourceFile = ConformanceServer.ResolveFile(acidCase.Path);
            if (sourceFile is null)
            {
                Console.WriteLine($"  SKIP  {acidCase.Name}: {acidCase.Path} not vendored — run scripts\\fetch-tests.ps1");
                continue;
            }

            SKBitmap render;
            try
            {
                render = RefTestRunner.Render(acidCase.Path, Width, Height, acidCase.ScrollY, acidCase.Anchor);
            }
            catch (Exception ex)
            {
                result.Failed++;
                result.Problems.Add($"{acidCase.Name}: render crashed: {ex.Message}");
                Console.WriteLine($"  FAIL  {acidCase.Name}: render crashed: {ex}");
                continue;
            }

            using (render)
            {
                var artifactPath = Path.Combine(ConformancePaths.EnsureArtifacts(), $"{acidCase.Name}-render.png");
                PixelDiff.SavePng(render, artifactPath);

                var baselinePath = Path.Combine(ConformancePaths.Baselines, $"{acidCase.Name}.png");
                if (updateBaselines)
                {
                    Directory.CreateDirectory(ConformancePaths.Baselines);
                    PixelDiff.SavePng(render, baselinePath);
                    Console.WriteLine($"  BASE  {acidCase.Name}: baseline updated from current render");
                    result.Passed++;
                    continue;
                }

                using var baseline = PixelDiff.LoadPng(baselinePath);
                if (baseline is null)
                {
                    result.Failed++;
                    result.Problems.Add($"{acidCase.Name}: no baseline — inspect {artifactPath}, then rerun with --update-baselines");
                    Console.WriteLine($"  FAIL  {acidCase.Name}: no baseline yet (render saved to {artifactPath})");
                    continue;
                }

                var diff = PixelDiff.Compare(baseline, render);
                if (diff.Match)
                {
                    result.Passed++;
                    Console.WriteLine($"  PASS  {acidCase.Name}");
                }
                else
                {
                    result.Failed++;
                    PixelDiff.WriteFailureArtifacts(acidCase.Name, baseline, render);
                    var detail = diff.Detail ?? $"{diff.DiffPixels} differing pixels ({diff.DiffRatio:P3})";
                    result.Problems.Add($"{acidCase.Name}: {detail}");
                    Console.WriteLine($"  FAIL  {acidCase.Name}: {detail}");
                }
            }
        }

        return result.Report("acid");
    }
}
