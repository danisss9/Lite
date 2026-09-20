using System.Text.Json;
using Jint;
using Jint.Native;
using Lite.Conformance.Harness;
using Lite.Scripting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace Lite.Conformance.Test262;

internal static class Es2020HostRunner
{
    private static readonly Dictionary<string, Action<Site>> Cases = new(StringComparer.Ordinal)
    {
        ["globals-and-builtins"] = site => Check(site.Empty(), """
            if (window !== globalThis || self !== globalThis) throw Error('global identity');
            const d = Object.getOwnPropertyDescriptor(globalThis, 'globalThis');
            if (!d.writable || !d.configurable || d.enumerable) throw Error('globalThis descriptor');
            if (12345678901234567890n + 1n !== 12345678901234567891n) throw Error('BigInt');
            if (({a:null}).a?.b !== undefined || (0 ?? 1) !== 0) throw Error('short circuit');
            if ([...'ab'.matchAll(/./g)].length !== 2) throw Error('matchAll');
            const values = await Promise.allSettled([1,Promise.reject(2)]);
            if (values[0].value !== 1 || values[1].reason !== 2) throw Error('allSettled');
            """),
        ["module-graphs-live-bindings-and-identity"] = site => Check(site.Empty(), """
            const a = await import('/modules/root.js');
            const b = await import('/modules/./root.js');
            if (a !== b || globalThis.__evaluations !== 1) throw Error('module identity');
            a.bump();
            if (a.value !== 2 || a.ns.value !== 2) throw Error('live bindings');
            const nested = await import('/modules/sub/main.js');
            if (nested.value !== 2 || !nested.url.endsWith('/modules/sub/main.js')) throw Error('nested importer/meta');
            const cycle = await import('/modules/cycle-a.js');
            if (cycle.read() !== 'ab') throw Error('cyclic graph');
            """),
        ["classic-script-import-base"] = site =>
        {
            var (_, engine) = HeadlessPage.Load(site.BaseUrl + "/classic.html");
            Require(HeadlessPage.PumpUntil(engine, () => engine.RawEngine.GetValue("__classicDone").ToObject() is true), "External classic import did not finish");
            Require(engine.RawEngine.GetValue("__classicValue").ToObject()?.ToString() == "42", "Classic import resolved against wrong URL");
        },
        ["module-readiness-and-inline-meta"] = site =>
        {
            var (_, engine) = HeadlessPage.Load(site.BaseUrl + "/ordered.html");
            Require(HeadlessPage.PumpUntil(engine, () => engine.RawEngine.GetValue("__loaded").ToObject() is true), "Document load did not finish");
            Require(engine.RawEngine.GetValue("__order").ToString() == "module,defer,dom,load", "Deferred/module/readiness order");
            Require(engine.RawEngine.GetValue("__inlineMeta").ToString() == site.BaseUrl + "/ordered.html", "Inline import.meta.url");
            Require(engine.RawEngine.GetValue("__moduleReadyState").ToString() == "interactive", "Module executed before interactive readiness");
        },
        ["module-rejection-types"] = site => Check(site.Empty(), """
            for (const name of ['unmapped-package', '/missing.js', '/not-javascript.txt']) {
                let rejected = false;
                try { await import(name); } catch (e) { rejected = e instanceof TypeError; }
                if (!rejected) throw Error('Expected TypeError for ' + name);
            }
            let syntax = false;
            try { await import('/invalid.js'); } catch(e) { syntax = e instanceof SyntaxError; }
            if (!syntax) throw Error('Expected SyntaxError');
            """),
        ["module-redirect-base-and-meta"] = site => Check(site.Empty(), """
            const redirected = await import('/redirect.js');
            if (redirected.value !== 1 || !redirected.url.endsWith('/modules/sub/main.js')) throw Error('Redirect base/meta');
            """),
        ["module-cors"] = site => Check(site.Empty(), $$"""
            let rejected = false;
            try { await import('{{site.AlternateUrl}}/modules/sub/dep.js'); } catch(e) { rejected = e instanceof TypeError; }
            if (!rejected) throw Error('Cross-origin module without ACAO accepted');
            const allowed = await import('{{site.AlternateUrl}}/cors.js');
            if (allowed.value !== 7) throw Error('CORS module failed');
            """),
        ["microtasks-and-rejection-events"] = site => Check(site.Empty(), """
            const steps = [];
            await new Promise(resolve => {
                setTimeout(() => { steps.push('timer'); resolve(); }, 0);
                Promise.resolve().then(() => steps.push('promise'));
                steps.push('sync');
            });
            if (steps.join(',') !== 'sync,promise,timer') throw Error('Microtask order');
            let rejected, handled;
            addEventListener('unhandledrejection', e => { rejected = e.reason; e.promise.catch(() => {}); });
            addEventListener('rejectionhandled', e => { handled = e.reason; });
            const reason = { marker: 1 };
            Promise.reject(reason);
            for (let i=0; i<10 && !handled; i++) await new Promise(r => setTimeout(r, 0));
            if (rejected !== reason || handled !== reason) throw Error('Rejection event identity');
            """),
        ["nonblocking-window-agent"] = site => Check(site.Empty(), """
            let rejected = false;
            try { Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, 0); }
            catch(e) { rejected = e instanceof TypeError; }
            if (!rejected) throw Error('Window agent allowed blocking Atomics.wait');
            """),
        ["document-realm-isolation"] = site =>
        {
            var first = site.Empty(); var second = site.Empty();
            first.RawEngine.Execute("globalThis.privateValue=42; Array.prototype.privateMarker=true;");
            Require(second.RawEngine.Evaluate("typeof privateValue === 'undefined' && !Array.prototype.privateMarker").ToObject() is true, "Realms share globals/intrinsics");
            Check(first, "const m=await import('/modules/root.js'); if (__evaluations!==1) throw Error('first module map');");
            Check(second, "const m=await import('/modules/root.js'); if (__evaluations!==1 || m.value!==1) throw Error('second module map');");
        },
        ["module-cancellation"] = site =>
        {
            var engine = site.Empty();
            engine.Execute("globalThis.__cancelled=false; import('/slow.js').catch(e=>__cancelled=e instanceof TypeError);");
            engine.CancelModuleLoads();
            Require(HeadlessPage.PumpUntil(engine, () => engine.RawEngine.GetValue("__cancelled").ToObject() is true), "Cancelled module did not reject");
        },
        ["script-error-identity"] = site =>
        {
            var engine = site.Empty(); JsValue? observed = null;
            engine.ScriptFailed += error => observed = error;
            engine.Execute("globalThis.thrown = {code:42}; throw thrown;");
            Require(ReferenceEquals(observed, engine.RawEngine.GetValue("thrown")), "Script error identity lost");
            engine.Execute("return 1;");
            Require(observed is Jint.Native.Error.ErrorInstance && observed.AsObject().Get("name").ToString() == "SyntaxError", "Script parse error type lost");
        },
        ["annex-b-document-all"] = site => Check(site.Empty(), """
            if (document.all === undefined) throw Error('document.all is not implemented');
            if (typeof document.all !== 'undefined' || Boolean(document.all) || document.all != null)
                throw Error('document.all lacks the IsHTMLDDA semantics');
            """),
    };
    internal static string[] TestNames => Cases.Keys.Order(StringComparer.Ordinal).ToArray();

    internal static int Run(string? filter, ShardSpec shard, string? reportPath)
    {
        var identity = ExecutionEvidence.CaptureIdentity(); var started = DateTime.UtcNow;
        var results = new List<TestEvidence>();
        using var site = new Site();
        foreach (var name in shard.Apply(TestNames.Where(n => filter is null || n.Contains(filter, StringComparison.OrdinalIgnoreCase))))
        {
            var passed = true; var detail = "ok";
            try { Cases[name](site); } catch (Exception ex) { passed = false; detail = ex.Message; }
            results.Add(new("es2020-host", name, passed ? "pass" : "fail", detail, [new(name, passed ? 0 : 1, detail)], Context: "window", Kind: "javascript-host"));
            Console.WriteLine($"{(passed ? "PASS" : "FAIL")} es2020-host {name}: {detail}");
        }
        ExecutionEvidence.Write(reportPath ?? Path.Combine(ConformancePaths.EnsureArtifacts(), "es2020-host.json"), identity, started, results);
        return results.Count > 0 && results.All(t => t.Outcome == "pass") ? 0 : 1;
    }

    private static void Check(JsEngine engine, string script)
    {
        engine.RawEngine.Execute("globalThis.__hostDone=false;globalThis.__hostFailure=null;(async()=>{" + script +
            "})().then(()=>__hostDone=true,e=>{__hostFailure=String(e);__hostDone=true;});");
        Require(HeadlessPage.PumpUntil(engine, () => engine.RawEngine.GetValue("__hostDone").ToObject() is true), "Host test timed out");
        var failure = engine.RawEngine.GetValue("__hostFailure");
        Require(failure.IsNull(), failure.ToString());
    }
    private static void Require(bool condition, string detail) { if (!condition) throw new InvalidOperationException(detail); }

    private sealed class Site : IDisposable
    {
        private readonly WebApplication _first = Start();
        private readonly WebApplication _second = Start();
        internal string BaseUrl => _first.Urls.Single();
        internal string AlternateUrl => _second.Urls.Single();
        internal JsEngine Empty() => HeadlessPage.Load(BaseUrl + "/empty.html").Engine;
        private static WebApplication Start()
        {
            var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions());
            builder.WebHost.UseKestrelCore().UseUrls("http://127.0.0.1:0");
            var app = builder.Build();
            app.Run(async context =>
            {
                var path = context.Request.Path.Value;
                if (path == "/redirect.js") { context.Response.Redirect("/modules/sub/main.js"); return; }
                if (path == "/slow.js") await Task.Delay(200, context.RequestAborted);
                var code = path switch
                {
                    "/empty.html" => "<!doctype html><html><body></body></html>",
                    "/classic.html" => "<!doctype html><script src='/scripts/main.js'></script>",
                    "/ordered.html" => """
                        <!doctype html><script>var __order=[];var __loaded=false;
                        document.addEventListener('DOMContentLoaded',()=>__order.push('dom'));
                        addEventListener('load',()=>{__order.push('load');__order=__order.join(',');__loaded=true;});</script>
                        <script type='module'>import '/modules/root.js'; __order.push('module');globalThis.__inlineMeta=import.meta.url;globalThis.__moduleReadyState=document.readyState;</script>
                        <script defer src='/defer.js'></script>
                        """,
                    "/defer.js" => "__order.push('defer');",
                    "/scripts/main.js" => "import('./neighbor.js').then(m=>{globalThis.__classicValue=m.value;globalThis.__classicDone=true;});",
                    "/scripts/neighbor.js" => "export const value=42;",
                    "/modules/root.js" => "export {value,bump} from './sub/dep.js'; export * as ns from './sub/dep.js'; globalThis.__evaluations=(globalThis.__evaluations||0)+1;",
                    "/modules/sub/dep.js" => "export let value=1; export function bump(){value++;}",
                    "/modules/sub/main.js" => "export {value} from './dep.js'; export const url=import.meta.url;",
                    "/modules/cycle-a.js" => "import {b} from './cycle-b.js';export function a(){return 'a';}export function read(){return a()+b();}",
                    "/modules/cycle-b.js" => "import {a} from './cycle-a.js';export function b(){return 'b';}export function read(){return a()+b();}",
                    "/not-javascript.txt" => "export const wrong=true;",
                    "/invalid.js" => "export const = ;",
                    "/cors.js" => "export const value=7;",
                    "/slow.js" => "export const slow=true;",
                    _ => null,
                };
                if (code is null) { context.Response.StatusCode = 404; return; }
                if (path == "/cors.js") context.Response.Headers.AccessControlAllowOrigin = "*";
                context.Response.ContentType = path!.EndsWith(".html") ? "text/html" : path.EndsWith(".txt") ? "text/plain" : "text/javascript";
                await context.Response.WriteAsync(code);
            });
            app.Start(); return app;
        }
        public void Dispose() { _first.StopAsync().GetAwaiter().GetResult(); _second.StopAsync().GetAwaiter().GetResult(); _first.DisposeAsync().AsTask().GetAwaiter().GetResult(); _second.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
    }
}
