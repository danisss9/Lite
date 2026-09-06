using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Lite.Conformance.Wpt;

internal sealed record WptMetadata(IReadOnlyList<string> Scripts, IReadOnlyList<string> Variants,
    bool Window, bool LongTimeout)
{
    internal static WptMetadata Parse(string source)
    {
        var scripts = new List<string>();
        var variants = new List<string>();
        bool window = true, longTimeout = false;
        foreach (Match match in Regex.Matches(source, @"(?m)^\s*//\s*META:\s*([\w-]+)=(.*?)\s*$"))
        {
            var value = match.Groups[2].Value.Trim();
            switch (match.Groups[1].Value)
            {
                case "script": scripts.Add(value); break;
                case "variant": variants.Add(value); break;
                case "global": window = value.Split(',').Any(g => g.Trim() == "window"); break;
                case "timeout": longTimeout = value == "long"; break;
            }
        }
        foreach (Match tag in Regex.Matches(source, @"<meta\b[^>]*>", RegexOptions.IgnoreCase))
        {
            var attrs = Regex.Matches(tag.Value, "([\\w-]+)\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)'|([^\\s>]+))")
                .Cast<Match>().GroupBy(m => m.Groups[1].Value, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => WebUtility.HtmlDecode(g.First().Groups.Cast<Group>()
                    .Skip(2).First(x => x.Success).Value), StringComparer.OrdinalIgnoreCase);
            if (!attrs.TryGetValue("name", out var name) || !attrs.TryGetValue("content", out var content)) continue;
            if (name == "variant") variants.Add(content);
            if (name == "timeout" && content == "long") longTimeout = true;
        }
        if (variants.Any(v => v.Length > 0 && v[0] is not ('?' or '#')))
            throw new InvalidDataException("WPT variants must be empty or start with '?' or '#'.");
        return new(scripts, variants.Distinct(StringComparer.Ordinal).ToArray(), window, longTimeout);
    }

    internal static string UrlPath(string path)
    {
        var suffix = path.IndexOfAny(['?', '#']);
        var file = suffix < 0 ? path : path[..suffix];
        var variant = suffix < 0 ? "" : path[suffix..];
        if (file.EndsWith(".any.js", StringComparison.Ordinal)) file = file[..^3] + ".html";
        else if (file.EndsWith(".window.js", StringComparison.Ordinal)) file = file[..^3] + ".html";
        return file + variant;
    }

    internal string Wrapper(string scriptPath)
    {
        if (!Window) throw new InvalidDataException("This test has no window global.");
        var html = new StringBuilder("<!doctype html><meta charset=utf-8>");
        html.Append("<script>self.GLOBAL={isWindow:function(){return true;},isWorker:function(){return false;},isShadowRealm:function(){return false;}};</script>");
        html.Append("<script src=\"/resources/testharness.js\"></script><script src=\"/resources/testharnessreport.js\"></script>");
        foreach (var script in Scripts.Append(scriptPath))
            html.Append("<script src=\"").Append(WebUtility.HtmlEncode(script)).Append("\"></script>");
        return html.ToString();
    }
}
