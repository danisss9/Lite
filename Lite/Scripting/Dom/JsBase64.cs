using System.Text;

namespace Lite.Scripting.Dom;

/// <summary>
/// <see cref="JsEngine"/>-global base64 helpers <c>atob</c>/<c>btoa</c> (HTML5 §6.2). Input and
/// output are byte-oriented: <c>btoa</c> rejects code points above U+00FF, and <c>atob</c> maps
/// each decoded byte to the code point of the same value (no UTF-8 transcoding), exactly as the
/// specification defines. Whitespace is ignored on decode; malformed padding or a non-alphabet
/// character raises a catchable <c>InvalidCharacterError</c> DOMException.
/// </summary>
internal static class JsBase64
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
    private static readonly int[] Reverse = BuildReverse();

    private static int[] BuildReverse()
    {
        var table = new int[128];
        Array.Fill(table, -1);
        for (var i = 0; i < Alphabet.Length; i++) table[Alphabet[i]] = i;
        return table;
    }

    internal static string Encode(string data)
    {
        var bytes = new byte[data.Length];
        for (var i = 0; i < data.Length; i++)
        {
            if (data[i] > 0xFF)
                throw JsErrors.Dom("InvalidCharacterError",
                    $"The string to be encoded contains characters outside of the Latin1 range (U+{((int)data[i]).ToString("X4")}).");
            bytes[i] = (byte)data[i];
        }
        return Convert.ToBase64String(bytes);
    }

    internal static string Decode(string data)
    {
        // 1. Strip ASCII whitespace (space, tab, LF, FF, CR).
        var compact = new StringBuilder(data.Length);
        foreach (var ch in data)
            if (ch is not (' ' or '\t' or '\n' or '\u000C' or '\r'))
                compact.Append(ch);
        var input = compact.ToString();

        // 2. A length that leaves remainder 1 can never be valid base64.
        if (input.Length % 4 == 1)
            throw JsErrors.Dom("InvalidCharacterError", "The string to be decoded is not correctly encoded.");

        // 3. Validate characters and padding: '=' may only occupy the final two positions.
        for (var i = 0; i < input.Length; i++)
        {
            var ch = input[i];
            if (ch == '=')
            {
                if (i < input.Length - 2)
                    throw JsErrors.Dom("InvalidCharacterError", "The string to be decoded is not correctly encoded.");
                continue;
            }
            if (ch >= 128 || Reverse[ch] < 0)
                throw JsErrors.Dom("InvalidCharacterError", "The string to be decoded contains invalid characters.");
        }

        // 4. Decode: six-bit groups accumulate into a bit buffer drained a byte at a time.
        var output = new byte[input.Length / 4 * 3 + 3];
        var count = 0;
        var accumulator = 0;
        var bits = 0;
        foreach (var ch in input)
        {
            if (ch == '=') break;
            accumulator = (accumulator << 6) | Reverse[ch];
            bits += 6;
            if (bits < 8) continue;
            bits -= 8;
            output[count++] = (byte)((accumulator >> bits) & 0xFF);
        }
        return Encoding.Latin1.GetString(output, 0, count);
    }
}
