using System.Runtime.InteropServices;
using LibVLCSharp.Shared;
using Lite.Media;
using SkiaSharp;

namespace Lite.Media.Vlc;

/// <summary>
/// A real audio/video backend backed by LibVLC (via LibVLCSharp). Audio plays through the system
/// device; video frames are decoded to RGBA and exposed as an <see cref="SKBitmap"/> via
/// <see cref="CurrentFrame"/>, which the renderer composites into the element's box.
///
/// All LibVLC callbacks fire on VLC's own threads, so state changes and DOM events are marshalled
/// onto the page's JS task queue through the <c>schedule</c> delegate (Jint is single-threaded).
/// Everything is defensively wrapped: if LibVLC isn't available or a call fails, the backend
/// degrades to a no-op rather than taking down the host.
/// </summary>
public sealed class VlcMediaBackend : IMediaBackend
{
    private readonly Action<Action> _schedule;
    private readonly Action<string> _dispatch;
    private readonly LibVLC? _libVlc;
    private readonly MediaPlayer? _player;

    private bool _loop;
    private bool _metadataFired;

    // Updated from VLC threads, read from the UI thread (advisory values; benign races).
    private double _currentTime;
    private double _duration;
    private bool _paused = true;
    private bool _ended;
    private int _readyState;     // HAVE_NOTHING

    // ---- video frame plumbing ----
    private readonly object _frameLock = new();
    private SKBitmap? _frame;
    private IntPtr _buffer;
    private uint _vWidth, _vHeight, _vPitch, _vLines;
    // Keep the delegates rooted so the GC can't collect them while native code holds pointers.
    private MediaPlayer.LibVLCVideoFormatCb? _formatCb;
    private MediaPlayer.LibVLCVideoCleanupCb? _cleanupCb;
    private MediaPlayer.LibVLCVideoLockCb? _lockCb;
    private MediaPlayer.LibVLCVideoUnlockCb? _unlockCb;
    private MediaPlayer.LibVLCVideoDisplayCb? _displayCb;

    public VlcMediaBackend(Action<Action> schedule, Action<string> dispatch)
    {
        _schedule = schedule;
        _dispatch = dispatch;
        try
        {
            _libVlc = new LibVLC(enableDebugLogs: false);
            _player = new MediaPlayer(_libVlc);
            WireEvents(_player);
            WireVideoCallbacks(_player);
        }
        catch
        {
            _libVlc = null;
            _player = null;
        }
    }

    public double Duration => _duration;
    public bool Paused => _paused;
    public bool Ended => _ended;
    public int ReadyState => _readyState;

    public SKBitmap? CurrentFrame
    {
        get { lock (_frameLock) return _frame; }
    }

    public double CurrentTime
    {
        get => _currentTime;
        set
        {
            _currentTime = value;
            try { if (_player is not null) _player.Time = (long)(value * 1000); } catch { }
            _schedule(() => _dispatch("timeupdate"));
        }
    }

    public double Volume
    {
        get { try { return (_player?.Volume ?? 100) / 100.0; } catch { return 1.0; } }
        set { try { if (_player is not null) _player.Volume = (int)Math.Clamp(value * 100, 0, 100); } catch { } _schedule(() => _dispatch("volumechange")); }
    }

    public bool Muted
    {
        get { try { return _player?.Mute ?? false; } catch { return false; } }
        set { try { if (_player is not null) _player.Mute = value; } catch { } _schedule(() => _dispatch("volumechange")); }
    }

    public void Load(string? src, double duration, bool loop)
    {
        _loop = loop;
        _metadataFired = false;
        _ended = false;
        _currentTime = 0;
        _duration = 0;
        _readyState = 0;
        if (_player is null || _libVlc is null || string.IsNullOrEmpty(src)) return;
        try
        {
            var media = Uri.TryCreate(src, UriKind.Absolute, out var uri)
                ? new LibVLCSharp.Shared.Media(_libVlc, uri)
                : new LibVLCSharp.Shared.Media(_libVlc, src, FromType.FromPath);
            _player.Media = media;
            media.Dispose(); // the player holds its own reference
        }
        catch { /* unsupported source → no-op */ }
    }

    public void Play()
    {
        if (_player is null) return;
        if (_ended) { _ended = false; try { _player.Time = 0; } catch { } }
        _paused = false;
        _schedule(() => _dispatch("play"));
        try { _player.Play(); } catch { }
    }

    public void Pause()
    {
        if (_player is null) return;
        try { _player.SetPause(true); } catch { }
    }

    private void WireEvents(MediaPlayer mp)
    {
        mp.LengthChanged += (_, e) =>
        {
            _duration = e.Length / 1000.0;
            if (!_metadataFired)
            {
                _metadataFired = true;
                _readyState = 1; // HAVE_METADATA
                _schedule(() => { _dispatch("loadedmetadata"); _dispatch("durationchange"); });
            }
            else _schedule(() => _dispatch("durationchange"));
        };
        mp.Playing += (_, _) =>
        {
            _paused = false;
            _readyState = 4; // HAVE_ENOUGH_DATA
            _schedule(() => { _dispatch("canplay"); _dispatch("playing"); });
        };
        mp.Paused += (_, _) => { _paused = true; _schedule(() => _dispatch("pause")); };
        mp.TimeChanged += (_, e) => { _currentTime = e.Time / 1000.0; _schedule(() => _dispatch("timeupdate")); };
        mp.EndReached += (_, _) =>
        {
            _currentTime = _duration;
            if (_loop) { _schedule(() => { try { _player!.Stop(); _player.Play(); } catch { } }); return; }
            _ended = true; _paused = true;
            _schedule(() => _dispatch("ended"));
        };
        mp.EncounteredError += (_, _) => _schedule(() => _dispatch("error"));
    }

    private void WireVideoCallbacks(MediaPlayer mp)
    {
        _formatCb = Format;
        _cleanupCb = Cleanup;
        _lockCb = LockFrame;
        _unlockCb = UnlockFrame;
        _displayCb = DisplayFrame;
        try
        {
            mp.SetVideoFormatCallbacks(_formatCb, _cleanupCb);
            mp.SetVideoCallbacks(_lockCb, _unlockCb, _displayCb);
        }
        catch { /* audio-only build or no video support → frames simply won't appear */ }
    }

    // Negotiate RV32 (BGRA) at the media's native size; allocate one reusable frame buffer.
    private uint Format(ref IntPtr opaque, IntPtr chroma, ref uint width, ref uint height, ref uint pitches, ref uint lines)
    {
        WriteChroma(chroma, "RV32");
        _vWidth = width;
        _vHeight = height;
        _vPitch = width * 4;
        _vLines = height;
        pitches = _vPitch;
        lines = _vLines;
        lock (_frameLock)
        {
            if (_buffer != IntPtr.Zero) Marshal.FreeHGlobal(_buffer);
            _buffer = Marshal.AllocHGlobal((int)(_vPitch * _vLines));
            _frame?.Dispose();
            _frame = new SKBitmap((int)_vWidth, (int)_vHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
        }
        return 1; // one plane
    }

    private void Cleanup(ref IntPtr opaque)
    {
        lock (_frameLock)
        {
            if (_buffer != IntPtr.Zero) { Marshal.FreeHGlobal(_buffer); _buffer = IntPtr.Zero; }
        }
    }

    private IntPtr LockFrame(IntPtr opaque, IntPtr planes)
    {
        Marshal.WriteIntPtr(planes, _buffer);
        return _buffer;
    }

    private void UnlockFrame(IntPtr opaque, IntPtr picture, IntPtr planes) { }

    // Copy the freshly-decoded buffer into the SKBitmap the renderer reads.
    private void DisplayFrame(IntPtr opaque, IntPtr picture)
    {
        lock (_frameLock)
        {
            if (_frame is null || _buffer == IntPtr.Zero) return;
            var dst = _frame.GetPixels();
            if (dst == IntPtr.Zero) return;
            unsafe { Buffer.MemoryCopy((void*)_buffer, (void*)dst, _vPitch * _vLines, _vPitch * _vLines); }
            _frame.NotifyPixelsChanged();
        }
    }

    private static void WriteChroma(IntPtr chroma, string fourcc)
    {
        for (int i = 0; i < 4 && i < fourcc.Length; i++)
            Marshal.WriteByte(chroma, i, (byte)fourcc[i]);
    }

    public void Dispose()
    {
        try { _player?.Stop(); } catch { }
        try { _player?.Dispose(); } catch { }
        try { _libVlc?.Dispose(); } catch { }
        lock (_frameLock)
        {
            if (_buffer != IntPtr.Zero) { Marshal.FreeHGlobal(_buffer); _buffer = IntPtr.Zero; }
            _frame?.Dispose();
            _frame = null;
        }
    }
}
