using System.Diagnostics;
using System.Reflection;

namespace Lite.Conformance.Harness;

/// <summary>Process starts for conformance worker entry points on the same runtime as the
/// parent. A bare "dotnet" resolves via PATH and, on machines with several dotnet installs,
/// may pick a runtime that cannot load the harness. The current process's apphost is only
/// usable when that apphost IS Lite.Conformance: a host embedding the harness (Lite.Tests.exe)
/// has its own Main, which rejects the worker arguments (measured: it printed its usage line
/// and every worker "exited 2"). Every other host runs the harness DLL through the dotnet
/// muxer that owns the runtime executing this process.</summary>
internal static class WorkerHost
{
    internal static ProcessStartInfo Start(string entryPoint, params string[] arguments)
    {
        var harness = typeof(WorkerHost).Assembly;
        var host = Environment.ProcessPath;
        var isMuxer = host is not null &&
            Path.GetFileNameWithoutExtension(host).Equals("dotnet", StringComparison.OrdinalIgnoreCase);
        var isOwnAppHost = host is not null && !isMuxer && Assembly.GetEntryAssembly() == harness;
        var start = new ProcessStartInfo(isOwnAppHost || isMuxer ? host! : RuntimeMuxer())
        { UseShellExecute = false, CreateNoWindow = true };
        if (!isOwnAppHost) start.ArgumentList.Add(harness.Location);
        start.ArgumentList.Add(entryPoint);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        return start;
    }

    /// <summary>The dotnet muxer of the running runtime: the runtime directory is
    /// &lt;root&gt;/shared/Microsoft.NETCore.App/&lt;version&gt;/, and the muxer sits at &lt;root&gt;.
    /// Falls back to PATH resolution only when that layout is absent.</summary>
    private static string RuntimeMuxer()
    {
        var runtime = Path.GetDirectoryName(typeof(object).Assembly.Location) ?? "";
        var root = Path.GetFullPath(Path.Combine(runtime, "..", "..", ".."));
        var muxer = Path.Combine(root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
        return File.Exists(muxer) ? muxer : "dotnet";
    }
}
