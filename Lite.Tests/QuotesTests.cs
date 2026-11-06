using Lite;
using Lite.Layout;
using Lite.Models;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>
/// CSS 2.1 §12.3.2 quote keywords in generated content: open-quote/close-quote nest through
/// the 'quotes' pairs at the document's running quote depth, and the has-content pre-check
/// for a pseudo element must evaluate at that pseudo's own depth — a :after made purely of
/// close-quote keywords emits the closing text only after :before's opens nested (the official
/// quotes family's "PASS" = "PA" | "SS" shape).
/// </summary>
public static class QuotesTests
{
    private static LayoutNode? Find(LayoutNode node, Func<LayoutNode, bool> pred)
        => pred(node) ? node : node.Children.Select(c => Find(c, pred)).FirstOrDefault(r => r is not null);

    private static string FindRepoRoot()
    {
        var dir = Directory.GetCurrentDirectory();
        while (dir is not null && !File.Exists(Path.Combine(dir, "Lite.sln")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("repo root not found");
    }

    [Test]
    public static void DoubledCloseQuote_AfterGeneratesWithNestedDepth()
    {
        // quotes-002: 'quotes: "P" "S" "A" "S"' + :before "open-quote open-quote" (P, A) +
        // :after "close-quote close-quote" (S, S) paints PASS. The :after pre-check used to
        // evaluate at depth 0, read two empties, and drop the whole pseudo.
        FontRegistry.RegisterFile(
            Path.Combine(FindRepoRoot(), "Lite.Conformance", "vendor", "wpt", "fonts", "Ahem.ttf"));
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><head><style>" +
            "div:before { content: open-quote open-quote; }" +
            "div:after { content: close-quote close-quote; }" +
            "div { quotes: \"P\" \"S\" \"A\" \"S\"; border: 2px solid black; }" +
            "</style></head><body><div></div></body></html>",
            isSrcdoc: true, "http://test/", 800, 600);
        var div = Find(page.Root, n => n.TagName == "DIV")!;
        BoxEngine.Layout(page.Root, 800, 600);
        var before = Find(div, n => n.TagName == "#pseudo-before");
        var after = Find(div, n => n.TagName == "#pseudo-after");
        True(before is not null, "the :before pseudo must generate");
        True(after is not null, "the :after pseudo must generate (its close-quotes nest :before's opens)");
        True(before!.DisplayText == "PA", $"the :before opens read pair 0 then pair 1: 'PA', got '{before.DisplayText}'");
        True(after!.DisplayText == "SS", $"the :after closes read pair 1 then pair 0: 'SS', got '{after.DisplayText}'");
    }

    [Test]
    public static void SingleCloseQuote_AtDepthZero_GeneratesNothing()
    {
        // §12.3.2: a close-quote with no matching open emits nothing — a lone-close :after
        // with no :before opens generates no content at all.
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><head><style>" +
            "div:after { content: close-quote; }" +
            "div { quotes: \"A\" \"Z\"; }" +
            "</style></head><body><div id='d'>x</div></body></html>",
            isSrcdoc: true, "http://test/", 800, 600);
        var div = Find(page.Root, n => n.TagName == "DIV")!;
        BoxEngine.Layout(page.Root, 800, 600);
        var after = Find(div, n => n.TagName == "#pseudo-after");
        True(after is null || string.IsNullOrEmpty(after.DisplayText),
            $"a close-quote at depth 0 must generate nothing, got '{after?.DisplayText}'");
    }
}
