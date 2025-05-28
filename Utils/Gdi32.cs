using System.Runtime.InteropServices;
using Lite.Models.Structs;

namespace Lite.Utils;

internal static class Gdi32
{
    [DllImport("gdi32.dll", SetLastError = true)]
    internal static extern void SetDIBitsToDevice(
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
        uint fuColorUse
    );
}