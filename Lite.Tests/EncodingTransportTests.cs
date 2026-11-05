using Lite.Conformance.Harness;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>The §8.2.2.1 sniffing order measured against real network responses: each
/// fixture is served over HTTP by the harness server with the Content-Type charset a real
/// transport would send (the WPT <c>.headers</c> convention), loads through the engine's
/// navigation pipeline, and the applied encoding is observed through the decoded DOM text.
/// The byte-exact fixtures live in <c>overrides/lite/harness/encoding/</c>; each asserts an
/// order position the byte-level EncodingSnifferTests already cover in isolation, so a
/// regression here means the header→sniffer plumbing, not the sniffer's decision logic.</summary>
public static class EncodingTransportTests
{
    [Test]
    public static void RealResponses_FollowTheSection8221Order()
    {
        ConformanceServer.Start(cssRegressionMode: false);
        try
        {
            // A transport charset decodes the bytes — no UTF-8 sniffing, even though the
            // bytes are well-formed UTF-8 (the WPT encoding suite's sniffing.html contract:
            // "No (UTF-8) sniffing allowed").
            Equal("\u00C3\u201A\u00E2\u201A\u00AC", Text("lite/harness/encoding/transport-1252.html"));
            // A BOM beats the transport charset and is consumed.
            Equal("\u20ACbom", Text("lite/harness/encoding/bom-wins.html"));
            // The transport charset beats a meta declaration.
            Equal("\u20ACmeta", Text("lite/harness/encoding/meta-vs-transport.html"));
            // With the transport silent, the 1024-byte prescan reads the meta declaration;
            // the body's lone C2 byte decodes as U+00C2, not the UTF-8 replacement char.
            Equal("\u00C2prescan", Text("lite/harness/encoding/meta-prescan.html"));
            // Nothing declared anywhere: the implementation-defined default (UTF-8) applies.
            Equal("\u20ACdefault", Text("lite/harness/encoding/default-utf8.html"));
        }
        finally { ConformanceServer.Stop(); }
    }

    private static string Text(string path)
    {
        var (_, engine) = HeadlessPage.Load(ConformanceServer.TestUrl(path));
        return engine.RawEngine.Evaluate("document.body.textContent").AsString();
    }
}
