using System.Diagnostics;
using Acornima;
using Jint;
using Jint.Native;
using Jint.Runtime;
using Jint.Runtime.Modules;
using Lite.Scripting;

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
        if (meta.Errors.Length > 0 || !meta.Modes.Contains(mode)) return new("harness-error", string.Join("; ", meta.Errors.Append("Invalid execution metadata/mode")));
        var loader = new Test262ModuleLoader(Path.GetDirectoryName(file)!,
            Path.Combine(sourceRoot ?? root, sourceRoot is null ? "test" : "supplemental"));
        using var engine = new Engine(options =>
        {
            JavaScriptRuntime.Configure(options, canBlock: !meta.Flags.Contains("CanBlockIsFalse"));
            options.TimeoutInterval(TimeSpan.FromSeconds(20));
            options.EnableModules(loader);
        });
        var completions = new List<string>();
        var lateRejections = new Dictionary<JsValue, JsValue>();
        engine.Advanced.PromiseRejectionTracker += (_, args) =>
        {
            if (args.Operation.ToString() != "Reject") lateRejections.Remove(args.Promise);
            else if (meta.Flags.Contains("async") && completions.Count > 0)
                lateRejections[args.Promise] = args.Value ?? JsValue.Undefined;
        };
        engine.SetValue("print", new Action<JsValue>(value =>
        {
            var text = TypeConverter.ToString(value);
            if (text.StartsWith("Test262:AsyncTest", StringComparison.Ordinal)) completions.Add(text);
        }));
        using var host = new Test262Host(engine);
        string phase = "harness";
        try
        {
            if (!meta.Flags.Contains("raw"))
            {
                foreach (var include in new[] { "assert.js", "sta.js" }.Concat(meta.Flags.Contains("async") ? ["doneprintHandle.js"] : []).Concat(meta.Includes))
                {
                    var name = Path.Combine(root, "harness", include);
                    if (!Harness.TryGetValue(name, out var content)) Harness[name] = content = File.ReadAllText(name);
                    engine.Execute(content, name);
                }
            }
            phase = "parse";
            var code = mode == "strict" ? "\"use strict\";\n" + source : source;
            if (mode == "module")
            {
                var resolved = loader.Resolve(null, new ModuleRequest(new Uri(file).AbsoluteUri, []));
                var module = loader.LoadModule(engine, resolved);
                if (meta.NegativePhase == "parse") return Missing("parse");
                phase = "resolution";
                module.Link();
                if (meta.NegativePhase == "resolution") return Missing("resolution");
                phase = "runtime";
                var value = module.Evaluate();
                engine.Advanced.ProcessTasks();
                value.UnwrapIfPromise();
            }
            else
            {
                var prepared = Engine.PrepareScript(code, new Uri(file).AbsoluteUri, options: new ScriptPreparationOptions
                { ParsingOptions = JavaScriptRuntime.ScriptParsing });
                if (meta.NegativePhase == "parse") return Missing("parse");
                phase = "runtime";
                engine.Execute(prepared);
            }
            engine.Advanced.ProcessTasks();
            if (meta.Flags.Contains("async"))
            {
                while (completions.Count == 0 && clock.ElapsedMilliseconds < 20_000)
                { engine.Advanced.ProcessTasks(); if (completions.Count == 0) Thread.Sleep(1); }
                engine.Advanced.ProcessTasks();
                if (completions.Count == 0) return new("timeout", "No asynchronous completion", "runtime", DurationMs: clock.ElapsedMilliseconds);
                if (completions.Count != 1 || completions[0] != "Test262:AsyncTestComplete")
                    return new("fail", "Invalid asynchronous completion: " + string.Join("; ", completions), "runtime", DurationMs: clock.ElapsedMilliseconds);
                if (lateRejections.Count > 0)
                    return new("fail", "Unhandled rejection after asynchronous completion", "runtime", DurationMs: clock.ElapsedMilliseconds);
            }
            // Ordinary rejected promises (including intentionally rejected Test262Error values)
            // are legal. Async tests report assertion failures through $DONE instead.
            if (meta.NegativePhase is not null) return Missing(meta.NegativePhase);
            return new("pass", "ok", DurationMs: clock.ElapsedMilliseconds);
        }
        catch (Exception error)
        {
            var type = error is JavaScriptException js ? ErrorType(js.Error) :
                error is PromiseRejectedException rejected ? ErrorType(rejected.RejectedValue) :
                error is SyntaxErrorException || error.InnerException is SyntaxErrorException ? "SyntaxError" : error.GetType().Name;
            var passed = MatchesNegative(meta, phase, type);
            return new(passed ? "pass" : phase == "harness" ? "harness-error" : "fail",
                passed ? "Expected negative" : $"{phase}: {type}: {error.Message}", phase, type, clock.ElapsedMilliseconds);
        }
        Test262Outcome Missing(string expected) => new("fail", $"Expected {expected} {meta.NegativeType}, but phase completed", DurationMs: clock.ElapsedMilliseconds);
    }

    internal static bool MatchesNegative(Test262Metadata metadata, string phase, string? type) =>
        metadata.NegativePhase is not null && phase == metadata.NegativePhase && type == metadata.NegativeType;

    private static string? ErrorType(JsValue value)
    {
        if (value is not Jint.Native.Object.ObjectInstance error) return null;
        var constructor = error.Get("constructor");
        var name = constructor is Jint.Native.Function.Function function ? TypeConverter.ToString(function.Get("name")) : null;
        // Test262Error is the harness-defined ordinary constructor. Native negative
        // expectations require a real error object, not an object spoofing its name.
        return error is Jint.Native.Error.ErrorInstance || name == "Test262Error" ? name : null;
    }
}
