using System.Text;

namespace Lite;

/// <summary>
/// The Encoding Standard's canonical name for an encoding: <c>document.characterSet</c> (and its
/// inputEncoding/charset aliases) reports the name column of the encoding table, not .NET's
/// WebName — "ISO-8859-2", "windows-1252", "Shift_JIS", "IBM866", and so on. The mapping is by
/// code page because the label-to-encoding resolution itself already lives in the framework's
/// WHATWG label table.
/// </summary>
internal static class EncodingNames
{
    internal const string Utf8 = "UTF-8";

    /// <summary>The canonical (name-column) spelling for a resolved encoding.</summary>
    internal static string Canonical(Encoding encoding) => encoding.CodePage switch
    {
        65001 => "UTF-8",
        866 => "IBM866",
        28592 => "ISO-8859-2",
        28593 => "ISO-8859-3",
        28594 => "ISO-8859-4",
        28595 => "ISO-8859-5",
        28596 => "ISO-8859-6",
        28597 => "ISO-8859-7",
        28598 => "ISO-8859-8",
        38598 => "ISO-8859-8-I",
        28600 => "ISO-8859-10",
        28603 => "ISO-8859-13",
        28604 => "ISO-8859-14",
        28605 => "ISO-8859-15",
        28606 => "ISO-8859-16",
        20866 => "KOI8-R",
        21866 => "KOI8-U",
        10000 => "macintosh",
        874 => "windows-874",
        1250 => "windows-1250",
        1251 => "windows-1251",
        1252 => "windows-1252",
        1253 => "windows-1253",
        1254 => "windows-1254",
        1255 => "windows-1255",
        1256 => "windows-1256",
        1257 => "windows-1257",
        1258 => "windows-1258",
        10007 => "x-mac-cyrillic",
        936 => "GBK",
        54936 => "gb18030",
        950 => "Big5",
        51932 => "EUC-JP",
        50220 or 50221 or 50222 => "ISO-2022-JP",
        932 => "Shift_JIS",
        949 => "EUC-KR",
        1200 => "UTF-16LE",
        1201 => "UTF-16BE",
        _ => encoding.WebName.ToUpperInvariant(),
    };
}
