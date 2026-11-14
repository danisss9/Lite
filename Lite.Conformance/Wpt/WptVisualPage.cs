using System.Text.Json.Nodes;
using Lite;
using Lite.Conformance.Harness;
using Lite.Extensions;
using Lite.Layout;
using Lite.Platform;
using SkiaSharp;

namespace Lite.Conformance.Wpt;

/// <summary>Load/paint readiness shared by the pinned WPT reftest and crashtest protocols.</summary>
internal static class WptVisualPage
{
    /// <summary>A session carrying the case's user style sheet: a case whose flags declare
    /// <c>userstyle</c> states as its PREREQUISITE that a named .css file is enabled as the
    /// user agent's user style sheet — the suite's own per-case prerequisite sheet, loaded
    /// through the same UserStyleSheet channel the UA conformance requirement (§conform.html)
    /// provides. Any other case returns null (the engine's default session). Shared by the
    /// WPT visual pages and the official-suite render path (the official html4 userstyle
    /// family carries the same flags and PREREQUISITE link).</summary>
    internal static Lite.Network.BrowserSession? UserStyleSession(string path)
    {
        // The official-suite prefix branch of ResolveFile matches "/css21-official/..." with a
        // leading slash — catalog paths arrive without one (WPT paths resolve through the
        // vendor/wpt root either way), so normalize before resolving.
        var file = ConformanceServer.ResolveFile("/" + path.TrimStart('/'));
        if (file is null) return null;
        string text;
        try { text = File.ReadAllText(file); }
        catch (IOException) { return null; }
        var flags = System.Text.RegularExpressions.Regex.Match(
            text, "<meta\\s+name=[\"']flags[\"']\\s+content=[\"']([^\"']*)[\"']",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!flags.Success || !flags.Groups[1].Value
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Contains("userstyle"))
            return null;
        var href = System.Text.RegularExpressions.Regex.Match(text, "<a href=[\"']([^\"']+\\.css)[\"']");
        if (!href.Success) return null;
        var sheetUrl = new Uri(new Uri("http://harness.invalid/" + path.Replace('\\', '/')), href.Groups[1].Value).AbsolutePath;
        var sheetFile = ConformanceServer.ResolveFile(sheetUrl);
        if (sheetFile is null) return null;
        return new Lite.Network.BrowserSession { UserStyleSheet = File.ReadAllText(sheetFile) };
    }

    internal static (SKBitmap Bitmap, Lite.Models.LayoutNode Root, SKBitmap? Mid, SKBitmap? Before,
        IReadOnlyList<Lite.Rendering.PaintLog.Entry> Paint) Render(string path, string? viewport = null,
        string waitClass = "reftest-wait", Css21.ScrollSpec? elementScroll = null,
        Func<Lite.Models.LayoutNode, (float ScrollX, float ScrollY)>? pageScroll = null,
        bool testDriver = false, float scale = 1f)
    {
        var width = 800;
        var height = 600;
        if (viewport is not null)
        {
            var size = viewport.Split('x');
            if (size.Length != 2 || !int.TryParse(size[0], out width) || !int.TryParse(size[1], out height) || width <= 0 || height <= 0)
                throw new InvalidDataException("Invalid WPT viewport.");
        }
        var url = ConformanceServer.TestUrl(path);
        Lite.Models.LayoutNode root;
        Scripting.JsEngine engine;
        if (testDriver)
        {
            // A testdriver visual page drives the real input pipeline before its snapshot:
            // load through a headless BrowserWindow with the bridge registered, then run the
            // same quiescence protocol below on the window's root.
            var window = new BrowserWindow(url, width: width, height: height);
            engine = window.RunHeadless(new HeadlessHost()) ??
                throw new InvalidDataException($"WPT document failed to load: {path}.");
            TestDriverBridge.Register(engine, window);
            root = window.RootNode ?? throw new InvalidDataException($"WPT document has no root: {path}.");
        }
        else
        {
            (root, engine) = HeadlessPage.Load(url, width, height, UserStyleSession(path));
        }
        var document = engine.DocumentState.Document;
        if (document is null || (int)document.StatusCode is < 200 or >= 300)
            throw new InvalidDataException($"WPT document failed to load: {path} ({document?.StatusCode}).");
        if (!HeadlessPage.PumpUntil(engine, () => engine.DocumentReadyState == "complete"))
            throw new TimeoutException("WPT document did not finish loading.");

        // Mirrors the two animation-frame waits in the pinned executors/test-wait.js.
        HeadlessPage.PumpFrames(engine, 2);
        using (Lite.Drawer.DrawToBitmap(width, height, root, new Viewport { ViewportHeight = height }, scale)) { }
        bool Waiting() => root.Attributes.GetValueOrDefault("class", "")
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Contains(waitClass);
        if (Waiting())
            engine.RawEngine.Execute("document.documentElement.dispatchEvent(new Event('TestRendered', {bubbles:true}));");
        if (!HeadlessPage.PumpUntil(engine, () => !Waiting()))
            throw new TimeoutException($"WPT document did not clear {waitClass}.");
        HeadlessPage.PumpFrames(engine, 2);
        engine.FlushMicrotasksTree();
        (SKBitmap, IReadOnlyList<Lite.Rendering.PaintLog.Entry>) Draw()
        {
            List<Lite.Rendering.PaintLog.Entry> paint;
            Lite.Rendering.PaintLog.Begin();
            try { var bmp = Lite.Drawer.DrawToBitmap(width, height, root, new Viewport { ViewportHeight = height }, scale); paint = Lite.Rendering.PaintLog.End(); return (bmp, paint); }
            catch { Lite.Rendering.PaintLog.End(); throw; }
        }
        if (elementScroll is null && pageScroll is null)
        {
            var (plain, plainPaint) = Draw();
            // Diagnostic: the walk-context paint sequence (LITE_RENDER_PAINTLOG=1) — the
            // --render route serves .xht as HTML while the walk serves it as XML, so font
            // and line-shape questions need the walk's own paint trace.
            if (Environment.GetEnvironmentVariable("LITE_RENDER_PAINTLOG") is "1")
                foreach (var e in plainPaint)
                    Console.WriteLine($"[PAINT] '{(e.Text.Length > 44 ? e.Text[..44] + "…" : e.Text)}' x={e.X:F1} y={e.BaselineY:F1} face={e.Typeface} size={e.Size:F1} bold={e.Bold} italic={e.Italic}");
            return (plain, root, null, null, plainPaint);
        }

        // Two-render protocols (e.g. a stated condition about the scrolled state): capture
        // the initial render, apply the scroll phase(s) to the layout, and render each state.
        var scroll = elementScroll;
        var (beforeBmp, beforePaint) = Draw();
        var before = beforeBmp;
        SKBitmap? mid = null;
        if (scroll is not null)
        {
            Css21.VisualChecks.ApplyScroll(new Css21.ScrollSpec(scroll.To), root);
            HeadlessPage.PumpFrames(engine, 2);
            (mid, _) = Draw();
            if (scroll.Then is { } then)
            {
                Css21.VisualChecks.ApplyScroll(new Css21.ScrollSpec(then), root);
                HeadlessPage.PumpFrames(engine, 2);
            }
        }
        var (pageX, pageY) = pageScroll is { } ps ? ps(root) : (0f, 0f);
        var documentWidth = root.Box.BorderBox.Right;
        var documentHeight = root.Box.BorderBox.Bottom;
        var scrolledViewport = new Viewport { ViewportHeight = height, ContentHeight = documentHeight, ContentWidth = documentWidth };
        if (pageY > 0 || pageX > 0)
        {
            scrolledViewport.ScrollTo(pageY);
            scrolledViewport.ScrollXTo(pageX);
        }
        List<Lite.Rendering.PaintLog.Entry> paint;
        Lite.Rendering.PaintLog.Begin();
        try
        {
            var finalBmp = Lite.Drawer.DrawToBitmap(width, height, root, scrolledViewport, scale);
            paint = Lite.Rendering.PaintLog.End();
            return (finalBmp, root, mid, before, paint);
        }
        catch { Lite.Rendering.PaintLog.End(); throw; }
    }

    internal static WptRunner.RunResult RunCrash(WptCase test)
    {
        var (scale, scaleNote) = DeviceScale(test.Options);
        try
        {
            var (bitmap, _, _, _, _) = Render(test.Path, test.Options?["viewport_size"]?.GetValue<string>(), "test-wait",
                testDriver: test.TestDriver, scale: scale);
            using (bitmap)
            {
                var assertion = "Document loaded and rendered without a crash" + scaleNote;
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
    /// machine-checked stated condition instead of the scan alone. <paramref name="statedCheck"/>'s
    /// scroll protocol, when present, renders the initial state, scrolls the overflow box, and
    /// renders again — the stated condition then judges the scrolled render against the initial
    /// one (before it is handed to the check as <c>before</c>).</summary>
    internal static WptRunner.RunResult RunSelfTest(WptCase test, bool embeddedRequired = false,
        Func<Css21.RenderSet, Lite.Models.LayoutNode, WptRunner.RunResult>? statedCondition = null,
        Css21.VisualCheck? statedCheck = null)
    {
        if (test.TestDriver)
            return new(WptRunner.Cat.Unsupported, "Self-test requires test-driver support", 0, 0);
        var (scale, _) = DeviceScale(test.Options);
        try
        {
            var scroll = statedCheck?.Scroll;
            var pageTarget = scroll?.To is { } to && to.StartsWith("page-", StringComparison.Ordinal) ? to : null;
            var width = 800;
            var height = 600;
            if (test.Options?["viewport_size"]?.GetValue<string>() is { } vp)
            {
                var size = vp.Split('x');
                if (size.Length == 2 && int.TryParse(size[0], out var w) && int.TryParse(size[1], out var h) && w > 0 && h > 0)
                { width = w; height = h; }
            }
            var pageScroll = pageTarget is null
                ? null
                : (Func<Lite.Models.LayoutNode, (float, float)>)(root => Css21.VisualChecks.PageScrollExtent(pageTarget, root, width, height));
            var (bitmap, root, mid, before, paint) = Render(test.Path, test.Options?["viewport_size"]?.GetValue<string>(),
                elementScroll: pageTarget is null ? scroll : null, pageScroll: pageScroll, scale: scale);
            var renders = new Css21.RenderSet(bitmap, mid, before, paint);
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
                    var stated = statedCondition(renders, root);
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
                        // The canvas color is whatever the page paints (outline-color-029 sets a
                        // black body), so it is measured, not assumed white.
                        var page = DominantColor(bitmap, new SKRectI(0, 0, bitmap.Width, bitmap.Height));
                        var refColor = DominantColor(bitmap, rb, page);
                        var testColor = DominantColor(bitmap, tb, refColor == page ? null : page);
                        // One level of slack per channel: CSS 2.1 leaves percentage-to-integer
                        // rounding undefined and the suite encodes both choices (background-color-052's
                        // swatch is #7f7f7f for 50%; colors-001 requires 50% to be #80).
                        if (Math.Abs(testColor.Red - refColor.Red) <= 1 && Math.Abs(testColor.Green - refColor.Green) <= 1 &&
                            Math.Abs(testColor.Blue - refColor.Blue) <= 1)
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

    /// <summary>The device scale a case's DPI options request plus the evidence note naming
    /// what was honored: <c>dpr</c> (device pixel ratio) wins over <c>dpi</c> (dots per inch,
    /// 96 = 1), and either accepts the manifest's number or string spelling. Honoring renders
    /// at scale× the CSS viewport with layout unchanged — the replacement for the former DPI
    /// execution refusal — and a pathological value clamps into a sane range rather than
    /// producing a zero or absurd bitmap.</summary>
    internal static (float Scale, string Note) DeviceScale(JsonObject? options)
    {
        if (options is null) return (1f, "");
        if (TryPositiveNumber(options, "dpr", out var dpr)) return (Clamp(dpr), $" (dpr={Format(dpr)})");
        if (TryPositiveNumber(options, "dpi", out var dpi)) return (Clamp(dpi / 96.0), $" (dpi={Format(dpi)})");
        return (1f, "");

        static float Clamp(double value) => (float)Math.Clamp(value, 0.1, 8.0);
        static string Format(double value) => value == Math.Floor(value) ? ((int)value).ToString() : value.ToString("0.0#");
    }

    private static bool TryPositiveNumber(JsonObject options, string key, out double value)
    {
        value = 0;
        if (options[key] is not JsonValue node) return false;
        // A manifest-parsed option is JsonElement-backed; a synthetic case's is a boxed CLR
        // primitive whose TryGetValue<double> fails — read both through the node's JSON text.
        if (node.GetValueKind() == System.Text.Json.JsonValueKind.Number)
            return double.TryParse(node.ToJsonString(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out value) && Positive(value);
        if (node.GetValueKind() == System.Text.Json.JsonValueKind.String)
            return double.TryParse(node.GetValue<string>(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out value) && Positive(value);
        return false;

        static bool Positive(double v) => double.IsFinite(v) && v > 0;
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
