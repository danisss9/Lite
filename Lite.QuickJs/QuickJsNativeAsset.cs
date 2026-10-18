using System.Runtime.InteropServices;

namespace Lite.QuickJs;

/// <summary>Where the native QuickJS bridge for the current OS and architecture lives in an output folder.</summary>
internal static class QuickJsNativeAsset
{
    /// <summary>The NuGet runtime identifier the bridge is built for, e.g. <c>win-x64</c> or <c>linux-x64</c>.</summary>
    public static string RuntimeIdentifier { get; } =
        (OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsLinux() ? "linux" : "unsupported") + "-" +
        RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();

    /// <summary>The bridge's file name: <c>litequickjs.dll</c> on Windows, <c>liblitequickjs.so</c> elsewhere.</summary>
    public static string FileName { get; } = OperatingSystem.IsWindows() ? "litequickjs.dll" : "liblitequickjs.so";

    /// <summary>The bridge under <c>runtimes/&lt;rid&gt;/native</c> beside <paramref name="directory"/>.</summary>
    public static string PathBeside(string directory) =>
        Path.Combine(directory, "runtimes", RuntimeIdentifier, "native", FileName);
}
