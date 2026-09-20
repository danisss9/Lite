using Lite.Conformance.Acid;
using Lite.Conformance.Css21;
using Lite.Conformance.Harness;
using Lite.Conformance.Profile;
using Lite.Conformance.Test262;
using Lite.Conformance.Wpt;

namespace Lite.Conformance;

internal static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length is 1 or 2 && args[0] == "--test262-worker") return Test262Runner.Worker(args.Length == 2 ? args[1] : null);
        if (args.Length == 4 && args[0] == "--wpt-worker")
            return WptRunner.Worker(args[1], args[2], args[3]);
        string? suite = null;
        string? filter = null;
        string? survey = null;
        int surveyLimit = 0;
        bool updateBaselines = false;
        string? reportPath = null;
        var shard = ShardSpec.All;
        bool requireReady = false;
        bool requireHtmlReady = false;
        bool requireCssReady = false;
        bool requireEs2020Ready = false;
        string? cssMedia = null;
        string test262Set = "full";
        var evidencePaths = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--test262-set" when i + 1 < args.Length:
                    test262Set = args[++i];
                    if (test262Set is not ("full" or "smoke")) { Console.Error.WriteLine("--test262-set must be full or smoke"); return 2; }
                    break;
                case "--suite" when i + 1 < args.Length:
                    suite = args[++i];
                    break;
                case "--filter" when i + 1 < args.Length:
                    filter = args[++i];
                    break;
                case "--survey" when i + 1 < args.Length:
                    survey = args[++i];
                    break;
                case "--geom" when i + 2 < args.Length:
                    return RefTestRunner.ProbeGeometry(args[i + 1], args[i + 2]);
                case "--render" when i + 1 < args.Length:
                    return RefTestRunner.RenderToFile(args[i + 1], i + 2 < args.Length ? args[i + 2] : null);
                case "--survey-limit" when i + 1 < args.Length:
                    int.TryParse(args[++i], out surveyLimit);
                    break;
                case "--update-baselines":
                    updateBaselines = true;
                    break;
                case "--report" when i + 1 < args.Length:
                    reportPath = args[++i];
                    break;
                case "--shard" when i + 1 < args.Length:
                    if (!ShardSpec.TryParse(args[++i], out shard))
                    {
                        Console.WriteLine("--shard must be INDEX/COUNT with 0 <= INDEX < COUNT.");
                        return 2;
                    }
                    break;
                case "--require-ready":
                    requireReady = true;
                    break;
                case "--require-es2020-ready":
                    requireEs2020Ready = true;
                    break;
                case "--require-css-ready":
                    requireCssReady = true;
                    break;
                case "--media" when i + 1 < args.Length:
                    cssMedia = args[++i];
                    if (cssMedia is not ("screen" or "print")) { Console.Error.WriteLine("--media must be screen or print."); return 2; }
                    break;
                case "--require-html-ready":
                    requireHtmlReady = true;
                    break;
                case "--evidence" when i + 1 < args.Length:
                    evidencePaths.Add(args[++i]);
                    break;
                case "--wpt-base-url" when i + 1 < args.Length:
                    var baseUrl = args[++i];
                    if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var origin) || origin.Scheme is not ("http" or "https"))
                    { Console.WriteLine("--wpt-base-url requires an HTTP(S) URL."); return 2; }
                    Environment.SetEnvironmentVariable("LITE_WPT_BASE_URL", baseUrl);
                    break;
                case "--help" or "-h":
                    PrintUsage();
                    return 0;
                default:
                    Console.WriteLine($"Unknown argument: {args[i]}");
                    PrintUsage();
                    return 2;
            }
        }

        if (suite is null)
        {
            PrintUsage();
            return 2;
        }

        if ((requireReady || requireHtmlReady || requireCssReady || requireEs2020Ready) && !suite.Equals("profile", StringComparison.OrdinalIgnoreCase))
        { Console.WriteLine("Readiness and --evidence options require --suite profile."); return 2; }
        if (evidencePaths.Count > 0 && suite.ToLowerInvariant() is not ("profile" or "es2020-inventory"))
        { Console.Error.WriteLine("--evidence requires profile or es2020-inventory"); return 2; }
        if (cssMedia is not null && !suite.Equals("css21-full", StringComparison.OrdinalIgnoreCase))
        { Console.Error.WriteLine("--media requires --suite css21-full."); return 2; }
        if (reportPath is not null && suite.ToLowerInvariant() is not ("profile" or "wpt" or "html5" or "html5-inventory" or "css21" or "css21-full" or "css21-inventory" or "test262" or "es2020-inventory" or "es2020-host"))
        { Console.WriteLine("--report is supported by profile, wpt, html5, html5-inventory, css21, and test262."); return 2; }
        if (survey is not null && suite.Equals("css21", StringComparison.OrdinalIgnoreCase) && reportPath is not null)
        { Console.WriteLine("CSS surveys do not produce readiness evidence; use the curated CSS gate with --report."); return 2; }

        try
        {
            return suite.ToLowerInvariant() switch
            {
                "wpt" when survey is not null => WptRunner.Survey(survey, surveyLimit, reportPath, shard),
                "wpt" => WptRunner.Run(filter, shard, reportPath),
                "html5" => WptRunner.RunHtml(filter, shard, reportPath),
                "html5-inventory" => HtmlInventoryRunner.Run(reportPath),
                "css21" when survey is not null => RefTestRunner.Survey(survey, surveyLimit),
                "css21" => RefTestRunner.Run(filter, shard, reportPath),
                "css21-inventory" => Css21Inventory.Run(reportPath),
                "css21-full" => Css21FullRunner.Run(cssMedia ?? "screen", filter, shard, reportPath),
                "test262" => Test262Runner.Run(filter, shard, reportPath, test262Set),
                "es2020-inventory" => Test262Catalog.Run(reportPath, evidencePaths),
                "es2020-host" => Es2020HostRunner.Run(filter, shard, reportPath),
                "acid" => AcidRunner.Run(filter, updateBaselines, shard),
                "profile" => ProfileRunner.Run(reportPath, requireReady, requireHtmlReady, evidencePaths, requireCssReady, requireEs2020Ready),
                "all" => RunAll(filter, shard),
                _ => Unknown(suite),
            };
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException)
        {
            Console.Error.WriteLine($"Conformance input error: {ex.Message}");
            return 2;
        }
        finally
        {
            ConformanceServer.Stop();
        }
    }

    private static int RunAll(string? filter, ShardSpec shard)
    {
        var exit = 0;
        exit |= Test262Runner.Run(filter, shard);
        exit |= WptRunner.Run(filter, shard);
        exit |= RefTestRunner.Run(filter, shard);
        exit |= AcidRunner.Run(filter, updateBaselines: false, shard);
        return exit;
    }

    private static int Unknown(string suite)
    {
        Console.WriteLine($"Unknown suite: {suite}");
        PrintUsage();
        return 2;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            Lite conformance harness

            Usage:
              dotnet run --project Lite.Conformance -- --suite <wpt|html5|html5-inventory|css21|test262|acid|profile|all> [options]

            Options:
              --test262-set full|smoke  Select the full corpus (default) or the explicit smoke set
              --require-es2020-ready   (profile) Require complete ES2020 language and host evidence
              --suite es2020-inventory Export test/section inventories and an exhaustive remaining-work report
              --suite es2020-host      Run Lite browser JavaScript integration tests
              --filter <substring>   Only run tests whose path contains the substring
              --update-baselines     (acid) Approve the current render as the new baseline
              --geom <url> <sel>     Print the geometry of elements matching a selector
              --render <url> [name]  Render one page to artifacts/<name>.png
              --report <path>        Write suite evidence, inventory or profile JSON (except acid/all)
              --shard <index/count>  Run one stable zero-based shard (for example 2/8)
              --require-ready        (profile) Fail unless every release-readiness check passes
              --require-css-ready    (profile) Require CSS 2.1 screen and print completion
              --media <screen|print> (css21-full) Select medium; defaults to screen
              --require-html-ready   (profile) Require completion of the HTML 5.0 profile
              --evidence <path>      (profile) Consume executed evidence; may be repeated
              --wpt-base-url <url>   Use an upstream wpt serve instance for vendored WPT tests

            Test files are vendored by scripts\fetch-tests.ps1 (pinned commits).
            CSS inventory: --suite css21-inventory; complete candidate run: --suite css21-full.
            Exit code 0 = green (no unexpected failures, no unexpected passes).
            """);
    }
}
