namespace Lite.Scripting.Dom;

/// <summary>
/// WHATWG URL, backed by mutable component fields (System.Uri is immutable, and the spec
/// requires settable properties). Parsing leans on System.Uri; the curated WPT url/ subset
/// drives correctness. <see cref="searchParams"/> is live-linked back to <see cref="search"/>.
/// </summary>
public class JsUrl
{
    private string _scheme = "";
    private string _username = "";
    private string _password = "";
    private string _host = "";
    private int? _port;
    private string _path = "";
    private string _query = "";     // without leading '?'
    private string _fragment = "";  // without leading '#'

    private JsUrlSearchParams? _searchParams;

    public JsUrl(string url) : this(url, null) { }

    public JsUrl(string url, string? @base)
    {
        Uri uri;
        if (@base is not null)
        {
            if (!Uri.TryCreate(new Uri(@base, UriKind.Absolute), url, out uri!))
                throw new InvalidOperationException($"Invalid URL: {url}");
        }
        else if (!Uri.TryCreate(url, UriKind.Absolute, out uri!))
        {
            throw new InvalidOperationException($"Invalid URL: {url}");
        }
        LoadFrom(uri);
    }

    private void LoadFrom(Uri uri)
    {
        _scheme = uri.Scheme;
        _host = uri.Host;
        _port = uri.IsDefaultPort || uri.Port < 0 ? null : uri.Port;
        _path = string.IsNullOrEmpty(uri.AbsolutePath) ? "" : uri.AbsolutePath;
        _query = uri.Query.StartsWith('?') ? uri.Query[1..] : uri.Query;
        _fragment = uri.Fragment.StartsWith('#') ? uri.Fragment[1..] : uri.Fragment;
        var userInfo = uri.UserInfo;
        if (!string.IsNullOrEmpty(userInfo))
        {
            var colon = userInfo.IndexOf(':');
            if (colon < 0) { _username = userInfo; _password = ""; }
            else { _username = userInfo[..colon]; _password = userInfo[(colon + 1)..]; }
        }
    }

    // ---- components ----
    public string protocol
    {
        get => _scheme + ":";
        set => _scheme = value.TrimEnd(':');
    }

    public string username { get => _username; set => _username = value; }
    public string password { get => _password; set => _password = value; }

    public string hostname { get => _host; set => _host = value; }

    public string host
    {
        get => _port is { } p ? $"{_host}:{p}" : _host;
        set
        {
            var colon = value.LastIndexOf(':');
            if (colon >= 0 && int.TryParse(value[(colon + 1)..], out var p))
            {
                _host = value[..colon];
                _port = p;
            }
            else { _host = value; _port = null; }
        }
    }

    public string port
    {
        get => _port?.ToString() ?? "";
        set => _port = int.TryParse(value, out var p) ? p : null;
    }

    public string pathname
    {
        get => _path;
        set => _path = value.StartsWith('/') || value.Length == 0 ? value : "/" + value;
    }

    public string search
    {
        get => string.IsNullOrEmpty(_query) ? "" : "?" + _query;
        set
        {
            _query = value.StartsWith('?') ? value[1..] : value;
            _searchParams?.Parse(_query);
        }
    }

    public string hash
    {
        get => string.IsNullOrEmpty(_fragment) ? "" : "#" + _fragment;
        set => _fragment = value.StartsWith('#') ? value[1..] : value;
    }

    public string origin
    {
        get
        {
            if (string.IsNullOrEmpty(_host)) return "null";
            return _port is { } p ? $"{_scheme}://{_host}:{p}" : $"{_scheme}://{_host}";
        }
    }

    public string href
    {
        get => BuildHref();
        set
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
                throw new InvalidOperationException($"Invalid URL: {value}");
            LoadFrom(uri);
            _searchParams?.Parse(_query);
        }
    }

    /// <summary>Live URLSearchParams view of the query; mutating it updates <see cref="search"/>.</summary>
    public JsUrlSearchParams searchParams
    {
        get
        {
            if (_searchParams is null)
            {
                _searchParams = new JsUrlSearchParams(_query);
                _searchParams.OnChange = sp => _query = sp.ToString();
            }
            return _searchParams;
        }
    }

    private string BuildHref()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(_scheme).Append(':');
        // Special schemes use the authority form "//".
        sb.Append("//");
        if (!string.IsNullOrEmpty(_username))
        {
            sb.Append(_username);
            if (!string.IsNullOrEmpty(_password)) sb.Append(':').Append(_password);
            sb.Append('@');
        }
        sb.Append(_host);
        if (_port is { } p) sb.Append(':').Append(p);
        sb.Append(_path.Length == 0 ? "/" : _path);
        sb.Append(search);
        sb.Append(hash);
        return sb.ToString();
    }

    public override string ToString() => BuildHref();
    public string toString() => BuildHref();
    public string toJSON() => BuildHref();
}
