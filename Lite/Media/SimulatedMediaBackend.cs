using SkiaSharp;

namespace Lite.Media;

/// <summary>
/// A deterministic, decoder-free media backend: it advances a fake timeline on the page's task
/// queue and fires the HTMLMediaElement events in spec order (loadedmetadata → canplay → play →
/// playing → timeupdate… → ended). Used so the media API, event ordering, and controls work and
/// are testable without native codecs. <see cref="CurrentFrame"/> is always null (no video).
/// </summary>
internal sealed class SimulatedMediaBackend : IMediaBackend
{
    private readonly Action<Action> _schedule;   // enqueue work onto the owning engine's task queue
    private readonly Action<string> _dispatch;   // fire a DOM media event on the element by name

    private double _duration;
    private double _currentTime;
    private bool _paused = true;
    private bool _ended;
    private bool _loop;
    private int _readyState;       // HAVE_NOTHING
    private double _volume = 1.0;
    private bool _muted;
    private int _playToken;        // invalidates in-flight ticks across pause/load

    public SimulatedMediaBackend(Action<Action> schedule, Action<string> dispatch)
    {
        _schedule = schedule;
        _dispatch = dispatch;
    }

    public double Duration => _duration;
    public bool Paused => _paused;
    public bool Ended => _ended;
    public int ReadyState => _readyState;
    public SKBitmap? CurrentFrame => null;

    public double CurrentTime
    {
        get => _currentTime;
        set
        {
            _currentTime = Math.Clamp(value, 0, _duration > 0 ? _duration : value);
            if (_currentTime < _duration) _ended = false;
            _dispatch("timeupdate");
        }
    }

    public double Volume
    {
        get => _volume;
        set { _volume = Math.Clamp(value, 0, 1); _dispatch("volumechange"); }
    }

    public bool Muted
    {
        get => _muted;
        set { _muted = value; _dispatch("volumechange"); }
    }

    public void Load(string? src, double duration, bool loop)
    {
        _playToken++;
        _duration = duration > 0 ? duration : 0;
        _loop = loop;
        _currentTime = 0;
        _ended = false;
        _paused = true;
        _readyState = 0;
        _schedule(() =>
        {
            _readyState = 1; // HAVE_METADATA
            _dispatch("loadedmetadata");
            _dispatch("durationchange");
            _readyState = 4; // HAVE_ENOUGH_DATA
            _dispatch("loadeddata");
            _dispatch("canplay");
            _dispatch("canplaythrough");
        });
    }

    public void Play()
    {
        if (!_paused) return;
        _paused = false;
        if (_ended) { _currentTime = 0; _ended = false; }
        var token = ++_playToken;
        _schedule(() => { _dispatch("play"); _dispatch("playing"); });
        ScheduleTick(token);
    }

    public void Pause()
    {
        if (_paused) return;
        _paused = true;
        _playToken++;             // cancel pending ticks
        _schedule(() => _dispatch("pause"));
    }

    public void Dispose() => _playToken++;

    private void ScheduleTick(int token) => _schedule(() => Tick(token));

    // One advance of the fake clock. Step is sized so a handful of ticks reaches the end, keeping
    // tests fast and deterministic; the live window plays this fake timeline quickly (no decoder).
    private void Tick(int token)
    {
        if (token != _playToken || _paused) return;   // stale (paused/loaded/replayed since)
        var step = _duration > 0 ? Math.Max(_duration / 4.0, 0.25) : 0.25;
        _currentTime += step;
        if (_duration > 0 && _currentTime >= _duration)
        {
            _currentTime = _duration;
            _dispatch("timeupdate");
            if (_loop)
            {
                _currentTime = 0;
                ScheduleTick(token);
                return;
            }
            _ended = true;
            _paused = true;
            _dispatch("ended");
            return;
        }
        _dispatch("timeupdate");
        ScheduleTick(token);
    }
}
