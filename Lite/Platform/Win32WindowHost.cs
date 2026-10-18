using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Lite.Models;
using Lite.Models.Delegates;
using Lite.Models.Structs;
using Lite.Utils;

namespace Lite.Platform;

/// <summary>Win32 window host: a plain overlapped window whose WndProc forwards to the BrowserWindow.</summary>
[SupportedOSPlatform("windows")]
internal sealed class Win32WindowHost(BrowserWindow window) : IWindowHost
{
    private const string CLASS_NAME = "LiteBrowserWindowClass";
    private const int WS_OVERLAPPEDWINDOW = 0xCF0000;
    private const int SW_SHOW = 5;
    private const int WM_PAINT = 0x000F;
    private const int WM_DESTROY = 0x0002;
    private const int WM_SIZE = 0x0005;
    private const int WM_TIMER = 0x0113;
    private const int WM_CHAR = 0x0102;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_MOUSEMOVE = 0x0200;
    private const int WM_MOUSEWHEEL = 0x020A;
    private const int WM_SETCURSOR = 0x0020;
    // Custom message posted (thread-safely) when the JS event loop has work to drain.
    private const uint WM_APP_TASK = 0x8000; // WM_APP
    private const int HTCLIENT = 1;
    private const int IDC_ARROW = 32512;
    private const int IDC_IBEAM = 32513;
    private const int IDC_HAND = 32649;
    private const uint BI_RGB = 0;
    private const uint DIB_RGB_COLORS = 0;
    private static readonly IntPtr AnimationTimerId = new(1);

    private IntPtr _hWnd;
    private WndProcDelegate? _wndProcDelegate;

    public bool Create(string title, int width, int height)
    {
        _wndProcDelegate = WndProc;
        var hInstance = Marshal.GetHINSTANCE(typeof(BrowserWindow).Module);

        var wcex = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf(typeof(WNDCLASSEX)),
            style = 0,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
            cbClsExtra = 0,
            cbWndExtra = 0,
            hInstance = hInstance,
            hIcon = IntPtr.Zero,
            hCursor = IntPtr.Zero,
            hbrBackground = IntPtr.Zero,
            lpszMenuName = null,
            lpszClassName = CLASS_NAME,
            hIconSm = IntPtr.Zero
        };

        var regResult = User32.RegisterClassEx(ref wcex);
        if (regResult == 0)
        {
            Console.WriteLine("Window class registration failed!");
            return false;
        }

        _hWnd = User32.CreateWindowEx(
            0,
            CLASS_NAME,
            title,
            WS_OVERLAPPEDWINDOW,
            100, 100, width, height,
            IntPtr.Zero,
            IntPtr.Zero,
            hInstance,
            IntPtr.Zero
        );

        if (_hWnd == IntPtr.Zero) return false;

        User32.ShowWindow(_hWnd, SW_SHOW);
        User32.UpdateWindow(_hWnd);
        return true;
    }

    public void RunLoop()
    {
        while (User32.GetMessage(out var msg, IntPtr.Zero, 0, 0))
        {
            User32.TranslateMessage(ref msg);
            User32.DispatchMessage(ref msg);
        }
    }

    public void Invalidate() => User32.InvalidateRect(_hWnd, IntPtr.Zero, false);

    // PostMessage is thread-safe, so background timer threads can wake the loop.
    public void PostTask() => User32.PostMessage(_hWnd, WM_APP_TASK, IntPtr.Zero, IntPtr.Zero);

    public void StartTimer(uint intervalMs) => User32.SetTimer(_hWnd, AnimationTimerId, intervalMs, IntPtr.Zero);

    public void StopTimer() => User32.KillTimer(_hWnd, AnimationTimerId);

    public void SetTitle(string title) => User32.SetWindowText(_hWnd, title);

    public void CaptureMouse() => User32.SetCapture(_hWnd);

    public void ReleaseMouse() => User32.ReleaseCapture();

    public string[] ShowOpenFileDialog(string accept, bool multiple) => Comdlg32.ShowOpenDialog(_hWnd, accept, multiple);

    public void Dispose() { }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        // CreateWindowEx sends WM_SIZE (and the handlers call back into this host) before it
        // returns the handle.
        if (_hWnd == IntPtr.Zero) _hWnd = hWnd;

        switch (msg)
        {
            case WM_PAINT:
                var hdc = User32.BeginPaint(hWnd, out var ps);
                var (pixels, width, height) = window.Frame;

                var bmi = new BITMAPINFO();
                bmi.bmiHeader.biSize = (uint)Marshal.SizeOf(typeof(BITMAPINFOHEADER));
                bmi.bmiHeader.biWidth = width;
                bmi.bmiHeader.biHeight = -height;
                bmi.bmiHeader.biPlanes = 1;
                bmi.bmiHeader.biBitCount = 32;
                bmi.bmiHeader.biCompression = BI_RGB;
                bmi.bmiHeader.biSizeImage = (uint)(width * height * 4);
                bmi.bmiHeader.biXPelsPerMeter = 0;
                bmi.bmiHeader.biYPelsPerMeter = 0;
                bmi.bmiHeader.biClrUsed = 0;
                bmi.bmiHeader.biClrImportant = 0;

                Gdi32.SetDIBitsToDevice(
                    hdc,
                    0, 0,
                    (uint)width, (uint)height,
                    0, 0,
                    0, (uint)height,
                    pixels,
                    ref bmi,
                    DIB_RGB_COLORS);

                User32.EndPaint(hWnd, ref ps);
                return IntPtr.Zero;

            case WM_DESTROY:
                User32.PostQuitMessage(0);
                break;

            case WM_APP_TASK:
                window.OnTask();
                break;

            case WM_TIMER:
                window.OnTimer();
                break;

            case WM_SIZE:
                User32.GetClientRect(hWnd, out var clientRect);
                window.OnResize(clientRect.right - clientRect.left, clientRect.bottom - clientRect.top);
                break;

            case WM_MOUSEWHEEL:
                {
                    var delta = (short)((wParam.ToInt64() >> 16) & 0xFFFF);
                    // The wheel message carries screen coordinates; use the client-space cursor.
                    User32.GetCursorPos(out var wheelPt);
                    User32.ScreenToClient(hWnd, ref wheelPt);
                    window.OnWheel(wheelPt.X, wheelPt.Y, delta);
                    break;
                }

            case WM_LBUTTONDOWN:
                window.OnMouseDown(LowWord(lParam), HighWord(lParam));
                break;

            case WM_MOUSEMOVE:
                window.OnMouseMove(LowWord(lParam), HighWord(lParam));
                break;

            case WM_LBUTTONUP:
                window.OnMouseUp(LowWord(lParam), HighWord(lParam));
                break;

            case WM_KEYDOWN:
            case WM_KEYUP:
                window.OnKey(msg == WM_KEYDOWN, (int)wParam.ToInt64(), GetKeyModifiers());
                break;

            case WM_CHAR:
                window.OnChar((char)wParam.ToInt32());
                break;

            case WM_SETCURSOR:
                if ((lParam.ToInt32() & 0xFFFF) == HTCLIENT)
                {
                    User32.GetCursorPos(out var pt);
                    User32.ScreenToClient(hWnd, ref pt);
                    var cursorId = window.CursorAt(pt.X, pt.Y) switch
                    {
                        CursorType.Pointer => IDC_HAND,
                        CursorType.Text => IDC_IBEAM,
                        _ => IDC_ARROW
                    };
                    User32.SetCursor(User32.LoadCursor(IntPtr.Zero, cursorId));
                    return new IntPtr(1);
                }
                return User32.DefWindowProc(hWnd, msg, wParam, lParam);

            default:
                return User32.DefWindowProc(hWnd, msg, wParam, lParam);
        }
        return IntPtr.Zero;
    }

    private static int LowWord(IntPtr lParam) => (short)(lParam.ToInt32() & 0xFFFF);

    private static int HighWord(IntPtr lParam) => (short)((lParam.ToInt32() >> 16) & 0xFFFF);

    private static KeyModifiers GetKeyModifiers() => new(
        Ctrl: (User32.GetKeyState(0x11) & 0x8000) != 0,
        Shift: (User32.GetKeyState(0x10) & 0x8000) != 0,
        Alt: (User32.GetKeyState(0x12) & 0x8000) != 0,
        Meta: (User32.GetKeyState(0x5B) & 0x8000) != 0 || (User32.GetKeyState(0x5C) & 0x8000) != 0);
}
