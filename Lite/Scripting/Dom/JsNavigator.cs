namespace Lite.Scripting.Dom;

/// <summary>Minimal navigator object for feature detection and UA strings.</summary>
public class JsNavigator
{
    public string userAgent { get; } =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Lite/1.0";
    public string appName { get; } = "Netscape";
    public string appVersion { get; } = "5.0 (Windows)";
    public string platform { get; } = "Win32";
    public string product { get; } = "Gecko";
    public string vendor { get; } = "";
    public string language { get; } = "en-US";
    public string[] languages { get; } = ["en-US", "en"];
    public bool onLine { get; } = true;
    public bool cookieEnabled { get; } = true;
    public int hardwareConcurrency { get; } = Environment.ProcessorCount;
    public int maxTouchPoints { get; } = 0;

    public bool javaEnabled() => false;
}
