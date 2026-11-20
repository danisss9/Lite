using Lite.Layout;
using Lite.Media;
using Lite.Network;
using SkiaSharp;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>Animated GIFs (background-animated-001: "There should be an animated construction
/// sign below"). The cached bitmap is the animation's display surface; the window timer, or a
/// render that samples a moment, advances it.</summary>
public static class AnimatedImageTests
{
    private static readonly SKColor Red = new(0xff, 0, 0);
    private static readonly SKColor Blue = new(0, 0, 0xff);

    /// <summary>A 1x1 GIF89a whose frames alternate red and blue. <paramref name="loops"/> is the
    /// NETSCAPE2.0 loop count (0 = forever); null omits the extension (play once).</summary>
    private static byte[] Gif(int frames, int delayCs, int? loops)
    {
        var bytes = new List<byte>();
        bytes.AddRange("GIF89a"u8.ToArray());
        bytes.AddRange([1, 0, 1, 0, 0x80, 0, 0]);          // 1x1, 2-entry global color table
        bytes.AddRange([0xff, 0, 0, 0, 0, 0xff]);           // index 0 red, index 1 blue
        if (loops is { } count)
        {
            bytes.AddRange([0x21, 0xff, 0x0b]);
            bytes.AddRange("NETSCAPE2.0"u8.ToArray());
            bytes.AddRange([3, 1, (byte)(count & 0xff), (byte)(count >> 8), 0]);
        }
        for (var i = 0; i < frames; i++)
        {
            bytes.AddRange([0x21, 0xf9, 4, 1 << 2, (byte)(delayCs & 0xff), (byte)(delayCs >> 8), 0, 0]);
            bytes.AddRange([0x2c, 0, 0, 0, 0, 1, 0, 1, 0, 0]);
            // LZW, minimum code size 2: clear, the pixel's index, end of information.
            bytes.AddRange([2, 2, (byte)(i % 2 == 0 ? 0x44 : 0x4c), 0x01, 0]);
        }
        bytes.Add(0x3b);
        return [.. bytes];
    }

    private static SKBitmap Decode(byte[] gif, long now = 0)
    {
        using var codec = SKCodec.Create(new MemoryStream(gif));
        return AnimatedImages.Decode(codec, now, gif) ?? throw new InvalidOperationException("not decoded as animated");
    }

    private static void Pixel(SKColor expected, SKBitmap bitmap, string when) =>
        True(bitmap.GetPixel(0, 0) == expected, $"{when}: expected {expected}, got {bitmap.GetPixel(0, 0)}");

    [Test]
    public static void AnimatedGif_ShowsFrameZeroUntilTheClockAdvances()
    {
        using var surface = Decode(Gif(2, 5, 0));
        True(AnimatedImages.IsAnimated(surface), "a two-frame GIF must register an animation");
        Pixel(Red, surface, "on decode");
        Equal(50, AnimatedImages.FrameEnd(surface, 0) ?? -1);
        Equal(100, AnimatedImages.FrameEnd(surface, 1) ?? -1);
    }

    [Test]
    public static void AnimatedGif_FollowsItsFrameDelaysAndLoops()
    {
        using var surface = Decode(Gif(2, 5, 0), now: 1_000);
        AnimatedImages.Touch(surface);
        AnimatedImages.Advance(1_049, null);
        Pixel(Red, surface, "49ms in");
        AnimatedImages.Touch(surface);
        True(AnimatedImages.Advance(1_050, null), "a looping animation keeps the timer alive");
        Pixel(Blue, surface, "50ms in");
        AnimatedImages.Touch(surface);
        AnimatedImages.Advance(1_120, null);
        Pixel(Red, surface, "120ms in (second loop)");
    }

    [Test]
    public static void AnimatedGif_WithoutALoopExtensionPlaysOnceAndRestsOnItsLastFrame()
    {
        // Total plays: no loop block once, a count of 0 forever (0), a count of n n+1 times.
        Equal(1, AnimatedImages.GifPlays(Gif(2, 5, null)) ?? -1);
        Equal(0, AnimatedImages.GifPlays(Gif(2, 5, 0)) ?? -1);
        Equal(3, AnimatedImages.GifPlays(Gif(2, 5, 2)) ?? -1);
        True(AnimatedImages.GifPlays([0x89, (byte)'P', (byte)'N', (byte)'G', 0, 0, 0, 0, 0, 0, 0, 0, 0]) is null, "only a GIF has a GIF loop block");
        using var surface = Decode(Gif(2, 5, null), now: 0);
        AnimatedImages.Touch(surface);
        True(!AnimatedImages.Advance(500, null), "a finished animation must let the timer stop");
        Pixel(Blue, surface, "after its single play");
        Equal(1, AnimatedImages.FrameIndex([50, 100], plays: 1, 120));
        Equal(1, AnimatedImages.FrameIndex([50, 100], plays: 2, 450));
        Equal(0, AnimatedImages.FrameIndex([50, 100], plays: 2, 120));
    }

    [Test]
    public static void AnimatedGif_ClampsZeroDelaysToOneHundredMilliseconds()
    {
        using var surface = Decode(Gif(2, 0, 0));
        Equal(100, AnimatedImages.FrameEnd(surface, 0) ?? -1);
        Equal(100, AnimatedImages.Delay(10));
        Equal(20, AnimatedImages.Delay(20));
    }

    [Test]
    public static void AnimatedGif_OnlyAShownAnimationKeepsTheTimerAlive()
    {
        using var surface = Decode(Gif(2, 5, 0), now: 0);
        True(!AnimatedImages.Advance(10, null), "an animation no paint reached must not keep the timer alive");
        Pixel(Red, surface, "unshown animations do not advance");
        AnimatedImages.Touch(surface);
        True(AnimatedImages.Pending(10), "a painted animation with frames ahead is pending");
    }

    /// <summary>A 2x1 looping GIF: a transparent pixel (so the judge can read what lies
    /// beneath), then a pixel alternating red and blue, 300ms per frame.</summary>
    private static byte[] TwoPixelGif()
    {
        var bytes = new List<byte>();
        bytes.AddRange("GIF89a"u8.ToArray());
        bytes.AddRange([2, 0, 1, 0, 0x81, 0, 0]);                        // 2x1, 4-entry global color table
        bytes.AddRange([0xff, 0, 0, 0, 0, 0xff, 0, 0, 0, 0, 0, 0]);       // red, blue, (transparent), black
        bytes.AddRange([0x21, 0xff, 0x0b]);
        bytes.AddRange("NETSCAPE2.0"u8.ToArray());
        bytes.AddRange([3, 1, 0, 0, 0]);
        for (var i = 0; i < 2; i++)
        {
            bytes.AddRange([0x21, 0xf9, 4, 1 << 2 | 1, 30, 0, 2, 0]);      // 300ms, transparent index 2
            bytes.AddRange([0x2c, 0, 0, 0, 0, 2, 0, 1, 0, 0]);
            // LZW, code size 3: clear(4), 2, red(0)/blue(1), end(5).
            bytes.AddRange([2, 2, (byte)(i == 0 ? 0x14 : 0x54), 0x0a, 0]);
        }
        bytes.Add(0x3b);
        return [.. bytes];
    }

    [Test]
    public static void AnimatedImageCheck_PassesEachFrameInTurnAndFailsAFrozenImage()
    {
        var uri = "data:image/gif;base64," + Convert.ToBase64String(TwoPixelGif());
        var page = Parser.ParseChildPage(
            $"<!DOCTYPE html><html><body style='margin:0; background:#fff'><img src='{uri}' style='display:block'></body></html>",
            isSrcdoc: true, "http://test/", 100, 100);
        var shown = Lite.Conformance.Css21.AnimatedImageCheck.Find(page.Root);
        True(shown is not null, "the page's animated <img> must be found");
        var times = Lite.Conformance.Css21.AnimatedImageCheck.SampleTimes(shown!.Surface);
        Equal(2, times.Count);
        SKBitmap Render(long t)
        {
            AnimatedImages.SeekAll(t, Environment.TickCount64);
            return Drawer.DrawToBitmap(100, 100, page.Root, new Viewport { ViewportHeight = 100 });
        }
        using var first = Render(times[0]);
        using var second = Render(times[1]);
        using var frozen = Render(times[0]);
        AnimatedImages.SeekAll(0, Environment.TickCount64);

        var animating = Lite.Conformance.Css21.AnimatedImageCheck.Judge(
            new Lite.Conformance.Css21.RenderSet(first, null, null, null, [("t", second)]), page.Root);
        True(animating.Passed, $"each frame shown in turn must pass: {animating.Detail}");
        var stuck = Lite.Conformance.Css21.AnimatedImageCheck.Judge(
            new Lite.Conformance.Css21.RenderSet(first, null, null, null, [("t", frozen)]), page.Root);
        True(stuck.Cat == Lite.Conformance.Wpt.WptRunner.Cat.Fail, $"a frozen image must fail: {stuck.Cat} {stuck.Detail}");
    }

    [Test]
    public static void SingleFrameGif_IsNotAnimated()
    {
        using var codec = SKCodec.Create(new MemoryStream(Gif(1, 5, 0)));
        True(AnimatedImages.Decode(codec, 0) is null, "a single-frame GIF decodes the ordinary way");
    }

    [Test]
    public static void AnimatedGifImage_PaintsTheCurrentFrame()
    {
        var uri = "data:image/gif;base64," + Convert.ToBase64String(Gif(2, 30, 0));
        var page = Parser.ParseChildPage(
            $"<!DOCTYPE html><html><body style='margin:0'><img src='{uri}' style='display:block; width:20px; height:20px'></body></html>",
            isSrcdoc: true, "http://test/", 200, 200);
        var viewport = new Viewport { ViewportHeight = 200 };
        using (var first = Drawer.DrawToBitmap(200, 200, page.Root, viewport))
            True(first.GetPixel(10, 10) == Red, $"frame 0 paints first, got {first.GetPixel(10, 10)}");
        AnimatedImages.SeekAll(300, Environment.TickCount64);
        using (var second = Drawer.DrawToBitmap(200, 200, page.Root, viewport))
            True(second.GetPixel(10, 10) == Blue, $"frame 1 paints 300ms in, got {second.GetPixel(10, 10)}");
        AnimatedImages.SeekAll(0, Environment.TickCount64);
    }
}
