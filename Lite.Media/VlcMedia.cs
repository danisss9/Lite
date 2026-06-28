using LibVLCSharp.Shared;
using Lite.Media;

namespace Lite.Media.Vlc;

/// <summary>
/// Entry point for the optional LibVLC-backed media support. Call <see cref="Register"/> once at
/// application startup (before any page loads) to initialize LibVLC and make <c>&lt;audio&gt;</c>/
/// <c>&lt;video&gt;</c> elements decode real media via <see cref="VlcMediaBackend"/> instead of the
/// built-in simulated timeline.
/// </summary>
public static class VlcMedia
{
    private static bool _initialized;

    /// <summary>Initializes the native LibVLC core and installs the VLC media backend factory.
    /// Safe to call multiple times. No-op-safe if the native libraries are unavailable.</summary>
    public static void Register()
    {
        try
        {
            if (!_initialized)
            {
                Core.Initialize();   // locates the bundled libvlc native libraries
                _initialized = true;
            }
            MediaBackends.Register((schedule, dispatch) => new VlcMediaBackend(schedule, dispatch));
        }
        catch (Exception ex)
        {
            // Native libs missing/incompatible — keep the simulated backend so the app still runs.
            Console.WriteLine($"[VlcMedia] LibVLC unavailable, using simulated media backend: {ex.Message}");
        }
    }
}
