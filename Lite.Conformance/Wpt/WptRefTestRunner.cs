using Lite.Conformance.Harness;
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
            bitmap = WptVisualPage.Render(path, test.Options?["viewport_size"]?.GetValue<string>());
            images.Add(path, bitmap);
            return bitmap;
        }
    }
}
