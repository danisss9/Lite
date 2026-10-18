namespace Lite.Platform.X11;

/// <summary>Maps X11 KeySyms to the Win32 virtual-key codes BrowserWindow's key tables use.</summary>
internal static class X11Keys
{
    public static int ToVirtualKey(nuint keysym) => (ulong)keysym switch
    {
        0xff08 => 0x08,                                   // BackSpace
        0xff09 or 0xfe20 => 0x09,                         // Tab, ISO_Left_Tab
        0xff0d or 0xff8d => 0x0D,                         // Return, KP_Enter
        0xffe1 or 0xffe2 => 0x10,                         // Shift_L/R
        0xffe3 or 0xffe4 => 0x11,                         // Control_L/R
        0xffe9 or 0xffea or 0xfe03 => 0x12,               // Alt_L/R, ISO_Level3_Shift (AltGr)
        0xff13 => 0x13,                                   // Pause
        0xffe5 => 0x14,                                   // Caps_Lock
        0xff1b => 0x1B,                                   // Escape
        0x20 => 0x20,                                     // space
        0xff55 or 0xff9a => 0x21,                         // Prior (PageUp), KP_Prior
        0xff56 or 0xff9b => 0x22,                         // Next (PageDown), KP_Next
        0xff57 or 0xff9c => 0x23,                         // End, KP_End
        0xff50 or 0xff95 => 0x24,                         // Home, KP_Home
        0xff51 or 0xff96 => 0x25,                         // Left, KP_Left
        0xff52 or 0xff97 => 0x26,                         // Up, KP_Up
        0xff53 or 0xff98 => 0x27,                         // Right, KP_Right
        0xff54 or 0xff99 => 0x28,                         // Down, KP_Down
        0xff63 or 0xff9e => 0x2D,                         // Insert, KP_Insert
        0xffff or 0xff9f => 0x2E,                         // Delete, KP_Delete
        >= 0x30 and <= 0x39 => (int)keysym,               // 0-9
        >= 0x41 and <= 0x5A => (int)keysym,               // A-Z
        >= 0x61 and <= 0x7A => (int)keysym - 0x20,        // a-z
        0xffeb => 0x5B,                                   // Super_L
        0xffec => 0x5C,                                   // Super_R
        >= 0xffb0 and <= 0xffb9 => 0x60 + (int)(keysym - 0xffb0), // KP_0-KP_9
        0xffaa => 0x6A,                                   // KP_Multiply
        0xffab => 0x6B,                                   // KP_Add
        0xffad => 0x6D,                                   // KP_Subtract
        0xffae => 0x6E,                                   // KP_Decimal
        0xffaf => 0x6F,                                   // KP_Divide
        >= 0xffbe and <= 0xffc9 => 0x70 + (int)(keysym - 0xffbe), // F1-F12
        0x3b => 0xBA,                                     // semicolon
        0x3d => 0xBB,                                     // equal
        0x2c => 0xBC,                                     // comma
        0x2d => 0xBD,                                     // minus
        0x2e => 0xBE,                                     // period
        0x2f => 0xBF,                                     // slash
        0x60 => 0xC0,                                     // grave
        0x5b => 0xDB,                                     // bracketleft
        0x5c => 0xDC,                                     // backslash
        0x5d => 0xDD,                                     // bracketright
        0x27 => 0xDE,                                     // apostrophe
        _ => 0,
    };

    /// <summary>The text a character keysym stands for: Latin-1 keysyms map directly, and
    /// 0x01xxxxxx keysyms carry a Unicode code point. Null for function/modifier keysyms.</summary>
    public static string? ToText(nuint keysym) => (ulong)keysym switch
    {
        >= 0x20 and <= 0x7e or >= 0xa0 and <= 0xff => ((char)keysym).ToString(),
        >= 0x01000100 and <= 0x0110ffff and not (>= 0x0100d800 and <= 0x0100dfff) =>
            char.ConvertFromUtf32((int)(keysym - 0x01000000)),
        _ => null,
    };
}
