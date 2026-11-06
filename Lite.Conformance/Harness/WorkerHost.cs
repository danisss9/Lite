using System.Diagnostics;

namespace Lite.Conformance.Harness;

/// <summary>Process starts for conformance worker entry points on the same host running the
/// parent: a bare "dotnet" resolves via PATH and, on machines with several dotnet installs,
/// may pick a runtime that cannot load the harness. When the current host is the app's own
/// apphost it already knows its application DLL; the dotnet host needs it spelled out.</summary>
internal static class WorkerHost
{
    internal static ProcessStartInfo Start(string entryPoint, params string[] arguments)
    {
        var host = Environment.ProcessPath;
        var start = new ProcessStartInfo(host ?? "dotnet") { UseShellExecute = false, CreateNoWindow = true };
        var isAppHost = host is not null &&
            !Path.GetFileNameWithoutExtension(host).StartsWith("dotnet", StringComparison.OrdinalIgnoreCase);
        if (!isAppHost) start.ArgumentList.Add(typeof(WorkerHost).Assembly.Location);
        start.ArgumentList.Add(entryPoint);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        return start;
    }
}
