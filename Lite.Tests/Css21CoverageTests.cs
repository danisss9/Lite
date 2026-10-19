using System.Text.Json.Nodes;
using Lite.Conformance.Css21;
using Lite.Conformance.Harness;
using Lite.Conformance.Wpt;
using Lite.Layout;
using Lite.Models;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

public static class Css21CoverageTests
{
    [Test]
    public static void SpecificationIndexes_RejectOmissionsEvenWhenMarkedComplete()
    {
        var index = Css21Inventory.Read("Profile/css21-sections.json");
        Css21Inventory.ValidateIndex(index, properties: false);
        index["reviewComplete"] = true;
        index["sections"]!.AsArray().RemoveAt(0);
        Reject(() => Css21Inventory.ValidateIndex(index, properties: false));
        var properties = Css21Inventory.Read("Profile/css21-properties.json");
        Css21Inventory.ValidateIndex(properties, properties: true);
        properties["properties"]![0]!["initial"] = "invented";
        Reject(() => Css21Inventory.ValidateIndex(properties, properties: true));
    }

    [Test]
    public static void Evidence_SeparatesMediaAndRejectsConflictingRuns()
    {
        var pass = Evidence();
        True(Css21Inventory.HasEvidence([pass], pass.Suite, pass.Path, "screen", "inventory"));
        True(!Css21Inventory.HasEvidence([pass], pass.Suite, pass.Path, "print", "inventory"));
        True(!Css21Inventory.HasEvidence([pass], pass.Suite, pass.Path, "screen", "changed"));
        True(!Css21Inventory.HasEvidence([pass, pass with { Outcome = "fail" }], pass.Suite, pass.Path, "screen", "inventory"));
        True(!Css21Inventory.HasEvidence([pass with { Environment = "local" }], pass.Suite, pass.Path, "screen", "inventory"));
        True(!Css21Inventory.HasEvidence([pass with { Css = null }], pass.Suite, pass.Path, "screen", "inventory"));
    }

    [Test]
    public static void Evidence_RequiresActualDocumentModeAndPaginatedArtifacts()
    {
        var pass = Evidence() with { Path = "css/CSS2/example.xht" };
        True(!Css21Inventory.HasEvidence([pass], pass.Suite, pass.Path, "screen", "inventory"));
        pass = pass with { Css = pass.Css! with { DocumentMode = "xhtml" } };
        True(Css21Inventory.HasEvidence([pass], pass.Suite, pass.Path, "screen", "inventory"));
        pass = pass with { Css = pass.Css! with { Media = "print" } };
        True(!Css21Inventory.HasEvidence([pass], pass.Suite, pass.Path, "print", "inventory"));
        pass = pass with
        {
            Css = pass.Css! with { PageCount = 2, PageWidthPoints = 595, PageHeightPoints = 842 },
            Artifacts = [new("test.pdf", "hash", "pdf")]
        };
        True(Css21Inventory.HasEvidence([pass], pass.Suite, pass.Path, "print", "inventory"));
    }

    [Test]
    public static void Review_RejectsApplicableTestsWithoutObligationMappings()
    {
        var requirements = Css21Inventory.Read(Css21Inventory.RequirementsFile);
        var tests = Css21Inventory.Read(Css21Inventory.ApplicabilityFile);
        tests["tests"]!.AsArray().Add(new JsonObject
        {
            ["path"] = "css/CSS2/example.html",
            ["classification"] = "applicable",
            ["rationale"] = "Reviewed against 2011",
            ["media"] = new JsonArray("screen"),
            ["requirementIds"] = new JsonArray()
        });
        Reject(() => Css21Inventory.ValidateReviews(requirements, tests));
    }

    [Test]
    public static void TableSpacing_UsesIndependentAxesAndPhysicalUnits()
    {
        var first = Node("TD", "table-cell");
        var second = Node("TD", "table-cell");
        var third = Node("TD", "table-cell");
        var fourth = Node("TD", "table-cell");
        var row1 = Node("TR", "table-row", first, second);
        var row2 = Node("TR", "table-row", third, fourth);
        var table = Node("TABLE", "table", row1, row2);
        table.StyleOverrides["border-spacing"] = "3pt 9px";
        var height = TableEngine.LayoutTable(table, 0, 0, 120, 800, 600);
        True(Math.Abs(first.Box.BorderBox.Left - 4) < .01f);
        True(Math.Abs(first.Box.BorderBox.Top - 9) < .01f);
        True(Math.Abs(second.Box.BorderBox.Left - first.Box.BorderBox.Right - 4) < .01f);
        True(Math.Abs(third.Box.BorderBox.Top - first.Box.BorderBox.Bottom - 9) < .01f);
        True(Math.Abs(height - fourth.Box.BorderBox.Bottom - 9) < .01f);
        table.StyleOverrides["border-collapse"] = "collapse";
        TableEngine.LayoutTable(table, 0, 0, 120, 800, 600);
        True(Math.Abs(first.Box.BorderBox.Top) < .01f);
        True(Math.Abs(second.Box.BorderBox.Left - first.Box.BorderBox.Right) < .01f);
    }

    [Test]
    public static void TableSpacing_RejectsTheEntireInvalidPair()
    {
        var table = Node("DIV", "table");
        foreach (var value in new[] { "4px -2px", "3px 50%", "4px 5px 6px", "4px NaNpx", "4px 2" })
        {
            table.StyleOverrides["border-spacing"] = value;
            var spacing = TableEngine.GetBorderSpacing(table);
            True(spacing == (0f, 0f), value);
        }
    }

    [Test]
    public static void DynamicInheritance_IncludesTableListsAndPaginationProperties()
    {
        var child = Node("SPAN", "inline");
        var parent = Node("DIV", "block", child);
        var values = new Dictionary<string, string>
        {
            ["border-spacing"] = "3px 7px",
            ["border-collapse"] = "collapse",
            ["caption-side"] = "bottom",
            ["empty-cells"] = "hide",
            ["list-style-image"] = "url(http://test/bullet.png)",
            ["quotes"] = "\"[\" \"]\"",
            ["orphans"] = "3",
            ["widows"] = "4"
        };
        foreach (var (property, value) in values) parent.StyleOverrides[property] = value;
        StyleResolver.Apply(child);
        foreach (var (property, value) in values) Equal(value, child.StyleOverrides.GetValueOrDefault(property));
        child.StyleOverrides["border-collapse"] = "initial";
        StyleResolver.ResolveCssWideKeyword(child, "border-collapse");
        Equal("separate", child.StyleOverrides["border-collapse"]);
    }

    [Test]
    public static void OfficialCounts_ReconcileAgainstThePublishedSuiteTotals()
    {
        True(OfficialCatalog.CountsReconcile(new OfficialCounts(9379, 9160, 219, 339, 9040, 9418, 9191, 39, ["xhtml1-screen"])));
        True(!OfficialCatalog.CountsReconcile(new OfficialCounts(9419, 9191, 228, 339, 9080, 9418, 9191, 0, [])));
        True(!OfficialCatalog.CountsReconcile(new OfficialCounts(9192, 9192, 0, 1, 9191, 9418, 9191, 2226, [])));
        True(!OfficialCatalog.CountsReconcile(new OfficialCounts(9418, 9190, 228, 339, 9079, 9418, 9191, 0, [])));
        True(!OfficialCatalog.CountsReconcile(new OfficialCounts(9000, 9000, 0, 0, 9000, 9418, 9191, 100, [])));
    }

    [Test]
    public static void FuzzyLimits_ComeOnlyFromManifestMetadata()
    {
        var current = RefCase("css/CSS2/a.htm", FuzzyEntry(null, 1, 15));
        var addressedToA = RefCase("css/CSS2/a-ref.xht", FuzzyEntry("a-ref.xht", 4, 40));
        var addressedToB = RefCase("css/CSS2/b-ref.xht", FuzzyEntry("a-ref.xht", 4, 40));
        True(WptRefTestRunner.FuzzyLimit(current, addressedToA, "css/CSS2/a-ref.xht") == (4, 40));
        True(WptRefTestRunner.FuzzyLimit(current, addressedToB, "css/CSS2/b-ref.xht") == (1, 15));
        True(WptRefTestRunner.FuzzyLimit(RefCase("css/CSS2/a.htm", null), null, "css/CSS2/a-ref.xht") is null);
    }

    [Test]
    public static void OfficialEvidence_AllowsLocalServingAndRequiresTheDocumentMode()
    {
        var official = new TestEvidence("css21-official", "css21-official/html4/example.htm", "pass", "ok",
            [new("assertion", 0, null)], 0, "local", Css: new("screen", "html", "inventory", 800, 600, 0, 1, null));
        True(Css21Inventory.HasEvidence([official], official.Suite, official.Path, "screen", "inventory"));
        True(!Css21Inventory.HasEvidence([official with { Css = official.Css! with { DocumentMode = "xhtml" } }],
            official.Suite, official.Path, "screen", "inventory"));
        True(!Css21Inventory.HasEvidence([official with { Outcome = "fail" }], official.Suite, official.Path, "screen", "inventory"));
    }

    [Test]
    public static void OfficialTwin_EquivalenceIgnoresSyntaxButNotRevisedValuesOrText()
    {
        const string html4 = "<html><head><style type=\"text/css\">\n  div { width: 2in }\n</style></head>" +
            "<body>\n<p>Test passes if there is no red.</p>\n<div><br></div></body></html>";
        const string xhtml = "<html xmlns=\"http://www.w3.org/1999/xhtml\"><head><style type=\"text/css\"><![CDATA[\ndiv { width: 2in }\n]]></style>" +
            "<!-- reviewed --></head><body><p>Test passes if there is no red.</p><div><br /></div></body></html>";
        True(OfficialTwinOracle.Normalize(html4) == OfficialTwinOracle.Normalize(xhtml));
        True(OfficialTwinOracle.Normalize(html4) != OfficialTwinOracle.Normalize(xhtml.Replace("2in", "200px")));
        True(OfficialTwinOracle.Normalize(html4) != OfficialTwinOracle.Normalize(xhtml.Replace("no red", "no red visible")));
    }

    [Test]
    public static void OfficialTwin_RetargetsReferencesOntoTheOfficialDocument()
    {
        var twin = RefCase("css/CSS2/floats/floats-001.xht", null) with
        {
            References = [new("floats-001-ref.xht", "=="), new("/css/reference/ref-filled-green-100px-square.xht", "!=")],
        };
        var subject = OfficialTwinOracle.Retarget("css21-official/html4/floats-001.htm", twin);
        True(subject.Path == "css21-official/html4/floats-001.htm");
        True(subject.References[0] == new WptReference("/css/CSS2/floats/floats-001-ref.xht", "=="));
        True(subject.References[1] == new WptReference("/css/reference/ref-filled-green-100px-square.xht", "!="));
    }

    [Test]
    public static void OfficialTwin_NeverTurnsARedScanFailureIntoAPassAndSkipsNonOfficialCases()
    {
        var failed = new WptRunner.RunResult(WptRunner.Cat.Fail, "3 red pixels (diagnostic)", 1, 1);
        var twin = new OfficialTwinOracle.Twin(RefCase("css/CSS2/floats/floats-001.xht", null), false);
        True(OfficialTwinOracle.Strengthen(twin, "css21-official/html4/floats-001.htm", failed, _ => null) == failed);
        True(OfficialTwinOracle.Find("css/CSS2/floats/floats-001.xht", [RefCase("css/CSS2/floats/floats-001.xht", null)]) is null);
    }

    [Test]
    public static void SelfTestOracle_RedScanPassesOnlyWhereNoRedIsTheWholeCondition()
    {
        const string path = "css21-official/html4/example.htm";
        var scanned = new WptRunner.RunResult(WptRunner.Cat.Pass, "Self-describing test shows no failure red (diagnostic)", 1, 0,
            [new("Self-describing test shows no failure red (diagnostic)", 0, null)], 0);
        True(SelfTestOracle.Decide(path, "no-red", scanned, null, _ => null).Passed);
        True(SelfTestOracle.Decide(path, "same-color", scanned, null, _ => null).Passed);
        foreach (var condition in new[] { "compound-no-red", "visual", "manual", "unstated" })
            True(SelfTestOracle.Decide(path, condition, scanned, null, _ => null).Cat == WptRunner.Cat.Unsupported);
        var failed = new WptRunner.RunResult(WptRunner.Cat.Fail, "3 red pixels (diagnostic)", 1, 1);
        True(SelfTestOracle.Decide(path, "no-red", failed, null, _ => null) == failed);
    }

    private static TestEvidence Evidence() => new("css21-wpt", "css/CSS2/example.html", "pass", "ok", [new("assertion", 0, null)],
        0, "upstream-wpt", Css: new("screen", "html", "inventory", 800, 600, 0, 1, null));

    private static WptCase RefCase(string path, JsonObject? options) => new(path, path, "reftest", "window", false, false, [], options);

    private static JsonObject FuzzyEntry(string? reference, int maxDifference, int maxPixels) => new()
    {
        ["fuzzy"] = new JsonArray(new JsonArray(
            reference is null ? null : JsonValue.Create(reference),
            new JsonArray(
                new JsonArray(JsonValue.Create(0), JsonValue.Create(maxDifference)),
                new JsonArray(JsonValue.Create(0), JsonValue.Create(maxPixels)))))
    };

    private static LayoutNode Node(string tag, string display, params LayoutNode[] children)
    {
        var style = Parser.ParseFragment("<div></div>")[0].Style;
        var node = new LayoutNode(null, tag, "", style);
        node.StyleOverrides["display"] = display;
        node.StyleOverrides["height"] = "15px";
        foreach (var side in new[] { "top", "right", "bottom", "left" })
        {
            node.StyleOverrides[$"margin-{side}"] = "0";
            node.StyleOverrides[$"padding-{side}"] = "0";
            node.StyleOverrides[$"border-{side}-width"] = "0";
        }
        foreach (var child in children) node.AddChild(child);
        return node;
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new Exception("Expected invalid CSS inventory to be rejected.");
    }
}
