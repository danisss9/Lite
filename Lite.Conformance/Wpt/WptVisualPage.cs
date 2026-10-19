using Lite.Conformance.Harness;
using Lite.Extensions;
using Lite.Layout;
using SkiaSharp;

namespace Lite.Conformance.Wpt;

/// <summary>Load/paint readiness shared by the pinned WPT reftest and crashtest protocols.</summary>
internal static class WptVisualPage
{
    internal static (SKBitmap Bitmap, Lite.Models.LayoutNode Root) Render(string path, string? viewport = null, string waitClass = "reftest-wait")
    {
        var width = 800;
        var height = 600;
        if (viewport is not null)
        {
            var size = viewport.Split('x');
            if (size.Length != 2 || !int.TryParse(size[0], out width) || !int.TryParse(size[1], out height) || width <= 0 || height <= 0)
                throw new InvalidDataException("Invalid WPT viewport.");
        }
        var (root, engine) = HeadlessPage.Load(ConformanceServer.TestUrl(path), width, height);
        var document = engine.DocumentState.Document;
        if (document is null || (int)document.StatusCode is < 200 or >= 300)
            throw new InvalidDataException($"WPT document failed to load: {path} ({document?.StatusCode}).");
        if (!HeadlessPage.PumpUntil(engine, () => engine.DocumentReadyState == "complete"))
            throw new TimeoutException("WPT document did not finish loading.");

        // Mirrors the two animation-frame waits in the pinned executors/test-wait.js.
        HeadlessPage.PumpFrames(engine, 2);
        using (Lite.Drawer.DrawToBitmap(width, height, root, new Viewport { ViewportHeight = height })) { }
        bool Waiting() => root.Attributes.GetValueOrDefault("class", "")
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Contains(waitClass);
        if (Waiting())
            engine.RawEngine.Execute("document.documentElement.dispatchEvent(new Event('TestRendered', {bubbles:true}));");
        if (!HeadlessPage.PumpUntil(engine, () => !Waiting()))
            throw new TimeoutException($"WPT document did not clear {waitClass}.");
        HeadlessPage.PumpFrames(engine, 2);
        engine.FlushMicrotasksTree();
        return (Lite.Drawer.DrawToBitmap(width, height, root, new Viewport { ViewportHeight = height }), root);
    }

    internal static WptRunner.RunResult RunCrash(WptCase test)
    {
        if (test.TestDriver || test.Options?.ContainsKey("dpi") == true)
            return new(WptRunner.Cat.Unsupported, "Crashtest requires test-driver or DPI support", 0, 0);
        try
        {
            var (bitmap, _) = Render(test.Path, test.Options?["viewport_size"]?.GetValue<string>(), "test-wait");
            using (bitmap)
            {
                const string assertion = "Document loaded and rendered without a crash";
                return new(WptRunner.Cat.Pass, assertion, 1, 0, [new(assertion, 0, null)], 0);
            }
        }
        catch (TimeoutException ex) { return new(WptRunner.Cat.Timeout, ex.Message, 0, 0); }
        catch (Exception ex) { return new(WptRunner.Cat.Crash, WptVisualPage.CrashDetail(ex), 0, 0); }
    }

    /// <summary>Official CSS 2.1 suite self-describing tests state that, unless the test
    /// says otherwise, any red on the page means failure. The primary route is the no-red
    /// scan counting only exact #ff0000 — the suite's failure red, from which the
    /// "maximum minus one" expected colors (#fe0000) are deliberately distinguishable. When
    /// failure red IS detected, the embedded-reference check arbitrates: the suite's swatch
    /// selftests carry a #test/#reference pair whose assertion is "the boxes are the same
    /// color", so a dominant-color match over the rendered boxes validates the test — the
    /// only route that can pass the -012 family, whose expected color IS #ff0000. Both
    /// routes are diagnostic signals: readiness requires reviewed evidence per case.
    /// <paramref name="embeddedRequired"/> runs the embedded-reference check even without
    /// red, for cases whose stated condition is the swatch pair's colour match.
    /// <paramref name="statedCondition"/>, when set, decides a red-free render by the case's
    /// machine-checked stated condition instead of the scan alone.</summary>
    internal static WptRunner.RunResult RunSelfTest(WptCase test, bool embeddedRequired = false,
        Func<SKBitmap, Lite.Models.LayoutNode, WptRunner.RunResult>? statedCondition = null)
    {
        if (test.TestDriver || test.Options?.ContainsKey("dpi") == true)
            return new(WptRunner.Cat.Unsupported, "Self-test requires test-driver or DPI support", 0, 0);
        try
        {
            var (bitmap, root) = Render(test.Path, test.Options?["viewport_size"]?.GetValue<string>());
            using (bitmap)
            {
                // The suite's Ahem font-requirement notice ("Ahem_font_required_for_this_test.")
                // is a red element BY DESIGN (white text on a red background) — it states the
                // test's font dependency, not a failure. Exclude its box from the red scan.
                var notices = new List<SKRectI>();
                CollectNoticeBoxes(root, notices);

                int red = 0;
                var pixels = bitmap.Pixels;
                var bitmapWidth = bitmap.Width;
                for (int i = 0; i < pixels.Length; i++)
                {
                    var pixel = pixels[i];
                    if (pixel.Red == 255 && pixel.Green == 0 && pixel.Blue == 0)
                    {
                        int x = i % bitmapWidth, y = i / bitmapWidth;
                        bool inNotice = false;
                        foreach (var n in notices)
                        {
                            if (n.Contains(x, y)) { inNotice = true; break; }
                        }
                        if (!inNotice) red++;
                    }
                }
                const string assertion = "Self-describing test shows no failure red (diagnostic)";
                if (red == 0 && statedCondition is not null)
                {
                    var stated = statedCondition(bitmap, root);
                    var subtests = new List<SubtestEvidence> { new(assertion, 0, null) };
                    subtests.AddRange(stated.Subtests ?? []);
                    return stated with { Detail = $"{assertion}; {stated.Detail}", Total = subtests.Count, Subtests = subtests };
                }
                if (red == 0 && !embeddedRequired)
                    return new(WptRunner.Cat.Pass, assertion, 1, 0, [new(assertion, 0, null)], 0);

                // Failure red present (or a "same color" case, which the scan alone cannot
                // decide) — the suite's own #test/#reference pair arbitrates when
                // one exists (the outline selftests name their swatch #div1). The reference's
                // dominant color is the asserted color; the test's is its dominant color
                // EXCLUDING the page background (a border-side swatch's border box is half
                // white content, half colored border), over the region the swatch paints —
                // an outline paints OUTSIDE the border box, so its width inflates the crop.
                var testNode = FindBoxById(root, "test", out var tbox)
                    ?? FindBoxById(root, "div1", out tbox);
                var refNode = FindBoxById(root, "reference", out var rbox);
                if (testNode is not null && refNode is not null &&
                    tbox is { } tb && rbox is { } rb)
                {
                    // An outline paints OUTSIDE the border box (a 0x0 swatch is all outline),
                    // so the crop inflates by the outline width before the size check.
                    var outline = testNode.GetOutlineStyle() is not (BorderStyle.None or BorderStyle.Hidden)
                        ? (int)MathF.Ceiling(testNode.GetOutlineWidth()) : 0;
                    if (outline > 0)
                        tb = new SKRectI(tb.Left - outline, tb.Top - outline, tb.Right + outline, tb.Bottom + outline);
                    if (tb.Width > 1 && tb.Height > 1 && rb.Width > 1 && rb.Height > 1)
                    {
                        // The reference's box may WRAP its swatch (an <img> swatch under a <br>
                        // inside a full-width div): the page background must be excluded on this
                        // side too, or the wrapping whitespace dominates the count and every
                        // image-swatch selftest reads white (background-color-012/035/076+).
                        var refColor = DominantColor(bitmap, rb, PageBackground);
                        var testColor = DominantColor(bitmap, tb, refColor == PageBackground ? null : PageBackground);
                        if (testColor == refColor)
                        {
                            const string pass = "Self-describing test: #test renders the same color as #reference";
                            return new(WptRunner.Cat.Pass, pass, 1, 0, [new(pass, 0, null)], 0);
                        }
                        const string fail = "Self-describing test: #test and #reference render different colors";
                        return new(WptRunner.Cat.Fail, fail, 1, 1,
                            [new(fail, 1, $"#test {testColor} vs #reference {refColor}")], 0);
                    }
                }

                if (red == 0)
                    return new(WptRunner.Cat.Unsupported, "Same-color pass condition without an embedded #test/#reference pair", 0, 0);
                return new(WptRunner.Cat.Fail, $"{red} red pixels (diagnostic)", 1, 1, [new(assertion, 1, $"{red} red pixels")], 0);
            }
        }
        catch (TimeoutException ex) { return new(WptRunner.Cat.Timeout, ex.Message, 0, 0); }
        catch (Exception ex) { return new(WptRunner.Cat.Crash, WptVisualPage.CrashDetail(ex), 0, 0); }
    }

    /// <summary>Crash evidence names the innermost exception and where it was thrown: an
    /// AggregateException's own message ("One or more errors occurred") says nothing.</summary>
    internal static string CrashDetail(Exception ex)
    {
        var inner = ex;
        while (inner is AggregateException { InnerExceptions.Count: 1 } aggregate) inner = aggregate.InnerExceptions[0];
        while (inner.InnerException is { } next && inner is not AggregateException) inner = next;
        // A cancelled async task's own trace may hold no Lite frame; the outer exception's
        // trace then names the synchronous wait that surfaced it.
        static string? LiteFrame(Exception e) => e.StackTrace?.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(f => f.Contains(" Lite.", StringComparison.Ordinal));
        var frame = LiteFrame(inner) ?? LiteFrame(ex) ?? "";
        return $"{inner.GetType().Name}: {inner.Message}" + (frame.Length > 0 ? $" ({frame})" : "");
    }

    private static readonly SKColor PageBackground = new(255, 255, 255);

    /// <summary>Collects the border boxes of the suite's standard Ahem font-requirement
    /// notice (<c>Ahem_font_required_for_this_test.</c>, white on red) — a red element by
    /// design that the no-red scan must ignore.</summary>
    private static void CollectNoticeBoxes(Lite.Models.LayoutNode node, List<SKRectI> boxes)
    {
        if (node.DisplayText.Contains("Ahem_font_required_for_this_test", StringComparison.Ordinal))
        {
            var b = node.Box.BorderBox;
            boxes.Add(new SKRectI((int)b.Left, (int)b.Top, (int)b.Right, (int)b.Bottom));
        }
        foreach (var child in node.Children) CollectNoticeBoxes(child, boxes);
    }

    private static Lite.Models.LayoutNode? FindBoxById(Lite.Models.LayoutNode node, string id, out SKRectI? box)
    {
        box = null;
        if (node.Attributes.GetValueOrDefault("id") == id)
        {
            var b = node.Box.BorderBox;
            // Zero-size boxes are returned too: an outline swatch is a 0x0 box whose paint
            // region exists only after the caller inflates it by the outline width.
            box = new SKRectI((int)b.Left, (int)b.Top, (int)b.Right, (int)b.Bottom);
            return node;
        }
        foreach (var child in node.Children)
            if (FindBoxById(child, id, out var childBox) is { } hit) { box = childBox; return hit; }
        return null;
    }

    /// <summary>The most frequent color in a crop — the suite's swatches are solid, so the
    /// mode is the asserted color (antialiased edge pixels are a small minority).
    /// <paramref name="exclude"/>, when set, drops one color from the count (the page
    /// background that fills the empty half of a border-side swatch's border box).</summary>
    private static SKColor DominantColor(SKBitmap bitmap, SKRectI crop, SKColor? exclude = null)
    {
        crop = SKRectI.Intersect(crop, new SKRectI(0, 0, bitmap.Width, bitmap.Height));
        if (crop.IsEmpty) return default;
        var counts = new Dictionary<int, int>();
        int excludeKey = exclude.HasValue ? (exclude.Value.Red << 16) | (exclude.Value.Green << 8) | exclude.Value.Blue : -1;
        for (int y = crop.Top; y < crop.Bottom; y++)
            for (int x = crop.Left; x < crop.Right; x++)
            {
                var c = bitmap.GetPixel(x, y);
                var key = (c.Red << 16) | (c.Green << 8) | c.Blue;
                if (key == excludeKey) continue;
                counts[key] = counts.GetValueOrDefault(key) + 1;
            }
        // A crop painted entirely in the excluded color (an empty swatch) IS that color.
        if (counts.Count == 0) return exclude ?? default;
        var best = counts.OrderByDescending(kv => kv.Value).First();
        return new SKColor((byte)(best.Key >> 16), (byte)(best.Key >> 8), (byte)best.Key);
    }
}
