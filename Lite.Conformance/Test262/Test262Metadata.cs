using YamlDotNet.RepresentationModel;

namespace Lite.Conformance.Test262;

internal sealed record Test262Metadata(string[] Includes, string[] Flags, string[] Features,
    string? Esid, string? NegativePhase, string? NegativeType, string[] Errors)
{
    private static readonly HashSet<string> KnownFlags = new(StringComparer.Ordinal)
    {
        "onlyStrict", "noStrict", "module", "raw", "async", "generated", "CanBlockIsFalse", "CanBlockIsTrue", "non-deterministic",
    };

    internal string[] Modes => Flags.Contains("module") ? ["module"] : Flags.Contains("raw") ? ["raw"] :
        Flags.Contains("onlyStrict") ? ["strict"] : Flags.Contains("noStrict") ? ["sloppy"] : ["sloppy", "strict"];

    internal static Test262Metadata Parse(string source)
    {
        var errors = new List<string>();
        var start = source.IndexOf("/*---", StringComparison.Ordinal);
        var end = start < 0 ? -1 : source.IndexOf("---*/", start + 5, StringComparison.Ordinal);
        if (start < 0 || end < 0) return new([], [], [], null, null, null, ["Missing frontmatter"]);
        try
        {
            var yaml = new YamlStream();
            yaml.Load(new StringReader(source[(start + 5)..end]));
            if (yaml.Documents.Count != 1 || yaml.Documents[0].RootNode is not YamlMappingNode map)
                throw new InvalidDataException("Frontmatter must contain one mapping");
            string? Scalar(YamlMappingNode node, string key)
            {
                if (!node.Children.TryGetValue(new YamlScalarNode(key), out var value)) return null;
                if (value is not YamlScalarNode scalar) throw new InvalidDataException($"{key} must be a scalar");
                return scalar.Value;
            }
            string[] List(string key)
            {
                if (!map.Children.TryGetValue(new YamlScalarNode(key), out var value)) return [];
                if (value is not YamlSequenceNode list) throw new InvalidDataException($"{key} must be a sequence");
                return list.Children.Select(n => n is YamlScalarNode { Value: { Length: > 0 } text } ? text :
                    throw new InvalidDataException($"Invalid {key} entry")).ToArray();
            }
            var includes = List("includes");
            var flags = List("flags");
            var features = List("features");
            foreach (var flag in flags.Except(KnownFlags)) errors.Add($"Unknown flag: {flag}");
            foreach (var include in includes)
                if (include.Contains('\\') || include.StartsWith('/') || include.Split('/').Any(p => p is ".." or "." or "") || include.Contains(':'))
                    errors.Add($"Invalid harness include: {include}");
            if (flags.Contains("onlyStrict") && flags.Contains("noStrict") ||
                flags.Contains("CanBlockIsTrue") && flags.Contains("CanBlockIsFalse") ||
                flags.Contains("raw") && flags.Any(f => f is "module" or "onlyStrict" or "async") ||
                flags.Contains("module") && flags.Any(f => f is "onlyStrict" or "noStrict"))
                errors.Add("Contradictory flags");
            string? phase = null, type = null;
            if (map.Children.TryGetValue(new YamlScalarNode("negative"), out var negative))
            {
                if (negative is not YamlMappingNode neg) throw new InvalidDataException("negative must be a mapping");
                phase = Scalar(neg, "phase"); type = Scalar(neg, "type");
                if (phase is not ("parse" or "resolution" or "runtime")) errors.Add("Invalid negative phase");
                if (type is not ("SyntaxError" or "ReferenceError" or "TypeError" or "RangeError" or "EvalError" or "URIError" or "Error" or "Test262Error"))
                    errors.Add("Invalid negative error type");
                if (phase == "resolution" && !flags.Contains("module")) errors.Add("Resolution negative requires module flag");
            }
            return new(includes, flags, features, Scalar(map, "esid"), phase, type, errors.ToArray());
        }
        catch (Exception ex) when (ex is YamlDotNet.Core.YamlException or InvalidDataException or ArgumentException)
        { return new([], [], [], null, null, null, [$"Invalid frontmatter: {ex.Message}"]); }
    }
}
