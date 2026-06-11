using System.Runtime.CompilerServices;

namespace Lite.Conformance.Harness;

/// <summary>Resolves directories relative to the Lite.Conformance project folder so the
/// runners work regardless of the build output directory.</summary>
internal static class ConformancePaths
{
    /// <summary>Lite.Conformance project root (resolved from this source file's path).</summary>
    public static string ProjectRoot { get; } =
        Path.GetFullPath(Path.Combine(SourceDir(), ".."));

    public static string Vendor => Path.Combine(ProjectRoot, "vendor");
    public static string Baselines => Path.Combine(ProjectRoot, "baselines");
    public static string Artifacts => Path.Combine(ProjectRoot, "artifacts");

    /// <summary>Files served at higher priority than vendor (custom testharnessreport.js,
    /// local smoke/sanity tests).</summary>
    public static string Overrides => Path.Combine(ProjectRoot, "overrides");

    public static string Manifest(string name) => Path.Combine(ProjectRoot, name);

    public static string EnsureArtifacts()
    {
        Directory.CreateDirectory(Artifacts);
        return Artifacts;
    }

    private static string SourceDir([CallerFilePath] string path = "") =>
        Path.GetDirectoryName(path)!;
}
