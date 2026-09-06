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
            var report = new EvidenceReport(2, identity, DateTime.UtcNow.ToString("O"), DateTime.UtcNow.ToString("O"), true, [Pass()]);
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
        True(blockers.Any(b => b.StartsWith("html53-unreviewed-sections:")));
        inventory["reviewComplete"] = true;
        True(HtmlSectionInventory.Evaluate(inventory, Profile(), errors).Count > 0);
        inventory["sections"]!.AsArray().RemoveAt(0);
        HtmlSectionInventory.Evaluate(inventory, Profile(), errors);
        True(errors.Any(e => e.Contains("index differs")));
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
        {"coverage":{"html53ClauseInventoryComplete":true,"html53TestInventoryComplete":true,"html53RequiredDependencies":[]},
         "requirements":[
          {"id":"html53.test","specification":"html53","applicability":"included","status":"implemented",
           "tests":[{"suite":"wpt","path":"html/test.html","assertion":"required"}]},
          {"id":"html53.chrome","specification":"html53","applicability":"excluded","status":"profile-excluded","tests":[]},
          {"id":"css21.unrelated","specification":"css21","applicability":"included","status":"failing","tests":[]}]}
        """)!.AsObject();

    [Test]
    public static void HtmlReadiness_SeparatesExplicitExclusionsAndUnrelatedStandards()
    {
        Equal(0, ProfileRunner.EvaluateHtmlReadiness(Profile(), [Pass()]).Count);
        True(ProfileRunner.EvaluateHtmlReadiness(Profile(), []).Count > 0);
        var profile = Profile();
        profile["coverage"]!["html53ClauseInventoryComplete"] = false;
        True(ProfileRunner.EvaluateHtmlReadiness(profile, [Pass()]).Contains("html53-clause-inventory-incomplete"));
        profile = Profile();
        profile["coverage"]!["html53RequiredDependencies"]!.AsArray().Add("css21.unrelated");
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
        True(ConformanceServer.ResolveFile("../Profile/lite-html53-css21-es2020-profile.json") is null);
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
