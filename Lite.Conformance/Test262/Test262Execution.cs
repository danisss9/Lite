using System.Diagnostics;
using Lite.QuickJs;

namespace Lite.Conformance.Test262;

internal sealed record Test262Outcome(string Outcome, string Detail, string? Phase = null, string? ErrorType = null, long DurationMs = 0);

internal static class Test262Execution
{
    private static readonly Dictionary<string, string> Harness = new(StringComparer.Ordinal);

    internal static Test262Outcome Run(string root, string path, string mode, string? sourceRoot = null)
    {
        var clock = Stopwatch.StartNew();
        var file = Path.GetFullPath(Path.Combine(sourceRoot ?? root, path));
        var source = File.ReadAllText(file);
        var meta = Test262Metadata.Parse(source);
        if (meta.Errors.Length > 0 || !meta.Modes.Contains(mode))
            return new("harness-error", string.Join("; ", meta.Errors.Append("Invalid execution metadata/mode")));

        using var runtime = new QuickJsRuntime();
        using var realm = runtime.CreateRealm();
        var loader = new Test262ModuleLoader(Path.GetDirectoryName(file)!,
            Path.Combine(sourceRoot ?? root, sourceRoot is null ? "test" : "supplemental"));
        loader.Bind(runtime, realm);
        new Test262Host(runtime, realm);
        runtime.SetInterruptHandler(() => clock.ElapsedMilliseconds > 20_000);
        var completions = new List<string>();
        var lateRejections = new Dictionary<nint, string>();
        runtime.SetRejectionHandler((_, promise, reason, handled) =>
        {
            if (handled) lateRejections.Remove(promise.Identity);
            else if (meta.Flags.Contains("async") && completions.Count > 0)
                lateRejections[promise.Identity] = reason.AsString();
        });
        using var global = realm.Global();
        using var print = realm.HostFunction("print", 1, (owner, args) =>
        {
            var text = args.Length > 0 ? args[0].AsString() : "undefined";
            if (text.StartsWith("Test262:AsyncTest", StringComparison.Ordinal)) completions.Add(text);
            return owner.Undefined();
        });
        global.Set("print", print);

        string phase = "harness";
        try
        {
            if (!meta.Flags.Contains("raw"))
            {
                foreach (var include in new[] { "assert.js", "sta.js" }
                    .Concat(meta.Flags.Contains("async") ? ["doneprintHandle.js"] : [])
                    .Concat(meta.Includes))
                {
                    var name = Path.Combine(root, "harness", include);
                    if (!Harness.TryGetValue(name, out var content))
                        Harness[name] = content = File.ReadAllText(name);
                    realm.Execute(content, name);
                }
            }

            phase = "parse";
            var code = mode == "strict" ? "\"use strict\";\n" + source : source;
            using var compiled = realm.CompileValue(code, new Uri(file).AbsoluteUri,
                module: mode == "module");
            if (meta.NegativePhase == "parse") return Missing("parse");
            if (mode == "module")
            {
                phase = "resolution";
                compiled.ResolveModule();
            }
            phase = "runtime";
            using var result = compiled.EvaluateCompiled();
            QuickJsException? moduleFailure = null;
            if (mode == "module")
            {
                using var then = result.Get("then");
                using var fulfilled = realm.HostFunction("module fulfilled", 1,
                    (owner, _) => owner.Undefined());
                using var rejected = realm.HostFunction("module rejected", 1, (owner, args) =>
                {
                    using var name = args[0].Get("name");
                    moduleFailure = new QuickJsException(args[0].AsString(), name.AsString(), args[0].Clone());
                    return owner.Undefined();
                });
                using var observation = then.Call(result, fulfilled, rejected);
            }
            runtime.PumpJobs();
            if (moduleFailure is not null) throw moduleFailure;
            if (meta.Flags.Contains("async"))
            {
                while (completions.Count == 0 && clock.ElapsedMilliseconds < 20_000)
                {
                    runtime.PumpJobs();
                    if (completions.Count == 0) Thread.Sleep(1);
                }
                runtime.PumpJobs();
                if (completions.Count == 0)
                    return new("timeout", "No asynchronous completion", "runtime", DurationMs: clock.ElapsedMilliseconds);
                if (completions.Count != 1 || completions[0] != "Test262:AsyncTestComplete")
                    return new("fail", "Invalid asynchronous completion: " + string.Join("; ", completions),
                        "runtime", DurationMs: clock.ElapsedMilliseconds);
                if (lateRejections.Count > 0)
                    return new("fail", "Unhandled rejection after asynchronous completion", "runtime",
                        DurationMs: clock.ElapsedMilliseconds);
            }
            if (meta.NegativePhase is not null) return Missing(meta.NegativePhase);
            return new("pass", "ok", DurationMs: clock.ElapsedMilliseconds);
        }
        catch (QuickJsException error)
        {
            // QuickJS reports export binding errors when evaluation begins, before any
            // module body executes. Test262 classifies these link failures as resolution.
            if (mode == "module" && phase == "runtime" && error.ErrorName == "SyntaxError" &&
                (error.Message.Contains("Could not find export", StringComparison.Ordinal) ||
                 error.Message.Contains(" is ambiguous", StringComparison.Ordinal) ||
                 error.Message.Contains("circular reference when looking for export", StringComparison.Ordinal)))
                phase = "resolution";
            var type = ErrorType(error);
            var passed = MatchesNegative(meta, phase, type);
            error.Dispose();
            return new(passed ? "pass" : phase == "harness" ? "harness-error" : "fail",
                passed ? "Expected negative" : $"{phase}: {type}: {error.Message}",
                phase, type, clock.ElapsedMilliseconds);
        }
        catch (Exception error)
        {
            var passed = MatchesNegative(meta, phase, error.GetType().Name);
            return new(passed ? "pass" : phase == "harness" ? "harness-error" : "fail",
                passed ? "Expected negative" : $"{phase}: {error.GetType().Name}: {error.Message}",
                phase, error.GetType().Name, clock.ElapsedMilliseconds);
        }

        Test262Outcome Missing(string expected) => new("fail",
            $"Expected {expected} {meta.NegativeType}, but phase completed", DurationMs: clock.ElapsedMilliseconds);
    }

    internal static bool MatchesNegative(Test262Metadata metadata, string phase, string? type) =>
        metadata.NegativePhase is not null && phase == metadata.NegativePhase && type == metadata.NegativeType;

    private static string? ErrorType(QuickJsException error)
    {
        if (error.ErrorValue is not { } value) return error.ErrorName;
        if (value.IsError) return error.ErrorName;
        if (value.Kind is not (QuickJsKind.Object or QuickJsKind.Function)) return null;
        using var constructor = value.Get("constructor");
        using var name = constructor.Get("name");
        return name.AsString() == "Test262Error" ? "Test262Error" : null;
    }
}
