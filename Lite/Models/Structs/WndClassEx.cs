using System.Runtime.InteropServices;

namespace Lite.Models.Structs;

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