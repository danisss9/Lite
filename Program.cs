using System.Diagnostics;
using System.Runtime.InteropServices;
using Lite.Models;
using Lite.Models.Delegates;
using Lite.Models.Structs;
using Lite.Utils;

namespace Lite;

internal class Program
{
    // Constants for window styling and painting.
    private const string CLASS_NAME = "MySkiaWindowClass";
    private const int WS_OVERLAPPEDWINDOW = 0xCF0000;
    private const int SW_SHOW = 5;
    private const int WM_PAINT = 0x000F;
    private const int WM_DESTROY = 0x0002;
    private const int WM_SIZE = 0x0005;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_SETCURSOR = 0x0020;
    private const int HTCLIENT = 1;
    private const int IDC_ARROW = 32512;
    private const int IDC_IBEAM = 32513;
    private const int IDC_HAND  = 32649;
    private const uint BI_RGB = 0;
    private const uint DIB_RGB_COLORS = 0;

    private static int Width { get; set; }
    private static int Height { get; set; }
    private static LayoutNode? RootNode { get; set; }
    private static IntPtr Pixels { get; set; }
    private static List<HitRegion> HitRegions { get; set; } = [];
    private static readonly WndProcDelegate WndProcDelegate = WndProc;

    private static void Main()
    {
        RootNode = Parser.TraverseHtml("https://example.com");
        
        // Retrieve the module handle.
        var hInstance = Marshal.GetHINSTANCE(typeof(Program).Module);

        // Set up the window class.
        var wcex = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf(typeof(WNDCLASSEX)),
            style = 0,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(WndProcDelegate),
            cbClsExtra = 0,
            cbWndExtra = 0,
            hInstance = hInstance,
            hIcon = IntPtr.Zero,
            hCursor = IntPtr.Zero,
            // No background brush needed since SkiaSharp does custom drawing.
            hbrBackground = IntPtr.Zero,
            lpszMenuName = null,
            lpszClassName = CLASS_NAME,
            hIconSm = IntPtr.Zero
        };

        var regResult = User32.RegisterClassEx(ref wcex);
        if (regResult == 0)
        {
            Console.WriteLine("Window class registration failed!");
            return;
        }

        // Create the window.
        var hWnd = User32.CreateWindowEx(
            0,
            CLASS_NAME,
            "SkiaSharp Rendering in Native Window",
            WS_OVERLAPPEDWINDOW,
            100, 100, 800, 600,
            IntPtr.Zero,
            IntPtr.Zero,
            hInstance,
            IntPtr.Zero
        );

        if (hWnd == IntPtr.Zero)
        {
            Console.WriteLine("Window creation failed!");
            return;
        }

        User32.ShowWindow(hWnd, SW_SHOW);
        User32.UpdateWindow(hWnd);

        // Enter the message loop.
        while (User32.GetMessage(out var msg, IntPtr.Zero, 0, 0))
        {
            User32.TranslateMessage(ref msg);
            User32.DispatchMessage(ref msg);
        }
    }

    // The window procedure processes messages for the window.
    private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WM_PAINT:
                // Begin painting and obtain the device context.
                var hdc = User32.BeginPaint(hWnd, out var ps);

                // Prepare BITMAPINFO for transferring data to the window.
                var bmi = new BITMAPINFO();
                bmi.bmiHeader.biSize = (uint)Marshal.SizeOf(typeof(BITMAPINFOHEADER));
                bmi.bmiHeader.biWidth = Width;
                // Use a negative height for a top–down DIB.
                bmi.bmiHeader.biHeight = -Height;
                bmi.bmiHeader.biPlanes = 1;
                bmi.bmiHeader.biBitCount = 32;
                bmi.bmiHeader.biCompression = BI_RGB;
                bmi.bmiHeader.biSizeImage = (uint)(Width * Height * 4);
                bmi.bmiHeader.biXPelsPerMeter = 0;
                bmi.bmiHeader.biYPelsPerMeter = 0;
                bmi.bmiHeader.biClrUsed = 0;
                bmi.bmiHeader.biClrImportant = 0;

                // Blit the offscreen bitmap to the window's device context.
                Gdi32.SetDIBitsToDevice(
                    hdc,
                    0, 0,
                    (uint)Width, (uint)Height,
                    0, 0,
                    0, (uint)Height,
                    Pixels,
                    ref bmi,
                    DIB_RGB_COLORS);

                User32.EndPaint(hWnd, ref ps);
                
                return IntPtr.Zero;

            case WM_DESTROY:
                User32.PostQuitMessage(0);
                break;
            
            case WM_SIZE:
                User32.GetClientRect(hWnd, out var clientRect);
                Width = clientRect.right - clientRect.left;
                Height = clientRect.bottom - clientRect.top;

                if (RootNode != null)
                {
                    (Pixels, HitRegions) = Drawer.Draw(Width, Height, RootNode);
                    User32.InvalidateRect(hWnd, IntPtr.Zero, false);
                }

                break;

            case WM_LBUTTONUP:
            {
                var x = (short)(lParam.ToInt32() & 0xFFFF);
                var y = (short)((lParam.ToInt32() >> 16) & 0xFFFF);
                foreach (var region in HitRegions)
                {
                    if (region.Href != null && region.Bounds.Contains(x, y))
                    {
                        Process.Start(new ProcessStartInfo(region.Href) { UseShellExecute = true });
                        break;
                    }
                }
                break;
            }

            case WM_SETCURSOR:
                if ((lParam.ToInt32() & 0xFFFF) == HTCLIENT)
                {
                    User32.GetCursorPos(out var pt);
                    User32.ScreenToClient(hWnd, ref pt);
                    var cursorId = IDC_ARROW;
                    foreach (var region in HitRegions)
                    {
                        if (region.Bounds.Contains(pt.X, pt.Y))
                        {
                            cursorId = region.Cursor switch
                            {
                                CursorType.Pointer => IDC_HAND,
                                CursorType.Text    => IDC_IBEAM,
                                _                  => IDC_ARROW
                            };
                        }
                    }
                    User32.SetCursor(User32.LoadCursor(IntPtr.Zero, cursorId));
                    return new IntPtr(1);
                }
                return User32.DefWindowProc(hWnd, msg, wParam, lParam);

            default:
                return User32.DefWindowProc(hWnd, msg, wParam, lParam);
        }
        return IntPtr.Zero;
    }
}