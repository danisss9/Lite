using System.Runtime.CompilerServices;
using Lite.Models;
using SkiaSharp;

namespace Lite.Media;

/// <summary>
/// Animated raster images (GIF, and any other multi-frame format SKCodec decodes).
///
/// Every painter draws a fetched image from the one <see cref="SKBitmap"/> the session's
/// image cache holds. For a multi-frame image that bitmap is the animation's display
/// surface: the frames are decoded and composited once, and advancing the animation copies
/// the frame due at that moment into the surface. Backgrounds, &lt;img&gt;, list markers and
/// generated images therefore animate without the painters knowing. With no clock advance,
/// the surface shows frame 0, so a single static render is unchanged.
///
/// Timing follows browsers: a frame's delay of 10ms or less counts as 100ms, a GIF without
/// a loop extension plays once, and a finished animation rests on its last frame.
/// </summary>
internal static class AnimatedImages
{
    /// <summary>Browsers show a frame whose stated delay is this short (0 or 1 centisecond) for
    /// <see cref="ClampedDelayMs"/> instead.</summary>
    internal const int MinDelayMs = 10;
    internal const int ClampedDelayMs = 100;

    private sealed class Animation
    {
        internal required SKBitmap[] Frames;   // composited frames, same info as the surface
        internal required int[] Ends;          // cumulative end time of each frame, in ms
        internal required int Plays;           // total plays; 0 = forever
        internal long Start;
        internal int Shown;
        internal bool Touched;
    }

    private static readonly ConditionalWeakTable<SKBitmap, Animation> Live = new();
    private static readonly object Gate = new();

    /// <summary>Decodes a multi-frame image into its display surface (showing frame 0) and
    /// registers its animation. Returns null when the image has a single frame, so the caller
    /// decodes it the ordinary way.</summary>
    internal static SKBitmap? Decode(SKCodec codec, long nowMs, byte[]? source = null)
    {
        var infos = codec.FrameInfo;
        if (infos.Length < 2) return null;
        var premul = codec.Info.WithColorType(SKColorType.Rgba8888).WithAlphaType(SKAlphaType.Premul);
        var unpremul = premul.WithAlphaType(SKAlphaType.Unpremul);
        var composed = new SKBitmap?[infos.Length];
        var frames = new List<SKBitmap>();
        var ends = new List<int>();
        var time = 0;
        try
        {
            for (var i = 0; i < infos.Length; i++)
            {
                // Skia composites frame i over its required prior frame when handed that
                // frame's pixels; a frame with no required frame starts from transparent.
                var work = new SKBitmap(premul);
                var required = infos[i].RequiredFrame;
                if (required >= 0 && required < i && composed[required] is { } prior)
                    prior.PeekPixels().ReadPixels(premul, work.GetPixels(), work.RowBytes);
                else
                {
                    work.Erase(SKColors.Transparent);
                    required = -1;
                }
                var result = codec.GetPixels(premul, work.GetPixels(), new SKCodecOptions(i, required));
                if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
                {
                    work.Dispose();
                    break;
                }
                composed[i] = work;
                // Painters expect straight alpha (see ResourceLoader.DecodeImageBytes).
                var frame = new SKBitmap(unpremul);
                work.PeekPixels().ReadPixels(unpremul, frame.GetPixels(), frame.RowBytes);
                frames.Add(frame);
                time += Delay(infos[i].Duration);
                ends.Add(time);
            }
        }
        finally
        {
            foreach (var bitmap in composed) bitmap?.Dispose();
        }
        if (frames.Count == 0) return null;
        var surface = new SKBitmap(unpremul);
        frames[0].PeekPixels().ReadPixels(unpremul, surface.GetPixels(), surface.RowBytes);
        if (frames.Count == 1)
        {
            frames[0].Dispose();
            return surface;
        }
        // SKCodec: -1 loops forever, n repeats n times. Its GIF decoder reports -1 for a GIF
        // with no loop extension too, which browsers play once, so a GIF's own block decides.
        var repetitions = codec.RepetitionCount;
        var animation = new Animation
        {
            Frames = [.. frames],
            Ends = [.. ends],
            Plays = (source is null ? null : GifPlays(source)) ?? (repetitions < 0 ? 0 : repetitions + 1),
            Start = nowMs,
        };
        lock (Gate) Live.AddOrUpdate(surface, animation);
        return surface;
    }

    internal static int Delay(int statedMs) => statedMs <= MinDelayMs ? ClampedDelayMs : statedMs;

    /// <summary>How many times a GIF plays: its NETSCAPE2.0 (or ANIMEXTS1.0) loop block's count n
    /// repeats it n times after the first play, 0 loops forever (returned as 0), and no block
    /// plays it once. Null for anything that is not a GIF.</summary>
    internal static int? GifPlays(byte[] bytes)
    {
        if (bytes.Length < 13 || bytes[0] != 'G' || bytes[1] != 'I' || bytes[2] != 'F') return null;
        ReadOnlySpan<byte> netscape = "NETSCAPE2.0"u8, animexts = "ANIMEXTS1.0"u8;
        var span = bytes.AsSpan();
        for (var i = 0; i + 19 <= span.Length; i++)
        {
            // Application extension: 0x21 0xFF, block size 11, identifier, then sub-block 3 / id 1 / count.
            if (span[i] != 0x21 || span[i + 1] != 0xff || span[i + 2] != 0x0b) continue;
            var id = span.Slice(i + 3, 11);
            if (!id.SequenceEqual(netscape) && !id.SequenceEqual(animexts)) continue;
            if (span[i + 14] != 3 || span[i + 15] != 1) continue;
            var count = span[i + 16] | span[i + 17] << 8;
            return count == 0 ? 0 : count + 1;
        }
        return 1;
    }

    internal static bool IsAnimated(SKBitmap? bitmap)
    {
        if (bitmap is null) return false;
        lock (Gate) return Live.TryGetValue(bitmap, out _);
    }

    /// <summary>The frame a timeline shows <paramref name="elapsedMs"/> after it started.</summary>
    internal static int FrameIndex(IReadOnlyList<int> ends, int plays, long elapsedMs)
    {
        var total = ends[^1];
        if (elapsedMs < 0) return 0;
        if (plays > 0 && elapsedMs >= (long)total * plays) return ends.Count - 1;
        var within = elapsedMs % total;
        for (var i = 0; i < ends.Count; i++)
            if (within < ends[i]) return i;
        return ends.Count - 1;
    }

    /// <summary>Marks a fetched image as being painted. The painters fetch backgrounds and
    /// list images through the session cache on every paint.</summary>
    internal static void Touch(SKBitmap? bitmap)
    {
        if (bitmap is null) return;
        lock (Gate)
            if (Live.TryGetValue(bitmap, out var animation)) animation.Touched = true;
    }

    /// <summary>True when a paint since the last <see cref="Advance"/> drew an animation that
    /// still has frames ahead: the window timer must keep running.</summary>
    internal static bool Pending(long nowMs)
    {
        lock (Gate)
        {
            DropDisposed();
            foreach (var (_, animation) in Live)
                if (animation.Touched && !Finished(animation, nowMs)) return true;
        }
        return false;
    }

    /// <summary>Window timer tick: every shown animation (painted since the last tick, or held
    /// by a node of <paramref name="root"/>) shows the frame due at <paramref name="nowMs"/>.
    /// Returns true while a shown animation still has frames ahead. Animations no paint
    /// reaches (a previous page's images left in the session cache) do not keep the timer alive.</summary>
    internal static bool Advance(long nowMs, LayoutNode? root)
    {
        var held = new HashSet<SKBitmap>(ReferenceEqualityComparer.Instance);
        if (root is not null) CollectImages(root, held);
        var running = false;
        lock (Gate)
        {
            DropDisposed();
            foreach (var (surface, animation) in Live)
            {
                if (!animation.Touched && !held.Contains(surface)) continue;
                animation.Touched = false;
                Show(surface, animation, nowMs - animation.Start);
                running |= !Finished(animation, nowMs);
            }
        }
        return running;
    }

    /// <summary>A disposed surface (a session's image cache torn down) is never painted again.</summary>
    private static void DropDisposed()
    {
        List<SKBitmap>? gone = null;
        foreach (var (surface, _) in Live)
            if (surface.Handle == IntPtr.Zero) (gone ??= []).Add(surface);
        if (gone is not null)
            foreach (var surface in gone) Live.Remove(surface);
    }

    /// <summary>Shows, on every animation, the frame due <paramref name="elapsedMs"/> after its
    /// start, and restarts its clock there. Renders that sample a fixed moment use this.</summary>
    internal static void SeekAll(long elapsedMs, long nowMs)
    {
        lock (Gate)
            foreach (var (surface, animation) in Live)
            {
                animation.Start = nowMs - elapsedMs;
                Show(surface, animation, elapsedMs);
            }
    }

    /// <summary>The frame count and the composited frame <paramref name="index"/> of an
    /// animated surface (owned by the animation: do not dispose it).</summary>
    internal static SKBitmap? Frame(SKBitmap surface, int index)
    {
        lock (Gate)
            return Live.TryGetValue(surface, out var animation) && index >= 0 && index < animation.Frames.Length
                ? animation.Frames[index] : null;
    }

    /// <summary>When frame <paramref name="index"/> of an animated surface ends, in ms after its start.</summary>
    internal static int? FrameEnd(SKBitmap surface, int index)
    {
        lock (Gate)
            return Live.TryGetValue(surface, out var animation) && index >= 0 && index < animation.Ends.Length
                ? animation.Ends[index] : null;
    }

    private static bool Finished(Animation animation, long nowMs) =>
        animation.Plays > 0 && nowMs - animation.Start >= (long)animation.Ends[^1] * animation.Plays;

    private static void Show(SKBitmap surface, Animation animation, long elapsedMs)
    {
        var index = FrameIndex(animation.Ends, animation.Plays, elapsedMs);
        if (index == animation.Shown || surface.Handle == IntPtr.Zero) return;
        animation.Frames[index].PeekPixels().ReadPixels(surface.Info, surface.GetPixels(), surface.RowBytes);
        surface.NotifyPixelsChanged();
        animation.Shown = index;
    }

    private static void CollectImages(LayoutNode node, HashSet<SKBitmap> into)
    {
        if (node.Image is { } image) into.Add(image);
        foreach (var child in node.Children) CollectImages(child, into);
    }
}
