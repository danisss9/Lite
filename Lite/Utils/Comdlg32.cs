using System.Runtime.InteropServices;

namespace Lite.Utils;

/// <summary>Thin wrapper over the Win32 common "Open File" dialog (comdlg32 GetOpenFileName),
/// used to back &lt;input type=file&gt;. Returns the chosen path(s), or empty if cancelled.</summary>
internal static class Comdlg32
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct OpenFileName
    {
        public int lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        public string? lpstrFilter;
        public string? lpstrCustomFilter;
        public int nMaxCustFilter;
        public int nFilterIndex;
        public string? lpstrFile;
        public int nMaxFile;
        public string? lpstrFileTitle;
        public int nMaxFileTitle;
        public string? lpstrInitialDir;
        public string? lpstrTitle;
        public int Flags;
        public short nFileOffset;
        public short nFileExtension;
        public string? lpstrDefExt;
        public IntPtr lCustData;
        public IntPtr lpfnHook;
        public string? lpTemplateName;
        public IntPtr pvReserved;
        public int dwReserved;
        public int FlagsEx;
    }

    private const int OFN_FILEMUSTEXIST = 0x00001000;
    private const int OFN_PATHMUSTEXIST = 0x00000800;
    private const int OFN_ALLOWMULTISELECT = 0x00000200;
    private const int OFN_EXPLORER = 0x00080000;

    [DllImport("comdlg32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GetOpenFileName(ref OpenFileName ofn);

    /// <summary>Shows the Open dialog. Returns the selected absolute file paths (possibly several
    /// when <paramref name="multiple"/> is true), or an empty array if the user cancelled.</summary>
    public static string[] ShowOpenDialog(IntPtr owner, string accept, bool multiple)
    {
        // Buffer must be large enough to hold a directory + several filenames (multi-select).
        var buffer = new string('\0', 4096);
        var ofn = new OpenFileName
        {
            lStructSize = Marshal.SizeOf<OpenFileName>(),
            hwndOwner = owner,
            lpstrFile = buffer,
            nMaxFile = buffer.Length,
            lpstrFilter = BuildFilter(accept),
            lpstrTitle = "Choose File" + (multiple ? "(s)" : ""),
            Flags = OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_EXPLORER | (multiple ? OFN_ALLOWMULTISELECT : 0),
        };

        if (!GetOpenFileName(ref ofn)) return Array.Empty<string>();

        // Explorer multi-select returns: dir\0file1\0file2\0\0 ; single-select returns one full path.
        var parts = (ofn.lpstrFile ?? "").Split('\0', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length <= 1) return parts;
        var dir = parts[0];
        return parts.Skip(1).Select(f => Path.Combine(dir, f)).ToArray();
    }

    /// <summary>Translates an HTML <c>accept</c> attribute into a comdlg32 filter string.</summary>
    private static string BuildFilter(string accept)
    {
        if (string.IsNullOrWhiteSpace(accept))
            return "All Files\0*.*\0\0";
        var exts = accept.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(a => a.StartsWith('.') ? "*" + a : a)   // ".png" → "*.png"; MIME types pass through
            .Where(a => a.StartsWith("*."))
            .ToArray();
        var pattern = exts.Length > 0 ? string.Join(";", exts) : "*.*";
        return $"Accepted\0{pattern}\0All Files\0*.*\0\0";
    }
}
