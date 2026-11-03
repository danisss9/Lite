namespace Lite.Platform;

/// <summary>
/// A window host with no OS window: every member is a no-op. The conformance harness and
/// unit tests use it to drive <see cref="BrowserWindow"/>'s real input pipeline — focus,
/// form controls, activation, event dispatch — through the same <c>On*</c> callbacks the
/// Win32/X11 hosts translate native input into, so injected input exercises the engine
/// itself rather than a parallel simulation of it.
/// </summary>
internal sealed class HeadlessHost : IWindowHost
{
    public bool Create(string title, int width, int height) => true;
    public void RunLoop() { }
    public void Invalidate() { }
    public void PostTask() { }
    public void StartTimer(uint intervalMs) { }
    public void StopTimer() { }
    public void SetTitle(string title) { }
    public void CaptureMouse() { }
    public void ReleaseMouse() { }
    public string[] ShowOpenFileDialog(string accept, bool multiple) => [];
    public void Dispose() { }
}
