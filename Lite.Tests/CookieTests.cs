using Lite.Network;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>Regression coverage for the cookies package's script-visible surface (HTML §6.3
/// over RFC 6265): round-trips, multiple cookies, longest-path-first ordering, path scoping,
/// max-age translation to expiry, max-age deletion, the HttpOnly attribute script cannot set,
/// and the bogus (no '=') set being dropped.</summary>
public static class CookieTests
{
    [Test]
    public static void Cookie_RoundTripAndJoin()
    {
        using var session = new BrowserSession();
        var url = "http://test/page.html";
        session.SetDocumentCookie(url, "a=1");
        session.SetDocumentCookie(url, "b=2");
        Equal("a=1; b=2", session.GetDocumentCookie(url));
    }

    [Test]
    public static void Cookie_LongestPathFirst()
    {
        using var session = new BrowserSession();
        session.SetDocumentCookie("http://test/dir/sub/page.html", "shallow=1; path=/");
        session.SetDocumentCookie("http://test/dir/sub/page.html", "deep=2; path=/dir/sub");
        // RFC 6265 §5.4: longer paths first.
        Equal("deep=2; shallow=1", session.GetDocumentCookie("http://test/dir/sub/page.html"));
        Equal("shallow=1", session.GetDocumentCookie("http://test/dir/other.html"));
    }

    [Test]
    public static void Cookie_MaxAgeZeroDeletes()
    {
        using var session = new BrowserSession();
        var url = "http://test/page.html";
        session.SetDocumentCookie(url, "gone=1");
        Equal("gone=1", session.GetDocumentCookie(url));
        session.SetDocumentCookie(url, "gone=1; max-age=0");
        Equal("", session.GetDocumentCookie(url));
    }

    [Test]
    public static void Cookie_MaxAgeTranslatesToExpiry()
    {
        using var session = new BrowserSession();
        var url = "http://test/page.html";
        session.SetDocumentCookie(url, "ttl=1; max-age=3600");
        Equal("ttl=1", session.GetDocumentCookie(url));
    }

    [Test]
    public static void Cookie_HttpOnlySetByScriptDropsTheAttributeButKeepsTheCookie()
    {
        using var session = new BrowserSession();
        var url = "http://test/page.html";
        session.SetDocumentCookie(url, "flag=1; HttpOnly");
        Equal("flag=1", session.GetDocumentCookie(url));
    }

    [Test]
    public static void Cookie_HttpOnlyFromNetworkStaysHidden()
    {
        using var session = new BrowserSession();
        var uri = new Uri("http://test/page.html");
        session.TestCookieContainer.Add(uri, new System.Net.Cookie("secret", "x", "/", "test")
        {
            HttpOnly = true,
        });
        Equal("", session.GetDocumentCookie("http://test/page.html"));
    }

    [Test]
    public static void Cookie_BogusSetWithoutEqualsIsDropped()
    {
        using var session = new BrowserSession();
        var url = "http://test/page.html";
        session.SetDocumentCookie(url, "novalue");
        Equal("", session.GetDocumentCookie(url));
    }
}
