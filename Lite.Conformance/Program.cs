using Lite.Conformance.Acid;
using Lite.Conformance.Css21;
using Lite.Conformance.Harness;
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
                case "--survey-limit" when i + 1 < args.Length:
                    int.TryParse(args[++i], out surveyLimit);
                    break;
                case "--update-baselines":
                    updateBaselines = true;
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
                "wpt" => WptRunner.Run(filter),
                "css21" when survey is not null => RefTestRunner.Survey(survey, surveyLimit),
                "css21" => RefTestRunner.Run(filter),
                "test262" => Test262Runner.Run(filter),
                "acid" => AcidRunner.Run(filter, updateBaselines),
                "all" => RunAll(filter),
                _ => Unknown(suite),
            };
        }
        finally
        {
            ConformanceServer.Stop();
        }
    }

    private static int RunAll(string? filter)
    {
        var exit = 0;
        exit |= Test262Runner.Run(filter);
        exit |= WptRunner.Run(filter);
        exit |= RefTestRunner.Run(filter);
        exit |= AcidRunner.Run(filter, updateBaselines: false);
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
              dotnet run --project Lite.Conformance -- --suite <wpt|css21|test262|acid|all> [options]

            Options:
              --filter <substring>   Only run tests whose path contains the substring
              --update-baselines     (acid) Approve the current render as the new baseline

            Test files are vendored by scripts\fetch-tests.ps1 (pinned commits).
            Exit code 0 = green (no unexpected failures, no unexpected passes).
            """);
    }
}
