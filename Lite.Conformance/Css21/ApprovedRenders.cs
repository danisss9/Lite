using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Lite.Conformance.Harness;
using Lite.Conformance.Wpt;
using SkiaSharp;

namespace Lite.Conformance.Css21;

/// <summary>The approved-render ledger (<c>Css21/css21-approved-renders.json</c>, written only by
/// <c>scripts/review-css21-renders.py</c>): for a self-describing case no machine check can read,
/// a person compared the render with the case's stated condition once and approved it. The
/// harness then decides the case by machine: the final render's RGBA SHA-256 must equal the
/// approved one, on the same platform and viewport. A changed render — any engine change that
/// moves a pixel — drops the case back to manual-pending for re-review; nothing ever passes on
/// a stale approval.</summary>
internal static class ApprovedRenders
{
    internal const string LedgerFile = "Css21/css21-approved-renders.json";

    internal sealed record Entry(string Path, string Platform, string Viewport, string Sha256, string Sentence,
        string Operator, string Date);

    private static Dictionary<string, Entry>? _entries;

    internal static Entry? Find(string path)
    {
        _entries ??= Load();
        return _entries.GetValueOrDefault(path);
    }

    private static Dictionary<string, Entry> Load()
    {
        var file = ConformancePaths.Manifest(LedgerFile);
        if (!File.Exists(file)) return new(StringComparer.Ordinal);
        return JsonNode.Parse(File.ReadAllText(file))!["entries"]!.AsArray().OfType<JsonObject>()
            .Select(e => new Entry(Text(e, "path"), Text(e, "platform"), Text(e, "viewport"), Text(e, "sha256"),
                Text(e, "sentence"), Text(e, "operator"), Text(e, "date")))
            .ToDictionary(e => e.Path, StringComparer.Ordinal);

        static string Text(JsonObject o, string key) => o[key]?.GetValue<string>() ?? "";
    }

    /// <summary>The platform an approval holds on: font rasterization differs between
    /// operating systems, so a render approved on one is not evidence on another.</summary>
    internal static string Platform =>
        (OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux") + "-" +
        RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();

    /// <summary>SHA-256 of the render's pixels as RGBA bytes, row by row (independent of the
    /// bitmap's native channel order).</summary>
    internal static string Hash(SKBitmap bitmap)
    {
        var pixels = bitmap.Pixels;
        var bytes = new byte[pixels.Length * 4];
        for (var i = 0; i < pixels.Length; i++)
        {
            var c = pixels[i];
            bytes[i * 4] = c.Red;
            bytes[i * 4 + 1] = c.Green;
            bytes[i * 4 + 2] = c.Blue;
            bytes[i * 4 + 3] = c.Alpha;
        }
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    /// <summary>Renders each listed case exactly as the self-test route renders it (same server,
    /// fonts and viewport) and writes <c>NNNN.png</c> plus <c>index.json</c> (path, suite,
    /// sentence, viewport, platform, sha256, file) into <paramref name="outDir"/> — the input of
    /// <c>scripts/review-css21-renders.py</c>. Nothing is approved here.</summary>
    internal static int DumpForReview(string listFile, string outDir)
    {
        ConformanceServer.Start(cssRegressionMode: false);
        ConformanceServer.EnsureTestFontsProvisioned();
        Directory.CreateDirectory(outDir);
        var paths = File.ReadAllLines(listFile).Select(l => l.Trim()).Where(l => l.Length > 0).Distinct().ToList();
        var index = new JsonArray();
        var n = 0;
        foreach (var path in paths)
        {
            var test = WptRunner.CatalogCase(path);
            var viewport = test?.Options?["viewport_size"]?.GetValue<string>();
            // The same call WptVisualPage.RunSelfTest makes for a case with no scroll or
            // interaction protocol: device scale and sentence included.
            var (scale, _) = WptVisualPage.DeviceScale(test?.Options);
            try
            {
                var (bitmap, _, mid, before, _, states) = WptVisualPage.Render(path, viewport, scale: scale,
                    sentence: SelfTestOracle.Condition(path).Sentence);
                mid?.Dispose();
                before?.Dispose();
                foreach (var (_, state) in states ?? []) state.Dispose();
                using (bitmap)
                {
                    var file = $"{++n:D4}.png";
                    using (var data = bitmap.Encode(SKEncodedImageFormat.Png, 100))
                        File.WriteAllBytes(Path.Combine(outDir, file), data.ToArray());
                    index.Add(new JsonObject
                    {
                        ["path"] = path,
                        ["suite"] = path.StartsWith("css21-official/", StringComparison.Ordinal) ? "css21-official" : "css21-wpt",
                        ["sentence"] = SelfTestOracle.Condition(path).Sentence,
                        ["viewport"] = $"{bitmap.Width}x{bitmap.Height}",
                        ["platform"] = Platform,
                        ["sha256"] = Hash(bitmap),
                        ["file"] = file,
                    });
                }
                Console.WriteLine($"  rendered {path}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  FAILED   {path}: {ex.Message}");
            }
        }
        File.WriteAllText(Path.Combine(outDir, "index.json"), index.ToJsonString(new() { WriteIndented = true }));
        Console.WriteLine($"{index.Count}/{paths.Count} renders written to {outDir}");
        return index.Count == paths.Count ? 0 : 1;
    }

    /// <summary>Decides a case against its approval: the same platform, the same viewport and
    /// the same pixels pass; anything else is manual-pending for re-review.</summary>
    internal static WptRunner.RunResult Judge(Entry entry, SKBitmap render)
    {
        const string assertion = "The render matches the approved render";
        if (!string.Equals(entry.Platform, Platform, StringComparison.Ordinal))
            return new(WptRunner.Cat.ManualPending,
                $"Approved on {entry.Platform}; a render on {Platform} needs its own review", 0, 0);
        var viewport = $"{render.Width}x{render.Height}";
        if (!string.Equals(entry.Viewport, viewport, StringComparison.Ordinal))
            return new(WptRunner.Cat.ManualPending,
                $"Approved at {entry.Viewport}; this render is {viewport} and needs its own review", 0, 0);
        var hash = Hash(render);
        return string.Equals(hash, entry.Sha256, StringComparison.OrdinalIgnoreCase)
            ? new(WptRunner.Cat.Pass, $"{assertion} (approved by {entry.Operator} on {entry.Date})", 1, 0,
                [new(assertion, 0, null)], 0)
            : new(WptRunner.Cat.ManualPending,
                $"The render drifted from its approved render ({hash[..12]} vs {entry.Sha256[..Math.Min(12, entry.Sha256.Length)]}); re-review", 0, 0);
    }
}
