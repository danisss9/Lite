using System.Runtime.InteropServices;
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
    private const int BI_RGB = 0;
    private const uint DIB_RGB_COLORS = 0;

    // Delegate for the window procedure.
    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    private static readonly WndProcDelegate _wndProcDelegate = WndProc;

    private static void Main(string[] args)
    {
        // Retrieve the module handle.
        IntPtr hInstance = Marshal.GetHINSTANCE(typeof(Program).Module);

        // Set up the window class.
        WNDCLASSEX wcex = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf(typeof(WNDCLASSEX)),
            style = 0,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
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

        ushort regResult = User32.RegisterClassEx(ref wcex);
        if (regResult == 0)
        {
            Console.WriteLine("Window class registration failed!");
            return;
        }

        // Create the window.
        IntPtr hWnd = User32.CreateWindowEx(
            0,
            CLASS_NAME,
            "SkiaSharp Rendering in Native Window",
            WS_OVERLAPPEDWINDOW,
            100, 100, 800, 600,
            IntPtr.Zero,
            IntPtr.Zero,
            hInstance,
            IntPtr.Zero);

        if (hWnd == IntPtr.Zero)
        {
            Console.WriteLine("Window creation failed!");
            return;
        }

        User32.ShowWindow(hWnd, SW_SHOW);
        User32.UpdateWindow(hWnd);

        // Enter the message loop.
        MSG msg;
        while (User32.GetMessage(out msg, IntPtr.Zero, 0, 0))
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
            {
                // Begin painting and obtain the device context.
                PAINTSTRUCT ps;
                IntPtr hdc = User32.BeginPaint(hWnd, out ps);

                // Get the client area dimensions.
                User32.GetClientRect(hWnd, out RECT clientRect);
                int width = clientRect.right - clientRect.left;
                int height = clientRect.bottom - clientRect.top;

                // Get pointer to the pixel data.
                List<DrawCommand> drawCommands = Parser.TraverseHtml("https://example.com");
                IntPtr pixels = Drawer.Draw(width, height, drawCommands);

                // Prepare BITMAPINFO for transferring data to the window.
                BITMAPINFO bmi = new BITMAPINFO();
                bmi.bmiHeader.biSize = (uint)Marshal.SizeOf(typeof(BITMAPINFOHEADER));
                bmi.bmiHeader.biWidth = width;
                // Use a negative height for a top–down DIB.
                bmi.bmiHeader.biHeight = -height;
                bmi.bmiHeader.biPlanes = 1;
                bmi.bmiHeader.biBitCount = 32;
                bmi.bmiHeader.biCompression = (uint)BI_RGB;
                bmi.bmiHeader.biSizeImage = (uint)(width * height * 4);
                bmi.bmiHeader.biXPelsPerMeter = 0;
                bmi.bmiHeader.biYPelsPerMeter = 0;
                bmi.bmiHeader.biClrUsed = 0;
                bmi.bmiHeader.biClrImportant = 0;

                // Blit the offscreen bitmap to the window's device context.
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
            }
                return IntPtr.Zero;

            case WM_DESTROY:
                User32.PostQuitMessage(0);
                break;

            default:
                return User32.DefWindowProc(hWnd, msg, wParam, lParam);
        }
        return IntPtr.Zero;
    }
}