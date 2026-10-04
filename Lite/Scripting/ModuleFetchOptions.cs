namespace Lite.Scripting;

/// <summary>The HTML fetch-options subset a module fetch carries: the referrer URL sent as the
/// Referer header, and the credentials mode — "omit", "same-origin" (cookies on same-origin
/// requests only), or "include" (cookies on cross-origin requests too, which then require an
/// exact-origin Access-Control-Allow-Origin). A module graph's descendants inherit the root
/// fetch's credentials mode, while each fetch's referrer is the importing module's URL.</summary>
internal sealed record ModuleFetchOptions(string? Referrer = null, string CredentialsMode = "same-origin")
{
    /// <summary>Maps a module script's crossorigin content attribute to the credentials mode
    /// (HTML §4.12.1): absent or "anonymous" is "same-origin", "use-credentials" is "include".</summary>
    internal static string CredentialsFor(string? crossOrigin) =>
        string.Equals(crossOrigin, "use-credentials", StringComparison.OrdinalIgnoreCase) ? "include" : "same-origin";
}
