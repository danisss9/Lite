using SkiaSharp;

namespace Lite.Media;

/// <summary>
/// Drives an HTMLMediaElement's timeline and (for video) supplies decoded frames. The default
/// <see cref="SimulatedMediaBackend"/> provides a deterministic fake timeline with no real
/// decoding — enough for the HTMLMediaElement API, event ordering, and the controls UI. A real
/// backend (e.g. a VlcMediaBackend using LibVLCSharp in an optional Lite.Media companion project)
/// can implement the same surface to play actual audio/video.
/// </summary>
public interface IMediaBackend : IDisposable
{
    double Duration { get; }
    double CurrentTime { get; set; }
    double Volume { get; set; }
    bool Muted { get; set; }
    bool Paused { get; }
    bool Ended { get; }
    /// <summary>HAVE_NOTHING=0, HAVE_METADATA=1, HAVE_CURRENT_DATA=2, HAVE_FUTURE_DATA=3, HAVE_ENOUGH_DATA=4.</summary>
    int ReadyState { get; }
    /// <summary>The current video frame to composite, or null (audio / simulated backend).</summary>
    SKBitmap? CurrentFrame { get; }

    /// <summary>Begins (re)loading the resource; fires loadedmetadata/canplay as it becomes ready.</summary>
    void Load(string? src, double duration, bool loop);
    void Play();
    void Pause();
}
