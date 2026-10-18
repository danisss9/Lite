using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Lite.Models;
using Lite.Platform.X11;

namespace Lite.Platform;

/// <summary>
/// X11 window host for Linux (also runs on Wayland desktops through XWayland). A single UI
/// thread owns the Xlib connection; other threads wake it through a self-pipe, and the loop
/// poll()s the X socket and that pipe with the animation timer's deadline as the timeout.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class X11WindowHost(BrowserWindow window) : IWindowHost
{
    private IntPtr _display;
    private nuint _window;
    private IntPtr _gc;
    private IntPtr _im;
    private IntPtr _ic;
    private int _depth;
    private nuint _wmDeleteWindow;
    private nuint _netWmName;
    private nuint _utf8String;
    private readonly int[] _wakePipe = [-1, -1];
    private readonly object _wakeLock = new();
    private IntPtr _className;
    private IntPtr _classClass;

    private int _width;
    private int _height;
    private bool _dirty;
    private bool _running;

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private uint _timerIntervalMs;
    private long _nextTickMs;

    private readonly Dictionary<CursorType, nuint> _cursors = new();
    private CursorType _currentCursor = CursorType.Default;

    private readonly byte[] _textBuffer = new byte[64];

    public bool Create(string title, int width, int height)
    {
        // Let XIM decode composed/non-ASCII input in the user's locale.
        LibC.setlocale(LibC.LC_CTYPE, "");
        Xlib.XSetLocaleModifiers("");

        _display = Xlib.XOpenDisplay(IntPtr.Zero);
        if (_display == IntPtr.Zero)
        {
            Console.WriteLine("Cannot open the X display (is DISPLAY set? On Wayland, XWayland is required).");
            return false;
        }

        var screen = Xlib.XDefaultScreen(_display);
        _depth = Xlib.XDefaultDepth(_display, screen);
        if (_depth is not (24 or 32))
            Console.WriteLine($"[X11] Unsupported default visual depth {_depth}; colors may be wrong.");

        _window = Xlib.XCreateSimpleWindow(_display, Xlib.XRootWindow(_display, screen),
            100, 100, (uint)width, (uint)height, 0,
            Xlib.XBlackPixel(_display, screen), Xlib.XWhitePixel(_display, screen));
        if (_window == 0) return false;

        Xlib.XSelectInput(_display, _window, (nint)(Xlib.ExposureMask | Xlib.StructureNotifyMask |
            Xlib.KeyPressMask | Xlib.KeyReleaseMask | Xlib.ButtonPressMask | Xlib.ButtonReleaseMask |
            Xlib.PointerMotionMask | Xlib.FocusChangeMask));

        // Ask the window manager for a ClientMessage instead of killing the connection on close.
        _wmDeleteWindow = Xlib.XInternAtom(_display, "WM_DELETE_WINDOW", false);
        Xlib.XSetWMProtocols(_display, _window, ref _wmDeleteWindow, 1);
        _netWmName = Xlib.XInternAtom(_display, "_NET_WM_NAME", false);
        _utf8String = Xlib.XInternAtom(_display, "UTF8_STRING", false);

        _className = Marshal.StringToHGlobalAnsi("lite");
        _classClass = Marshal.StringToHGlobalAnsi("Lite");
        var classHint = new XClassHint { res_name = _className, res_class = _classClass };
        Xlib.XSetClassHint(_display, _window, ref classHint);
        SetTitle(title);

        _gc = Xlib.XCreateGC(_display, _window, 0, IntPtr.Zero);

        _im = Xlib.XOpenIM(_display, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (_im != IntPtr.Zero)
            _ic = Xlib.XCreateIC(_im, "inputStyle", (nint)(Xlib.XIMPreeditNothing | Xlib.XIMStatusNothing),
                "clientWindow", _window, "focusWindow", _window, IntPtr.Zero);

        if (LibC.pipe2(_wakePipe, LibC.O_NONBLOCK | LibC.O_CLOEXEC) != 0)
        {
            Console.WriteLine($"[X11] pipe2 failed (errno {Marshal.GetLastPInvokeError()}).");
            return false;
        }

        // Lay out at the requested size before mapping so the first Expose has a frame to show
        // (the Win32 host gets the same from the WM_SIZE sent while the window is shown).
        Resize(width, height);
        Xlib.XMapWindow(_display, _window);
        Xlib.XFlush(_display);
        return true;
    }

    public void RunLoop()
    {
        var fds = new LibC.PollFd[]
        {
            new() { fd = Xlib.XConnectionNumber(_display), events = LibC.POLLIN },
            new() { fd = _wakePipe[0], events = LibC.POLLIN },
        };
        var drain = new byte[64];

        _running = true;
        while (_running)
        {
            while (_running && Xlib.XPending(_display) > 0)
            {
                Xlib.XNextEvent(_display, out var ev);
                HandleEvent(ref ev);
            }
            if (!_running) break;

            if (_timerIntervalMs > 0 && _clock.ElapsedMilliseconds >= _nextTickMs)
            {
                _nextTickMs = _clock.ElapsedMilliseconds + _timerIntervalMs;
                window.OnTimer();
            }

            if (_dirty) Paint();
            Xlib.XFlush(_display);

            // Handlers may have pulled more events into Xlib's queue; handle them before sleeping.
            if (Xlib.XPending(_display) > 0) continue;

            var timeout = _timerIntervalMs > 0
                ? (int)Math.Max(0, _nextTickMs - _clock.ElapsedMilliseconds)
                : -1;
            fds[0].revents = fds[1].revents = 0;
            if (LibC.poll(fds, (nuint)fds.Length, timeout) <= 0) continue;

            if ((fds[1].revents & LibC.POLLIN) != 0)
            {
                while (LibC.read(_wakePipe[0], drain, drain.Length) > 0) { }
                window.OnTask();
            }
        }
    }

    private void HandleEvent(ref XEvent ev)
    {
        // Give the input method first look (dead keys / compose sequences).
        if (_ic != IntPtr.Zero && Xlib.XFilterEvent(ref ev, 0) != 0) return;

        switch (ev.type)
        {
            case Xlib.Expose:
                if (ev.exposeCount == 0) _dirty = true;
                break;

            case Xlib.ConfigureNotify:
                if (ev.configureWidth != _width || ev.configureHeight != _height)
                    Resize(ev.configureWidth, ev.configureHeight);
                break;

            case Xlib.ClientMessage:
                if ((nuint)ev.clientData0 == _wmDeleteWindow) _running = false;
                break;

            case Xlib.MotionNotify:
                // Coalesce a burst of motion into its latest position.
                if (Xlib.XPending(_display) > 0 && Xlib.XPeekEvent(_display, out var next) == 0 &&
                    next.type == Xlib.MotionNotify)
                    break;
                window.OnMouseMove(ev.x, ev.y);
                UpdateCursor(ev.x, ev.y);
                break;

            case Xlib.ButtonPress:
                switch (ev.keycodeOrButton)
                {
                    case 1: window.OnMouseDown(ev.x, ev.y); break;
                    case 4: window.OnWheel(ev.x, ev.y, 120); break;  // wheel up (away from the user)
                    case 5: window.OnWheel(ev.x, ev.y, -120); break; // wheel down
                }
                break;

            case Xlib.ButtonRelease:
                if (ev.keycodeOrButton == 1)
                {
                    window.OnMouseUp(ev.x, ev.y);
                    UpdateCursor(ev.x, ev.y);
                }
                break;

            case Xlib.KeyPress:
            case Xlib.KeyRelease:
                HandleKey(ref ev);
                break;
        }
    }

    private void HandleKey(ref XEvent ev)
    {
        var down = ev.type == Xlib.KeyPress;
        var keysym = Xlib.XLookupKeysym(ref ev, 0);
        var vk = X11Keys.ToVirtualKey(keysym);
        var modifiers = new KeyModifiers(
            Ctrl: (ev.state & Xlib.ControlMask) != 0,
            Shift: (ev.state & Xlib.ShiftMask) != 0,
            Alt: (ev.state & Xlib.Mod1Mask) != 0,
            Meta: (ev.state & Xlib.Mod4Mask) != 0);
        window.OnKey(down, vk, modifiers);
        if (!down) return;

        // Character input follows the key-down, as WM_CHAR follows WM_KEYDOWN on Windows.
        if (vk == 0x08) { window.OnChar('\b'); return; }
        if (vk == 0x0D) { window.OnChar('\r'); return; }

        int length;
        nuint typedKeysym;
        if (_ic != IntPtr.Zero)
        {
            length = Xlib.Xutf8LookupString(_ic, ref ev, _textBuffer, _textBuffer.Length, out typedKeysym, out var status);
            if (status is not (Xlib.XLookupChars or Xlib.XLookupBoth)) length = 0;
        }
        else
        {
            length = Xlib.XLookupString(ref ev, _textBuffer, _textBuffer.Length, out typedKeysym, IntPtr.Zero);
        }
        if (length <= 0)
        {
            // The locale's charset could not encode the key (e.g. "é" under LANG=C): fall back
            // to the keysym's own Unicode value.
            if (X11Keys.ToText(typedKeysym) is { } keysymText && (ev.state & Xlib.ControlMask) == 0)
                foreach (var c in keysymText) window.OnChar(c);
            return;
        }

        var text = _ic != IntPtr.Zero
            ? Encoding.UTF8.GetString(_textBuffer, 0, length)
            : Encoding.Latin1.GetString(_textBuffer, 0, length);
        foreach (var c in text)
            if (!char.IsControl(c)) window.OnChar(c);
    }

    private void Resize(int width, int height)
    {
        _width = width;
        _height = height;
        window.OnResize(width, height);
    }

    private void Paint()
    {
        _dirty = false;
        var (pixels, width, height) = window.Frame;
        if (pixels == IntPtr.Zero || width <= 0 || height <= 0) return;

        // Skia's BGRA8888 buffer read as little-endian 32-bit words is 0xAARRGGBB, which is
        // exactly a 24/32-bit TrueColor ZPixmap pixel. The image only borrows the buffer.
        var image = new XImage
        {
            width = width,
            height = height,
            xoffset = 0,
            format = Xlib.ZPixmap,
            data = pixels,
            byte_order = Xlib.LSBFirst,
            bitmap_unit = 32,
            bitmap_bit_order = Xlib.LSBFirst,
            bitmap_pad = 32,
            depth = _depth,
            bytes_per_line = width * 4,
            bits_per_pixel = 32,
            red_mask = 0xFF0000,
            green_mask = 0x00FF00,
            blue_mask = 0x0000FF,
        };
        if (Xlib.XInitImage(ref image) == 0) return;
        Xlib.XPutImage(_display, _window, _gc, ref image, 0, 0, 0, 0, (uint)width, (uint)height);
    }

    private void UpdateCursor(int x, int y)
    {
        var cursor = window.CursorAt(x, y);
        if (cursor == _currentCursor) return;
        _currentCursor = cursor;
        if (!_cursors.TryGetValue(cursor, out var handle))
        {
            handle = Xlib.XCreateFontCursor(_display, cursor switch
            {
                CursorType.Pointer => Xlib.XC_hand2,
                CursorType.Text => Xlib.XC_xterm,
                _ => Xlib.XC_left_ptr,
            });
            _cursors[cursor] = handle;
        }
        Xlib.XDefineCursor(_display, _window, handle);
    }

    public void Invalidate() => _dirty = true;

    public void PostTask()
    {
        // Non-blocking: a full pipe already guarantees a pending wake-up.
        // Locked against Dispose so a late wake from a background timer never writes to a
        // closed (and possibly reused) descriptor.
        byte one = 1;
        lock (_wakeLock)
            if (_wakePipe[1] >= 0) LibC.write(_wakePipe[1], ref one, 1);
    }

    public void StartTimer(uint intervalMs)
    {
        _timerIntervalMs = Math.Max(1, intervalMs);
        _nextTickMs = _clock.ElapsedMilliseconds + _timerIntervalMs;
    }

    public void StopTimer() => _timerIntervalMs = 0;

    public void SetTitle(string title)
    {
        Xlib.XStoreName(_display, _window, title);
        var utf8 = Encoding.UTF8.GetBytes(title);
        Xlib.XChangeProperty(_display, _window, _netWmName, _utf8String, 8, Xlib.PropModeReplace, utf8, utf8.Length);
    }

    // X11 grabs the pointer implicitly while a button is held, so drags keep reporting motion.
    public void CaptureMouse() { }

    public void ReleaseMouse() { }

    public string[] ShowOpenFileDialog(string accept, bool multiple) => PlatformDialogs.ShowOpenDialog(accept, multiple);

    public void Dispose()
    {
        if (_display != IntPtr.Zero)
        {
            if (_ic != IntPtr.Zero) Xlib.XDestroyIC(_ic);
            if (_im != IntPtr.Zero) Xlib.XCloseIM(_im);
            foreach (var cursor in _cursors.Values) Xlib.XFreeCursor(_display, cursor);
            if (_gc != IntPtr.Zero) Xlib.XFreeGC(_display, _gc);
            if (_window != 0) Xlib.XDestroyWindow(_display, _window);
            Xlib.XCloseDisplay(_display);
            _display = IntPtr.Zero;
        }
        lock (_wakeLock)
        {
            foreach (var i in new[] { 0, 1 })
            {
                if (_wakePipe[i] >= 0) LibC.close(_wakePipe[i]);
                _wakePipe[i] = -1;
            }
        }
        if (_className != IntPtr.Zero) Marshal.FreeHGlobal(_className);
        if (_classClass != IntPtr.Zero) Marshal.FreeHGlobal(_classClass);
        _className = _classClass = IntPtr.Zero;
    }
}
