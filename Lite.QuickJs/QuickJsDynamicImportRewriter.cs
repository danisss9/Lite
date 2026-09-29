using Acornima;
using Acornima.Ast;
using System.Text.Json;

namespace Lite.QuickJs;

/// <summary>Routes browser dynamic imports through the asynchronous host fetch path.</summary>
internal static class QuickJsDynamicImportRewriter
{
    internal static string Rewrite(string source, string filename, bool module)
    {
        if (!source.Contains("import", StringComparison.Ordinal)) return source;
        Node root;
        try
        {
            var parser = new Acornima.Parser();
            root = module ? parser.ParseModule(source, filename) : parser.ParseScript(source, filename);
        }
        catch (Acornima.SyntaxErrorException) { return source; }

        var edits = new List<(int Position, int Delete, string Text)>();
        Walk(root);
        if (edits.Count == 0) return source;
        var result = new System.Text.StringBuilder(source);
        foreach (var edit in edits.OrderByDescending(e => e.Position))
        {
            result.Remove(edit.Position, edit.Delete);
            result.Insert(edit.Position, edit.Text);
        }
        return result.ToString();

        void Walk(Node node)
        {
            if (node is ImportExpression expression)
            {
                edits.Add((expression.Range.Start, 6, "__liteImport"));
                edits.Add((expression.Range.End - 1, 0, ", undefined, " + JsonSerializer.Serialize(filename)));
            }
            foreach (var child in node.ChildNodes) Walk(child);
        }
    }
}
