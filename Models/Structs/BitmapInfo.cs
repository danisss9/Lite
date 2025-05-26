using System.Runtime.InteropServices;

namespace Lite.Models.Structs;

[StructLayout(LayoutKind.Sequential)]
public struct BITMAPINFO
{
    public BITMAPINFOHEADER bmiHeader;
    public uint bmiColors;
}
