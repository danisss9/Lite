using SkiaSharp;

namespace Lite.Conformance.Harness;

internal sealed record PixelDiffResult(bool Match, int DiffPixels, int TotalPixels, string? Detail)
{
    public double DiffRatio => TotalPixels == 0 ? 0 : (double)DiffPixels / TotalPixels;
}

/// <summary>Pixel comparison for reftests and Acid baselines.</summary>
internal static class PixelDiff
{
    /// <summary>Per-channel tolerance for anti-aliasing wobble.</summary>
    public const byte ChannelTolerance = 3;

    /// <summary>Fraction of pixels allowed to differ (anti-aliased edges).</summary>
    public const double DiffPixelBudget = 0.001; // 0.1%

    public static PixelDiffResult Compare(SKBitmap expected, SKBitmap actual,
        byte tolerance = ChannelTolerance, double budget = DiffPixelBudget)
    {
        if (expected.Width != actual.Width || expected.Height != actual.Height)
            return new PixelDiffResult(false, 0, 0,
                $"size mismatch: expected {expected.Width}x{expected.Height}, actual {actual.Width}x{actual.Height}");

        var expectedPixels = expected.Pixels;
        var actualPixels = actual.Pixels;
        int diff = 0;
        for (int i = 0; i < expectedPixels.Length; i++)
        {
            var e = expectedPixels[i];
            var a = actualPixels[i];
            if (Math.Abs(e.Red - a.Red) > tolerance ||
                Math.Abs(e.Green - a.Green) > tolerance ||
                Math.Abs(e.Blue - a.Blue) > tolerance ||
                Math.Abs(e.Alpha - a.Alpha) > tolerance)
                diff++;
        }

        bool match = (double)diff / expectedPixels.Length <= budget;
        return new PixelDiffResult(match, diff, expectedPixels.Length, null);
    }

    /// <summary>Writes expected/actual/diff PNGs into the artifacts folder for triage.</summary>
    public static void WriteFailureArtifacts(string name, SKBitmap expected, SKBitmap actual)
    {
        var dir = ConformancePaths.EnsureArtifacts();
        SavePng(expected, Path.Combine(dir, $"{name}-expected.png"));
        SavePng(actual, Path.Combine(dir, $"{name}-actual.png"));

        if (expected.Width == actual.Width && expected.Height == actual.Height)
        {
            using var diff = new SKBitmap(expected.Width, expected.Height);
            var ep = expected.Pixels;
            var ap = actual.Pixels;
            var dp = new SKColor[ep.Length];
            for (int i = 0; i < ep.Length; i++)
            {
                bool same = Math.Abs(ep[i].Red - ap[i].Red) <= ChannelTolerance &&
                            Math.Abs(ep[i].Green - ap[i].Green) <= ChannelTolerance &&
                            Math.Abs(ep[i].Blue - ap[i].Blue) <= ChannelTolerance &&
                            Math.Abs(ep[i].Alpha - ap[i].Alpha) <= ChannelTolerance;
                dp[i] = same ? new SKColor(255, 255, 255) : new SKColor(255, 0, 0);
            }
            diff.Pixels = dp;
            SavePng(diff, Path.Combine(dir, $"{name}-diff.png"));
        }
    }

    public static void SavePng(SKBitmap bitmap, string path)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data.SaveTo(stream);
    }

    public static SKBitmap? LoadPng(string path)
    {
        if (!File.Exists(path)) return null;
        return SKBitmap.Decode(path);
    }
}
