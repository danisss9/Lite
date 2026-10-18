using System.ComponentModel;
using System.Diagnostics;
using Lite.Utils;

namespace Lite.Platform;

/// <summary>
/// Modal dialogs for script and form controls. Windows uses the Win32 dialogs; Linux shells
/// out to zenity or kdialog when either is installed (falling back to the console for alerts).
/// </summary>
internal static class PlatformDialogs
{
    /// <summary>window.alert(): blocks until the user dismisses the message.</summary>
    public static void Alert(string message)
    {
        if (OperatingSystem.IsWindows())
        {
            User32.MessageBox(IntPtr.Zero, message, "Alert", 0);
            return;
        }

        if (RunDialog("zenity", ["--info", "--title=Alert", "--no-markup", "--text", message], out _) is not null ||
            RunDialog("kdialog", ["--title", "Alert", "--msgbox", message], out _) is not null)
            return;
        Console.Error.WriteLine($"[alert] {message}");
    }

    /// <summary>Linux Open dialog for &lt;input type=file&gt;; returns absolute paths, or empty if cancelled or unavailable.</summary>
    public static string[] ShowOpenDialog(string accept, bool multiple)
    {
        var patterns = AcceptPatterns(accept);

        var zenityArgs = new List<string> { "--file-selection", "--title=Choose File" + (multiple ? "(s)" : "") };
        if (multiple) zenityArgs.AddRange(["--multiple", "--separator=\n"]);
        if (patterns.Length > 0) zenityArgs.Add("--file-filter=Accepted | " + string.Join(' ', patterns));
        if (RunDialog("zenity", zenityArgs, out var output) is { } zenityExit)
            return zenityExit == 0 ? SplitPaths(output) : [];

        var kdialogArgs = new List<string> { "--getopenfilename", Environment.CurrentDirectory };
        if (patterns.Length > 0) kdialogArgs.Add(string.Join(' ', patterns));
        if (multiple) kdialogArgs.AddRange(["--multiple", "--separate-output"]);
        if (RunDialog("kdialog", kdialogArgs, out output) is { } kdialogExit)
            return kdialogExit == 0 ? SplitPaths(output) : [];

        Console.Error.WriteLine("[file picker] Install zenity or kdialog to choose files on Linux.");
        return [];
    }

    /// <summary>Translates an HTML <c>accept</c> attribute into glob patterns (".png" → "*.png"; MIME types are skipped).</summary>
    private static string[] AcceptPatterns(string accept) =>
        accept.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(a => a.StartsWith('.'))
            .Select(a => "*" + a)
            .ToArray();

    private static string[] SplitPaths(string output) =>
        output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Runs a dialog tool and waits for it. Returns its exit code, or null if the tool is not installed.</summary>
    private static int? RunDialog(string tool, IEnumerable<string> args, out string output)
    {
        output = "";
        var info = new ProcessStartInfo(tool) { RedirectStandardOutput = true, UseShellExecute = false };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        try
        {
            using var process = Process.Start(info);
            if (process is null) return null;
            output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode;
        }
        catch (Win32Exception)
        {
            return null; // not on PATH
        }
    }
}
