namespace Lite.Media;

/// <summary>
/// The factory used to create a media element's backend. Defaults to the decoder-free
/// <see cref="SimulatedMediaBackend"/>; the optional <c>Lite.Media</c> companion project calls
/// <see cref="Register"/> at startup to swap in a real (LibVLCSharp) backend for actual playback.
/// </summary>
public static class MediaBackends
{
    /// <param>schedule — enqueues work onto the owning page's JS task queue (UI thread).</param>
    /// <param>dispatch — fires a DOM media event on the element by name.</param>
    public delegate IMediaBackend Factory(Action<Action> schedule, Action<string> dispatch);

    private static Factory _current = (schedule, dispatch) => new SimulatedMediaBackend(schedule, dispatch);

    /// <summary>Installs a backend factory (e.g. a VLC-based one). Last registration wins.</summary>
    public static void Register(Factory factory) => _current = factory;

    internal static IMediaBackend Create(Action<Action> schedule, Action<string> dispatch)
        => _current(schedule, dispatch);
}
