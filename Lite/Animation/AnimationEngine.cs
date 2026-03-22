using System.Globalization;
using Lite.Models;
using SkiaSharp;

namespace Lite.Animation;

/// <summary>
/// Drives CSS transitions and @keyframes animations.
///
/// Usage pattern each frame:
///   1. AnimationEngine.SnapshotForTransition(root)   — before state change
///   2. [change pseudo-class state]
///   3. AnimationEngine.DetectAndStartTransitions(root) — start new transitions
///   4. AnimationEngine.Tick(root)                     — advance + write overrides
///   5. Drawer.Draw(...)                               — render with animated values
/// </summary>
public static class AnimationEngine
{
    // Per-node active transitions (NodeKey → list)
    private static readonly Dictionary<Guid, List<ActiveTransition>> _transitions = [];
    // Per-node active animations (NodeKey → list)
    private static readonly Dictionary<Guid, List<ActiveAnimation>>  _animations  = [];
    // Pre-change value snapshot for transition detection
    private static readonly Dictionary<Guid, Dictionary<string, string>> _snapshot = [];

    // ── Property sets ─────────────────────────────────────────────────────────

    private static readonly HashSet<string> s_numericProps =
    [
        "opacity", "width", "height", "min-width", "max-width", "min-height", "max-height",
        "font-size", "border-radius",
        "border-top-left-radius", "border-top-right-radius",
        "border-bottom-left-radius", "border-bottom-right-radius",
        "top", "left", "right", "bottom",
        "margin-top", "margin-right", "margin-bottom", "margin-left",
        "padding-top", "padding-right", "padding-bottom", "padding-left",
        "border-top-width", "border-right-width", "border-bottom-width", "border-left-width",
        "flex-basis", "flex-grow", "flex-shrink", "line-height", "letter-spacing",
    ];

    private static readonly HashSet<string> s_colorProps =
    [
        "color", "background-color",
        "border-top-color", "border-right-color", "border-bottom-color", "border-left-color",
    ];

    // Union used for `transition: all`
    private static readonly string[] s_allTransitionable =
        [.. s_numericProps, .. s_colorProps];

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Snapshot current rendered values for nodes that have transition specs.
    /// Must be called BEFORE changing pseudo-class state.
    /// </summary>
    public static void SnapshotForTransition(LayoutNode root)
    {
        _snapshot.Clear();
        SnapshotTree(root);
    }

    /// <summary>
    /// Walk the tree, detect which properties changed since the last snapshot,
    /// and start a transition for each. Returns true if any transitions were started.
    /// Call AFTER changing pseudo-class state.
    /// </summary>
    public static bool DetectAndStartTransitions(LayoutNode root)
    {
        var any = false;
        DetectTree(root, ref any);
        return any;
    }

    /// <summary>
    /// Start CSS @keyframes animations declared on nodes.
    /// Call once after the initial page parse (and re-call after JS-driven style changes if needed).
    /// </summary>
    public static void StartAnimations(LayoutNode root)
    {
        foreach (var spec in root.AnimationSpecs)
        {
            // Don't restart an already-running identical animation
            if (_animations.TryGetValue(root.NodeKey, out var existing) &&
                existing.Any(a => a.Name == spec.Name && !a.IsComplete))
                continue;

            if (!_animations.ContainsKey(root.NodeKey))
                _animations[root.NodeKey] = [];

            _animations[root.NodeKey].Add(new ActiveAnimation(
                spec.Name, spec.Duration, spec.Delay, spec.TimingFunction,
                spec.IterationCount, spec.Alternate, spec.FillForwards,
                Environment.TickCount64));
        }

        foreach (var child in root.Children)
            StartAnimations(child);
    }

    /// <summary>
    /// Advance all transitions and animations, writing interpolated values into
    /// <see cref="LayoutNode.AnimationOverrides"/> on each node.
    /// Returns true when at least one animation/transition is still running
    /// (caller should schedule another frame).
    /// </summary>
    public static bool Tick(LayoutNode root)
    {
        var anyActive = false;
        TickTree(root, ref anyActive);
        return anyActive;
    }

    /// <summary>Remove all active state (e.g. after a full page reload).</summary>
    public static void Reset()
    {
        _transitions.Clear();
        _animations.Clear();
        _snapshot.Clear();
    }

    // ── Snapshot / detection ──────────────────────────────────────────────────

    private static void SnapshotTree(LayoutNode root)
    {
        var stack = new Stack<LayoutNode>();
        stack.Push(root);
        var visited = new HashSet<LayoutNode>(ReferenceEqualityComparer.Instance);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (!visited.Add(node)) continue;

            if (node.TransitionSpecs.Count > 0)
            {
                var snap = new Dictionary<string, string>();
                foreach (var spec in node.TransitionSpecs)
                {
                    var props = IsAll(spec.Property) ? s_allTransitionable : (IEnumerable<string>)[spec.Property];
                    foreach (var p in props)
                    {
                        var v = GetDisplayedValue(node, p);
                        if (v != null) snap[p] = v;
                    }
                }
                _snapshot[node.NodeKey] = snap;
            }

            for (int i = node.Children.Count - 1; i >= 0; i--)
                stack.Push(node.Children[i]);
        }
    }

    private static void DetectTree(LayoutNode root, ref bool any)
    {
        var stack = new Stack<LayoutNode>();
        stack.Push(root);
        var visited = new HashSet<LayoutNode>(ReferenceEqualityComparer.Instance);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (!visited.Add(node)) continue;

            if (node.TransitionSpecs.Count > 0 &&
                _snapshot.TryGetValue(node.NodeKey, out var snap))
            {
                foreach (var spec in node.TransitionSpecs)
                {
                    var props = IsAll(spec.Property) ? s_allTransitionable : (IEnumerable<string>)[spec.Property];
                    foreach (var p in props)
                    {
                        var newVal = GetTargetValue(node, p);
                        if (newVal == null) continue;

                        snap.TryGetValue(p, out var oldVal);
                        if (oldVal == null || oldVal == newVal) continue;

                        StartTransitionInternal(node, p, oldVal, newVal,
                            spec.Duration, spec.Delay, spec.TimingFunction);
                        any = true;
                    }
                }
            }

            for (int i = node.Children.Count - 1; i >= 0; i--)
                stack.Push(node.Children[i]);
        }
    }

    private static void StartTransitionInternal(LayoutNode node, string property,
        string from, string to, float duration, float delay, string timingFunc)
    {
        if (!_transitions.ContainsKey(node.NodeKey))
            _transitions[node.NodeKey] = [];

        // Cancel any existing transition for this property — start fresh from current value
        _transitions[node.NodeKey].RemoveAll(t => t.Property == property);
        _transitions[node.NodeKey].Add(new ActiveTransition(
            property, from, to, duration, delay, Environment.TickCount64, timingFunc));
    }

    // ── Tick ──────────────────────────────────────────────────────────────────

    private static void TickTree(LayoutNode node, ref bool anyActive)
    {
        var now = Environment.TickCount64;
        node.AnimationOverrides.Clear();

        // @keyframes animations (lower layer — transitions win on conflict)
        if (_animations.TryGetValue(node.NodeKey, out var anims))
        {
            foreach (var anim in anims)
            {
                if (anim.IsComplete) continue;
                var (offset, done) = anim.GetOffset(now);
                if (done)
                {
                    anim.IsComplete = true;
                    if (offset < 0) continue;  // fill-mode: none → leave nothing
                    ApplyKeyframeAt(node, anim.Name, offset, anim.TimingFunc);
                    continue;
                }
                if (offset >= 0)
                    ApplyKeyframeAt(node, anim.Name, offset, anim.TimingFunc);
                anyActive = true;
            }
            anims.RemoveAll(a => a.IsComplete && !a.FillForwards);
        }

        // Transitions (higher layer — overwrite animation values for same property)
        if (_transitions.TryGetValue(node.NodeKey, out var trans))
        {
            foreach (var t in trans)
            {
                if (t.IsComplete) continue;
                var progress = t.Progress(now);
                var eased    = Ease(progress, t.TimingFunc);
                var current  = Interpolate(t.FromValue, t.ToValue, eased);
                if (current != null)
                    node.AnimationOverrides[t.Property] = current;

                if (progress >= 1f)
                    t.IsComplete = true;
                else
                    anyActive = true;
            }
            trans.RemoveAll(t => t.IsComplete);
        }

        foreach (var child in node.Children)
            TickTree(child, ref anyActive);
    }

    // ── Keyframe application ──────────────────────────────────────────────────

    private static void ApplyKeyframeAt(LayoutNode node, string animName,
        float offset, string defaultEasing)
    {
        if (!AnimationRegistry.TryGet(animName, out var frames) || frames.Count == 0)
            return;

        // Find surrounding keyframes: largest offset ≤ current, smallest offset ≥ current
        int fromIdx = 0, toIdx = frames.Count - 1;
        for (int i = 0; i < frames.Count; i++)
        {
            if (frames[i].Offset <= offset) fromIdx = i;
        }
        for (int i = frames.Count - 1; i >= 0; i--)
        {
            if (frames[i].Offset >= offset) { toIdx = i; break; }
        }

        var fromFrame = frames[fromIdx];
        var toFrame   = frames[toIdx];

        // Collect every property that appears in either keyframe
        var allProps = fromFrame.Props.Keys.Union(toFrame.Props.Keys);
        foreach (var prop in allProps)
        {
            fromFrame.Props.TryGetValue(prop, out var fv);
            toFrame.Props.TryGetValue(prop,   out var tv);

            if (fv == null || tv == null)
            {
                node.AnimationOverrides[prop] = fv ?? tv!;
                continue;
            }

            if (fromIdx == toIdx || fromFrame.Offset >= toFrame.Offset)
            {
                node.AnimationOverrides[prop] = tv;
                continue;
            }

            var localT    = (offset - fromFrame.Offset) / (toFrame.Offset - fromFrame.Offset);
            var easedT    = Ease(localT, defaultEasing);
            var interp    = Interpolate(fv, tv, easedT);
            if (interp != null)
                node.AnimationOverrides[prop] = interp;
        }
    }

    // ── Value resolution ──────────────────────────────────────────────────────

    /// <summary>
    /// The value currently being displayed (including any ongoing animation/transition).
    /// Used as the "from" value when starting a new transition.
    /// </summary>
    private static string? GetDisplayedValue(LayoutNode node, string prop)
    {
        if (node.AnimationOverrides.TryGetValue(prop, out var ao)) return ao;
        if (node.TryResolveStyle(prop, out var ov)) return ov;
        var sv = node.Style.GetPropertyValue(prop);
        return string.IsNullOrEmpty(sv) ? null : sv;
    }

    /// <summary>
    /// The CSS target value after a state change — deliberately skips AnimationOverrides
    /// so we get the declared CSS destination, not the mid-animation position.
    /// </summary>
    private static string? GetTargetValue(LayoutNode node, string prop)
    {
        if (node.IsActive  && node.MediaActiveStyles.TryGetValue(prop, out var v1m)) return v1m;
        if (node.IsActive  && node.ActiveStyles.TryGetValue(prop,      out var v1))  return v1;
        if (node.IsFocused && node.MediaFocusStyles.TryGetValue(prop,  out var v2m)) return v2m;
        if (node.IsFocused && node.FocusStyles.TryGetValue(prop,       out var v2))  return v2;
        if (node.IsHovered && node.MediaHoverStyles.TryGetValue(prop,  out var v3m)) return v3m;
        if (node.IsHovered && node.HoverStyles.TryGetValue(prop,       out var v3))  return v3;
        if (node.MediaOverrides.TryGetValue(prop,                      out var v4m)) return v4m;
        if (node.StyleOverrides.TryGetValue(prop,                      out var v4))  return v4;
        var sv = node.Style.GetPropertyValue(prop);
        return string.IsNullOrEmpty(sv) ? null : sv;
    }

    // ── Interpolation ─────────────────────────────────────────────────────────

    private static string? Interpolate(string from, string to, float t)
    {
        if (t <= 0f) return from;
        if (t >= 1f) return to;
        if (from == to) return from;

        // Color
        if (TryParseColor(from, out var fc) && TryParseColor(to, out var tc))
        {
            var r = (int)(fc.Red   + (tc.Red   - fc.Red)   * t);
            var g = (int)(fc.Green + (tc.Green - fc.Green) * t);
            var b = (int)(fc.Blue  + (tc.Blue  - fc.Blue)  * t);
            var a = (int)(fc.Alpha + (tc.Alpha - fc.Alpha) * t);
            return $"rgba({r},{g},{b},{Math.Clamp(a, 0, 255) / 255f:F3})";
        }

        // Numeric with unit
        if (TryParseNumeric(from, out var fv, out var unit) &&
            TryParseNumeric(to,   out var tv, out _))
        {
            var v = fv + (tv - fv) * t;
            return unit.Length > 0
                ? v.ToString("G6", CultureInfo.InvariantCulture) + unit
                : v.ToString("G6", CultureInfo.InvariantCulture);
        }

        // Non-interpolatable — snap at the midpoint
        return t < 0.5f ? from : to;
    }

    private static bool TryParseNumeric(string val, out float number, out string unit)
    {
        number = 0; unit = "";
        val = val.Trim();
        int i = val.Length;
        while (i > 0 && (char.IsLetter(val[i - 1]) || val[i - 1] == '%')) i--;
        unit = val[i..].ToLowerInvariant();
        return float.TryParse(val[..i].Trim(),
            NumberStyles.Float, CultureInfo.InvariantCulture, out number);
    }

    private static bool TryParseColor(string val, out SKColor color)
    {
        color = SKColors.Transparent;
        val   = val.Trim();

        if (val.StartsWith("rgba(", StringComparison.OrdinalIgnoreCase) && val.EndsWith(')'))
        {
            var parts = val[5..^1].Split(',');
            if (parts.Length >= 4 &&
                float.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var r) &&
                float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var g) &&
                float.TryParse(parts[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var b) &&
                float.TryParse(parts[3].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var a))
            {
                color = new SKColor(
                    (byte)Math.Clamp((int)r, 0, 255),
                    (byte)Math.Clamp((int)g, 0, 255),
                    (byte)Math.Clamp((int)b, 0, 255),
                    (byte)Math.Clamp((int)(a * 255), 0, 255));
                return true;
            }
        }

        if (val.StartsWith("rgb(", StringComparison.OrdinalIgnoreCase) && val.EndsWith(')'))
        {
            var parts = val[4..^1].Split(',');
            if (parts.Length >= 3 &&
                float.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var r) &&
                float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var g) &&
                float.TryParse(parts[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var b))
            {
                color = new SKColor(
                    (byte)Math.Clamp((int)r, 0, 255),
                    (byte)Math.Clamp((int)g, 0, 255),
                    (byte)Math.Clamp((int)b, 0, 255));
                return true;
            }
        }

        if (val.StartsWith('#') && SKColor.TryParse(val, out color)) return true;

        // Named colors
        if (SKColor.TryParse(val, out color)) return color != SKColors.Empty;
        return false;
    }

    // ── Easing ────────────────────────────────────────────────────────────────

    private static float Ease(float t, string func) => func.Trim().ToLowerInvariant() switch
    {
        "linear"       => t,
        "ease"         => CubicBezier(t, 0.25f, 0.10f, 0.25f, 1.00f),
        "ease-in"      => CubicBezier(t, 0.42f, 0.00f, 1.00f, 1.00f),
        "ease-out"     => CubicBezier(t, 0.00f, 0.00f, 0.58f, 1.00f),
        "ease-in-out"  => CubicBezier(t, 0.42f, 0.00f, 0.58f, 1.00f),
        "step-start"   => t <= 0f ? 0f : 1f,
        "step-end"     => t >= 1f ? 1f : 0f,
        _              => TryParseCubicBezier(func, out var p) ? CubicBezier(t, p[0], p[1], p[2], p[3]) : t,
    };

    private static bool TryParseCubicBezier(string func, out float[] p)
    {
        p = [];
        if (!func.StartsWith("cubic-bezier(", StringComparison.OrdinalIgnoreCase) || !func.EndsWith(')'))
            return false;
        var parts = func[13..^1].Split(',');
        if (parts.Length != 4) return false;
        var vals = new float[4];
        for (int i = 0; i < 4; i++)
            if (!float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out vals[i]))
                return false;
        p = vals;
        return true;
    }

    /// <summary>Approximates a CSS cubic-bezier via 8-iteration Newton-Raphson.</summary>
    private static float CubicBezier(float x, float p1x, float p1y, float p2x, float p2y)
    {
        x = Math.Clamp(x, 0f, 1f);
        var t = x;
        for (int i = 0; i < 8; i++)
        {
            var bx  = Bez(t, 0, p1x, p2x, 1) - x;
            var dbx = BezD(t, 0, p1x, p2x, 1);
            if (MathF.Abs(dbx) < 1e-6f) break;
            t -= bx / dbx;
        }
        return Math.Clamp(Bez(t, 0, p1y, p2y, 1), 0f, 1f);
    }

    private static float Bez(float t, float p0, float p1, float p2, float p3)
    {
        var m = 1 - t;
        return m*m*m*p0 + 3*m*m*t*p1 + 3*m*t*t*p2 + t*t*t*p3;
    }

    private static float BezD(float t, float p0, float p1, float p2, float p3)
    {
        var m = 1 - t;
        return 3*m*m*(p1-p0) + 6*m*t*(p2-p1) + 3*t*t*(p3-p2);
    }

    private static bool IsAll(string property) =>
        property.Equals("all", StringComparison.OrdinalIgnoreCase);
}
