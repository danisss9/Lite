using Lite.QuickJs;
using Lite.Scripting;
using Lite.Scripting.Runtime;
using Lite.Network;
using System.Collections.Concurrent;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

public static class QuickJsBridgeTests
{
    private sealed class HostCounter
    {
        public int value { get; set; }
        public int increment(int amount = 1) => value += amount;
    }

    [Test]
    public static void ManagedBrowserBoundaryUsesQuickJs()
    {
        using var engine = new Engine();
        var counter = new HostCounter();
        engine.SetValue("counter", counter);
        Equal(3, (int)engine.Evaluate("counter.increment(3)").AsNumber());
        Equal(3, counter.value);
        True(engine.Evaluate("counter === counter && counter.value === 3").AsBoolean());
        engine.SetValue("add", new Func<int, int, int>((a, b) => a + b));
        Equal(42, (int)engine.Evaluate("add(20, 22)").AsNumber());
    }

    /// <summary>
    /// Deep JS recursion must surface as a catchable JavaScript error, never as a native stack
    /// overrun. QuickJS bounds recursion with a stack watermark anchored on the owning thread;
    /// the runtime pins a conservative budget (QuickJsRuntime.StackBudgetBytes) so the watermark
    /// always fires inside the host thread's real stack. Without it, a host whose thread stack
    /// is close to the budget dies with an uncatchable AccessViolationException instead.
    /// </summary>
    [Test]
    public static void DeepRecursionThrowsCatchableStackOverflow()
    {
        using var engine = new Engine();
        try
        {
            engine.Evaluate("function f(){ return f(); }\nf();");
            True(false, "unbounded recursion must throw");
        }
        catch (QuickJsException error)
        {
            Contains("stack overflow", error.Message);
            error.Dispose();
        }
    }

    /// <summary>Reproduction shape of the flaky native crash: many engines created and torn
    /// down in one process, each evaluating a large single script. Every failure must remain a
    /// catchable QuickJsException — a native crash here terminates the whole process.</summary>
    [Test]
    public static void HeavyEngineCreationWithLargeScriptsStaysClean()
    {
        var script = new System.Text.StringBuilder(5_000 * 12);
        for (var i = 1; i <= 5_000; i++) script.Append("var x").Append(i).Append("=1;\n");
        var source = script.ToString();
        for (var i = 0; i < 8; i++)
        {
            using var engine = new Engine();
            try
            {
                engine.Evaluate(source);
            }
            catch (QuickJsException error)
            {
                error.Dispose(); // catchable engine errors are acceptable; native crashes are not
            }
        }
    }

    [Test]
    public static void MissingNamedModuleExportFailsLinking()
    {
        using var runtime = new QuickJsRuntime();
        using var realm = runtime.CreateRealm();
        runtime.SetModuleProvider(realm,
            (origin, specifier) => new Uri(new Uri(origin), specifier).AbsoluteUri,
            _ => "export const existing = 1;");
        using var compiled = realm.CompileValue("import { missing } from './child.js';",
            "https://site.test/main.js", module: true);
        try
        {
            compiled.ResolveModule();
            using var result = compiled.EvaluateCompiled();
            runtime.PumpJobs();
            throw new Exception("Missing named export was accepted: " + result.AsString());
        }
        catch (QuickJsException error)
        {
            Equal("SyntaxError", error.ErrorName);
            error.Dispose();
        }
    }

    [Test]
    public static void EvaluatesHostCallbacksAndJobs()
    {
        using var runtime = new QuickJsRuntime();
        using var realm = runtime.CreateRealm();
        using var global = realm.Global();
        using var add = realm.HostFunction("add", 2,
            (owner, args) => owner.Number(args[0].AsNumber() + args[1].AsNumber()));
        global.Set("add", add);
        using (var result = realm.Eval("add(20, 22)")) Equal(42, (int)result.AsNumber());

        realm.Execute("globalThis.answer = 0; Promise.resolve(7).then(value => answer = value)");
        using (var answer = global.Get("answer")) Equal(7, (int)answer.AsNumber());

        using var other = runtime.CreateRealm();
        using var otherResult = other.Eval("typeof add");
        Equal("undefined", otherResult.AsString());
    }

    [Test]
    public static void HostPromiseSettlesAtExplicitJobCheckpoint()
    {
        using var runtime = new QuickJsRuntime();
        using var realm = runtime.CreateRealm();
        using var capability = realm.NewPromise();
        using var global = realm.Global();
        global.Set("hostPromise", capability.Promise);
        using (var setup = realm.Eval("globalThis.answer = 0; hostPromise.then(value => answer = value)")) { }
        using var fortyTwo = realm.Number(42);
        capability.Resolve(fortyTwo);
        using (var before = global.Get("answer")) Equal(0, (int)before.AsNumber());
        runtime.PumpJobs();
        using (var after = global.Get("answer")) Equal(42, (int)after.AsNumber());
    }

    [Test]
    public static void DeepConditionalExpressionRunsInQuickJs()
    {
        using var runtime = new QuickJsRuntime();
        using var realm = runtime.CreateRealm();
        using var result = realm.Eval("new Function('return ' + 'false ? 0 : '.repeat(300) + '42;')()");
        Equal(42, (int)result.AsNumber());
    }

    [Test]
    public static void PreservesEmbeddedNullAndSeparatesCompilationFromEvaluation()
    {
        using var runtime = new QuickJsRuntime();
        using var realm = runtime.CreateRealm();
        using var global = realm.Global();
        using var text = realm.String("a\0é");
        global.Set("text", text);
        using (var result = realm.Eval("text.length === 3 && text.charCodeAt(1) === 0"))
            Equal(true, result.AsBoolean());
        using (var result = realm.Eval("'a\\0é'"))
            Equal("a\0é", result.AsString());
        realm.Compile("globalThis.compiledSideEffect = true");
        using (var sideEffect = global.Get("compiledSideEffect"))
            True(sideEffect.Kind == QuickJsKind.Undefined);
        try { realm.Compile("let = ;"); throw new Exception("Expected a syntax error"); }
        catch (QuickJsException) { }
    }

    [Test]
    public static void SupportsCrossRealmValuesAndTest262HostPrimitives()
    {
        using var runtime = new QuickJsRuntime();
        using var first = runtime.CreateRealm();
        using var second = runtime.CreateRealm();
        using var otherGlobal = second.Global();
        using var imported = first.ImportFrom(otherGlobal);
        using var firstGlobal = first.Global();
        firstGlobal.Set("other", imported);
        using (var result = first.Eval("other.Array !== Array"))
            Equal(true, result.AsBoolean());
        using var buffer = first.Eval("new ArrayBuffer(8)");
        firstGlobal.Set("buffer", buffer);
        buffer.DetachArrayBuffer();
        using (var result = first.Eval("buffer.byteLength")) Equal(0, (int)result.AsNumber());
        using var dda = first.Object();
        dda.SetIsHtmlDda();
        firstGlobal.Set("dda", dda);
        using (var result = first.Eval("typeof dda === 'undefined' && !dda && dda == null"))
            Equal(true, result.AsBoolean());
        using var error = first.Eval("new TypeError('bad')");
        Equal(true, error.IsError);
        runtime.CollectGarbage();
    }

    [Test]
    public static void HostCallbacksPreserveJavaScriptThrows()
    {
        using var runtime = new QuickJsRuntime();
        using var caller = runtime.CreateRealm();
        using var target = runtime.CreateRealm();
        using var global = caller.Global();
        using var run = caller.HostFunction("run", 0, (owner, _) =>
        {
            using var result = target.Eval("throw new TypeError('target error')");
            return owner.Undefined();
        });
        global.Set("run", run);
        using var result = caller.Eval("try { run(); false } catch (e) { e.name === 'TypeError' && e.message === 'target error' }");
        Equal(true, result.AsBoolean());
    }

    [Test]
    public static void Test262HostCreatesNestedRealmsAndPreservesErrors()
    {
        using var runtime = new QuickJsRuntime();
        using var realm = runtime.CreateRealm();
        new QuickJsTest262Host(runtime).Install(realm);
        using var result = realm.Eval("""
            const first = $262.createRealm();
            const second = first.createRealm();
            first.evalScript('globalThis.marker = 42');
            const buffer = new ArrayBuffer(8);
            $262.detachArrayBuffer(buffer);
            let syntax = false;
            try { second.evalScript('return 1;'); }
            catch (error) { syntax = error instanceof second.global.SyntaxError; }
            first.global.marker === 42 && typeof marker === 'undefined' &&
            first.global.Array !== Array && first.global.Array !== second.global.Array &&
            buffer.byteLength === 0 && syntax &&
            typeof $262.IsHTMLDDA === 'undefined' && !$262.IsHTMLDDA &&
            typeof $262.gc === 'function'
            """);
        Equal(true, result.AsBoolean());
    }

    [Test]
    public static void RegisteredHostObjectHasIdentityAndLiveProperties()
    {
        using var runtime = new QuickJsRuntime();
        using var realm = runtime.CreateRealm();
        var counter = new Counter();
        QuickJsValue Bind(Counter value) => realm.HostObjects.Wrap(value, binding => binding
            .Property("value", c => realm.Number(c.Value), (c, next) => c.Value = (int)next.AsNumber())
            .Method("increment", 0, (c, owner, _) => owner.Number(++c.Value)));

        using var first = Bind(counter);
        using var second = Bind(counter);
        using var global = realm.Global();
        global.Set("first", first);
        global.Set("second", second);
        using var result = realm.Eval("first === second && first.increment() === 1 && second.value === 1");
        Equal(true, result.AsBoolean());
        True(ReferenceEquals(counter, realm.HostObjects.Unwrap<Counter>(first)));
    }

    private sealed class Counter { internal int Value; }

    [Test]
    public static void LoadsPreparedModuleGraphWithoutHostFileAccess()
    {
        using var runtime = new QuickJsRuntime();
        var modules = new Dictionary<string, string>
        {
            ["https://site.test/part.js"] = "export const answer = 42; export const url = import.meta.url;"
        };
        using var realm = runtime.CreateRealm();
        runtime.SetModuleProvider(realm, (origin, specifier) => new Uri(new Uri(origin), specifier).AbsoluteUri,
            url => modules.GetValueOrDefault(url));
        realm.Execute("import {answer, url} from './part.js'; globalThis.moduleAnswer = answer; globalThis.moduleUrl = url; globalThis.mainUrl = import.meta.url;",
            "https://site.test/main.js", module: true);
        using var global = realm.Global();
        using var answer = global.Get("moduleAnswer");
        Equal(42, (int)answer.AsNumber());
        using var childUrl = global.Get("moduleUrl");
        using var mainUrl = global.Get("mainUrl");
        Equal("https://site.test/part.js", childUrl.AsString());
        Equal("https://site.test/main.js", mainUrl.AsString());
        realm.Execute("import('./part.js').then(part => globalThis.dynamicAnswer = part.answer)",
            "https://site.test/main.js", module: true);
        using var dynamicAnswer = global.Get("dynamicAnswer");
        Equal(42, (int)dynamicAnswer.AsNumber());
    }

    [Test]
    public static void ModuleCompilationLinksBeforeEvaluation()
    {
        using var runtime = new QuickJsRuntime();
        using var realm = runtime.CreateRealm();
        runtime.SetModuleProvider(realm, (origin, specifier) => new Uri(new Uri(origin), specifier).AbsoluteUri,
            url => url.EndsWith("child.js", StringComparison.Ordinal) ? "export const value = 42;" : null);
        using var compiled = realm.CompileValue("import {value} from './child.js'; globalThis.value = value;",
            "https://site.test/main.js", module: true);
        compiled.ResolveModule();
        using var global = realm.Global();
        using (var before = global.Get("value")) True(before.Kind == QuickJsKind.Undefined);
        using var result = compiled.EvaluateCompiled();
        runtime.PumpJobs();
        using (var after = global.Get("value")) Equal(42, (int)after.AsNumber());

        try
        {
            using var unresolved = realm.CompileValue("import {missing} from './absent.js';",
                "https://site.test/unresolved.js", module: true);
            throw new Exception("Expected resolution failure");
        }
        catch (QuickJsException error)
        {
            Equal("ReferenceError", error.ErrorName);
            error.Dispose();
        }
    }

    [Test]
    public static void ModuleProvidersStayWithinTheirRealms()
    {
        using var runtime = new QuickJsRuntime();
        using var first = runtime.CreateRealm();
        using var second = runtime.CreateRealm();
        static string Resolve(string origin, string specifier) => new Uri(new Uri(origin), specifier).AbsoluteUri;
        runtime.SetModuleProvider(first, Resolve, _ => "export const value = 1;");
        runtime.SetModuleProvider(second, Resolve, _ => "export const value = 2;");
        const string code = "import {value} from './dep.js'; globalThis.answer = value;";
        first.Execute(code, "https://first.test/main.js", module: true);
        second.Execute(code, "https://second.test/main.js", module: true);
        using var firstGlobal = first.Global();
        using var secondGlobal = second.Global();
        using var firstAnswer = firstGlobal.Get("answer");
        using var secondAnswer = secondGlobal.Get("answer");
        Equal(1, (int)firstAnswer.AsNumber());
        Equal(2, (int)secondAnswer.AsNumber());
    }

    [Test]
    public static void TracksUnhandledAndHandledPromiseRejections()
    {
        using var runtime = new QuickJsRuntime();
        using var realm = runtime.CreateRealm();
        var operations = new List<string>();
        runtime.SetRejectionHandler((owner, promise, reason, handled) =>
            operations.Add((handled ? "handled:" : "rejected:") + reason.AsString()));
        realm.Execute("globalThis.rejected = Promise.reject('reason')");
        realm.Execute("rejected.catch(() => {})");
        Equal("rejected:reason,handled:reason", string.Join(',', operations));
    }

    [Test]
    public static void InterruptsRunawayScript()
    {
        using var runtime = new QuickJsRuntime();
        using var realm = runtime.CreateRealm();
        var checks = 0;
        runtime.SetInterruptHandler(() => ++checks > 10);
        try { realm.Execute("for (;;) {}"); throw new Exception("Expected interruption"); }
        catch (QuickJsException) { True(checks > 10); }
    }

    [Test]
    public static void PrefetchesStaticImportsBeforeNativeLinking()
    {
        using var session = new BrowserSession();
        using var graph = new QuickJsModuleGraph(new BrowserModuleFetcher(session, "https://site.test/main.js").FetchSource,
            "https://site.test/main.js");
        const string entry = "https://site.test/main.js";
        graph.RegisterInline(entry,
            "import {answer} from 'data:text/javascript,export%20const%20answer%3D42%3B'; globalThis.graphAnswer = answer;");
        Equal(entry, graph.PrefetchAsync(entry).GetAwaiter().GetResult());
        using var runtime = new QuickJsRuntime();
        using var realm = runtime.CreateRealm();
        graph.Attach(realm);
        realm.Execute(graph.Source(entry)!, entry, module: true);
        using var global = realm.Global();
        using var answer = global.Get("graphAnswer");
        Equal(42, (int)answer.AsNumber());
    }

    [Test]
    public static void DynamicImportSettlesAfterAsynchronousPrefetch()
    {
        using var session = new BrowserSession();
        using var graph = new QuickJsModuleGraph(new BrowserModuleFetcher(session, "https://site.test/main.js").FetchSource,
            "https://site.test/main.js");
        using var runtime = new QuickJsRuntime();
        using var realm = runtime.CreateRealm();
        graph.Attach(realm);
        var queued = new ConcurrentQueue<Action>();
        using var imported = graph.ImportAsync(realm, "https://site.test/main.js",
            "data:text/javascript,export%20const%20answer%3D42%3B", queued.Enqueue);
        using var global = realm.Global();
        global.Set("imported", imported);
        using (var setup = realm.Eval("globalThis.answer = 0; imported.then(module => answer = module.answer)")) { }
        Action? completion = null;
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline && !queued.TryDequeue(out completion)) Thread.Sleep(5);
        True(completion is not null, "Dynamic import did not finish prefetching");
        completion!();
        using (var before = global.Get("answer")) Equal(0, (int)before.AsNumber());
        runtime.PumpJobs();
        using (var after = global.Get("answer")) Equal(42, (int)after.AsNumber());
    }

    [Test]
    public static void DynamicImportRejectsInvalidSpecifierAndCancellation()
    {
        using var session = new BrowserSession();
        using var graph = new QuickJsModuleGraph(new BrowserModuleFetcher(session, "https://site.test/main.js").FetchSource,
            "https://site.test/main.js");
        using var runtime = new QuickJsRuntime();
        using var realm = runtime.CreateRealm();
        graph.Attach(realm);
        var queued = new ConcurrentQueue<Action>();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        using var bad = graph.ImportAsync(realm, "https://site.test/main.js", "bare", queued.Enqueue);
        using var aborted = graph.ImportAsync(realm, "https://site.test/main.js",
            "data:text/javascript,export%20const%20value%3D1", queued.Enqueue, cancelled.Token);
        using var global = realm.Global();
        global.Set("bad", bad);
        global.Set("aborted", aborted);
        using (var setup = realm.Eval("globalThis.errors = 0; bad.catch(e => { if (e instanceof TypeError) errors++ }); aborted.catch(e => { if (e instanceof TypeError) errors++ })")) { }
        var deadline = DateTime.UtcNow.AddSeconds(5);
        var count = 0;
        while (DateTime.UtcNow < deadline && count < 2)
        {
            if (queued.TryDequeue(out var completion)) { completion(); count++; }
            else Thread.Sleep(5);
        }
        Equal(2, count);
        runtime.PumpJobs();
        using var errors = global.Get("errors");
        Equal(2, (int)errors.AsNumber());
    }
}
