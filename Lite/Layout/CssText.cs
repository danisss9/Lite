namespace Lite.Layout;

/// <summary>CSS white space (§16.6.1: space, tab, line feed, carriage return; form feed to the
/// tokenizer). string.Trim and char.IsWhiteSpace also strip U+00A0 and the U+2000 block, which
/// CSS keeps as ordinary characters — a cell's leading "&amp;nbsp;Text" must keep its space.</summary>
internal static class CssText
{
    internal static readonly char[] WhiteSpace = [' ', '\t', '\n', '\r', '\f'];

    internal static string TrimCss(this string text) => text.Trim(WhiteSpace);
    internal static string TrimStartCss(this string text) => text.TrimStart(WhiteSpace);
    internal static string TrimEndCss(this string text) => text.TrimEnd(WhiteSpace);

    /// <summary>True for an empty run or one made only of collapsible white space.</summary>
    internal static bool IsCssWhiteSpaceOnly(this string? text) =>
        string.IsNullOrEmpty(text) || text.AsSpan().Trim(WhiteSpace).IsEmpty;
}
