using System.Text.RegularExpressions;
using Lite.Conformance.Harness;
using Lite.Conformance.Wpt;

namespace Lite.Conformance.Css21;

/// <summary>Strengthens the official suite's self-describing oracle with the WPT reftest
/// that succeeded it. The no-red scan only proves the absence of failure red, while many
/// official cases pass on other criteria ("the boxes are the same width"). A WPT
/// <c>css/CSS2</c> reftest with the same file stem is the maintained twin of the case:
/// when the two documents are equivalent, the official document itself must match the
/// twin's reference graph; when WPT revised the case (inches rewritten to pixels, pass
/// sentences reworded — the reference follows the revision), the twin's own reftest must
/// pass. A case with no reftest twin keeps the red scan alone.</summary>
internal static class OfficialTwinOracle
{
    internal sealed record Twin(WptCase Case, bool Equivalent);

    internal const string Assertion = "Twin reference graph has a passing path";

    private static readonly Regex Comment = new("<!--.*?-->", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex Style = new("<style[^>]*>(.*?)</style>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Body = new("<body[^>]*>(.*)</body>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    private static (IReadOnlyList<WptCase> Catalog, Dictionary<string, WptCase[]> ByStem)? _index;

    /// <summary>The reftest twin of an official case, or null when it has none.</summary>
    internal static Twin? Find(string officialPath, IReadOnlyList<WptCase> catalog)
    {
        if (!officialPath.StartsWith(OfficialCatalog.UrlPrefix, StringComparison.Ordinal)) return null;
        if (_index is not { } index || !ReferenceEquals(index.Catalog, catalog))
        {
            index = (catalog, catalog
                .Where(c => c.Kind == "reftest" && c.Source.StartsWith("css/CSS2/", StringComparison.Ordinal))
                .GroupBy(c => Stem(c.Source), StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.OrderBy(c => c.Path, StringComparer.Ordinal).ToArray(), StringComparer.Ordinal));
            _index = index;
        }
        if (!index.ByStem.TryGetValue(Stem(officialPath), out var twins)) return null;
        var official = ReadOrNull(Path.Combine(OfficialCatalog.Root, officialPath[OfficialCatalog.UrlPrefix.Length..]));
        // A stem shared by several WPT files (4 cases) prefers the equivalent one.
        foreach (var twin in twins)
            if (official is not null && ReadOrNull(Path.Combine(ConformancePaths.Vendor, "wpt", twin.Source)) is { } text &&
                Normalize(official) == Normalize(text))
                return new(twin, true);
        return new(twins[0], false);
    }

    /// <summary>Applies the twin requirement to a self-describing result. A red-scan
    /// failure stands as is; a red-scan pass becomes the twin's verdict, so a twin that
    /// fails, crashes, times out or cannot execute is never reported as a pass.</summary>
    internal static WptRunner.RunResult Strengthen(Twin twin, string officialPath, WptRunner.RunResult selfTest,
        Func<string, WptCase?> lookup)
    {
        if (!selfTest.Passed) return selfTest;
        var subject = twin.Equivalent ? Retarget(officialPath, twin.Case) : twin.Case;
        var label = twin.Equivalent ? $"official document vs reference graph of {twin.Case.Path}" : $"maintained twin {twin.Case.Path}";
        var result = WptRefTestRunner.Run(subject, lookup);
        var detail = $"{selfTest.Detail}; twin reftest ({label}): {result.Detail}";
        var subtests = new List<SubtestEvidence>(selfTest.Subtests ?? [])
            { new(Assertion, result.Passed ? 0 : 1, label) };
        return result with { Detail = detail, Total = subtests.Count, Failures = subtests.Count(s => s.Status != 0), Subtests = subtests };
    }

    /// <summary>The twin's relations re-anchored on the official document: references
    /// become root-absolute so they resolve from the official URL.</summary>
    internal static WptCase Retarget(string officialPath, WptCase twin) => twin with
    {
        Source = officialPath,
        Path = officialPath,
        References = twin.References.Select(r =>
            new WptReference(new Uri(new Uri("http://wpt.invalid/" + twin.Path), r.Url).AbsolutePath, r.Relation)).ToArray(),
    };

    /// <summary>Compares what renders: style text and body markup, ignoring comments,
    /// whitespace, case, XHTML self-closing slashes and CDATA markers.</summary>
    internal static string Normalize(string document)
    {
        document = Comment.Replace(document, "");
        var styles = string.Join(" ", Style.Matches(document).Select(m => m.Groups[1].Value));
        var body = Body.Match(document) is { Success: true } b ? b.Groups[1].Value : "";
        var text = (styles + "|" + body).Replace("/>", ">").Replace("<![CDATA[", "").Replace("]]>", "");
        return Whitespace.Replace(text, "").ToLowerInvariant();
    }

    private static string Stem(string path) => Path.GetFileNameWithoutExtension(path.Split(['?', '#'])[0]);

    private static string? ReadOrNull(string file)
    {
        try { return File.ReadAllText(file); }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}
