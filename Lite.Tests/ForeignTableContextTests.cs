using Lite;
using Lite.Models;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>Fragment parses where table-context dispatch meets foreign content: a stray
/// HTML &lt;tr&gt; at the fragment root is the historical bug (the in-table re-dispatch ran
/// before the integration-point rule), so these assert the REC §8.2.5.5 shapes — foreign
/// tr/td elements stay inside their foreign subtree, tokens at MathML text integration
/// points take the in-body rules (which ignore a table-related start tag), and an HTML
/// table-context fragment keeps its corpus shape (html5lib tests_innerHTML_1).</summary>
public static class ForeignTableContextTests
{
    private static void Dump(System.Text.StringBuilder sb, LayoutNode node, int depth)
    {
        sb.Append(new string(' ', depth * 2)).Append('|').Append(node.TagName);
        if (node.Text.Length > 0) sb.Append(" \"").Append(node.Text).Append('"');
        sb.Append('\n');
        foreach (var child in node.Children) Dump(sb, child, depth + 1);
    }

    private static string DumpFragment(string html, string context)
    {
        var nodes = Parser.ParseFragment(html, context);
        var sb = new System.Text.StringBuilder();
        foreach (var node in nodes) Dump(sb, node, 0);
        return sb.ToString().TrimEnd('\n');
    }

    [Test]
    public static void TrContext_MathTr_DoesNotLeakHtmlTrToRoot()
    {
        // <math> keeps its MathML subtree; the second <tr> reaches the <mo> text
        // integration point and is in-body-ignored — never re-dispatched in-table.
        Equal("|MATH\n  |TR\n    |TD\n      |MO", DumpFragment("<math><tr><td><mo><tr>", "TR"));
    }

    [Test]
    public static void TrContext_HtmlTrTd_KeepsCorpusShape()
    {
        // html5lib tests_innerHTML_1: the row closes and the cell lands in the context.
        Equal("|TD", DumpFragment("<tr><td>", "TR"));
    }

    [Test]
    public static void SvgContext_TrTd_StaysForeign()
    {
        // SVG tr/td are foreign elements, not HTML table dispatches.
        Equal("|SVG\n  |TR\n    |TD", DumpFragment("<svg><tr><td>", "svg"));
    }
}
