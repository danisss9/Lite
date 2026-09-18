using System.Diagnostics;
using System.Text.Json;
using Lite.Conformance.Harness;

namespace Lite.Conformance.Test262;

/// <summary>Full pinned Test262 corpus, executed in supervised, replaceable worker processes.</summary>
internal static class Test262Runner
{
    internal sealed record Request(string Path, string Mode);
    private static readonly JsonSerializerOptions Wire = new() { PropertyNameCaseInsensitive = true };

    public static int Run(string? filter, ShardSpec shard, string? reportPath = null, string selection = "full")
    {
        if (selection is not ("full" or "smoke")) throw new InvalidDataException("--test262-set must be full or smoke");
        var inventory = Test262Catalog.Read();
        if (!inventory.CheckoutComplete) { Console.Error.WriteLine(string.Join("\n", inventory.Blockers)); return 2; }
        var candidates = inventory.Tests.Where(t => t.Classification != "fixture");
        if (selection == "smoke") candidates = candidates.Where(t => Test262Catalog.IsSmoke(t.Path));
        if (filter is not null) candidates = candidates.Where(t => t.Path.Contains(filter, StringComparison.OrdinalIgnoreCase));
        var tests = shard.Apply(candidates).ToArray();
        if (tests.Length == 0) { Console.Error.WriteLine("No matching Test262 tests"); return 2; }
        var started = DateTime.UtcNow;
        var identity = ExecutionEvidence.CaptureIdentity();
        var outcomes = new List<TestEvidence>();
        var exceptions = Manifest.Load(ConformancePaths.Manifest("Test262/skip-list.txt")).Select(x => x.Path).ToHashSet(StringComparer.Ordinal);
        using var worker = new WorkerClient();
        var failures = 0; var passes = 0; var excluded = 0;
        foreach (var test in tests)
        {
            if (test.Classification != "included")
            {
                var outcome = test.Classification is "invalid" or "unreviewed" ? "unreviewed" : "excluded";
                outcomes.Add(new("test262", test.Path, outcome, test.Reason, [], Context: "javascript", Kind: "language"));
                if (outcome == "unreviewed") failures++; else excluded++;
                continue;
            }
            foreach (var mode in test.Metadata.Modes)
            {
                if (Environment.GetEnvironmentVariable("T262_TRACE") == "1") Console.Error.WriteLine($"[running] {test.Path} [{mode}]");
                var result = exceptions.Contains(test.Path) ? new Test262Outcome("skipped", "Stock engine dependency exception") : worker.Run(new(test.Path, mode));
                var passed = result.Outcome == "pass";
                if (passed) passes++; else { failures++; Console.WriteLine($"FAIL {test.Path} [{mode}]: {result.Detail}"); }
                outcomes.Add(new("test262", test.Path, result.Outcome, result.Detail, [new(mode, passed ? 0 : 1, result.Detail)],
                    Context: "javascript", Kind: "language", JavaScript: new(mode, inventory.Sha256, selection,
                        shard.Index, shard.Count, filter, test.Metadata.NegativePhase, result.Phase,
                        test.Metadata.NegativeType, result.ErrorType, result.DurationMs)));
            }
            if ((passes + failures) % 1000 == 0) Console.WriteLine($"test262 {shard}: {passes} passing executions, {failures} failures");
        }
        ExecutionEvidence.Write(reportPath ?? Path.Combine(ConformancePaths.EnsureArtifacts(), $"test262-{shard.Index}-of-{shard.Count}.json"), identity, started, outcomes);
        Console.WriteLine($"test262 {selection} {shard}: {passes} passing executions, {failures} failures/unreviewed, {excluded} excluded tests");
        return failures == 0 ? 0 : 1;
    }

    internal static int Worker()
    {
        var output = Console.Out;
        Console.SetOut(TextWriter.Null);
        string? line;
        while ((line = Console.ReadLine()) is not null)
        {
            Test262Outcome result;
            try
            {
                var request = JsonSerializer.Deserialize<Request>(line, Wire) ?? throw new InvalidDataException("Missing request");
                if (!request.Path.StartsWith("test/", StringComparison.Ordinal) || request.Path.Contains("..") || request.Path.Contains('\\') || request.Path.Contains(':'))
                    throw new InvalidDataException("Invalid worker path");
                result = Test262Execution.Run(Test262Catalog.Root, request.Path, request.Mode);
            }
            catch (Exception error) { result = new("harness-error", error.ToString()); }
            output.WriteLine(JsonSerializer.Serialize(result, Wire));
            output.Flush();
        }
        return 0;
    }

    internal sealed class WorkerClient : IDisposable
    {
        private Process? _process;
        private Task<string>? _stderr;
        private int _count;
        internal Test262Outcome Run(Request request, int timeoutMs = 30_000)
        {
            try
            {
                if (_process is null || _process.HasExited || _count >= 500) Start();
                _count++;
                _process!.StandardInput.WriteLine(JsonSerializer.Serialize(request, Wire));
                _process.StandardInput.Flush();
                var line = _process.StandardOutput.ReadLineAsync();
                if (!line.Wait(timeoutMs)) { Stop(); return new("timeout", $"Worker exceeded {timeoutMs}ms", DurationMs: timeoutMs); }
                if (line.Result is null)
                {
                    _process.WaitForExit(1000);
                    var detail = _stderr?.IsCompleted == true ? _stderr.Result : "Worker exited without an outcome";
                    Stop(); return new("crash", detail.Length > 4000 ? detail[..4000] : detail);
                }
                return JsonSerializer.Deserialize<Test262Outcome>(line.Result, Wire) ?? new("harness-error", "Invalid worker outcome");
            }
            catch (Exception ex) { Stop(); return new("crash", ex.Message); }
        }
        private void Start()
        {
            Stop();
            var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add(typeof(Test262Runner).Assembly.Location);
            start.ArgumentList.Add("--test262-worker");
            _process = Process.Start(start) ?? throw new IOException("Cannot start Test262 worker");
            _stderr = _process.StandardError.ReadToEndAsync(); _count = 0;
        }
        private void Stop()
        {
            if (_process is null) return;
            if (!_process.HasExited) { _process.Kill(entireProcessTree: true); _process.WaitForExit(5000); }
            _process.Dispose(); _process = null;
        }
        public void Dispose() => Stop();
    }
}
