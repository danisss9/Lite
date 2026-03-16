using System.Runtime.InteropServices;

namespace Lite.Models.Structs;

[StructLayout(LayoutKind.Sequential)]
public struct RECT
{
    public int left;
    public int top;
    public int right;
    public int bottom;
}