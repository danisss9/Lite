using System;
using System.Runtime.InteropServices;
using System.Drawing;  // Only used for Point in MSG
using SkiaSharp;

namespace Lite
{
    class Program
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
        private static WndProcDelegate _wndProcDelegate = WndProc;

        // P/Invoke declarations for window creation and message loop.
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        static extern ushort RegisterClassEx([In] ref WNDCLASSEX lpwcx);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        static extern IntPtr CreateWindowEx(
            int dwExStyle,
            string lpClassName,
            string lpWindowName,
            int dwStyle,
            int x,
            int y,
            int nWidth,
            int nHeight,
            IntPtr hWndParent,
            IntPtr hMenu,
            IntPtr hInstance,
            IntPtr lpParam);

        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        static extern bool UpdateWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern bool GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("user32.dll")]
        static extern bool TranslateMessage([In] ref MSG lpMsg);

        [DllImport("user32.dll")]
        static extern IntPtr DispatchMessage([In] ref MSG lpMsg);

        [DllImport("user32.dll")]
        static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        static extern void PostQuitMessage(int nExitCode);

        // P/Invoke declarations for painting.
        [DllImport("user32.dll")]
        static extern IntPtr BeginPaint(IntPtr hWnd, out PAINTSTRUCT lpPaint);

        [DllImport("user32.dll")]
        static extern bool EndPaint(IntPtr hWnd, [In] ref PAINTSTRUCT lpPaint);

        [DllImport("user32.dll")]
        static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("gdi32.dll", SetLastError = true)]
        static extern int SetDIBitsToDevice(
            IntPtr hdc,
            int xDest,
            int yDest,
            uint w,
            uint h,
            int xSrc,
            int ySrc,
            uint startScan,
            uint cScanLines,
            IntPtr lpvBits,
            [In] ref BITMAPINFO lpbmi,
            uint fuColorUse);

        // Structures for window class and messaging.
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct WNDCLASSEX
        {
            public uint cbSize;
            public uint style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            [MarshalAs(UnmanagedType.LPTStr)]
            public string lpszMenuName;
            [MarshalAs(UnmanagedType.LPTStr)]
            public string lpszClassName;
            public IntPtr hIconSm;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public UIntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public Point pt;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PAINTSTRUCT
        {
            public IntPtr hdc;
            public bool fErase;
            public RECT rcPaint;
            public bool fRestore;
            public bool fIncUpdate;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public byte[] rgbReserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }

        // Structures for passing bitmap info to GDI.
        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFOHEADER
        {
            public uint biSize;
            public int biWidth;
            public int biHeight;
            public ushort biPlanes;
            public ushort biBitCount;
            public uint biCompression;
            public uint biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public uint biClrUsed;
            public uint biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFO
        {
            public BITMAPINFOHEADER bmiHeader;
            public uint bmiColors;
        }

        static void Main(string[] args)
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

            ushort regResult = RegisterClassEx(ref wcex);
            if (regResult == 0)
            {
                Console.WriteLine("Window class registration failed!");
                return;
            }

            // Create the window.
            IntPtr hWnd = CreateWindowEx(
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

            ShowWindow(hWnd, SW_SHOW);
            UpdateWindow(hWnd);

            // Enter the message loop.
            MSG msg;
            while (GetMessage(out msg, IntPtr.Zero, 0, 0))
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
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
                        IntPtr hdc = BeginPaint(hWnd, out ps);

                        // Get the client area dimensions.
                        GetClientRect(hWnd, out RECT clientRect);
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
                        SetDIBitsToDevice(
                            hdc,
                            0, 0,
                            (uint)width, (uint)height,
                            0, 0,
                            0, (uint)height,
                            pixels,
                            ref bmi,
                            DIB_RGB_COLORS);

                        EndPaint(hWnd, ref ps);
                    }
                    return IntPtr.Zero;

                case WM_DESTROY:
                    PostQuitMessage(0);
                    break;

                default:
                    return DefWindowProc(hWnd, msg, wParam, lParam);
            }
            return IntPtr.Zero;
        }
    }
}
