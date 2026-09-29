using Lite.Conformance.Harness;
using Lite.Layout;
using SkiaSharp;

namespace Lite.Conformance.Wpt;

/// <summary>Load/paint readiness shared by the pinned WPT reftest and crashtest protocols.</summary>
internal static class WptVisualPage
{
    internal static SKBitmap Render(string path, string? viewport = null, string waitClass = "reftest-wait")
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
        return Lite.Drawer.DrawToBitmap(width, height, root, new Viewport { ViewportHeight = height });
    }

    internal static WptRunner.RunResult RunCrash(WptCase test)
    {
        if (test.TestDriver || test.Options?.ContainsKey("dpi") == true)
            return new(WptRunner.Cat.Unsupported, "Crashtest requires test-driver or DPI support", 0, 0);
        try
        {
            using var bitmap = Render(test.Path, test.Options?["viewport_size"]?.GetValue<string>(), "test-wait");
            const string assertion = "Document loaded and rendered without a crash";
            return new(WptRunner.Cat.Pass, assertion, 1, 0, [new(assertion, 0, null)], 0);
        }
        catch (TimeoutException ex) { return new(WptRunner.Cat.Timeout, ex.Message, 0, 0); }
        catch (Exception ex) { return new(WptRunner.Cat.Crash, ex.Message, 0, 0); }
    }

    /// <summary>Official CSS 2.1 suite self-describing tests state that, unless the test
    /// says otherwise, any red on the page means failure. The no-red scan is a diagnostic
    /// signal only — readiness requires reviewed evidence per case, never this heuristic
    /// (red can legitimately appear in a passing rendering of some tests).</summary>
    internal static WptRunner.RunResult RunSelfTest(WptCase test)
    {
        if (test.TestDriver || test.Options?.ContainsKey("dpi") == true)
            return new(WptRunner.Cat.Unsupported, "Self-test requires test-driver or DPI support", 0, 0);
        try
        {
            using var bitmap = Render(test.Path, test.Options?["viewport_size"]?.GetValue<string>());
            int red = 0;
            foreach (var pixel in bitmap.Pixels)
                if (pixel.Red >= 200 && pixel.Green <= 80 && pixel.Blue <= 80) red++;
            const string assertion = "Self-describing test shows no failure red (diagnostic)";
            return red == 0
                ? new(WptRunner.Cat.Pass, assertion, 1, 0, [new(assertion, 0, null)], 0)
                : new(WptRunner.Cat.Fail, $"{red} red pixels (diagnostic)", 1, 1, [new(assertion, 1, $"{red} red pixels")], 0);
        }
        catch (TimeoutException ex) { return new(WptRunner.Cat.Timeout, ex.Message, 0, 0); }
        catch (Exception ex) { return new(WptRunner.Cat.Crash, ex.Message, 0, 0); }
    }
}
