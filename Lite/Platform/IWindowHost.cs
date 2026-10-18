namespace Lite.Platform;

/// <summary>Keyboard modifier state captured with a key event.</summary>
internal readonly record struct KeyModifiers(bool Ctrl, bool Shift, bool Alt, bool Meta);

/// <summary>
/// A native top-level window that hosts a <see cref="BrowserWindow"/>. The host owns the OS
/// window and message loop, blits <see cref="BrowserWindow.Frame"/> on paint, and translates
/// native input into the BrowserWindow's <c>On*</c> callbacks on the UI thread.
/// </summary>
internal interface IWindowHost : IDisposable
{
    /// <summary>Creates and shows the window. Returns false if the native window could not be created.</summary>
    bool Create(string title, int width, int height);

    /// <summary>Runs the message loop on the calling thread until the window closes.</summary>
    void RunLoop();

    /// <summary>Schedules a repaint of the whole client area from the current frame.</summary>
    void Invalidate();

    /// <summary>Wakes the UI thread to call <see cref="BrowserWindow.OnTask"/>. Safe to call from any thread.</summary>
    void PostTask();

    /// <summary>Starts (or restarts) the repeating timer that drives <see cref="BrowserWindow.OnTimer"/>.</summary>
    void StartTimer(uint intervalMs);

    void StopTimer();

    void SetTitle(string title);

    /// <summary>Keeps delivering pointer events to this window while the button is held outside it.</summary>
    void CaptureMouse();

    void ReleaseMouse();

    /// <summary>Shows a modal Open dialog for &lt;input type=file&gt;; returns the chosen paths, or empty if cancelled.</summary>
    string[] ShowOpenFileDialog(string accept, bool multiple);
}
