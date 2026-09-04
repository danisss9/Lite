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
        string? suite = null;
        string? filter = null;
        string? survey = null;
        int surveyLimit = 0;
        bool updateBaselines = false;
        string? reportPath = null;
        var shard = ShardSpec.All;
        bool requireReady = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
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

        try
        {
            return suite.ToLowerInvariant() switch
            {
                "wpt" when survey is not null => WptRunner.Survey(survey, surveyLimit),
                "wpt" => WptRunner.Run(filter, shard),
                "css21" when survey is not null => RefTestRunner.Survey(survey, surveyLimit),
                "css21" => RefTestRunner.Run(filter, shard),
                "test262" => Test262Runner.Run(filter, shard),
                "acid" => AcidRunner.Run(filter, updateBaselines, shard),
                "profile" => ProfileRunner.Run(reportPath, requireReady),
                "all" => RunAll(filter, shard),
                _ => Unknown(suite),
            };
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
              dotnet run --project Lite.Conformance -- --suite <wpt|css21|test262|acid|profile|all> [options]

            Options:
              --filter <substring>   Only run tests whose path contains the substring
              --update-baselines     (acid) Approve the current render as the new baseline
              --geom <url> <sel>     Print the geometry of elements matching a selector
              --render <url> [name]  Render one page to artifacts/<name>.png
              --report <path>        (profile) Write the compatibility JSON report to this path
              --shard <index/count>  Run one stable zero-based shard (for example 2/8)
              --require-ready        (profile) Fail unless every release-readiness check passes

            Test files are vendored by scripts\fetch-tests.ps1 (pinned commits).
            Exit code 0 = green (no unexpected failures, no unexpected passes).
            """);
    }
}
