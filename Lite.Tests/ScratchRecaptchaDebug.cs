using Lite.Models;
using Lite.Network;
using Lite.Scripting;

namespace Lite.Tests;

// Temporary diagnostic harness; not part of the suite.
public static class ScratchRecaptchaDebug
{
    [Test]
    public static void Probe()
    {
        // Scratch harness: the probe script only exists on this machine; skip elsewhere (CI).
        const string widgetPath = @"C:\Users\Dani\AppData\Local\Temp\recaptcha_probe.js";
        if (!File.Exists(widgetPath)) return;
        var reps = int.TryParse(Environment.GetEnvironmentVariable("LITE_SCRATCH_REPS"), out var n) ? n : 3;
        var variant = Environment.GetEnvironmentVariable("LITE_SCRATCH_SHIM") ?? "trivial";
        var shim = variant switch
        {
            "trivial" => "globalThis.__x = 1 + 1;",
            "arr" => "globalThis.__probeLogs = [];",
            "fn" => "globalThis.__probe = function(tag, a, b) {};",
            "fncap" => "globalThis.__probeLogs = []; globalThis.__probe = function(tag, a, b) { __probeLogs.push(tag); };",
            "fnargs" => "globalThis.__probe = function(tag, a, b) { String(a); String(b); };",
            "trycatch" => "globalThis.__probe = function(tag, a, b) { try { Object.keys(a); } catch (e) {} };",
            "symbol" => "globalThis.__probe = function(tag, a, b) { var q = a && a[Symbol.toStringTag]; };",
            _ => "globalThis.__x = 1 + 1;",
        };
        for (var i = 0; i < reps; i++)
        {
            using var session = new BrowserSession();
            var page = Parser.ParseChildPage("<!doctype html><body></body>", true,
                "https://embed.test/", 400, 200, session);
            var engine = page.Engine;
            engine.Execute(shim);
            Console.WriteLine($"{i}: shim ok");
            engine.Execute(File.ReadAllText(widgetPath),
                "https://www.gstatic.com/recaptcha/releases/kemdRjWFxNjgsGdhRslyEPwU/recaptcha__pt_pt.js");
            Console.WriteLine($"{i}: widget ok");
        }
        Console.WriteLine("all done");
    }
}
