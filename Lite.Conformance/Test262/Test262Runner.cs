using System.Diagnostics;
using Jint;
using Jint.Runtime;
using Lite.Conformance.Harness;

namespace Lite.Conformance.Test262;

/// <summary>
/// Runs the curated ES2020 subset of tc39/test262 against bare Jint engines (this suite
/// verifies the JS engine itself; Lite's DOM is not involved). Tests are discovered under
/// vendor\test262\test\, harness includes come from vendor\test262\harness\.
/// </summary>
internal static class Test262Runner
{
    private sealed record Frontmatter(
        List<string> Includes,
        HashSet<string> Flags,
        string? NegativePhase,
        string? NegativeType);

    private static readonly Dictionary<string, string> _harnessCache = new();

    public static int Run(string? filter)
    {
        var test262Root = Path.Combine(ConformancePaths.Vendor, "test262");
        var testRoot = Path.Combine(test262Root, "test");
        if (!Directory.Exists(testRoot))
        {
            Console.WriteLine($"test262: {testRoot} not found — run scripts\\fetch-tests.ps1 first.");
            return 1;
        }

        var skipList = LoadSkipList();
        var files = Directory.EnumerateFiles(testRoot, "*.js", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith("_FIXTURE.js", StringComparison.OrdinalIgnoreCase))
            .Select(f => Path.GetRelativePath(test262Root, f).Replace('\\', '/'))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        if (!string.IsNullOrEmpty(filter))
            files = files.Where(f => f.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

        if (files.Count == 0)
        {
            Console.WriteLine("test262: no test files match.");
            return 0;
        }

        var result = new SuiteResult();
        int skipped = 0;

        foreach (var rel in files)
        {
            var skip = skipList.FirstOrDefault(s => rel.Contains(s.Path, StringComparison.OrdinalIgnoreCase));
            if (skip is not null)
            {
                skipped++;
                continue;
            }

            if (Environment.GetEnvironmentVariable("T262_TRACE") == "1")
            {
                Console.Error.WriteLine($"[running] {rel}");
                Console.Error.Flush();
            }

            bool passed;
            string detail;
            try
            {
                passed = RunOne(test262Root, rel, out detail);
            }
            catch (Exception ex)
            {
                passed = false;
                detail = $"runner crashed: {ex.Message}";
            }

            if (passed)
            {
                result.Passed++;
            }
            else
            {
                result.Failed++;
                result.Problems.Add($"{rel}: {detail}");
                Console.WriteLine($"  FAIL  {rel}: {detail}");
            }
        }

        Console.WriteLine($"  ({skipped} skipped via skip-list.txt)");
        return result.Report("test262");
    }

    private static bool RunOne(string test262Root, string relPath, out string detail)
    {
        var fullPath = Path.Combine(test262Root, relPath.Replace('/', Path.DirectorySeparatorChar));
        var source = File.ReadAllText(fullPath);
        var meta = ParseFrontmatter(source);

        if (meta.Flags.Contains("raw"))
            return ExecuteMode(test262Root, fullPath, source, meta, strict: false, raw: true, out detail);

        if (meta.Flags.Contains("module"))
            return ExecuteMode(test262Root, fullPath, source, meta, strict: false, raw: false, out detail);

        bool runSloppy = !meta.Flags.Contains("onlyStrict");
        bool runStrict = !meta.Flags.Contains("noStrict");

        if (runSloppy && !ExecuteMode(test262Root, fullPath, source, meta, strict: false, raw: false, out detail))
        {
            detail = $"[sloppy] {detail}";
            return false;
        }
        if (runStrict && !ExecuteMode(test262Root, fullPath, source, meta, strict: true, raw: false, out detail))
        {
            detail = $"[strict] {detail}";
            return false;
        }

        detail = "ok";
        return true;
    }

    private static bool ExecuteMode(string test262Root, string fullPath, string source, Frontmatter meta,
        bool strict, bool raw, out string detail)
    {
        var isModule = meta.Flags.Contains("module");
        var isAsync = meta.Flags.Contains("async");
        var testDir = Path.GetDirectoryName(fullPath)!;

        string? asyncOutcome = null;

        var engine = new Engine(opts =>
        {
            opts.TimeoutInterval(TimeSpan.FromSeconds(10));
            // Convert runaway recursion into a catchable Jint exception instead of a CLR
            // StackOverflowException (which would kill the whole runner process).
            opts.LimitRecursion(2000);
            opts.MaxStatements(10_000_000);
            // Enable modules for every test rooted at the test's directory: module-flagged
            // tests are imported directly, and classic-script tests may still use dynamic
            // import() with relative specifiers to sibling _FIXTURE.js files.
            opts.EnableModules(testDir);
        });

        engine.SetValue("print", new Action<object?>(msg => asyncOutcome ??= msg?.ToString()));
        engine.SetValue("$262", new Dollar262(engine));

        Exception? error = null;
        try
        {
            if (!raw)
            {
                foreach (var include in HarnessIncludes(meta, isAsync))
                    engine.Execute(LoadHarnessFile(test262Root, include));
            }

            if (isModule)
            {
                engine.Modules.Import("./" + Path.GetFileName(fullPath));
            }
            else
            {
                var code = strict ? "\"use strict\";\n" + source : source;
                engine.Execute(code);
            }

            if (isAsync)
            {
                var sw = Stopwatch.StartNew();
                while (asyncOutcome is null && sw.ElapsedMilliseconds < 10_000)
                {
                    engine.Advanced.ProcessTasks();
                    if (asyncOutcome is null) Thread.Sleep(1);
                }
            }
            else
            {
                engine.Advanced.ProcessTasks();
            }
        }
        catch (Exception ex)
        {
            error = ex;
        }

        // Negative tests: an error of the right kind is the pass condition.
        if (meta.NegativeType is not null || meta.NegativePhase is not null)
        {
            if (error is null)
            {
                detail = $"expected {meta.NegativePhase} {meta.NegativeType} but no error was thrown";
                return false;
            }
            if (MatchesNegative(error, meta))
            {
                detail = "ok (negative)";
                return true;
            }
            detail = $"expected {meta.NegativePhase} {meta.NegativeType} but got: {error.GetType().Name}: {error.Message}";
            return false;
        }

        if (error is not null)
        {
            detail = $"{error.GetType().Name}: {Truncate(error.Message)}";
            return false;
        }

        if (isAsync)
        {
            if (asyncOutcome is null)
            {
                detail = "async test timed out (no Test262:AsyncTestComplete)";
                return false;
            }
            if (!asyncOutcome.StartsWith("Test262:AsyncTestComplete", StringComparison.Ordinal))
            {
                detail = $"async failure: {Truncate(asyncOutcome)}";
                return false;
            }
        }

        detail = "ok";
        return true;
    }

    private static bool MatchesNegative(Exception error, Frontmatter meta)
    {
        var text = error.Message + " " + error.GetType().Name;
        if (error is JavaScriptException jsEx)
            text += " " + jsEx.Error.ToString();

        if (meta.NegativePhase is "parse" or "resolution")
        {
            // Parse/resolution failures surface as parser or syntax errors of various shapes.
            return text.Contains("SyntaxError", StringComparison.OrdinalIgnoreCase)
                || error.GetType().FullName?.Contains("Parser", StringComparison.OrdinalIgnoreCase) == true
                || (meta.NegativeType is not null && text.Contains(meta.NegativeType, StringComparison.Ordinal));
        }

        return meta.NegativeType is null || text.Contains(meta.NegativeType, StringComparison.Ordinal);
    }

    private static IEnumerable<string> HarnessIncludes(Frontmatter meta, bool isAsync)
    {
        yield return "assert.js";
        yield return "sta.js";
        if (isAsync) yield return "doneprintHandle.js";
        foreach (var include in meta.Includes)
            yield return include;
    }

    private static string LoadHarnessFile(string test262Root, string name)
    {
        if (_harnessCache.TryGetValue(name, out var cached)) return cached;
        var path = Path.Combine(test262Root, "harness", name);
        var content = File.ReadAllText(path);
        _harnessCache[name] = content;
        return content;
    }

    // ---- frontmatter ----

    private static Frontmatter ParseFrontmatter(string source)
    {
        var includes = new List<string>();
        var flags = new HashSet<string>(StringComparer.Ordinal);
        string? negPhase = null, negType = null;

        var start = source.IndexOf("/*---", StringComparison.Ordinal);
        var end = source.IndexOf("---*/", StringComparison.Ordinal);
        if (start < 0 || end < 0 || end <= start)
            return new Frontmatter(includes, flags, negPhase, negType);

        var yaml = source[(start + 5)..end];
        string? currentKey = null;
        bool inNegative = false;

        foreach (var rawLine in yaml.Split('\n'))
        {
            var line = rawLine.TrimEnd();
            if (line.TrimStart().Length == 0) continue;
            var indented = line.StartsWith(' ') || line.StartsWith('\t');
            var trimmed = line.Trim();

            if (!indented)
            {
                inNegative = false;
                var colon = trimmed.IndexOf(':');
                if (colon < 0) { currentKey = null; continue; }
                currentKey = trimmed[..colon].Trim();
                var value = trimmed[(colon + 1)..].Trim();

                switch (currentKey)
                {
                    case "includes":
                        includes.AddRange(ParseInlineList(value));
                        break;
                    case "flags":
                        foreach (var f in ParseInlineList(value)) flags.Add(f);
                        break;
                    case "negative":
                        inNegative = true;
                        break;
                }
            }
            else if (inNegative)
            {
                var colon = trimmed.IndexOf(':');
                if (colon < 0) continue;
                var key = trimmed[..colon].Trim();
                var value = trimmed[(colon + 1)..].Trim();
                if (key == "phase") negPhase = value;
                else if (key == "type") negType = value;
            }
            else if (trimmed.StartsWith('-') && currentKey is "includes" or "flags")
            {
                var item = trimmed[1..].Trim();
                if (currentKey == "includes") includes.Add(item);
                else flags.Add(item);
            }
        }

        return new Frontmatter(includes, flags, negPhase, negType);
    }

    private static IEnumerable<string> ParseInlineList(string value)
    {
        value = value.Trim();
        if (value.StartsWith('[') && value.EndsWith(']'))
            value = value[1..^1];
        foreach (var item in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            yield return item;
    }

    private static List<ManifestEntry> LoadSkipList() =>
        Manifest.Load(ConformancePaths.Manifest(Path.Combine("Test262", "skip-list.txt")));

    private static string Truncate(string? s, int max = 200) =>
        s is null ? "" : s.Length <= max ? s : s[..max] + "…";

    /// <summary>Minimal $262 host object — enough for the curated subset; tests needing
    /// realms/agents belong on the skip list.</summary>
    private sealed class Dollar262(Engine engine)
    {
        public object? evalScript(string code) => engine.Evaluate(code).ToObject();
        public object? global => engine.Evaluate("globalThis");
        public void gc() => GC.Collect();
        public object? createRealm() => throw new NotSupportedException("realms not supported");
    }
}
