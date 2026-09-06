using System.Reflection;
using Lite.Conformance.Harness;

namespace Lite.Tests;

/// <summary>Minimal zero-dependency test runner. Methods marked [Test] are auto-discovered.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class TestAttribute : Attribute { }

public static class TestRunner
{
    private static int _passed;
    private static int _failed;
    private static readonly List<string> _failures = [];

    public static int Main(string[] args)
    {
        string? reportPath = null;
        if (args.Length == 2 && args[0] == "--report") reportPath = args[1];
        else if (args.Length != 0) { Console.WriteLine("Usage: Lite.Tests [--report path]"); return 2; }
        var identity = reportPath is null ? null : ExecutionEvidence.CaptureIdentity();
        var started = DateTime.UtcNow;
        var outcomes = new List<TestEvidence>();
        if (Environment.GetEnvironmentVariable("LITE_PROBE") == "1")
        {
            Probe.Dump();
            return 0;
        }

        var testMethods = Assembly.GetExecutingAssembly()
            .GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(m => m.GetCustomAttribute<TestAttribute>() is not null)
            .OrderBy(m => m.DeclaringType!.Name).ThenBy(m => m.Name);

        foreach (var method in testMethods)
        {
            var name = $"{method.DeclaringType!.Name}.{method.Name}";
            try
            {
                method.Invoke(null, null);
                _passed++;
                Console.WriteLine($"  PASS  {name}");
                outcomes.Add(new("unit", $"Lite.Tests/{method.DeclaringType!.Name}.cs#{method.Name}", "pass", name,
                    [new SubtestEvidence(method.Name, 0, null)]));
            }
            catch (Exception ex)
            {
                _failed++;
                var msg = (ex.InnerException ?? ex).Message;
                _failures.Add($"{name}: {msg}");
                Console.WriteLine($"  FAIL  {name}: {msg}");
                outcomes.Add(new("unit", $"Lite.Tests/{method.DeclaringType!.Name}.cs#{method.Name}", "fail", msg,
                    [new SubtestEvidence(method.Name, 1, msg)]));
            }
        }

        Console.WriteLine();
        Console.WriteLine($"=== {_passed} passed, {_failed} failed ===");
        if (reportPath is not null) ExecutionEvidence.Write(reportPath, identity!, started, outcomes);
        return _failed == 0 ? 0 : 1;
    }

    // ---- assertions ----
    public static void True(bool cond, string? message = null)
    {
        if (!cond) throw new Exception(message ?? "Expected true");
    }

    public static void Equal(string? expected, string? actual)
    {
        if (expected != actual)
            throw new Exception($"Expected \"{expected}\" but got \"{actual}\"");
    }

    public static void Equal(int expected, int actual)
    {
        if (expected != actual)
            throw new Exception($"Expected {expected} but got {actual}");
    }

    public static void Equal(bool expected, bool actual)
    {
        if (expected != actual)
            throw new Exception($"Expected {expected} but got {actual}");
    }

    public static void Contains(string needle, string? haystack)
    {
        if (haystack is null || !haystack.Contains(needle))
            throw new Exception($"Expected to contain \"{needle}\" but got \"{haystack}\"");
    }
}
