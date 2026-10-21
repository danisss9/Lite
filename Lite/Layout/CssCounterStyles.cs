namespace Lite.Layout;

/// <summary>CSS 2.1 §4.3.1 counter (list) styles: the text a counter's value renders as in
/// 'counter(name, style)' and for 'list-style-type' markers. One table serves both readers —
/// ParseContentValue and the list-marker painter — so the two can never drift. Unknown style
/// names fall back to decimal (§4.3.1: an unrecognized value keeps the default rendering);
/// the bullet styles return null and the caller draws the glyph itself.</summary>
internal static class CssCounterStyles
{
    /// <summary>Every keyword CSS 2.1 defines for list-style-type (§4.3.1) plus 'none'.</summary>
    internal static readonly HashSet<string> Known = new(StringComparer.Ordinal)
    {
        "disc", "circle", "square", "decimal", "decimal-leading-zero",
        "lower-roman", "upper-roman", "lower-alpha", "lower-latin",
        "upper-alpha", "upper-latin", "lower-greek",
        "armenian", "georgian", "hebrew",
        "hiragana", "hiragana-iroha", "katakana", "katakana-iroha",
        "cjk-ideographic", "none",
    };

    /// <summary>True when a marker for this style is text and takes the "." suffix; false
    /// for the drawn bullets and 'none'.</summary>
    internal static bool HasTextMarker(string style) => style is not ("disc" or "circle" or "square" or "none");

    /// <summary>The counter's text as the 'content' property renders it (§12.4): the bullet
    /// styles are the glyph characters themselves and 'none' is empty.</summary>
    internal static string Content(int value, string style) => style switch
    {
        "disc" => "\u2022",
        "circle" => "\u25E6",
        "square" => "\u25AA",
        "none" or "" => "",
        _ => Format(value, style) ?? value.ToString(),
    };

    /// <summary>The counter's text in the given style, without any marker suffix. Returns
    /// null for the bullet styles (the caller draws the glyph) and for 'none'.</summary>
    internal static string? Format(int value, string style) => style switch
    {
        "disc" or "circle" or "square" or "none" or "" => null,
        "decimal" => value.ToString(),
        "decimal-leading-zero" => value.ToString("00"),
        "lower-alpha" or "lower-latin" => Alphabetic(value, LowerLatin),
        "upper-alpha" or "upper-latin" => Alphabetic(value, UpperLatin),
        "lower-roman" => Roman(value).ToLowerInvariant(),
        "upper-roman" => Roman(value),
        "lower-greek" => Alphabetic(value, LowerGreek),
        "armenian" => Additive(value, Armenian),
        "georgian" => Additive(value, Georgian),
        "hebrew" => Hebrew(value),
        "hiragana" => Alphabetic(value, Hiragana),
        "hiragana-iroha" => Alphabetic(value, HiraganaIroha),
        "katakana" => Alphabetic(value, Katakana),
        "katakana-iroha" => Alphabetic(value, KatakanaIroha),
        "cjk-ideographic" => CjkIdeographic(value),
        _ => value.ToString(),
    };

    // Alphabetic systems (§4.3.1: "the specified sequence of characters"), values below 1
    // and beyond the sequence fall back to decimal.
    private static readonly string[] LowerLatin = ["a", "b", "c", "d", "e", "f", "g", "h", "i", "j", "k", "l", "m", "n", "o", "p", "q", "r", "s", "t", "u", "v", "w", "x", "y", "z"];
    private static readonly string[] UpperLatin = LowerLatin.Select(c => c.ToUpperInvariant()).ToArray();
    private static readonly string[] LowerGreek = ["α", "β", "γ", "δ", "ε", "ζ", "η", "θ", "ι", "κ", "λ", "μ", "ν", "ξ", "ο", "π", "ρ", "σ", "τ", "υ", "φ", "χ", "ψ", "ω"];
    private static readonly string[] Hiragana = ["あ", "い", "う", "え", "お", "か", "き", "く", "け", "こ", "さ", "し", "す", "せ", "そ", "た", "ち", "つ", "て", "と", "な", "に", "ぬ", "ね", "の", "は", "ひ", "ふ", "へ", "ほ", "ま", "み", "む", "め", "も", "や", "ゆ", "よ", "ら", "り", "る", "れ", "ろ", "わ", "ゐ", "ゑ", "を", "ん"];
    private static readonly string[] HiraganaIroha = ["い", "ろ", "は", "に", "ほ", "へ", "と", "ち", "り", "ぬ", "る", "を", "わ", "か", "よ", "た", "れ", "そ", "つ", "ね", "な", "ら", "む", "う", "ゐ", "の", "お", "く", "や", "ま", "け", "ふ", "こ", "え", "て", "あ", "さ", "き", "ゆ", "め", "み", "し", "ゑ", "ひ", "も", "せ", "す"];
    private static readonly string[] Katakana = Hiragana.Select(c => c.ToString()).Select(HiraganaToKatakana).ToArray();
    private static readonly string[] KatakanaIroha = HiraganaIroha.Select(HiraganaToKatakana).ToArray();

    private static string HiraganaToKatakana(string h) => new(h.Select(c => (char)(c + 0x60)).ToArray());

    private static string Alphabetic(int value, string[] symbols)
    {
        if (value < 1) return value.ToString();
        var sb = new System.Text.StringBuilder();
        while (value > 0)
        {
            value--;
            sb.Insert(0, symbols[value % symbols.Length]);
            value /= symbols.Length;
        }
        return sb.ToString();
    }

    private static string Roman(int num)
    {
        if (num <= 0 || num > 3999) return num.ToString();
        string[] thousands = ["", "M", "MM", "MMM"];
        string[] hundreds = ["", "C", "CC", "CCC", "CD", "D", "DC", "DCC", "DCCC", "CM"];
        string[] tens = ["", "X", "XX", "XXX", "XL", "L", "LX", "LXX", "LXXX", "XC"];
        string[] ones = ["", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX"];
        return thousands[num / 1000] + hundreds[num % 1000 / 100] + tens[num % 100 / 10] + ones[num % 10];
    }

    // Additive systems: (weight, symbol) descending; a digit's symbol repeats like roman
    // numerals (XXX), per §4.3.1's armenian/georgian definitions.
    private static readonly (int Weight, string Symbol)[] Armenian =
    [
        (9000, "Ք"), (8000, "Փ"), (7000, "Ւ"), (6000, "Ց"), (5000, "Ր"), (4000, "Տ"),
        (3000, "Վ"), (2000, "Ս"), (1000, "Ռ"), (900, "Ջ"), (800, "Պ"), (700, "Չ"),
        (600, "Ո"), (500, "Շ"), (400, "Ն"), (300, "Յ"), (200, "Մ"), (100, "Ճ"),
        (90, "Ղ"), (80, "Ձ"), (70, "Հ"), (60, "Կ"), (50, "Ծ"), (40, "Խ"),
        (30, "Լ"), (20, "Ի"), (10, "Ժ"), (9, "Թ"), (8, "Ը"), (7, "Է"),
        (6, "Զ"), (5, "Ե"), (4, "Դ"), (3, "Գ"), (2, "Բ"), (1, "Ա"),
    ];

    private static readonly (int Weight, string Symbol)[] Georgian =
    [
        (10000, "ჵ"), (9000, "ჰ"), (8000, "ჯ"), (7000, "ჴ"), (6000, "ხ"), (5000, "ჭ"),
        (4000, "წ"), (3000, "ძ"), (2000, "ც"), (1000, "ჩ"), (900, "შ"), (800, "ყ"),
        (700, "ღ"), (600, "ქ"), (500, "ფ"), (400, "ჳ"), (300, "ტ"), (200, "ს"),
        (100, "რ"), (90, "ჟ"), (80, "პ"), (70, "ო"), (60, "ჲ"), (50, "ნ"),
        (40, "მ"), (30, "ლ"), (20, "კ"), (10, "ი"), (9, "თ"), (8, "ჱ"),
        (7, "ზ"), (6, "ვ"), (5, "ე"), (4, "დ"), (3, "გ"), (2, "ბ"), (1, "ა"),
    ];

    private static string Additive(int value, (int Weight, string Symbol)[] symbols)
    {
        if (value < 1) return value.ToString();
        var sb = new System.Text.StringBuilder();
        foreach (var (weight, symbol) in symbols)
            while (value >= weight) { sb.Append(symbol); value -= weight; }
        return sb.ToString();
    }

    /// <summary>Hebrew numbering: additive with the tradition's two exceptions — 15 and 16 are
    /// written טו/טז (9+5, 9+6) rather than 10+5/10+6 — and the group separator gershayem
    /// between the hundreds and the rest (§4.3.1 "e.g. 19 is י"ט").</summary>
    private static string Hebrew(int value)
    {
        if (value < 1 || value > 10999) return value.ToString();
        (int Weight, string Symbol)[] hundreds = [(400, "ת"), (300, "ש"), (200, "ר"), (100, "ק")];
        (int Weight, string Symbol)[] tens = [(90, "צ"), (80, "פ"), (70, "ע"), (60, "ס"),
            (50, "נ"), (40, "מ"), (30, "ל"), (20, "כ"), (10, "י")];
        (int Weight, string Symbol)[] ones = [(9, "ט"), (8, "ח"), (7, "ז"), (6, "ו"),
            (5, "ה"), (4, "ד"), (3, "ג"), (2, "ב"), (1, "א")];
        var rest = value;
        var head = new System.Text.StringBuilder();
        foreach (var (weight, symbol) in hundreds)
            while (rest >= weight) { head.Append(symbol); rest -= weight; }
        if (rest is 15 or 16) return head.Length > 0 ? head.ToString() + "״" + (rest == 15 ? "טו" : "טז") : (rest == 15 ? "טו" : "טז");
        var tail = new System.Text.StringBuilder();
        foreach (var (weight, symbol) in tens)
            while (rest >= weight) { tail.Append(symbol); rest -= weight; }
        foreach (var (weight, symbol) in ones)
            while (rest >= weight) { tail.Append(symbol); rest -= weight; }
        if (tail.Length == 0) return head.ToString();
        if (head.Length == 0) return tail.ToString();
        return head.ToString() + "״" + tail.ToString();
    }

    /// <summary>CJK ideographic numbering in groups of 万 (§4.3.1 "e.g. 5439 is 五千四百三十九").</summary>
    private static string CjkIdeographic(int value)
    {
        if (value < 0) return value.ToString();
        if (value == 0) return "零";
        string[] digits = ["", "一", "二", "三", "四", "五", "六", "七", "八", "九"];
        string[] units = ["", "十", "百", "千"];
        string Group(int n)
        {
            var sb = new System.Text.StringBuilder();
            var unit = 0;
            var lastDigit = 0;
            for (var scale = 1000; scale >= 1; scale /= 10)
            {
                var d = n / scale % 10;
                if (d == 0)
                {
                    // A zero is spoken only when it separates two nonzero digits (三百零四).
                    if (lastDigit != 0 && n % scale != 0) sb.Append("零");
                }
                else
                {
                    if (d == 1 && scale == 10 && unit == 0) sb.Append(units[1]); // 十一, not 一十一
                    else sb.Append(digits[d]).Append(units[unit]);
                    lastDigit = d;
                    unit++;
                    continue;
                }
                unit++;
            }
            return sb.ToString();
        }
        if (value < 10000) return Group(value);
        var high = value / 10000;
        var low = value % 10000;
        return Group(high) + "万" + (low > 0 && low < 1000 ? "零" : "") + (low > 0 ? Group(low) : "");
    }
}
