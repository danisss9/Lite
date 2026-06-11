namespace Lite.Scripting.Dom;

/// <summary>
/// window.location. Reads its components from the engine's current URL (parsed via
/// <see cref="JsUrl"/>); assigning href / a component, or calling assign/replace/reload,
/// routes through <see cref="JsEngine.Navigate"/> which decides same-document (fragment)
/// vs. cross-document navigation.
/// </summary>
public class JsLocation
{
    private readonly JsEngine _engine;

    internal JsLocation(JsEngine engine) => _engine = engine;

    private JsUrl Parsed()
    {
        try { return new JsUrl(_engine.CurrentUrl); }
        catch { return new JsUrl("about:blank"); }
    }

    public string href
    {
        get => _engine.CurrentUrl;
        set => _engine.Navigate(value, replace: false);
    }

    public string protocol
    {
        get => Parsed().protocol;
        set { var u = Parsed(); u.protocol = value; _engine.Navigate(u.href, false); }
    }

    public string host
    {
        get => Parsed().host;
        set { var u = Parsed(); u.host = value; _engine.Navigate(u.href, false); }
    }

    public string hostname
    {
        get => Parsed().hostname;
        set { var u = Parsed(); u.hostname = value; _engine.Navigate(u.href, false); }
    }

    public string port
    {
        get => Parsed().port;
        set { var u = Parsed(); u.port = value; _engine.Navigate(u.href, false); }
    }

    public string pathname
    {
        get => Parsed().pathname;
        set { var u = Parsed(); u.pathname = value; _engine.Navigate(u.href, false); }
    }

    public string search
    {
        get => Parsed().search;
        set { var u = Parsed(); u.search = value; _engine.Navigate(u.href, false); }
    }

    public string hash
    {
        get => Parsed().hash;
        set { var u = Parsed(); u.hash = value; _engine.Navigate(u.href, false); }
    }

    public string origin => Parsed().origin;

    public void assign(string url) => _engine.Navigate(url, replace: false);

    public void replace(string url) => _engine.Navigate(url, replace: true);

    public void reload() => _engine.RequestNavigation(_engine.CurrentUrl);

    public override string ToString() => _engine.CurrentUrl;
    public string toString() => _engine.CurrentUrl;
}
