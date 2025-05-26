using System.Drawing;
using System.Runtime.InteropServices;

namespace Lite.Models.Structs;

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