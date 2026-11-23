namespace Lite;

internal static class AsciiChar
{
    public static bool IsDigit(char c) =>
#if NET7_0_OR_GREATER
        char.IsAsciiDigit(c);
#else
        (uint)(c - '0') <= '9' - '0';
#endif

    public static bool IsLetterLower(char c) =>
#if NET7_0_OR_GREATER
        char.IsAsciiLetterLower(c);
#else
        (uint)(c - 'a') <= 'z' - 'a';
#endif

    public static bool IsLetter(char c) =>
#if NET7_0_OR_GREATER
        char.IsAsciiLetter(c);
#else
        (uint)((c | 0x20) - 'a') <= 'z' - 'a';
#endif

    public static bool IsLetterOrDigit(char c) =>
#if NET7_0_OR_GREATER
        char.IsAsciiLetterOrDigit(c);
#else
        IsLetter(c) || IsDigit(c);
#endif
}
