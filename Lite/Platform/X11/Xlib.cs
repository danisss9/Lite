using System.Runtime.InteropServices;

namespace Lite.Platform.X11;

// Xlib bindings for the X11 window host (LP64: XID/Atom/KeySym are C `unsigned long` → nuint).
// Only the calls and event views the host needs are declared.
internal static class Xlib
{
    private const string Lib = "libX11.so.6";

    // Event types
    internal const int KeyPress = 2;
    internal const int KeyRelease = 3;
    internal const int ButtonPress = 4;
    internal const int ButtonRelease = 5;
    internal const int MotionNotify = 6;
    internal const int Expose = 12;
    internal const int ConfigureNotify = 22;
    internal const int ClientMessage = 33;

    // Event masks
    internal const long KeyPressMask = 1L << 0;
    internal const long KeyReleaseMask = 1L << 1;
    internal const long ButtonPressMask = 1L << 2;
    internal const long ButtonReleaseMask = 1L << 3;
    internal const long PointerMotionMask = 1L << 6;
    internal const long ExposureMask = 1L << 15;
    internal const long StructureNotifyMask = 1L << 17;
    internal const long FocusChangeMask = 1L << 21;

    // Modifier state bits
    internal const uint ShiftMask = 1 << 0;
    internal const uint ControlMask = 1 << 2;
    internal const uint Mod1Mask = 1 << 3; // Alt
    internal const uint Mod4Mask = 1 << 6; // Super/Meta

    internal const int ZPixmap = 2;
    internal const int LSBFirst = 0;
    internal const int PropModeReplace = 0;

    // Cursor font shapes (X11/cursorfont.h)
    internal const uint XC_hand2 = 60;
    internal const uint XC_left_ptr = 68;
    internal const uint XC_xterm = 152;

    // Xutf8LookupString status
    internal const int XLookupChars = 2;
    internal const int XLookupBoth = 4;

    // XIM input style
    internal const long XIMPreeditNothing = 0x0008;
    internal const long XIMStatusNothing = 0x0400;

    [DllImport(Lib)] internal static extern IntPtr XOpenDisplay(IntPtr displayName);
    [DllImport(Lib)] internal static extern int XCloseDisplay(IntPtr display);
    [DllImport(Lib)] internal static extern int XDefaultScreen(IntPtr display);
    [DllImport(Lib)] internal static extern int XDefaultDepth(IntPtr display, int screen);
    [DllImport(Lib)] internal static extern nuint XRootWindow(IntPtr display, int screen);
    [DllImport(Lib)] internal static extern nuint XWhitePixel(IntPtr display, int screen);
    [DllImport(Lib)] internal static extern nuint XBlackPixel(IntPtr display, int screen);
    [DllImport(Lib)] internal static extern int XConnectionNumber(IntPtr display);

    [DllImport(Lib)]
    internal static extern nuint XCreateSimpleWindow(IntPtr display, nuint parent, int x, int y,
        uint width, uint height, uint borderWidth, nuint border, nuint background);

    [DllImport(Lib)] internal static extern int XDestroyWindow(IntPtr display, nuint window);
    [DllImport(Lib)] internal static extern int XSelectInput(IntPtr display, nuint window, nint eventMask);
    [DllImport(Lib)] internal static extern int XMapWindow(IntPtr display, nuint window);
    [DllImport(Lib)] internal static extern int XFlush(IntPtr display);
    [DllImport(Lib)] internal static extern int XPending(IntPtr display);
    [DllImport(Lib)] internal static extern int XNextEvent(IntPtr display, out XEvent ev);
    [DllImport(Lib)] internal static extern int XPeekEvent(IntPtr display, out XEvent ev);
    [DllImport(Lib)] internal static extern int XFilterEvent(ref XEvent ev, nuint window);

    [DllImport(Lib, CharSet = CharSet.Ansi)]
    internal static extern nuint XInternAtom(IntPtr display, string atomName, bool onlyIfExists);

    [DllImport(Lib)] internal static extern int XSetWMProtocols(IntPtr display, nuint window, ref nuint protocols, int count);

    [DllImport(Lib, CharSet = CharSet.Ansi)]
    internal static extern int XStoreName(IntPtr display, nuint window, string windowName);

    [DllImport(Lib)]
    internal static extern int XChangeProperty(IntPtr display, nuint window, nuint property, nuint type,
        int format, int mode, byte[] data, int elementCount);

    [DllImport(Lib)] internal static extern int XSetClassHint(IntPtr display, nuint window, ref XClassHint hint);

    [DllImport(Lib)] internal static extern IntPtr XCreateGC(IntPtr display, nuint drawable, nuint valueMask, IntPtr values);
    [DllImport(Lib)] internal static extern int XFreeGC(IntPtr display, IntPtr gc);
    [DllImport(Lib)] internal static extern int XInitImage(ref XImage image);

    [DllImport(Lib)]
    internal static extern int XPutImage(IntPtr display, nuint drawable, IntPtr gc, ref XImage image,
        int srcX, int srcY, int destX, int destY, uint width, uint height);

    [DllImport(Lib)] internal static extern nuint XCreateFontCursor(IntPtr display, uint shape);
    [DllImport(Lib)] internal static extern int XDefineCursor(IntPtr display, nuint window, nuint cursor);
    [DllImport(Lib)] internal static extern int XFreeCursor(IntPtr display, nuint cursor);

    [DllImport(Lib)] internal static extern nuint XLookupKeysym(ref XEvent keyEvent, int index);

    [DllImport(Lib)]
    internal static extern int XLookupString(ref XEvent keyEvent, byte[] buffer, int bytes, out nuint keysym, IntPtr status);

    [DllImport(Lib, CharSet = CharSet.Ansi)]
    internal static extern IntPtr XSetLocaleModifiers(string modifiers);

    [DllImport(Lib)] internal static extern IntPtr XOpenIM(IntPtr display, IntPtr db, IntPtr resName, IntPtr resClass);
    [DllImport(Lib)] internal static extern int XCloseIM(IntPtr im);

    // XCreateIC is variadic: (im, XNInputStyle, style, XNClientWindow, window, XNFocusWindow, window, NULL).
    // Integer/pointer varargs use the regular calling convention on x86-64 SysV and AArch64 Linux.
    [DllImport(Lib, CharSet = CharSet.Ansi)]
    internal static extern IntPtr XCreateIC(IntPtr im, string inputStyleName, nint inputStyle,
        string clientWindowName, nuint clientWindow, string focusWindowName, nuint focusWindow, IntPtr terminator);

    [DllImport(Lib)] internal static extern void XDestroyIC(IntPtr ic);
    [DllImport(Lib)] internal static extern void XSetICFocus(IntPtr ic);

    [DllImport(Lib)]
    internal static extern int Xutf8LookupString(IntPtr ic, ref XEvent keyEvent, byte[] buffer, int bytes,
        out nuint keysym, out int status);
}

/// <summary>The 192-byte XEvent union with the views the host reads (LP64 offsets).</summary>
[StructLayout(LayoutKind.Explicit, Size = 192)]
internal struct XEvent
{
    [FieldOffset(0)] public int type;

    // XKeyEvent / XButtonEvent / XMotionEvent share this prefix.
    [FieldOffset(64)] public int x;
    [FieldOffset(68)] public int y;
    [FieldOffset(80)] public uint state;
    [FieldOffset(84)] public uint keycodeOrButton;

    // XExposeEvent
    [FieldOffset(56)] public int exposeCount;

    // XConfigureEvent
    [FieldOffset(56)] public int configureWidth;
    [FieldOffset(60)] public int configureHeight;

    // XClientMessageEvent (data.l[0])
    [FieldOffset(56)] public nint clientData0;
}

[StructLayout(LayoutKind.Sequential)]
internal struct XImage
{
    public int width, height, xoffset, format;
    public IntPtr data;
    public int byte_order, bitmap_unit, bitmap_bit_order, bitmap_pad, depth, bytes_per_line, bits_per_pixel;
    public nuint red_mask, green_mask, blue_mask;
    public IntPtr obdata;
    // struct funcs: filled in by XInitImage.
    public IntPtr create_image, destroy_image, get_pixel, put_pixel, sub_image, add_pixel;
}

[StructLayout(LayoutKind.Sequential)]
internal struct XClassHint
{
    public IntPtr res_name;
    public IntPtr res_class;
}

/// <summary>libc calls for the host's wake-up pipe and poll loop.</summary>
internal static class LibC
{
    internal const int O_NONBLOCK = 0x800;
    internal const int O_CLOEXEC = 0x80000;
    internal const short POLLIN = 0x1;
    internal const int LC_CTYPE = 0;

    [StructLayout(LayoutKind.Sequential)]
    internal struct PollFd
    {
        public int fd;
        public short events;
        public short revents;
    }

    [DllImport("libc", SetLastError = true)] internal static extern int pipe2(int[] fds, int flags);
    [DllImport("libc", SetLastError = true)] internal static extern nint write(int fd, ref byte buffer, nint count);
    [DllImport("libc", SetLastError = true)] internal static extern nint read(int fd, byte[] buffer, nint count);
    [DllImport("libc", SetLastError = true)] internal static extern int close(int fd);
    [DllImport("libc", SetLastError = true)] internal static extern int poll([In, Out] PollFd[] fds, nuint count, int timeoutMs);

    [DllImport("libc", CharSet = CharSet.Ansi)]
    internal static extern IntPtr setlocale(int category, string locale);
}
