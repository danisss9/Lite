namespace Lite.Conformance.Harness;

/// <summary>One line of a suite manifest: a test path (reftests: <c>test | reference</c>),
/// optionally annotated <c>[expected-fail: reason]</c>.</summary>
internal sealed record ManifestEntry(string Path, string? Reference, bool ExpectedFail, string? Reason);

internal static class Manifest
{
    /// <summary>Parses a manifest file. Lines starting with '#' and blank lines are skipped.</summary>
    public static List<ManifestEntry> Load(string file)
    {
        var entries = new List<ManifestEntry>();
        if (!File.Exists(file)) return entries;

        foreach (var raw in File.ReadAllLines(file))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            bool expectedFail = false;
            string? reason = null;
            var annotationStart = line.IndexOf("[expected-fail", StringComparison.OrdinalIgnoreCase);
            if (annotationStart >= 0)
            {
                var annotation = line[annotationStart..].Trim();
                line = line[..annotationStart].Trim();
                expectedFail = true;
                var colon = annotation.IndexOf(':');
                if (colon >= 0)
                    reason = annotation[(colon + 1)..].TrimEnd(']').Trim();
            }

            string path = line;
            string? reference = null;
            var sep = line.IndexOf('|');
            if (sep >= 0)
            {
                path = line[..sep].Trim();
                reference = line[(sep + 1)..].Trim();
            }

            entries.Add(new ManifestEntry(path, reference, expectedFail, reason));
        }
        return entries;
    }

    /// <summary>Applies a --filter substring to manifest entries.</summary>
    public static List<ManifestEntry> Filter(List<ManifestEntry> entries, string? filter) =>
        string.IsNullOrEmpty(filter)
            ? entries
            : entries.Where(e => e.Path.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
}

/// <summary>Tally of suite results with "green = no unexpected outcomes" semantics.</summary>
internal sealed class SuiteResult
{
    public int Passed;
    public int Failed;            // unexpected failures
    public int ExpectedFailures;  // annotated, still failing (OK)
    public int UnexpectedPasses;  // annotated as expected-fail but passing (manifest is stale)
    public readonly List<string> Problems = [];

    public bool Green => Failed == 0 && UnexpectedPasses == 0;

    public int Report(string suiteName)
    {
        Console.WriteLine();
        Console.WriteLine($"=== {suiteName}: {Passed} passed, {Failed} failed, " +
                          $"{ExpectedFailures} expected-fail, {UnexpectedPasses} unexpected-pass ===");
        foreach (var p in Problems)
            Console.WriteLine($"  !! {p}");
        return Green ? 0 : 1;
    }
}
