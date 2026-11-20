using Lite.Conformance.Harness;
using Lite.Conformance.Wpt;
using Lite.Extensions;
using Lite.Media;
using Lite.Models;
using Lite.Network;
using SkiaSharp;

namespace Lite.Conformance.Css21;

/// <summary>The time protocol for "There should be an animated construction sign below"
/// (background-animated-001): the page is rendered once per frame of the animation's first
/// loop, at the middle of that frame, and each render must show exactly that frame
/// composited over what lies beneath it. An animation that never advances, skips a frame or
/// paints the wrong pixels fails; a page whose animation the check cannot place (none, more
/// than one, a positioned or scaled background) is unsupported, never a pass.</summary>
internal static class AnimatedImageCheck
{
    internal const string Kind = "image-animates";

    /// <summary>The share of the box's pixels allowed to differ: none. The image is unscaled on
    /// whole pixels, and frames of a small animation can differ in only a few pixels, which any
    /// slack would let a frozen render pass. Per-pixel slack is the channel tolerance.</summary>
    private const double MismatchSlack = 0;

    internal sealed record Shown(LayoutNode Node, SKBitmap Surface, SKRectI Area, bool Tiled);

    /// <summary>The page's one painted animation: an element whose background image, or whose
    /// own replaced image, is an animated surface. Null when there is none or more than one.</summary>
    internal static Shown? Find(LayoutNode root)
    {
        var found = new List<Shown>();
        Walk(root, found);
        return found.Count == 1 ? found[0] : null;
    }

    private static void Walk(LayoutNode node, List<Shown> into)
    {
        if (node.Image is { } image && AnimatedImages.IsAnimated(image))
            into.Add(new(node, image, SKRectI.Round(node.Box.ContentBox), Tiled: false));
        else if (node.GetBackgroundImage() is { } url &&
                 ResourceLoader.FetchImage(url, node.DocumentState?.BaseUrl, node.DocumentState?.Session) is { } background &&
                 AnimatedImages.IsAnimated(background))
            into.Add(new(node, background, SKRectI.Round(node.Box.PaddingBox), Tiled: true));
        foreach (var child in node.Children) Walk(child, into);
    }

    /// <summary>The moments the protocol renders: the middle of each frame of the first loop.</summary>
    internal static List<long> SampleTimes(SKBitmap surface)
    {
        var times = new List<long>();
        var start = 0;
        for (var i = 0; AnimatedImages.FrameEnd(surface, i) is { } end; i++)
        {
            times.Add((start + end) / 2);
            start = end;
        }
        return times;
    }

    /// <summary>Judges <paramref name="renders"/>: <c>Final</c> is frame 0, and
    /// <c>States</c> holds one render per later frame, in order.</summary>
    internal static WptRunner.RunResult Judge(RenderSet renders, LayoutNode root)
    {
        const string assertion = "the image animates: each frame of its first loop is shown in turn";
        if (Find(root) is not { } shown)
            return new(WptRunner.Cat.Unsupported, "Visual check: the page shows no single animated image", 0, 0);
        if (shown.Tiled && (shown.Node.GetBackgroundPosition() is not (("0%" or "0" or "0px"), ("0%" or "0" or "0px")) ||
                            shown.Node.GetBackgroundSize() is not ("auto", "auto")))
            return new(WptRunner.Cat.Unsupported, "Visual check: the animated background is positioned or scaled", 0, 0);
        if (!shown.Tiled && (shown.Area.Width != shown.Surface.Width || shown.Area.Height != shown.Surface.Height))
            return new(WptRunner.Cat.Unsupported, "Visual check: the animated image is scaled", 0, 0);
        var times = SampleTimes(shown.Surface);
        var states = renders.States ?? [];
        if (times.Count < 2 || states.Count != times.Count - 1)
            return new(WptRunner.Cat.Unsupported, "Visual check: the time protocol did not render every frame", 0, 0);
        var area = shown.Area;
        area.Intersect(new SKRectI(0, 0, renders.Final.Width, renders.Final.Height));
        if (area.Width < shown.Surface.Width || area.Height < shown.Surface.Height)
            return new(WptRunner.Cat.Unsupported, "Visual check: the animated image is clipped by the viewport", 0, 0);

        // What lies beneath the image: read where every frame is fully transparent.
        if (Beneath(renders.Final, shown, times.Count) is not { } under)
            return new(WptRunner.Cat.Unsupported, "Visual check: no pixel shows what lies beneath the image", 0, 0);

        var subtests = new List<SubtestEvidence>();
        for (var k = 0; k < times.Count; k++)
        {
            var render = k == 0 ? renders.Final : states[k - 1].Bitmap;
            var frame = AnimatedImages.Frame(shown.Surface, k)!;
            var (mismatches, total) = Compare(render, frame, shown, area, under);
            var name = $"frame {k} shown at {times[k]}ms";
            subtests.Add(new(name, mismatches <= total * MismatchSlack ? 0 : 1,
                mismatches <= total * MismatchSlack ? null : $"{mismatches} of {total} pixels differ from frame {k}"));
        }
        var distinct = Enumerable.Range(1, times.Count - 1)
            .Any(k => !SamePixels(AnimatedImages.Frame(shown.Surface, 0)!, AnimatedImages.Frame(shown.Surface, k)!));
        if (!distinct)
            return new(WptRunner.Cat.Unsupported, "Visual check: every frame of the image is identical", 0, 0);
        var failed = subtests.Count(s => s.Status != 0);
        return failed == 0
            ? new(WptRunner.Cat.Pass, $"{assertion} ({times.Count} frames)", subtests.Count, 0, subtests, 0)
            : new(WptRunner.Cat.Fail, $"{assertion}: {failed} of {times.Count} frames not shown", subtests.Count, failed, subtests, 0);
    }

    private static SKColor? Beneath(SKBitmap render, Shown shown, int frames)
    {
        var w = shown.Surface.Width;
        var h = shown.Surface.Height;
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var clear = true;
                for (var k = 0; k < frames && clear; k++)
                    clear = AnimatedImages.Frame(shown.Surface, k)!.GetPixel(x, y).Alpha == 0;
                if (clear) return render.GetPixel(shown.Area.Left + x, shown.Area.Top + y);
            }
        return null;
    }

    private static (int Mismatches, int Total) Compare(SKBitmap render, SKBitmap frame, Shown shown, SKRectI area, SKColor under)
    {
        var mismatches = 0;
        var total = 0;
        for (var y = area.Top; y < area.Bottom; y++)
            for (var x = area.Left; x < area.Right; x++)
            {
                var fx = x - shown.Area.Left;
                var fy = y - shown.Area.Top;
                if (shown.Tiled) { fx %= frame.Width; fy %= frame.Height; }
                else if (fx >= frame.Width || fy >= frame.Height) continue;
                var expected = Over(frame.GetPixel(fx, fy), under);
                total++;
                if (Distance(render.GetPixel(x, y), expected) > VisualChecks.PixelTolerance) mismatches++;
            }
        return (mismatches, total);
    }

    /// <summary>Source-over of a straight-alpha pixel onto an opaque one.</summary>
    private static SKColor Over(SKColor top, SKColor under)
    {
        var a = top.Alpha / 255f;
        byte Mix(byte t, byte u) => (byte)MathF.Round(t * a + u * (1 - a));
        return new SKColor(Mix(top.Red, under.Red), Mix(top.Green, under.Green), Mix(top.Blue, under.Blue));
    }

    private static int Distance(SKColor a, SKColor b) =>
        Math.Max(Math.Abs(a.Red - b.Red), Math.Max(Math.Abs(a.Green - b.Green), Math.Abs(a.Blue - b.Blue)));

    private static bool SamePixels(SKBitmap a, SKBitmap b)
    {
        for (var y = 0; y < a.Height; y++)
            for (var x = 0; x < a.Width; x++)
                if (a.GetPixel(x, y) != b.GetPixel(x, y)) return false;
        return true;
    }
}
