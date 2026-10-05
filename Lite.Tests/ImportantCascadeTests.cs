using Lite;
using Lite.Models;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>CSS 2.1 §6.4.1/§6.4.3: an author !important declaration outranks every author
/// normal declaration — higher specificity, later source order, and the style attribute —
/// while an inline !important declaration still outranks it. The bang may legally be written
/// with whitespace ("! important", the official suite's spelling), which AngleSharp's
/// declaration parser drops outright; style blocks are normalized before the CSSOM is built.
/// </summary>
public static class ImportantCascadeTests
{
    [Test]
    public static void AuthorImportantBeatsInlineSpecificityAndOrder()
    {
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><head><style>" +
            "p { color: green ! important; } p { color: red; } p#id1 { color: red; }" +
            "</style></head><body>" +
            "<p>one</p><p id='id1'>two</p><p style='color: red'>three</p>" +
            "</body></html>", isSrcdoc: true, "http://test/", 800, 600);
        var ps = FindAll(page.Root, "P");
        True(ps.Count == 3, $"expected 3 p, got {ps.Count}");
        for (var i = 0; i < ps.Count; i++)
            Console.WriteLine($"p{i} color='{ps[i].Style.GetPropertyValue("color")}'");
        var rules = Parser.CssRules.Where(r => r.ImportantProps.Count > 0).ToList();
        Console.WriteLine($"important rules: {rules.Count} :: " +
            string.Join("; ", rules.Select(r => $"{r.Selector}[{string.Join(",", r.ImportantProps)}]={r.Properties.GetValueOrDefault("color")}")));
        True(ps.All(p => p.Style.GetPropertyValue("color").Contains("0, 128, 0") ||
                          p.Style.GetPropertyValue("color").Contains("green")),
            "all paragraphs must be green (author !important beats inline and specificity)");
    }

    private static List<LayoutNode> FindAll(LayoutNode node, string tag)
    {
        var result = new List<LayoutNode>();
        if (node.TagName == tag) result.Add(node);
        foreach (var c in node.Children) result.AddRange(FindAll(c, tag));
        return result;
    }
}
