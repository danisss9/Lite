using System.Reflection;

namespace Lite.Tests;

/// <summary>Minimal zero-dependency test runner. Methods marked [Test] are auto-discovered.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class TestAttribute : Attribute { }

public static class TestRunner
{
    private static int _passed;
    private static int _failed;
    private static readonly List<string> _failures = [];

    public static int Main()
    {
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
            }
            catch (Exception ex)
            {
                _failed++;
                var msg = (ex.InnerException ?? ex).Message;
                _failures.Add($"{name}: {msg}");
                Console.WriteLine($"  FAIL  {name}: {msg}");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"=== {_passed} passed, {_failed} failed ===");
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
