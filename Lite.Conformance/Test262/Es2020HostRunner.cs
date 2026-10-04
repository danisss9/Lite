using System.Text.Json;
using Lite.Scripting.Runtime;
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
            Require(observed?.Equals(engine.RawEngine.GetValue("thrown")) == true, "Script error identity lost");
            engine.Execute("return 1;");
            Require(observed?.IsError() == true && observed.Get("name").ToString() == "SyntaxError", "Script parse error type lost");
        },
        ["annex-b-document-all"] = site => Check(site.Empty(), """
            if (document.all === undefined) throw Error('document.all is not implemented');
            if (typeof document.all !== 'undefined' || Boolean(document.all) || document.all != null)
                throw Error('document.all lacks the IsHTMLDDA semantics');
            const all = document.all;
            if (all !== document.all || !('length' in all) || typeof all.length !== 'number')
                throw Error('document.all identity or length');
            const before = all.length;
            const first = document.createElement('div'); first.id = 'all-target';
            document.body.appendChild(first);
            if (all.length !== before + 1 || all.item(before) !== first ||
                all[before] !== first || all.namedItem('all-target') !== first ||
                all['all-target'] !== first || all('all-target') !== first ||
                all() !== null || all.item() !== null)
                throw Error('document.all indexed, named or callable access');
            const second = document.createElement('div'); second.id = 'all-target';
            document.body.appendChild(second);
            const matches = all.namedItem('all-target');
            if (matches.length !== 2 || matches.item(0) !== first || matches[1] !== second)
                throw Error('document.all multiple live named matches');
            document.body.removeChild(second);
            if (matches.length !== 1 || all.namedItem('all-target') !== first)
                throw Error('document.all collection did not stay live');
            """),
        ["module-credentials-and-referrer"] = site =>
        {
            site.Routes["/cred.html"] = $$"""
                <!doctype html><html><body>
                <script type='module' crossorigin='use-credentials' src='{{site.AlternateUrl}}/cred-star.js'></script>
                <script type='module' crossorigin='use-credentials' src='{{site.AlternateUrl}}/cred-exact.js'></script>
                <script type='module'>
                  globalThis.__credResults = {};
                  try {
                    const m = await import('{{site.AlternateUrl}}/cred-dyn.js');
                    __credResults.dynamicWildcard = m.value;
                  } catch (e) { __credResults.dynamicWildcard = 'rejected: ' + (e && e.message ? e.message : String(e)); }
                  await import('/modules/root.js');
                  // A same-origin module that redirects cross-origin is tainted: the redirected
                  // response still needs CORS, and a wildcard suffices for this dynamic import.
                  try {
                    const t = await import('/taint.js');
                    __credResults.redirectTaint = t.value;
                  } catch (e) { __credResults.redirectTaint = 'rejected: ' + (e && e.message ? e.message : String(e)); }
                  globalThis.__credDone = true;
                </script>
                </body></html>
                """;
            site.Referers.Clear();
            var (_, engine) = HeadlessPage.Load(site.BaseUrl + "/cred.html");
            Require(HeadlessPage.PumpUntil(engine, () => engine.RawEngine.GetValue("__credDone").ToObject() is true),
                "Credentials page did not finish");
            // A credentialed cross-origin module fetch must reject a wildcard ACAO but accept the
            // exact origin, while a dynamic import (always "same-origin" credentials) takes the
            // wildcard. The two script-element modules above observe the difference.
            Require(engine.RawEngine.GetValue("__credStarLoaded").IsUndefined(),
                "Credentialed module accepted wildcard Access-Control-Allow-Origin");
            Require(engine.RawEngine.GetValue("__credExactLoaded").ToObject() is true,
                "Credentialed module rejected an exact-origin Access-Control-Allow-Origin");
            Require(Convert.ToDouble(engine.RawEngine.GetValue("__credResults").Get("dynamicWildcard").ToObject()) == 3d,
                "Dynamic import did not use same-origin credentials with a wildcard ACAO");
            Require(Convert.ToDouble(engine.RawEngine.GetValue("__credResults").Get("redirectTaint").ToObject()) == 5d,
                "Same-origin module redirecting cross-origin did not enforce CORS on the redirected response");
            // Same-origin root and descendant module fetches carry their referrer URLs: the
            // document base URL for the entry fetch, the importing module's URL for descendants.
            Require(site.Referers.GetValueOrDefault("/modules/root.js") == site.BaseUrl + "/cred.html",
                $"Root module fetch referrer was {site.Referers.GetValueOrDefault("/modules/root.js")}");
            Require(site.Referers.GetValueOrDefault("/modules/sub/dep.js")?.EndsWith("/modules/root.js") == true,
                $"Descendant module fetch referrer was {site.Referers.GetValueOrDefault("/modules/sub/dep.js")}");
        },
        ["module-identity-edges"] = site => Check(site.Empty(), """
            const first = await import('/modules/root.js');
            if (globalThis.__evaluations !== 1) throw Error('baseline evaluation');
            const canonical = await import('/modules/../modules/root.js');
            if (canonical !== first || globalThis.__evaluations !== 1) throw Error('dot-segment identity');
            const queried = await import('/modules/root.js?v=1');
            if (queried === first || globalThis.__evaluations !== 2) throw Error('query distinctness');
            const again = await import('/modules/root.js');
            if (again !== first || again.ns !== first.ns) throw Error('namespace identity across imports');
            if (first.ns[Symbol.toStringTag] !== 'Module') throw Error('namespace toStringTag');
            first.ns.value = 99;
            if (first.ns.value !== 1) throw Error('namespace immutability');
            """),
        ["module-failed-load-caching"] = site =>
        {
            site.RequestCounts.Clear();
            Check(site.Empty(), """
                let first = null, second = null;
                try { await import('/missing-identity.js'); } catch (e) { first = e.constructor.name; }
                try { await import('/missing-identity.js'); } catch (e) { second = e.constructor.name; }
                if (first !== 'TypeError' || second !== 'TypeError') throw Error('Expected TypeErrors, saw ' + first + ' then ' + second);
                """);
            Require(site.RequestCounts.GetValueOrDefault("/missing-identity.js") == 1,
                $"The failed module was refetched: {site.RequestCounts.GetValueOrDefault("/missing-identity.js")} requests");
        },
        ["module-base-and-classic-redirect"] = site =>
        {
            var (_, baseEngine) = HeadlessPage.Load(site.BaseUrl + "/base-page.html");
            Require(HeadlessPage.PumpUntil(baseEngine, () =>
                !baseEngine.RawEngine.GetValue("__baseMeta").IsUndefined() &&
                !baseEngine.RawEngine.GetValue("__baseRel").IsUndefined()), "Base page did not finish");
            // An inline module's import.meta.url is the document base URL (after <base>), and
            // its relative imports resolve against that same base.
            Require(baseEngine.RawEngine.GetValue("__baseMeta").ToString() == site.BaseUrl + "/basedir/",
                $"Inline module import.meta.url was {baseEngine.RawEngine.GetValue("__baseMeta")}");
            Require(Convert.ToDouble(baseEngine.RawEngine.GetValue("__baseRel").ToObject()) == 9d, "Import against <base> failed");
            var (_, redirectEngine) = HeadlessPage.Load(site.BaseUrl + "/classic-redirect.html");
            Require(HeadlessPage.PumpUntil(redirectEngine, () =>
                redirectEngine.RawEngine.GetValue("__redirectedDone").ToObject() is true),
                "Redirected classic script did not finish");
            // The redirected classic script runs with its response URL, so its dynamic import
            // of './neighbor.js' resolves next to the redirected-to file, not the redirector.
            Require(Convert.ToDouble(redirectEngine.RawEngine.GetValue("__redirectedValue").ToObject()) == 42d,
                "Redirected classic script resolved imports against the wrong base");
            // A dynamic import inside a timer callback keeps the module's own URL as its base
            // (the referrer is baked into the module at compile time, not at call time).
            var (_, timerEngine) = HeadlessPage.Load(site.BaseUrl + "/timer-page.html");
            Require(HeadlessPage.PumpUntil(timerEngine, () =>
                timerEngine.RawEngine.GetValue("__timerDone").ToObject() is true),
                "Timer-callback import did not finish");
            Require(Convert.ToDouble(timerEngine.RawEngine.GetValue("__timerImport").ToObject()) == 1d,
                "Timer-callback import did not resolve against the module's URL");
        },
    };
    internal static string[] TestNames => Cases.Keys.Order(StringComparer.Ordinal).ToArray();

    internal const string ExpectedFailuresFile = "Test262/es2020-host-expected-failures.txt";

    /// <summary>Host obligations whose failure is already published in the compatibility profile.
    /// The recorded evidence is unchanged — these still block <c>es2020ProfileReady</c> — but the
    /// suite only turns red on an unexpected outcome, exactly like the curated WPT manifest.</summary>
    internal static IReadOnlyDictionary<string, string> ExpectedFailures() =>
        Manifest.Load(ConformancePaths.Manifest(ExpectedFailuresFile)).Where(e => e.ExpectedFail)
            .ToDictionary(e => e.Path, e => e.Reason ?? "expected", StringComparer.Ordinal);

    internal static int Run(string? filter, ShardSpec shard, string? reportPath)
    {
        var identity = ExecutionEvidence.CaptureIdentity(); var started = DateTime.UtcNow;
        var results = new List<TestEvidence>();
        var expectedFailures = ExpectedFailures();
        var unexpected = 0;
        using var site = new Site();
        foreach (var name in shard.Apply(TestNames.Where(n => filter is null || n.Contains(filter, StringComparison.OrdinalIgnoreCase))))
        {
            var passed = true; var detail = "ok";
            try { Cases[name](site); } catch (Exception ex) { passed = false; detail = ex.Message; }
            results.Add(new("es2020-host", name, passed ? "pass" : "fail", detail, [new(name, passed ? 0 : 1, detail)], Context: "window", Kind: "javascript-host"));
            var waived = expectedFailures.TryGetValue(name, out var reason);
            var label = (passed, waived) switch
            {
                (true, false) => "PASS ",
                (false, true) => "XFAIL",
                (true, true) => "XPASS",
                _ => "FAIL ",
            };
            if (passed == waived) unexpected++;
            Console.WriteLine($"{label} es2020-host {name}: {(waived && !passed ? reason : detail)}");
        }
        ExecutionEvidence.Write(reportPath ?? Path.Combine(ConformancePaths.EnsureArtifacts(), "es2020-host.json"), identity, started, results);
        if (unexpected > 0)
            Console.WriteLine($"es2020-host: {unexpected} unexpected outcome(s); update {ExpectedFailuresFile} and the profile.");
        return results.Count > 0 && unexpected == 0 ? 0 : 1;
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
        private readonly WebApplication _first;
        private readonly WebApplication _second;
        internal Site()
        {
            _first = Start(this);
            _second = Start(this);
        }
        internal string BaseUrl => _first.Urls.Single();
        internal string AlternateUrl => _second.Urls.Single();
        /// <summary>Per-case page sources; a case installs absolute-URL HTML here before loading it.</summary>
        internal Dictionary<string, string> Routes { get; } = new(StringComparer.Ordinal);
        /// <summary>Last Referer header per request path, for fetch-options assertions.</summary>
        internal Dictionary<string, string?> Referers { get; } = new(StringComparer.Ordinal);
        /// <summary>Total request count per path, for failed-load-caching assertions.</summary>
        internal Dictionary<string, int> RequestCounts { get; } = new(StringComparer.Ordinal);
        internal JsEngine Empty() => HeadlessPage.Load(BaseUrl + "/empty.html").Engine;
        private static WebApplication Start(Site site)
        {
            var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions());
            builder.WebHost.UseKestrelCore().UseUrls("http://127.0.0.1:0");
            var app = builder.Build();
            app.Run(async context =>
            {
                var path = context.Request.Path.Value;
                lock (site.Referers)
                {
                    site.Referers[path!] = context.Request.Headers.Referer.FirstOrDefault();
                    site.RequestCounts[path!] = site.RequestCounts.GetValueOrDefault(path!) + 1;
                }
                if (path == "/redirect.js") { context.Response.Redirect("/modules/sub/main.js"); return; }
                if (path == "/redirect-classic.js") { context.Response.Redirect("/scripts/redirected-importer.js"); return; }
                // Redirect taint: a same-origin module that redirects cross-origin.
                if (path == "/taint.js") { context.Response.Redirect(site.AlternateUrl + "/taint-target.js"); return; }
                if (path == "/slow.js") await Task.Delay(200, context.RequestAborted);
                string? code = site.Routes.GetValueOrDefault(path!);
                if (code is null) code = path switch
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
                    "/base-page.html" => """
                        <!doctype html><html><head><base href='/basedir/'></head><body>
                        <script type='module'>globalThis.__baseMeta=import.meta.url;
                        const m = await import('./rel.js'); globalThis.__baseRel=m.value;</script>
                        </body></html>
                        """,
                    "/classic-redirect.html" => "<!doctype html><script src='/redirect-classic.js'></script>",
                    "/timer-page.html" => "<!doctype html><script type='module' src='/modules/timer-import.js'></script>",
                    "/defer.js" => "__order.push('defer');",
                    "/scripts/main.js" => "import('./neighbor.js').then(m=>{globalThis.__classicValue=m.value;globalThis.__classicDone=true;});",
                    "/scripts/neighbor.js" => "export const value=42;",
                    "/scripts/redirected-importer.js" => "import('./neighbor.js').then(m=>{globalThis.__redirectedValue=m.value;globalThis.__redirectedDone=true;});",
                    "/basedir/rel.js" => "export const value=9;",
                    "/modules/root.js" => "export {value,bump} from './sub/dep.js'; export * as ns from './sub/dep.js'; globalThis.__evaluations=(globalThis.__evaluations||0)+1;",
                    "/modules/sub/dep.js" => "export let value=1; export function bump(){value++;}",
                    "/modules/sub/main.js" => "export {value} from './dep.js'; export const url=import.meta.url;",
                    "/modules/cycle-a.js" => "import {b} from './cycle-b.js';export function a(){return 'a';}export function read(){return a()+b();}",
                    "/modules/cycle-b.js" => "import {a} from './cycle-a.js';export function b(){return 'b';}export function read(){return a()+b();}",
                    "/not-javascript.txt" => "export const wrong=true;",
                    "/invalid.js" => "export const = ;",
                    "/cors.js" => "export const value=7;",
                    "/slow.js" => "export const slow=true;",
                    "/cred-star.js" => "globalThis.__credStarLoaded=true;export const value=3;",
                    "/cred-exact.js" => "globalThis.__credExactLoaded=true;export const value=4;",
                    "/cred-dyn.js" => "export const value=3;",
                    "/taint-target.js" => "export const value=5;",
                    "/modules/timer-import.js" => "setTimeout(() => import('./sub/dep.js').then(m => { globalThis.__timerImport = m.value; globalThis.__timerDone = true; }), 0);",
                    _ => null,
                };
                if (code is null) { context.Response.StatusCode = 404; return; }
                if (path == "/cors.js") context.Response.Headers.AccessControlAllowOrigin = "*";
                // Credentialed cross-origin fetches must match the exact origin, so the star
                // module stays a wildcard while the exact module echoes the request's Origin.
                if (path is "/cred-star.js" or "/cred-dyn.js" or "/taint-target.js") context.Response.Headers.AccessControlAllowOrigin = "*";
                if (path == "/cred-exact.js") context.Response.Headers.AccessControlAllowOrigin =
                    context.Request.Headers.Origin.FirstOrDefault() ?? "null";
                context.Response.ContentType = path!.EndsWith(".html") ? "text/html" : path.EndsWith(".txt") ? "text/plain" : "text/javascript";
                await context.Response.WriteAsync(code);
            });
            app.Start(); return app;
        }
        public void Dispose() { _first.StopAsync().GetAwaiter().GetResult(); _second.StopAsync().GetAwaiter().GetResult(); _first.DisposeAsync().AsTask().GetAwaiter().GetResult(); _second.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
    }
}
