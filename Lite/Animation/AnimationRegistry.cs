namespace Lite.Animation;

/// <summary>
/// Global store for parsed @keyframes rules.
/// Populated once during CSS parse; read by <see cref="AnimationEngine"/> at tick time.
/// </summary>
public static class AnimationRegistry
{
    // name → sorted list of (offset 0–1, property→value dict)
    private static readonly Dictionary<string, List<(float Offset, Dictionary<string, string> Props)>>
        _keyframes = new(StringComparer.OrdinalIgnoreCase);

    public static void Register(
        string name,
        List<(float Offset, Dictionary<string, string> Props)> frames)
    {
        _keyframes[name] = [.. frames.OrderBy(f => f.Offset)];
    }

    public static bool TryGet(
        string name,
        out List<(float Offset, Dictionary<string, string> Props)> frames)
        => _keyframes.TryGetValue(name, out frames!);

    public static void Clear() => _keyframes.Clear();
}
