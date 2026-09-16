using Lite.Conformance.Harness;
using Lite.Layout;
using SkiaSharp;

namespace Lite.Conformance.Wpt;

/// <summary>Implements the locked wptrunner reference graph algorithm; unsupported metadata blocks evidence.</summary>
internal static class WptRefTestRunner
{
    internal static WptRunner.RunResult Run(WptCase test, Func<string, WptCase?> lookup)
    {
        var images = new Dictionary<string, SKBitmap>(StringComparer.Ordinal);
        var artifacts = new List<EvidenceArtifact>();
        var pending = new Stack<WptCase>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        pending.Push(test);
        try
        {
            while (pending.TryPop(out var current))
            {
                if (!visited.Add(current.Path)) continue;
                if (current.TestDriver || current.Options?.ContainsKey("fuzzy") == true || current.Options?.ContainsKey("dpi") == true)
                    return new(WptRunner.Cat.Unsupported, "Reference requires test-driver, fuzzy or DPI support", 0, 0);
                foreach (var reference in current.References)
                {
                    var relative = new Uri(new Uri("http://wpt.invalid/" + current.Path), reference.Url);
                    if (relative.GetLeftPart(UriPartial.Authority) != "http://wpt.invalid")
                        return new(WptRunner.Cat.Unsupported, "External reference origin requires explicit serving configuration", 0, 0);
                    var targetPath = (relative.PathAndQuery + relative.Fragment).TrimStart('/');
                    var target = lookup(targetPath);
                    if (target?.TestDriver == true || target?.Options?.ContainsKey("dpi") == true)
                        return new(WptRunner.Cat.Unsupported, "Reference target requires test-driver or DPI support", 0, 0);
                    // The pinned runner uses the root test's viewport for the entire graph.
                    var left = Render(current.Path);
                    var right = Render(targetPath);
                    var equal = PixelDiff.Compare(right, left, tolerance: 0, budget: 0).Match;
                    if (reference.Relation == "==" ? equal : !equal)
                    {
                        if (target is { References.Count: > 0 }) pending.Push(target);
                        else return Result(true, "Reference graph has a passing path");
                    }
                    else
                    {
                        var name = "wpt-reference-" + Guid.NewGuid().ToString("N");
                        PixelDiff.WriteFailureArtifacts(name, right, left, tolerance: 0);
                        foreach (var suffix in new[] { "expected", "actual", "diff" })
                        {
                            var file = Path.Combine(ConformancePaths.EnsureArtifacts(), $"{name}-{suffix}.png");
                            if (File.Exists(file)) artifacts.Add(ExecutionEvidence.Artifact(file, suffix));
                        }
                    }
                }
            }
            return Result(false, "No reference path passed");
        }
        catch (TimeoutException ex) { return new(WptRunner.Cat.Timeout, ex.Message, 0, 0, Artifacts: artifacts); }
        catch (Exception ex) { return new(WptRunner.Cat.Crash, ex.Message, 0, 0, Artifacts: artifacts); }
        finally { foreach (var image in images.Values) image.Dispose(); }

        WptRunner.RunResult Result(bool pass, string detail) => new(pass ? WptRunner.Cat.Pass : WptRunner.Cat.Fail, detail, 1,
            pass ? 0 : 1, [new("Reference graph has a passing path", pass ? 0 : 1, detail)], 0, artifacts);

        SKBitmap Render(string path)
        {
            if (images.TryGetValue(path, out var bitmap)) return bitmap;
            var width = 800;
            var height = 600;
            if (test.Options?["viewport_size"]?.GetValue<string>() is { } viewport)
            {
                var size = viewport.Split('x');
                if (size.Length != 2 || !int.TryParse(size[0], out width) || !int.TryParse(size[1], out height) || width <= 0 || height <= 0)
                    throw new InvalidDataException("Invalid reference viewport.");
            }
            var (root, engine) = HeadlessPage.Load(ConformanceServer.TestUrl(path), width, height);
            var document = engine.DocumentState.Document;
            if (document is null || (int)document.StatusCode is < 200 or >= 300)
                throw new InvalidDataException($"Reference document failed to load: {path} ({document?.StatusCode}).");
            if (!HeadlessPage.PumpUntil(engine, () => engine.DocumentReadyState == "complete"))
                throw new TimeoutException("Reference document did not finish loading.");
            // Complete the first paint before allowing a delayed test to make its changes.
            using (Lite.Drawer.DrawToBitmap(width, height, root, new Viewport { ViewportHeight = height })) { }
            if (root.Attributes.GetValueOrDefault("class", "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Contains("reftest-wait"))
                engine.RawEngine.Execute("document.documentElement.dispatchEvent(new Event('TestRendered', {bubbles:true}));");
            if (!HeadlessPage.PumpUntil(engine, () => !root.Attributes.GetValueOrDefault("class", "")
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Contains("reftest-wait")))
                throw new TimeoutException("Reference document did not clear reftest-wait.");
            engine.FlushMicrotasks();
            bitmap = Lite.Drawer.DrawToBitmap(width, height, root, new Viewport { ViewportHeight = height });
            images.Add(path, bitmap);
            return bitmap;
        }
    }
}
