using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AngleSharp;
using AngleSharp.Css;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using Lite;
using Lite.Conformance.Harness;

namespace Lite.Conformance.Wpt;

/// <summary>
/// Parse-fidelity attribution diagnostic (Milestone 2, attribution pass).
///
/// Runs every html5lib tree-construction case twice — once through a plain AngleSharp
/// parse of the raw input (the "native" parser output), once through Lite's real
/// pipeline (Parser.ParseChildPage: input rewriting, origin-sheet injection, script
/// execution, projection side effects) — and compares both tree dumps against the
/// .dat expectation using the same pipe-format dump and sheet-visibility filter the
/// vendored WPT runner (html/syntax/parsing/resources/test.js) applies.
///
/// The classification separates engine work from dependency work:
///   pass            both dumps match the expectation
///   anglesharp      the native parse already differs and the pipeline output equals it
///                   (a tree-builder/tokenizer deviation to fix in or over AngleSharp)
///   pipeline        the native parse matches but the pipeline damaged the tree
///                   (input rewriting, sheet injection residue, script/projection effects)
///   pipeline-fixes  the native parse differs but the pipeline output matches (informational)
///   mixed           both differ, differently
///   error           one of the paths threw
///
/// This is a diagnostic: it is not readiness evidence and does not feed the profile.
/// </summary>
internal static class ParseAttribution
{
    private const string HtmlNs = "http://www.w3.org/1999/xhtml";
    private const string MathNs = "http://www.w3.org/1998/Math/MathML";
    private const string SvgNs = "http://www.w3.org/2000/svg";
    private const string XlinkNs = "http://www.w3.org/1999/xlink";
    private const string XmlNs = "http://www.w3.org/XML/1998/namespace";
    private const string XmlnsNs = "http://www.w3.org/2000/xmlns/";

    private const int ViewportWidth = 800;
    private const int ViewportHeight = 600;
    private const int MaxRecordedLength = 2000;

    private sealed class DatCase
    {
        public required string File;
        public required int Index;
        public required string Data;
        public required string Document;
        public string? Fragment;
    }

    private sealed record CaseOut(string File, int Index, string? Fragment, string Classification,
        string Cluster, string Signature, string Expected, string Native, string Pipeline,
        string? LedgerVerdict = null);

    private sealed record LedgerEntry(string Verdict, string Clause, string CorrectedDocument, bool Reviewed);

    /// <summary>Scripted cases whose corpus expectations encode the streaming
    /// document.write insertion point. The pipeline executes classic scripts
    /// after the tree parse by design (layout determinism), so a mid-parse
    /// write cannot splice into a finished token stream; the streaming
    /// semantics live on the frame-level write path (document.open/write/
    /// close), which the html5 runner suite and DocumentWriteTests exercise.
    /// The gate counts these as runner-excluded, never as decided.</summary>
    internal static readonly Dictionary<(string File, int Index), string> RunnerExclusions = new()
    {
        [("scripted_webkit01.dat", 0)] =
            "the case's inline document.write needs the streaming insertion point; " +
            "the pipeline's post-parse script execution defers it by design",
        [("scripted_webkit01.dat", 1)] =
            "nested document.write of script-bearing markup needs the streaming " +
            "insertion point; the frame-level write path covers the semantics",
    };

    internal const string LedgerFileName = "Wpt/html5-parse-expectations.json";

    private static Dictionary<(string File, int Index, string? Fragment), LedgerEntry> LoadLedger(
        out int unreviewed)
    {
        var ledger = new Dictionary<(string, int, string?), LedgerEntry>();
        unreviewed = 0;
        var path = ConformancePaths.Manifest(LedgerFileName);
        if (!File.Exists(path))
        {
            return ledger;
        }

        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        foreach (var caseNode in root["cases"]!.AsArray())
        {
            var obj = caseNode!.AsObject();
            var reviewed = obj["reviewed"]!.GetValue<bool>();
            var entry = new LedgerEntry(
                obj["verdict"]!.GetValue<string>(),
                obj["clause"]!.GetValue<string>(),
                obj["correctedDocument"]!.GetValue<string>(),
                reviewed);
            if (!reviewed)
            {
                unreviewed++;
                continue;
            }

            var fragment = obj["fragment"] is null ? null : obj["fragment"]!.GetValue<string>();
            ledger[(obj["file"]!.GetValue<string>(), obj["index"]!.GetValue<int>(), fragment)] = entry;
        }

        return ledger;
    }

    /// <summary>The parse-fidelity gate: every case must end in a verified,
    /// machine-checked outcome on this build identity — a raw pass, a
    /// pipeline-fixes artifact (the pipeline matches the corpus; only the
    /// scriptless native baseline differs), a reviewed ledger-pass, or a
    /// pinned runner-exclusion. Stale or mismatched ledger entries fail the
    /// gate.</summary>
    public static int RunGate(string? reportPath) => Run(reportPath, gate: true);

    public static int Run(string? reportPath, bool gate = false)
    {
        var resources = Path.Combine(ConformancePaths.Vendor, "wpt", "html", "syntax", "parsing", "resources");
        if (!Directory.Exists(resources))
        {
            Console.Error.WriteLine($"html5lib corpus not found at {resources}; run scripts/fetch-tests.ps1 first.");
            return 2;
        }

        var ledger = LoadLedger(out var unreviewedLedger);
        ConformanceServer.Start(cssRegressionMode: false);
        var baseUrl = ConformanceServer.BaseUrl;

        var files = Directory.GetFiles(resources, "*.dat").OrderBy(f => f, StringComparer.Ordinal).ToArray();
        var done = 0;
        var docCases = 0;
        var fragmentCases = 0;
        var totals = new Dictionary<string, int>();
        var byFile = new List<(string File, Dictionary<string, int> Totals, int Cases)>();
        var failures = new List<CaseOut>();
        var ledgerStale = new List<string>();
        var ledgerMismatch = new List<string>();
        var openCases = 0;

        foreach (var file in files)
        {
            var cases = ParseDat(File.ReadAllText(file))
                .Select((sections, index) => (sections, index))
                .Where(s => !s.sections.ContainsKey("script-off"))
                .Select(s => new DatCase
                {
                    File = Path.GetFileName(file),
                    Index = s.index,
                    Data = s.sections.GetValueOrDefault("data") ?? "",
                    Document = s.sections.GetValueOrDefault("document") ?? "",
                    Fragment = s.sections.TryGetValue("document-fragment", out var frag) ? frag : null,
                })
                .ToList();

            var fileTotals = new Dictionary<string, int>();
            foreach (var testCase in cases)
            {
                var (classification, cluster, signature, native, pipeline, expected) =
                    RunCase(testCase, baseUrl);
                Bump(totals, classification);
                Bump(fileTotals, classification);
                string? ledgerVerdict = null;
                if (RunnerExclusions.ContainsKey((testCase.File, testCase.Index)))
                {
                    ledgerVerdict = "runner-excluded";
                    Bump(totals, "runner-excluded");
                    Bump(fileTotals, "runner-excluded");
                }
                else if (ledger.TryGetValue((testCase.File, testCase.Index, testCase.Fragment), out var entry))
                {
                    if (classification == "pass")
                    {
                        ledgerVerdict = "stale";
                        ledgerStale.Add($"{testCase.File}#{testCase.Index}");
                    }
                    else if (pipeline == entry.CorrectedDocument)
                    {
                        ledgerVerdict = "pass";
                        Bump(totals, "ledger-pass");
                        Bump(fileTotals, "ledger-pass");
                    }
                    else
                    {
                        ledgerVerdict = "mismatch";
                        ledgerMismatch.Add($"{testCase.File}#{testCase.Index}");
                    }
                }
                if (classification is not "pass")
                {
                    failures.Add(new CaseOut(testCase.File, testCase.Index, testCase.Fragment,
                        classification, cluster, signature,
                        Truncate(expected), Truncate(native ?? ""), Truncate(pipeline ?? ""),
                        ledgerVerdict));
                    if (ledgerVerdict is null && classification is not "pipeline-fixes") openCases++;
                }
                if ((++done & 0xFF) == 0) GC.Collect();
            }
            docCases += cases.Count(c => c.Fragment is null);
            fragmentCases += cases.Count(c => c.Fragment is not null);
            byFile.Add((Path.GetFileName(file), fileTotals, cases.Count));
            Console.WriteLine($"{Path.GetFileName(file),-40} {cases.Count,4} cases  " + FormatCounts(fileTotals));
        }

        var pass = totals.GetValueOrDefault("pass");
        var fixes = totals.GetValueOrDefault("pipeline-fixes");
        var ledgerPass = totals.GetValueOrDefault("ledger-pass");
        var excluded = totals.GetValueOrDefault("runner-excluded");
        var total = totals.Where(kv => kv.Key is not ("ledger-pass" or "runner-excluded")).Sum(kv => kv.Value);
        var decided = pass + fixes + ledgerPass;
        Console.WriteLine();
        Console.WriteLine($"=== html5 parse attribution: {total} unique cases " +
                          $"(survey-aligned subtests ≈ {docCases * 3 + fragmentCases}) ===");
        Console.WriteLine($"  engine-pass      {pass + fixes,5}  = pass + pipeline-fixes (pipeline output matches)");
        Console.WriteLine($"  decided        {decided,5}  = engine-pass + ledger-pass (reviewed corrections)");
        foreach (var key in new[] { "pass", "ledger-pass", "runner-excluded", "anglesharp", "pipeline", "mixed", "pipeline-fixes", "error" })
            Console.WriteLine($"  {key,-15} {totals.GetValueOrDefault(key),5}");
        Console.WriteLine($"  ledger: {ledgerPass} accepted, {ledgerStale.Count} stale, " +
                          $"{ledgerMismatch.Count} mismatched, {unreviewedLedger} unreviewed entries");
        foreach (var stale in ledgerStale)
            Console.Error.WriteLine($"ledger stale (case passes raw, entry must be regenerated): {stale}");
        foreach (var mismatch in ledgerMismatch)
            Console.Error.WriteLine($"ledger mismatch (engine != correctedDocument): {mismatch}");
        Console.WriteLine();
        Console.WriteLine("=== per-cluster attribution (effective cluster) ===");
        foreach (var group in failures.GroupBy(f => f.Cluster).OrderByDescending(g => g.Count()))
            Console.WriteLine($"  {group.Key,-20} {group.Count(),5}  " +
                string.Join("  ", group.GroupBy(f => f.Classification).OrderBy(g => g.Key)
                    .Select(g => $"{g.Key}={g.Count()}")));

        var artifact = Path.GetFullPath(reportPath ?? Path.Combine(ConformancePaths.EnsureArtifacts(), "html5-parse-attribution.json"));
        WriteArtifact(artifact, totals, byFile, failures, ledgerStale.Count, ledgerMismatch.Count, unreviewedLedger);
        Console.WriteLine();
        Console.WriteLine($"attribution written: {artifact}");
        if (gate)
        {
            Console.WriteLine();
            Console.WriteLine($"html5-parse gate: decided {decided}/{total} (+{excluded} runner-excluded scripted cases)" +
                              (openCases > 0 ? $"; {openCases} open case(s) without a verified outcome" : "") +
                              (ledgerStale.Count > 0 ? $"; {ledgerStale.Count} stale ledger entr(y/ies)" : "") +
                              (ledgerMismatch.Count > 0 ? $"; {ledgerMismatch.Count} mismatched ledger entr(y/ies)" : ""));
            return openCases == 0 && ledgerStale.Count == 0 && ledgerMismatch.Count == 0 ? 0 : 1;
        }

        return 0;
    }

    // ----- one case, both paths -----

    private static (string Classification, string Cluster, string Signature, string? Native, string? Pipeline, string Expected) RunCase(
        DatCase testCase, string baseUrl)
    {
        var expected = "#document\n" + testCase.Document;

        string? native = null, pipeline = null;
        string? nativeError = null, pipelineError = null;
        try
        {
            native = testCase.Fragment is { } fragment
                ? DumpFragment(testCase.Data, fragment, baseUrl, usePipeline: false)
                : DumpDocument(testCase.Data, baseUrl, usePipeline: false);
        }
        catch (Exception ex) { nativeError = ex.Message; }
        try
        {
            pipeline = testCase.Fragment is { } fragment
                ? DumpFragment(testCase.Data, fragment, baseUrl, usePipeline: true)
                : DumpDocument(testCase.Data, baseUrl, usePipeline: true);
        }
        catch (Exception ex) { pipelineError = ex.Message; }

        string classification;
        if (nativeError is not null || pipelineError is not null)
            classification = "error";
        else
            classification = Classify(expected, native!, pipeline!);

        var (signature, signatureCluster) = classification is "pass"
            ? ("", "pass")
            : DiffSignature(expected, pipeline ?? native ?? "");
        var cluster = FileCluster(testCase.File) is { Length: > 0 } fc ? fc : signatureCluster;
        return (classification, cluster,
            classification is "error" ? $"native: {nativeError ?? "-"} | pipeline: {pipelineError ?? "-"}" : signature,
            native, pipeline, expected);
    }

    private static string Classify(string expected, string native, string pipeline)
    {
        if (native == expected && pipeline == expected) return "pass";
        if (native == expected) return "pipeline";
        if (pipeline == native) return "anglesharp";
        if (pipeline == expected) return "pipeline-fixes";
        return "mixed";
    }

    // ----- path A: plain AngleSharp on the raw input -----

    private static IDocument OpenNativeDocument(string content, string baseUrl)
    {
        // The engine's own parse configuration (scripting-flag semantics included), so the
        // native baseline isolates AngleSharp's tree construction rather than configuration
        // deltas the engine does not own. The context is deliberately NOT disposed: its
        // disposal tears down the document before the caller dumps it (a diagnostic-run
        // leak, bounded by the corpus size).
        var context = BrowsingContext.New(Lite.Parser.ParseConfiguration(ViewportWidth, ViewportHeight));
        return context.OpenAsync(req => req.Address(baseUrl).Content(content)).Result;
    }

    private static string DumpDocument(string input, string baseUrl, bool usePipeline)
    {
        if (!usePipeline)
        {
            using var document = OpenNativeDocument(input, baseUrl);
            return SerializeTree(document, static node => node.ChildNodes);
        }
        var page = Parser.ParseChildPage(input, isSrcdoc: true, baseUrl, ViewportWidth, ViewportHeight);
        var document1 = page.Document ?? throw new InvalidOperationException("pipeline produced no document");
        var filter = MakeFilter(page.Engine.DocumentState.ParserContext);
        var dump = SerializeTree(document1, filter);
        page.Engine.Dispose();
        return dump;
    }

    private static string DumpFragment(string input, string containerSpec, string baseUrl, bool usePipeline)
    {
        if (!usePipeline)
        {
            using var nativeDocument = OpenNativeDocument(
                "<!DOCTYPE html><html><head></head><body></body></html>", baseUrl);
            return SetAndDumpFragment(nativeDocument.CreateElement, nativeDocument, containerSpec, input,
                static node => node.ChildNodes);
        }
        var page = Parser.ParseChildPage("<!DOCTYPE html><html><head></head><body></body></html>",
            isSrcdoc: true, baseUrl, ViewportWidth, ViewportHeight);
        try
        {
            var document = page.Document ?? throw new InvalidOperationException("pipeline produced no document");
            var filter = MakeFilter(page.Engine.DocumentState.ParserContext);
            return SetAndDumpFragment(document.CreateElement, document, containerSpec, input, filter);
        }
        finally
        {
            page.Engine.Dispose();
        }
    }

    /// <summary>Identical to the vendored innerHTML runner: create the container element,
    /// assign innerHTML (AngleSharp parses the fragment against the container's context),
    /// dump the container — or the template's content fragment — and replace the root
    /// marker line with "#document".</summary>
    private static string SetAndDumpFragment(Func<string, IElement> createElement, IDocument document,
        string containerSpec, string input, Func<INode, IEnumerable<INode>> filter)
    {
        var container = CreateContainer(document, containerSpec);
        container.InnerHtml = input;
        INode root = container is IHtmlTemplateElement { Content: { } content } ? content : container;
        return ReplaceRootMarker(SerializeTree(root, filter));
    }

    private static string ReplaceRootMarker(string dump)
    {
        var newline = dump.IndexOf('\n');
        return newline < 0 ? "#document" : "#document" + dump[newline..];
    }

    private static IElement CreateContainer(IDocument document, string containerSpec)
    {
        var parts = containerSpec.Split(' ');
        if (parts.Length > 1)
            return document.CreateElement(NamespaceFor(parts[0]), parts[0] + ":" + parts[1]);
        return document.CreateElement(containerSpec);
    }

    /// <summary>The script-visible child filter the wrappers apply: the engine-injected
    /// origin sheets (UA/user style elements) are DOM members for the cascade but are not
    /// author content, so they stay out of every script-visible child list.</summary>
    private static Func<INode, IEnumerable<INode>> MakeFilter(Parser.ParseState? parse)
    {
        if (parse is null || (parse.UaStyleElement is null && parse.UserStyleElements.Count == 0))
            return static node => node.ChildNodes;
        return node => node.ChildNodes.Where(child =>
            !ReferenceEquals(parse.UaStyleElement, child) &&
            !parse.UserStyleElements.Any(user => ReferenceEquals(user, child)));
    }

    // ----- pipe-format tree dump (mirrors resources/test.js serializeTree) -----

    private static string SerializeTree(INode root, Func<INode, IEnumerable<INode>> childrenOf)
    {
        var lines = new List<string>();
        Walk(new WalkNode(root), 0, lines, childrenOf);
        return string.Join("\n", lines);
    }

    private readonly record struct WalkNode(INode? Node, string? MergedText = null);

    private static void Walk(WalkNode view, int depth, List<string> lines, Func<INode, IEnumerable<INode>> childrenOf)
    {
        var pad = depth > 0 ? new string(' ', 2 * depth - 1) : "";
        var innerPad = new string(' ', 2 * depth + 1);
        if (view.MergedText is { } merged)
        {
            lines.Add($"|{pad}\"{merged}\"");
            return;
        }
        var node = view.Node!;
        switch (node)
        {
            case IDocumentType doctype:
                if (string.IsNullOrEmpty(doctype.Name)) lines.Add($"|{pad}<!DOCTYPE >");
                else if (doctype.PublicIdentifier.Length > 0 || doctype.SystemIdentifier.Length > 0)
                    lines.Add($"|{pad}<!DOCTYPE {doctype.Name} \"{doctype.PublicIdentifier}\" \"{doctype.SystemIdentifier}\">");
                else lines.Add($"|{pad}<!DOCTYPE {doctype.Name}>");
                break;
            case IDocument:
                lines.Add("#document");
                break;
            case IDocumentFragment:
                lines.Add("#document-fragment");
                break;
            case IComment comment:
                lines.Add($"|{pad}<!-- {comment.Data} -->");
                break;
            case IProcessingInstruction instruction:
                lines.Add($"|{pad}<?{instruction.Target}{(instruction.Data.Length > 0 ? " " + instruction.Data : "")}?>");
                break;
            case IText text:
                lines.Add($"|{pad}\"{text.Data}\"");
                break;
            case IElement element:
            {
                if (element.GetAttribute("data-skip") is not null) return;
                var ns = element.NamespaceUri;
                var foreign = !string.IsNullOrEmpty(ns) && ns != HtmlNs;
                var tag = foreign ? $"{PrefixFor(ns!)} {element.LocalName}" : element.LocalName;
                lines.Add($"|{pad}<{tag}>");
                var attrs = element.Attributes
                    .Select(a => (
                        name: (string.IsNullOrEmpty(a.NamespaceUri) ? "" : PrefixFor(a.NamespaceUri) + " ") + a.LocalName,
                        value: a.Value))
                    .OrderBy(a => a.name, StringComparer.Ordinal);
                foreach (var (name, value) in attrs)
                    lines.Add($"|{innerPad}{name}=\"{value}\"");
                if (!foreign && element.LocalName == "template" &&
                    element is IHtmlTemplateElement { Content: { } templateContent })
                {
                    lines.Add($"|{innerPad}content");
                    foreach (var child in Coalesce(childrenOf(templateContent)))
                        Walk(child, depth + 2, lines, childrenOf);
                }
                break;
            }
        }
        foreach (var child in Coalesce(childrenOf(node)))
            Walk(child, depth + 1, lines, childrenOf);
    }

    /// <summary>Virtual DOM normalize(): adjacent text nodes merge and empty ones drop,
    /// exactly what the vendored runner calls on the root before dumping.</summary>
    private static IEnumerable<WalkNode> Coalesce(IEnumerable<INode> children)
    {
        var run = new StringBuilder();
        foreach (var child in children)
        {
            if (child is IText text)
            {
                run.Append(text.Data);
                continue;
            }
            if (run.Length > 0)
            {
                yield return new WalkNode(null, run.ToString());
                run.Clear();
            }
            yield return new WalkNode(child);
        }
        if (run.Length > 0)
            yield return new WalkNode(null, run.ToString());
    }

    private static string PrefixFor(string ns) => ns switch
    {
        MathNs => "math",
        SvgNs => "svg",
        XlinkNs => "xlink",
        XmlNs => "xml",
        XmlnsNs => "xmlns",
        HtmlNs => "html",
        _ => "html",
    };

    private static string NamespaceFor(string prefix) => prefix switch
    {
        "math" or "mathml" => MathNs,
        "svg" => SvgNs,
        "xlink" => XlinkNs,
        "xml" => XmlNs,
        "xmlns" => XmlnsNs,
        _ => HtmlNs,
    };

    // ----- .dat parsing (a literal port of resources/test.js parseDat) -----

    private static List<Dictionary<string, string>> ParseDat(string text)
    {
        if (text.EndsWith("\n")) text = text[..^1];
        var lines = text.Split('\n');
        var cases = new List<Dictionary<string, string>>();
        Dictionary<string, string>? data = null;
        string? key = null;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i] + (i == lines.Length - 1 ? "" : "\n");
            var heading = SectionHeading(line);
            if (heading is not null)
            {
                if (data is not null && heading == "data")
                {
                    if (key is not null && data[key].Length > 0)
                        data[key] = data[key][..^1];
                    cases.Add(NormalizeSections(data));
                    data = null;
                }
                data ??= [];
                key = heading;
                data[key] = "";
            }
            else if (data is not null && key is not null)
            {
                data[key] += line;
            }
        }
        if (data is not null)
            cases.Add(NormalizeSections(data));
        return cases;

        static string? SectionHeading(string line) =>
            line.StartsWith('#') && line[1..].Trim() is { Length: > 0 } heading ? heading : null;

        // The html5lib .dat format decodes "\uXXXX" escapes in every section
        // (processing-instructions.dat carries its non-ASCII inputs this way,
        // while its document sections already hold the raw characters), so a
        // case could never match while the input stayed escaped. Surrogate
        // pairs decode together into the astral character; a lone surrogate
        // escape becomes U+FFFD (a lone surrogate cannot exist in a string).
        static string DecodeDatEscapes(string value)
        {
            if (!value.Contains("\\u", StringComparison.Ordinal))
            {
                return value;
            }

            var builder = new StringBuilder(value.Length);
            var position = 0;
            var pendingHigh = -1;
            foreach (var match in Regex.Matches(value, @"\\u([0-9A-Fa-f]{4})").Cast<Match>())
            {
                builder.Append(value[position..match.Index]);
                position = match.Index + match.Length;
                var unit = Convert.ToInt32(match.Groups[1].Value, 16);

                if (pendingHigh >= 0)
                {
                    if (unit is >= 0xDC00 and <= 0xDFFF)
                    {
                        builder.Append(char.ConvertFromUtf32(
                            0x10000 + ((pendingHigh - 0xD800) << 10) + (unit - 0xDC00)));
                    }
                    else
                    {
                        builder.Append('\uFFFD');
                        builder.Append(unit is >= 0xD800 and <= 0xDFFF
                            ? "\uFFFD"
                            : char.ConvertFromUtf32(unit));
                    }

                    pendingHigh = -1;
                }
                else if (unit is >= 0xD800 and <= 0xDBFF)
                {
                    pendingHigh = unit;
                }
                else if (unit is >= 0xDC00 and <= 0xDFFF)
                {
                    builder.Append('\uFFFD');
                }
                else
                {
                    builder.Append(char.ConvertFromUtf32(unit));
                }
            }

            if (pendingHigh >= 0)
            {
                builder.Append('\uFFFD');
            }

            builder.Append(value[position..]);
            return builder.ToString();
        }

        static Dictionary<string, string> NormalizeSections(Dictionary<string, string> sections)
        {
            var normalized = new Dictionary<string, string>();
            foreach (var (section, value) in sections)
            {
                var decoded = DecodeDatEscapes(value);
                normalized[section] = decoded.EndsWith("\n") ? decoded[..^1] : decoded;
            }

            return normalized;
        }
    }

    // ----- clustering -----

    private static string FileCluster(string file) => Path.GetFileNameWithoutExtension(file) switch
    {
        "adoption01" or "adoption02" or "scripted_adoption01" => "adoption-agency",
        "entities01" or "entities02" or "named-character-references" => "named-entities",
        "template" => "template",
        "ruby" => "ruby",
        "foreign-fragment" or "math" or "svg" => "foreign-content",
        "comments01" or "processing-instructions" => "comments-pi",
        "doctype01" or "quirks01" => "doctype-quirks",
        _ => "",
    };

    private static (string Signature, string Cluster) DiffSignature(string expected, string actual)
    {
        var expectedLines = expected.Split('\n');
        var actualLines = actual.Split('\n');
        for (var i = 0; i < Math.Max(expectedLines.Length, actualLines.Length); i++)
        {
            var expectedLine = i < expectedLines.Length ? expectedLines[i] : "<eof>";
            var actualLine = i < actualLines.Length ? actualLines[i] : "<eof>";
            if (expectedLine == actualLine) continue;
            var pair = actualLine + "\n" + expectedLine;
            return (Truncate($"{actualLine}  ⇔  {expectedLine}"), ClassifyPair(pair));
        }
        return ("", "other");
    }

    private static string ClassifyPair(string pair)
    {
        if (pair.Contains("<svg", StringComparison.Ordinal) || pair.Contains("<math", StringComparison.Ordinal))
            return "foreign-content";
        if (pair.Contains("template", StringComparison.Ordinal) || Regex.IsMatch(pair, @"^\|\s+content$", RegexOptions.Multiline))
            return "template";
        if (pair.Contains("frameset", StringComparison.Ordinal) || pair.Contains("<frame", StringComparison.Ordinal))
            return "frameset";
        if (Regex.IsMatch(pair, @"<(ruby|rb|rt|rp|rbc)\b"))
            return "ruby";
        if (Regex.IsMatch(pair, "&[#a-zA-Z0-9]+;?"))
            return "named-entities";
        if (pair.Contains("<!--", StringComparison.Ordinal) || pair.Contains("<?", StringComparison.Ordinal))
            return "comments-pi";
        return "other";
    }

    // ----- reporting -----

    private static void Bump(Dictionary<string, int> counts, string key)
    {
        counts[key] = counts.GetValueOrDefault(key) + 1;
    }

    private static string FormatCounts(Dictionary<string, int> totals)
    {
        var pass = totals.GetValueOrDefault("pass");
        var total = totals.Values.Sum();
        return $"{pass}/{total} pass ({(total == 0 ? 0 : 100.0 * pass / total):F1}%)";
    }

    private static string Truncate(string value) =>
        value.Length <= MaxRecordedLength ? value : value[..MaxRecordedLength] + "…";

    private static void WriteArtifact(string path, Dictionary<string, int> totals,
        List<(string File, Dictionary<string, int> Totals, int Cases)> byFile, List<CaseOut> failures,
        int ledgerStale, int ledgerMismatch, int ledgerUnreviewed)
    {
        var payload = new
        {
            formatVersion = 1,
            kind = "html5-parse-attribution",
            generatedUtc = DateTime.UtcNow.ToString("o"),
            diagnostic = true,
            totals,
            ledger = new { accepted = totals.GetValueOrDefault("ledger-pass"), stale = ledgerStale,
                           mismatched = ledgerMismatch, unreviewed = ledgerUnreviewed },
            files = byFile.Select(f => new { file = f.File, cases = f.Cases, totals = f.Totals }),
            failures,
        };
        var options = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        File.WriteAllText(path, JsonSerializer.Serialize(payload, options));
    }
}
