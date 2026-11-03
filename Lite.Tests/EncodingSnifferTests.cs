using System.Text;
using Lite;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>REC §8.2.2.1 encoding determination over raw document bytes: BOM precedence,
/// transport charset, the 1024-byte meta prescan, the implementation-defined UTF-8 default,
/// and the late-declaration restart (the load-time equivalent of the parser's encoding
/// change re-decode).</summary>
public static class EncodingSnifferTests
{
    private static readonly Encoding Cp1252 = Encoding.GetEncoding("windows-1252");

    [Test]
    public static void Bom_WinsOverTransportCharset()
    {
        var bytes = Combine(new byte[] { 0xEF, 0xBB, 0xBF }, Encoding.UTF8.GetBytes("héllo"));
        Equal("héllo", EncodingSniffer.DecodeDocument(bytes, "windows-1252"));
    }

    [Test]
    public static void Bom_ConsumedFromTheDecodedText()
    {
        // The preprocessing step strips the mark: a U+FEFF must not survive into the
        // document string (it would become text content).
        var bytes = Combine(new byte[] { 0xEF, 0xBB, 0xBF }, Encoding.UTF8.GetBytes("<p>x"));
        Equal("<p>x", EncodingSniffer.DecodeDocument(bytes, null));
    }

    [Test]
    public static void Bom_Utf16LeDecodesWholeDocument()
    {
        var bytes = Combine(new byte[] { 0xFF, 0xFE }, Encoding.Unicode.GetBytes("héllo"));
        Equal("héllo", EncodingSniffer.DecodeDocument(bytes, null));
    }

    [Test]
    public static void TransportCharset_DecodesBytes()
    {
        Equal("héllo", EncodingSniffer.DecodeDocument(Cp1252.GetBytes("héllo"), "windows-1252"));
    }

    [Test]
    public static void MetaPrescan_DecodesWhenTransportIsSilent()
    {
        var bytes = Cp1252.GetBytes("<meta charset=\"windows-1252\"><p>é");
        Equal("<meta charset=\"windows-1252\"><p>é", EncodingSniffer.DecodeDocument(bytes, null));
    }

    [Test]
    public static void MetaHttpEquivalent_PrescanExtractsCharsetFromContent()
    {
        var text = "<meta http-equiv=\"Content-Type\" content=\"text/html; charset=windows-1252\"><p>é";
        Equal(text, EncodingSniffer.DecodeDocument(Cp1252.GetBytes(text), null));
    }

    [Test]
    public static void LateMetaDeclaration_RestartsTheDecode()
    {
        // The declaration sits beyond the 1024-byte prescan window: the tentative UTF-8
        // decode hits U+FFFD, then the restart re-decodes with the declared encoding.
        var head = new string('a', 1200);
        var bytes = Cp1252.GetBytes($"<p>{head}</p><meta charset=\"windows-1252\"><p>é");
        var decoded = EncodingSniffer.DecodeDocument(bytes, null);
        True(decoded.Contains('é'), "restart did not re-decode with windows-1252");
        True(!decoded.Contains('\uFFFD'), "replacement character survived the restart");
    }

    [Test]
    public static void DeclaredUtf16_NormalizesToUtf8()
    {
        var bytes = Encoding.UTF8.GetBytes("<meta charset=\"utf-16\">héllo");
        Equal("<meta charset=\"utf-16\">héllo", EncodingSniffer.DecodeDocument(bytes, null));
    }

    [Test]
    public static void NoDeclaration_FallsBackToUtf8()
    {
        Equal("héllo", EncodingSniffer.DecodeDocument(Encoding.UTF8.GetBytes("héllo"), null));
    }

    [Test]
    public static void CommentedMeta_IsNotPrescanned()
    {
        // The prescan skips comments: a declaration inside one never applies.
        var bytes = Cp1252.GetBytes("<!-- <meta charset=\"windows-1252\"> --><p>é");
        var decoded = EncodingSniffer.DecodeDocument(bytes, null);
        True(decoded.Contains('\uFFFD'), "commented-out declaration must not apply");
    }

    private static byte[] Combine(byte[] first, byte[] second)
    {
        var result = new byte[first.Length + second.Length];
        first.CopyTo(result, 0);
        second.CopyTo(result, first.Length);
        return result;
    }
}
