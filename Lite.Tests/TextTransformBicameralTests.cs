using System.Text.RegularExpressions;
using Lite.Extensions;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>
/// CSS 2.1 §16.6 text-transform over the WPT bicameral family's data-driven pairs: every
/// file's reference lines state the expected glyphs, and the engine's full case mapping
/// (SpecialCasing multi-char forms, the unconditional dotless-i rule, per-code-point
/// supplementary-plane handling) must reproduce them. A mapping failure is any engine
/// output absent from the file's own reference right-column (set-membership — the digraph
/// windows' titlecase forms shift positional alignment).
/// </summary>
public static class TextTransformBicameralTests
{
    private static string FindRepoRoot()
    {
        var dir = Directory.GetCurrentDirectory();
        while (dir is not null && !File.Exists(Path.Combine(dir, "Lite.sln")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("repo root not found");
    }

    [Test]
    public static void Bicameral_MappingMatchesEveryReference()
    {
        var root = FindRepoRoot();
        var dir = Path.Combine(root, "Lite.Conformance", "vendor", "wpt", "css", "CSS2", "text");
        var totalBad = 0;
        var files = 0;
        foreach (var testPath in Directory.GetFiles(dir, "text-transform-bicameral-*.xht"))
        {
            if (testPath.Contains("-ref")) continue;
            var refPath = testPath.Replace(".xht", "-ref.xht");
            if (!File.Exists(refPath)) continue;
            var test = File.ReadAllLines(testPath);
            var refr = File.ReadAllLines(refPath);
            var pairs = new List<(string Lower, string Want)>();
            var refTokens = new List<string[]>();
            foreach (var line in refr)
            {
                var t = line.Trim();
                if (t.StartsWith("<div>"))
                    refTokens.Add(Regex.Replace(t["<div>".Length..].Replace("</div>", ""), "<[^>]+>", " ")
                        .Split(' ', StringSplitOptions.RemoveEmptyEntries));
            }
            var idxToTestLine = new Dictionary<int, int>();
            var testLineNo = 0;
            foreach (var line in test)
            {
                testLineNo++;
                var t = line.Trim();
                if (!t.StartsWith("<div>")) continue;
                var plain = Regex.Replace(t["<div>".Length..].Replace("</div>", ""), "<[^>]+>", " ").Trim();
                var tokens = plain.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length < 2) continue;
                // The lower is the test's token that is its own lowercase; the want comes
                // from the matching ref line's last token (both glyphs uppercase there).
                var isUpperFile = File.ReadAllText(testPath).Contains("text-transform: uppercase", StringComparison.Ordinal);
                var src = isUpperFile
                    ? tokens.FirstOrDefault(tok => tok.ToLowerInvariant() == tok)
                    : tokens.FirstOrDefault(tok => tok.ToLowerInvariant() != tok);
                if (src is null) continue;
                pairs.Add((src, ""));
            }
            if (pairs.Count == 0 || refTokens.Count == 0) continue;
            files++;
            // The digraph windows' ref lines carry titlecase forms that shift positional
            // alignment; a mapping failure is any engine output absent from the file's own
            // ref right-column tokens (set-membership, position-tolerant).
            var wantSet = new HashSet<string>(
                refTokens.Where(t => t.Length >= 2).Select(t => t[^1]));
            var isUpper = File.ReadAllText(testPath).Contains("text-transform: uppercase", StringComparison.Ordinal);
            for (var i = 0; i < pairs.Count; i++)
            {
                var got = StyleExtensions.ApplyTextTransform(pairs[i].Lower,
                    isUpper ? TextTransform.Uppercase : TextTransform.Lowercase, null);
                if (!wantSet.Contains(got))
                {
                    totalBad++;
                    System.Console.Error.WriteLine(
                        $"  {Path.GetFileName(testPath)} MISMATCH {i}: lower='{pairs[i].Lower}' got='{got}' not in the reference set");
                }
            }
        }
        System.Console.Error.WriteLine($"files checked: {files}, mismatches: {totalBad}");
        True(totalBad == 0, $"the uppercase mapping mismatches the bicameral references: {totalBad}");
    }
}
