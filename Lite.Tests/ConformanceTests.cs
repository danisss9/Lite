using System.Text.Json.Nodes;
using System.Text.Json;
using Jint;
using Lite.Conformance.Harness;
using Lite.Conformance.Profile;
using Lite.Conformance.Wpt;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

public static class ConformanceTests
{
    [Test]
    public static void WptCrashtests_RequireLoadAndDeferredPaint()
    {
        ConformanceServer.Start(cssRegressionMode: false);
        try
        {
            const string path = "lite/harness/crash-wait.html";
            var test = new WptCase(path, path, "crashtest", "window", false, false, []);
            var result = WptVisualPage.RunCrash(test);
            True(result.Passed, result.Detail);
            True(!WptVisualPage.RunCrash(test with { Path = "lite/harness/missing-crash-test.html" }).Passed);
            True(WptVisualPage.RunCrash(test with { TestDriver = true }).Cat == WptRunner.Cat.Unsupported);
        }
        finally { ConformanceServer.Stop(); }
    }

    [Test]
    public static void WptReferences_UseAlternativePathsAndRejectCycles()
    {
        ConformanceServer.Start(cssRegressionMode: false);
        try
        {
            const string red = "lite/harness/reference-red.html";
            const string blue = "lite/harness/reference-blue.html";
            var test = new WptCase(red, red, "reftest", "window", false, false,
                [new("/" + blue, "=="), new("/" + red, "==")]);
            var result = WptRefTestRunner.Run(test, _ => null);
            True(result.Passed, result.Detail);
            True(result.Artifacts is { Count: >= 3 });
            True(!WptRefTestRunner.Run(test with { References = [new("/" + blue, "==")] }, _ => null).Passed);
            True(WptRefTestRunner.Run(test with { References = [new("/" + blue, "!=")] }, _ => null).Passed);
            True(!WptRefTestRunner.Run(test with { References = [new("/" + red, "==")] }, _ => test).Passed);
            True(!WptRefTestRunner.Run(test with { Path = "lite/harness/missing.html", References = [new("/lite/harness/also-missing.html", "==")] }, _ => null).Passed,
                "Two failed document loads must not compare as a passing blank page.");
            True(WptRefTestRunner.Run(test with { Options = new JsonObject { ["viewport_size"] = "320x240" },
                References = [new("/" + red, "==")] }, _ => null).Passed,
                "Reference documents inherit the root test viewport.");
            True(WptRefTestRunner.Run(test with { Path = "lite/harness/reference-wait.html", References = [new("/" + blue, "==")] }, _ => null).Passed,
                "TestRendered must release reftest-wait before comparison.");
        }
        finally { ConformanceServer.Stop(); }
    }

    [Test]
    public static void WptCatalog_IncludesVariantsWorkersReferencesAndManualTests()
    {
        var manifest = JsonNode.Parse("""
            {"version":9,"url_base":"/","items":{
              "testharness":{"html":{"sample.any.js":["hash",["html/sample.any.html?one",{}],
                ["html/sample.any.worker.html?one",{"timeout":"long","testdriver":true}]]}},
              "reftest":{"html":{"paint.html":["hash",[null,[["/html/reference.html","=="],["/html/not.html","!="]],{}]]}},
              "manual":{"html":{"input-manual.html":["hash",[null,{}]]}},
              "crashtest":{"html":{"layout-crash.html":["hash",[null,{}]]}},
              "support":{"html":{"reference.html":["hash",[]]}}
            }}
            """)!.AsObject();
        var catalog = WptCatalog.Parse(manifest);
        Equal(5, catalog.Count);
        var worker = catalog.Single(c => c.Context == "dedicatedworker");
        Equal("html/sample.any.js", worker.Source);
        True(worker.LongTimeout && worker.TestDriver);
        Equal(2, catalog.Single(c => c.Kind == "reftest").References.Count);
        True(catalog.Any(c => c.Kind == "manual"));
        True(catalog.Any(c => c.Kind == "crashtest"));
        True(!WptCatalog.ValidPath("html/%2e%2e/test.html"));
        True(!WptCatalog.ValidPath("C:/outside.html"));
    }

    [Test]
    public static void MixedEvidence_RequiresEveryReviewedTargetAssertion()
    {
        var review = JsonNode.Parse("""
            {"classification":"mixed","assertionInventoryComplete":true,"assertions":[
              {"name":"required","classification":"included","reason":"HTML 5.0 obligation"},
              {"name":"later","classification":"post-target","reason":"Introduced after target"}]}
            """)!.AsObject();
        var evidence = Pass() with { Outcome = "fail", Subtests = [new("required", 0, null), new("later", 1, "unsupported")] };
        True(ExecutionEvidence.HasPassingEvidence([evidence], "wpt", evidence.Path, "required", true, review));
        True(!ExecutionEvidence.HasPassingEvidence([evidence], "wpt", evidence.Path, "later", true, review));
        True(!ExecutionEvidence.HasPassingEvidence([evidence with { HarnessStatus = 2 }], "wpt", evidence.Path, null, true, review));
        True(!ExecutionEvidence.HasPassingEvidence([evidence with { Outcome = "timeout" }], "wpt", evidence.Path, null, true, review));
        True(!ExecutionEvidence.HasPassingEvidence([evidence with { Subtests = [new("required", 0, null), new("later", 2, "timeout")] }], "wpt", evidence.Path, null, true, review));
        True(!ExecutionEvidence.HasPassingEvidence([evidence with { Subtests = [new("required", 0, null)] }], "wpt", evidence.Path, null, true, review));
        True(!ExecutionEvidence.HasPassingEvidence([evidence with { Subtests = [new("required", 1, null), new("later", 0, null)] }], "wpt", evidence.Path, null, true, review));
        True(!ExecutionEvidence.HasPassingEvidence([evidence with { Subtests = [new("required", 0, null), new("unknown", 0, null)] }], "wpt", evidence.Path, null, true, review));
        True(!ExecutionEvidence.HasPassingEvidence([evidence], "wpt", evidence.Path, null, true, review, "dedicatedworker"));
        True(!ExecutionEvidence.HasPassingEvidence([evidence], "wpt", evidence.Path, null, true, review, "window", "reftest"));
    }

    [Test]
    public static void ManualEvidence_RequiresArtifactsAndRejectsTampering()
    {
        var identity = ExecutionEvidence.CaptureIdentity();
        var root = ConformancePaths.EnsureArtifacts();
        var id = Guid.NewGuid().ToString("N");
        var attachment = Path.Combine(root, id + ".txt");
        var path = Path.Combine(root, id + ".json");
        try
        {
            File.WriteAllText(attachment, "Observed native keyboard navigation.");
            var test = new TestEvidence("manual", "keyboard/tab", "pass", "Observed", [new("tab order", 0, null)],
                Kind: "manual", Artifacts: [ExecutionEvidence.Artifact(attachment, "observation")],
                Manual: new("tester", "keyboard/tab", "Windows x64 native host", DateTimeOffset.UtcNow.ToString("O")));
            var report = new EvidenceReport(ExecutionEvidence.FormatVersion, identity, DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O"),
                DateTimeOffset.UtcNow.ToString("O"), true, [test]);
            File.WriteAllText(path, JsonSerializer.Serialize(report, ExecutionEvidence.JsonOptions));
            Equal(1, ExecutionEvidence.ReadCurrent([path], identity, []).Count);
            var missingContext = JsonSerializer.SerializeToNode(report, ExecutionEvidence.JsonOptions)!;
            missingContext["tests"]![0]!.AsObject().Remove("context");
            File.WriteAllText(path, missingContext.ToJsonString());
            Equal(0, ExecutionEvidence.ReadCurrent([path], identity, []).Count);
            File.WriteAllText(path, JsonSerializer.Serialize(report with { Tests = [test with { Suite = "wpt", Manual = null }] }, ExecutionEvidence.JsonOptions));
            Equal(0, ExecutionEvidence.ReadCurrent([path], identity, []).Count);
            File.WriteAllText(path, JsonSerializer.Serialize(report, ExecutionEvidence.JsonOptions));
            File.WriteAllText(attachment, "changed");
            Equal(0, ExecutionEvidence.ReadCurrent([path], identity, []).Count);
            File.WriteAllText(path, JsonSerializer.Serialize(report with { Tests = [test with { Manual = null, Artifacts = [] }] }, ExecutionEvidence.JsonOptions));
            Equal(0, ExecutionEvidence.ReadCurrent([path], identity, []).Count);
            File.WriteAllText(path, JsonSerializer.Serialize(report with { FormatVersion = 2 }, ExecutionEvidence.JsonOptions));
            Equal(0, ExecutionEvidence.ReadCurrent([path], identity, []).Count);
        }
        finally { File.Delete(path); File.Delete(attachment); }
    }

    private static TestEvidence Pass(string name = "required") => new("wpt", "html/test.html", "pass", "ok", [new(name, 0, null)], 0, "upstream-wpt");

    [Test]
    public static void Evidence_RequiresMatchingPassingAssertions()
    {
        True(ExecutionEvidence.HasPassingEvidence([Pass()], "wpt", "html/test.html", "required"));
        True(!ExecutionEvidence.HasPassingEvidence([Pass()], "wpt", "html/test.html", "different"));
        True(!ExecutionEvidence.HasPassingEvidence([Pass()], "wpt", "html/other.html", null));
        True(!ExecutionEvidence.HasPassingEvidence([Pass() with { HarnessStatus = 2 }], "wpt", "html/test.html", null));
        True(!ExecutionEvidence.HasPassingEvidence([Pass(), Pass() with { Outcome = "timeout" }], "wpt", "html/test.html", null));
        True(!ExecutionEvidence.HasPassingEvidence([Pass() with { Subtests = [] }], "wpt", "html/test.html", null));
        True(!ExecutionEvidence.HasPassingEvidence([Pass() with { Environment = "local" }], "wpt", "html/test.html", null, requireUpstream: true));
    }

    [Test]
    public static void Evidence_RejectsStaleAndIncompleteReports()
    {
        var identity = ExecutionEvidence.CaptureIdentity();
        var path = Path.Combine(ConformancePaths.EnsureArtifacts(), $"evidence-test-{Guid.NewGuid():N}.json");
        try
        {
            var report = new EvidenceReport(ExecutionEvidence.FormatVersion, identity, DateTime.UtcNow.ToString("O"), DateTime.UtcNow.ToString("O"), true, [Pass()]);
            File.WriteAllText(path, JsonSerializer.Serialize(report, ExecutionEvidence.JsonOptions));
            var blockers = new List<string>();
            Equal(1, ExecutionEvidence.ReadCurrent([path], identity, blockers).Count);
            Equal(0, blockers.Count);
            Equal(0, ExecutionEvidence.ReadCurrent([path], identity with { SourceSha256 = "different" }, blockers).Count);
            True(blockers.Any(b => b.StartsWith("stale-or-incomplete-evidence:")));
            File.WriteAllText(path, JsonSerializer.Serialize(report with { Completed = false }, ExecutionEvidence.JsonOptions));
            Equal(0, ExecutionEvidence.ReadCurrent([path], identity, []).Count);
        }
        finally { File.Delete(path); }
    }

    [Test]
    public static void HtmlSectionReview_CannotOmitSectionsOrUseCompleteFlagAlone()
    {
        var inventory = JsonNode.Parse(File.ReadAllText(ConformancePaths.Manifest(HtmlSectionInventory.FileName)))!.AsObject();
        var errors = new List<string>();
        var blockers = HtmlSectionInventory.Evaluate(inventory, Profile(), errors);
        Equal(0, errors.Count);
        True(blockers.Any(b => b.StartsWith("html5-unreviewed-sections:")));
        inventory["reviewComplete"] = true;
        True(HtmlSectionInventory.Evaluate(inventory, Profile(), errors).Count > 0);
        inventory["sections"]!.AsArray().RemoveAt(0);
        HtmlSectionInventory.Evaluate(inventory, Profile(), errors);
        True(errors.Any(e => e.Contains("index differs")));
    }

    [Test]
    public static void Html5Contract_Uses2014ClausesAndDoesNotCountExtensions()
    {
        var profile = JsonNode.Parse(File.ReadAllText(ConformancePaths.Manifest(ExecutionEvidence.ProfileFile)))!.AsObject();
        var inventory = JsonNode.Parse(File.ReadAllText(ConformancePaths.Manifest(HtmlSectionInventory.FileName)))!.AsObject();
        Equal("https://www.w3.org/TR/2014/REC-html5-20141028/", HtmlSectionInventory.Target);
        Equal(762, inventory["sections"]!.AsArray().Count);
        var sections = inventory["sections"]!.AsArray().OfType<JsonObject>()
            .ToDictionary(s => s["clause"]!.GetValue<string>());
        var requirements = profile["requirements"]!.AsArray().OfType<JsonObject>()
            .Where(r => r["specification"]!.GetValue<string>() == "html5" && r["applicability"]!.GetValue<string>() == "included").ToArray();
        foreach (var requirement in requirements)
        {
            var clause = requirement["clause"]!.GetValue<string>().Split(' ')[0];
            True(sections.ContainsKey(clause), $"Unmapped HTML5 clause: {clause}");
            Equal(sections[clause]["url"]!.GetValue<string>(), requirement["url"]!.GetValue<string>());
        }
        True(requirements.Any(r => r["clause"]!.GetValue<string>() == "5.7")); // Application cache.
        True(requirements.Any(r => r["clause"]!.GetValue<string>() == "4.10.12")); // keygen.
        var reviews = HtmlApplicability.Read()["tests"]!.AsArray().OfType<JsonObject>().ToArray();
        var details = reviews.Where(r => r["path"]!.GetValue<string>().StartsWith("lite/html5/details-", StringComparison.Ordinal)).ToArray();
        Equal(2, details.Length);
        True(details.All(r => !HtmlApplicability.IsIncluded(r)));
        True(HtmlApplicability.IsIncluded(reviews.Single(r => r["path"]!.GetValue<string>() == "lite/html5/iframe-initial-document.html")));
        var invalid = HtmlApplicability.Read();
        invalid["target"] = "https://www.w3.org/TR/2018/WD-html53-20181018/";
        var errors = new List<string>();
        HtmlApplicability.Validate(invalid, errors);
        True(errors.Count > 0);
    }

    [Test]
    public static void HtmlEvidence_RequiresEveryDeclaredVariant()
    {
        const string path = "lite/harness/variants.window.js";
        var one = Pass() with { Path = path + "?one" };
        var two = Pass() with { Path = path + "?two" };
        True(!ProfileRunner.HasMappedEvidence([one], "wpt", path, "required"));
        True(ProfileRunner.HasMappedEvidence([one, two], "wpt", path, "required"));
    }

    [Test]
    public static void Fetch_UsesItsOwningDocumentBaseUrl()
    {
        ConformanceServer.Start();
        try
        {
            var first = Parser.ParseChildPage("<!doctype html><base href='/lite/harness/owner-a/'>", true,
                ConformanceServer.BaseUrl + "/first.html", 400, 200);
            var second = Parser.ParseChildPage("<!doctype html><base href='/lite/harness/owner-b/'>", true,
                ConformanceServer.BaseUrl + "/second.html", 400, 200);
            foreach (var page in new[] { first, second })
                page.Engine.RawEngine.Execute("fetch('value.txt').then(function(r){return r.text();}).then(function(t){globalThis.__fetched=t.trim();});");
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline &&
                   (first.Engine.RawEngine.GetValue("__fetched").IsUndefined() || second.Engine.RawEngine.GetValue("__fetched").IsUndefined()))
            {
                first.Engine.DrainTasks();
                second.Engine.DrainTasks();
                Thread.Sleep(10);
            }
            Equal("owner A", first.Engine.RawEngine.GetValue("__fetched").ToString());
            Equal("owner B", second.Engine.RawEngine.GetValue("__fetched").ToString());
        }
        finally { ConformanceServer.Stop(); }
    }

    private static JsonObject Profile() => JsonNode.Parse("""
        {"coverage":{"html5ClauseInventoryComplete":true,"html5TestInventoryComplete":true,"html5RequiredDependencies":[]},
         "requirements":[
          {"id":"html5.test","specification":"html5","applicability":"included","status":"implemented",
           "tests":[{"suite":"wpt","path":"html/test.html","assertion":"required"}]},
          {"id":"html5.chrome","specification":"html5","applicability":"excluded","status":"profile-excluded","tests":[]},
          {"id":"css21.unrelated","specification":"css21","applicability":"included","status":"failing","tests":[]}]}
        """)!.AsObject();

    [Test]
    public static void HtmlReadiness_SeparatesExplicitExclusionsAndUnrelatedStandards()
    {
        Equal(0, ProfileRunner.EvaluateHtmlReadiness(Profile(), [Pass()]).Count);
        True(ProfileRunner.EvaluateHtmlReadiness(Profile(), []).Count > 0);
        var profile = Profile();
        profile["coverage"]!["html5ClauseInventoryComplete"] = false;
        True(ProfileRunner.EvaluateHtmlReadiness(profile, [Pass()]).Contains("html5-clause-inventory-incomplete"));
        profile = Profile();
        profile["coverage"]!["html5RequiredDependencies"]!.AsArray().Add("css21.unrelated");
        True(ProfileRunner.EvaluateHtmlReadiness(profile, [Pass()]).Any(b => b.Contains("css21.unrelated")));
    }

    [Test]
    public static void WptMetadata_PreservesScriptsVariantsAndWindowApplicability()
    {
        var metadata = WptMetadata.Parse("// META: script=../helper.js\n// META: variant=?one\n// META: variant=?two\n// META: timeout=long\n// META: global=window,dedicatedworker\n");
        Equal(2, metadata.Variants.Count);
        True(metadata.Window && metadata.LongTimeout);
        Contains("../helper.js", metadata.Wrapper("/test.any.js"));
        Equal("test.any.html?one", WptMetadata.UrlPath("test.any.js?one"));
        Equal("test.window.html#two", WptMetadata.UrlPath("test.window.js#two"));
        True(!WptMetadata.Parse("// META: global=dedicatedworker\n").Window);
        var html = WptMetadata.Parse("<meta content='?a&amp;b' name='variant'><meta name=timeout content=long>");
        Equal("?a&b", html.Variants.Single());
        True(html.LongTimeout);
    }

    [Test]
    public static void WptReport_RejectsEmptyAndHarnessFailure()
    {
        True(!WptRunner.ParseReport("{\"status\":0,\"tests\":[]}").Passed);
        True(!WptRunner.ParseReport("{\"status\":2,\"tests\":[{\"name\":\"x\",\"status\":0}]}").Passed);
        var result = WptRunner.ParseReport("{\"status\":0,\"tests\":[{\"name\":\"x\",\"status\":0},{\"name\":\"y\",\"status\":1,\"message\":\"broken\"}]}");
        Equal(2, result.Subtests!.Count);
        Equal("broken", result.Subtests[1].Message);
        True(!result.Passed);
    }

    [Test]
    public static void ServerPaths_CannotEscapeTestRoots()
    {
        True(ConformanceServer.ResolveFile("../../Directory.Packages.props") is null);
        True(ConformanceServer.ResolveFile("../Profile/lite-html5-css21-es2020-profile.json") is null);
    }

    [Test]
    public static void WptWorker_ContainsLoadHangsAndRejectsChildReports()
    {
        ConformanceServer.Start();
        try
        {
            foreach (var fixture in new[] { "hangs-during-load", "child-report-only" })
            {
                var path = $"lite/harness/{fixture}.html";
                var outcome = WptRunner.RunIsolated(path, $"{ConformanceServer.BaseUrl}/{path}", 4000);
                True(outcome.Cat == WptRunner.Cat.Timeout, $"{fixture}: expected timeout, got {outcome.Cat}: {outcome.Detail}");
            }
            True(WptRunner.RunIsolated("lite/smoke.html", $"{ConformanceServer.BaseUrl}/lite/smoke.html", 20_000).Passed,
                "A timed-out worker must not prevent a subsequent test from passing.");
        }
        finally { ConformanceServer.Stop(); }
    }
}
