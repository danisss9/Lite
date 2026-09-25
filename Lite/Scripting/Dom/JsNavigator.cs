namespace Lite.Scripting.Dom;

/// <summary>Minimal navigator object for feature detection and UA strings.</summary>
public class JsNavigator
{
    public string userAgent { get; } = Lite.Network.BrowserSession.UserAgent;
    public JsNavigator() : this(null) { }
    internal JsNavigator(Lite.Network.BrowserSession? session)
    {
        language = session?.Language ?? "en-US";
        var neutral = language.Split('-')[0];
        languages = neutral == language ? [language] : [language, neutral];
    }
    public string appName { get; } = "Netscape";
    public string appVersion { get; } = "5.0 (Windows)";
    public string platform { get; } = "Win32";
    public string product { get; } = "Gecko";
    public string vendor { get; } = "";
    public string language { get; }
    public string[] languages { get; }
    public bool onLine { get; } = true;
    public bool cookieEnabled { get; } = true;
    public int hardwareConcurrency { get; } = Environment.ProcessorCount;
    public int maxTouchPoints { get; } = 0;

    public bool javaEnabled() => false;
}
