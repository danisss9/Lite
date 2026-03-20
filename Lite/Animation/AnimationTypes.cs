namespace Lite.Animation;

/// <summary>A parsed CSS `transition` entry for one property.</summary>
public record TransitionSpec(
    string Property,        // "all", "opacity", "background-color", …
    float  Duration,        // seconds
    float  Delay,           // seconds
    string TimingFunction); // "ease", "linear", "ease-in", …

/// <summary>A parsed CSS `animation` entry.</summary>
public record AnimationSpec(
    string Name,
    float  Duration,
    float  Delay,
    string TimingFunction,
    int    IterationCount,  // -1 = infinite
    bool   Alternate,
    bool   FillForwards);

// ── Runtime active state ──────────────────────────────────────────────────────

/// <summary>An in-flight CSS transition for one property on one node.</summary>
public sealed class ActiveTransition
{
    public string Property    { get; }
    public string FromValue   { get; }
    public string ToValue     { get; }
    public float  Duration    { get; }
    public float  Delay       { get; }
    public long   StartTimeMs { get; }
    public string TimingFunc  { get; }
    public bool   IsComplete  { get; set; }

    public ActiveTransition(string property, string from, string to,
        float duration, float delay, long startTimeMs, string timingFunc)
    {
        Property    = property;
        FromValue   = from;
        ToValue     = to;
        Duration    = duration;
        Delay       = delay;
        StartTimeMs = startTimeMs;
        TimingFunc  = timingFunc;
    }

    /// <summary>Returns 0–1 progress (0 = not started, 1 = finished).</summary>
    public float Progress(long nowMs)
    {
        var elapsed = (nowMs - StartTimeMs) / 1000f - Delay;
        if (elapsed <= 0)  return 0f;
        if (Duration <= 0) return 1f;
        return Math.Clamp(elapsed / Duration, 0f, 1f);
    }
}

/// <summary>An in-flight CSS @keyframes animation on one node.</summary>
public sealed class ActiveAnimation
{
    public string Name           { get; }
    public float  Duration       { get; }
    public float  Delay          { get; }
    public string TimingFunc     { get; }
    public int    IterationCount { get; }  // -1 = infinite
    public bool   Alternate      { get; }
    public bool   FillForwards   { get; }
    public long   StartTimeMs    { get; }
    public bool   IsComplete     { get; set; }

    public ActiveAnimation(string name, float duration, float delay, string timingFunc,
        int iterationCount, bool alternate, bool fillForwards, long startTimeMs)
    {
        Name           = name;
        Duration       = duration;
        Delay          = delay;
        TimingFunc     = timingFunc;
        IterationCount = iterationCount;
        Alternate      = alternate;
        FillForwards   = fillForwards;
        StartTimeMs    = startTimeMs;
    }

    /// <summary>
    /// Returns (offset 0–1, done).
    /// offset = -1 means the animation ended with fill-mode:none (remove overrides).
    /// </summary>
    public (float Offset, bool Done) GetOffset(long nowMs)
    {
        var elapsed       = (nowMs - StartTimeMs) / 1000f - Delay;
        if (elapsed < 0)   return (0f, false);   // in delay, hold at 0%
        if (Duration <= 0) return (1f, true);

        var totalProgress = elapsed / Duration;
        var iteration     = (int)MathF.Floor(totalProgress);
        var frac          = totalProgress - iteration;

        var done = IterationCount >= 0 && iteration >= IterationCount;
        if (done)
        {
            if (!FillForwards) return (-1f, true);           // no fill
            var fillOffset = (Alternate && IterationCount % 2 == 1) ? 0f : 1f;
            return (fillOffset, true);
        }

        var offset = (Alternate && iteration % 2 == 1) ? 1f - frac : frac;
        return (offset, false);
    }
}
