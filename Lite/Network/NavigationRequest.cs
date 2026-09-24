namespace Lite.Network;

/// <summary>A document navigation, including the request data needed by form submissions.</summary>
internal readonly record struct NavigationRequest(
    string Url, string Method = "GET", string? Body = null, string? ContentType = null);
