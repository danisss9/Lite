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

    [Test]
    public static void VisualChecks_OneShapeIsStrictAboutCountShapeAndColor()
    {
        var green = new SkiaSharp.SKColor(0, 0x80, 0);
        // A box with nested text is one top-level shape; its fractional edge is antialiased.
        var boxed = Render(c =>
        {
            c.DrawRect(new SkiaSharp.SKRect(20.5f, 30, 120.5f, 130), Fill(green));
            c.DrawRect(new SkiaSharp.SKRect(40, 60, 60, 70), Fill(SkiaSharp.SKColors.Black));
        });
        True(Judge(boxed, new VisualCheck("one-shape", "square", "green")).Passed);
        True(!Judge(boxed, new VisualCheck("one-shape", "box", "blue")).Passed);
        var twoBoxes = Render(c =>
        {
            c.DrawRect(new SkiaSharp.SKRect(20, 30, 120, 60), Fill(green));
            c.DrawRect(new SkiaSharp.SKRect(20, 80, 120, 110), Fill(green));
        });
        True(!Judge(twoBoxes, new VisualCheck("one-shape", "box")).Passed);
        True(!Judge(twoBoxes, new VisualCheck("one-shape", "square")).Passed);
        // A glyph-like blob (an L) does not cover its bounding box's perimeter.
        var blob = Render(c =>
        {
            c.DrawRect(new SkiaSharp.SKRect(20, 30, 30, 130), Fill(green));
            c.DrawRect(new SkiaSharp.SKRect(20, 120, 120, 130), Fill(green));
        });
        True(!Judge(blob, new VisualCheck("one-shape", "box")).Passed);
        // Two adjacent bars merge into the single bar the border-spacing tests require.
        var oneBar = Render(c =>
        {
            c.DrawRect(new SkiaSharp.SKRect(20, 30, 120, 50), Fill(SkiaSharp.SKColors.Black));
            c.DrawRect(new SkiaSharp.SKRect(120, 30, 220, 50), Fill(SkiaSharp.SKColors.Black));
        });
        True(Judge(oneBar, new VisualCheck("one-shape", "bar")).Passed);
    }

    [Test]
    public static void VisualChecks_ComparesDimensionsAndBarGaps()
    {
        var black = SkiaSharp.SKColors.Black;
        var same = Render(c =>
        {
            c.DrawRect(new SkiaSharp.SKRect(20, 30, 120.4f, 60), Fill(black));
            c.DrawRect(new SkiaSharp.SKRect(20.6f, 80, 121, 140), Fill(black));
        });
        True(Judge(same, new VisualCheck("same-dimension", Dimension: "width", Count: 2)).Passed);
        True(!Judge(same, new VisualCheck("same-dimension", Dimension: "size", Count: 2)).Passed);
        True(!Judge(same, new VisualCheck("same-dimension", Dimension: "width", Count: 3)).Passed);
        var bars = Render(c =>
        {
            c.DrawRect(new SkiaSharp.SKRect(10, 30, 110, 50), Fill(black));
            c.DrawRect(new SkiaSharp.SKRect(206, 30, 306, 50), Fill(black));
            c.DrawRect(new SkiaSharp.SKRect(110, 90, 206, 110), Fill(black));
        });
        True(Judge(bars, new VisualCheck("bar-gap")).Passed);
        var shortBar = Render(c =>
        {
            c.DrawRect(new SkiaSharp.SKRect(10, 30, 110, 50), Fill(black));
            c.DrawRect(new SkiaSharp.SKRect(206, 30, 306, 50), Fill(black));
            c.DrawRect(new SkiaSharp.SKRect(110, 90, 190, 110), Fill(black));
        });
        True(!Judge(shortBar, new VisualCheck("bar-gap")).Passed);
        // border-spacing-029: the middle bar touches the outer bars only at their corners.
        var cornerTouching = Render(c =>
        {
            c.DrawRect(new SkiaSharp.SKRect(4, 166, 124, 186), Fill(black));
            c.DrawRect(new SkiaSharp.SKRect(124, 146, 220, 166), Fill(black));
            c.DrawRect(new SkiaSharp.SKRect(220, 166, 340, 186), Fill(black));
        });
        True(Judge(cornerTouching, new VisualCheck("bar-gap")).Passed);
        // table-layout-applies-to-010: a black bar stacked edge to edge on a blue one.
        var stacked = Render(c =>
        {
            c.DrawRect(new SkiaSharp.SKRect(8, 50, 328, 82.5f), Fill(black));
            c.DrawRect(new SkiaSharp.SKRect(8, 82.5f, 328, 114), Fill(SkiaSharp.SKColors.Blue));
        });
        True(Judge(stacked, new VisualCheck("same-dimension", Dimension: "width", Count: 2)).Passed);
        True(!Judge(stacked, new VisualCheck("one-shape", "bar")).Passed);
    }

    [Test]
    public static void VisualChecks_LookSameComparesPixels()
    {
        var black = SkiaSharp.SKColors.Black;
        var pair = Render(c =>
        {
            c.DrawRect(new SkiaSharp.SKRect(20, 30, 120, 60), Fill(black));
            c.DrawRect(new SkiaSharp.SKRect(20, 90, 120, 120), Fill(black));
        });
        True(Judge(pair, new VisualCheck("look-same"), pair).Passed);
        var different = Render(c =>
        {
            c.DrawRect(new SkiaSharp.SKRect(20, 30, 120, 60), Fill(black));
            c.DrawRect(new SkiaSharp.SKRect(20, 90, 120, 125), Fill(black));
        });
        True(!Judge(different, new VisualCheck("look-same"), different).Passed);
        var colors = Render(c =>
        {
            c.DrawRect(new SkiaSharp.SKRect(20, 30, 120, 60), Fill(black));
            c.DrawRect(new SkiaSharp.SKRect(20, 90, 120, 120), Fill(SkiaSharp.SKColors.Blue));
        });
        True(!Judge(colors, new VisualCheck("look-same"), colors).Passed);
    }

    [Test]
    public static void VisualChecks_SolidLineDemandsALineInTheStatedColor()
    {
        var greenLine = Render(c => c.DrawRect(new SkiaSharp.SKRect(20, 100, 320, 105), Fill(SkiaSharp.SKColors.Green)));
        True(Judge(greenLine, new VisualCheck("solid-line", Color: "green"), greenLine).Passed);
        True(!Judge(greenLine, new VisualCheck("solid-line", Color: "blue"), greenLine).Passed);
        True(Judge(greenLine, new VisualCheck("solid-line"), greenLine).Passed);
        var square = Render(c => c.DrawRect(new SkiaSharp.SKRect(20, 100, 120, 200), Fill(SkiaSharp.SKColors.Green)));
        True(!Judge(square, new VisualCheck("solid-line", Color: "green"), square).Passed);
    }

    [Test]
    public static void VisualChecks_BorderRingDemandsEverySideInTheStatedStyle()
    {
        // A dashed ring: three dashes per side with the corners clipped like the real thing.
        var dashed = Render(c =>
        {
            foreach (var (x, y, w, h) in new[] { (20, 30, 40, 5), (65, 30, 40, 5), (110, 30, 30, 5),
                (20, 125, 40, 5), (65, 125, 40, 5), (110, 125, 30, 5),
                (20, 30, 5, 40), (20, 75, 5, 40), (20, 120, 5, 10),
                (135, 30, 5, 40), (135, 75, 5, 40), (135, 120, 5, 10) })
                c.DrawRect(new SkiaSharp.SKRect(x, y, x + w, y + h), Fill(SkiaSharp.SKColors.Lime));
        });
        True(Judge(dashed, new VisualCheck("border-ring", "dashed", "lime"), dashed).Passed);
        True(!Judge(dashed, new VisualCheck("border-ring", "dotted", "lime"), dashed).Passed);
        // A solid ring has one unbroken segment per side — it is not dashed.
        var solid = Render(c =>
        {
            c.DrawRect(new SkiaSharp.SKRect(20, 30, 140, 35), Fill(SkiaSharp.SKColors.Lime));
            c.DrawRect(new SkiaSharp.SKRect(20, 125, 140, 130), Fill(SkiaSharp.SKColors.Lime));
            c.DrawRect(new SkiaSharp.SKRect(20, 30, 25, 130), Fill(SkiaSharp.SKColors.Lime));
            c.DrawRect(new SkiaSharp.SKRect(135, 30, 140, 130), Fill(SkiaSharp.SKColors.Lime));
        });
        True(!Judge(solid, new VisualCheck("border-ring", "dashed", "lime"), solid).Passed);
        // A side missing entirely fails.
        var threeSides = Render(c =>
        {
            c.DrawRect(new SkiaSharp.SKRect(20, 30, 140, 35), Fill(SkiaSharp.SKColors.Lime));
            c.DrawRect(new SkiaSharp.SKRect(20, 125, 140, 130), Fill(SkiaSharp.SKColors.Lime));
            c.DrawRect(new SkiaSharp.SKRect(20, 30, 25, 130), Fill(SkiaSharp.SKColors.Lime));
        });
        True(!Judge(threeSides, new VisualCheck("border-ring", "dashed", "lime"), threeSides).Passed);
    }

    [Test]
    public static void VisualChecks_ShapeRelations()
    {
        var blue = SkiaSharp.SKColors.Blue;
        var orange = SkiaSharp.SKColors.Orange;
        var sizes = Render(c =>
        {
            c.DrawRect(new SkiaSharp.SKRect(20, 30, 120, 110), Fill(blue));
            c.DrawRect(new SkiaSharp.SKRect(20, 130, 70, 180), Fill(orange));
        });
        True(Judge(sizes, new VisualCheck("shape-relation", Relation: "larger", ColorA: "blue", ColorB: "orange"), sizes).Passed);
        True(Judge(sizes, new VisualCheck("shape-relation", Relation: "smaller", ColorA: "orange", ColorB: "blue"), sizes).Passed);
        True(!Judge(sizes, new VisualCheck("shape-relation", Relation: "larger", ColorA: "orange", ColorB: "blue"), sizes).Passed);
        // The inner square is flush right and vertically centered in the outer box.
        var nested = Render(c =>
        {
            c.DrawRect(new SkiaSharp.SKRect(20, 30, 220, 130), Fill(blue));
            c.DrawRect(new SkiaSharp.SKRect(180, 60, 220, 100), Fill(SkiaSharp.SKColors.Lime));
        });
        True(Judge(nested, new VisualCheck("shape-relation", Relation: "centered-right", ColorA: "lime", ColorB: "blue"), nested).Passed);
        True(!Judge(nested, new VisualCheck("shape-relation", Relation: "centered-right", ColorA: "blue", ColorB: "lime"), nested).Passed);
        // Two stacked squares with no vertical gap between them.
        var touching = Render(c =>
        {
            c.DrawRect(new SkiaSharp.SKRect(20, 30, 80, 90), Fill(blue));
            c.DrawRect(new SkiaSharp.SKRect(20, 90, 80, 150), Fill(orange));
        });
        True(Judge(touching, new VisualCheck("shape-relation", Relation: "no-vertical-gap", ColorA: "blue", ColorB: "orange"), touching).Passed);
        var gapped = Render(c =>
        {
            c.DrawRect(new SkiaSharp.SKRect(20, 30, 80, 90), Fill(blue));
            c.DrawRect(new SkiaSharp.SKRect(20, 95, 80, 150), Fill(orange));
        });
        True(!Judge(gapped, new VisualCheck("shape-relation", Relation: "no-vertical-gap", ColorA: "blue", ColorB: "orange"), gapped).Passed);
    }

    [Test]
    public static void VisualChecks_SpaceBetweenReadsTheGapColor()
    {
        var pair = Render(c =>
        {
            c.DrawRect(new SkiaSharp.SKRect(20, 30, 120, 60), Fill(SkiaSharp.SKColors.Black));
            c.DrawRect(new SkiaSharp.SKRect(20, 90, 120, 120), Fill(SkiaSharp.SKColors.Black));
        });
        True(Judge(pair, new VisualCheck("space-between", Color: "white"), pair).Passed);
        True(!Judge(pair, new VisualCheck("space-between", Color: "lime"), pair).Passed);
    }

    [Test]
    public static void VisualChecks_StackThreeAndTwoSize()
    {
        var stack = Render(c =>
        {
            c.DrawRect(new SkiaSharp.SKRect(20, 30, 120, 50), Fill(SkiaSharp.SKColors.Black));
            c.DrawRect(new SkiaSharp.SKRect(20, 50, 120, 70), Fill(SkiaSharp.SKColors.Blue));
            c.DrawRect(new SkiaSharp.SKRect(20, 70, 120, 90), Fill(SkiaSharp.SKColors.Lime));
        });
        True(Judge(stack, new VisualCheck("stack-three"), stack).Passed);
        var uneven = Render(c =>
        {
            c.DrawRect(new SkiaSharp.SKRect(20, 30, 120, 50), Fill(SkiaSharp.SKColors.Black));
            c.DrawRect(new SkiaSharp.SKRect(20, 50, 110, 70), Fill(SkiaSharp.SKColors.Blue));
            c.DrawRect(new SkiaSharp.SKRect(20, 70, 120, 90), Fill(SkiaSharp.SKColors.Lime));
        });
        True(!Judge(uneven, new VisualCheck("stack-three"), uneven).Passed);
        var sizes = Render(c =>
        {
            c.DrawRect(new SkiaSharp.SKRect(20, 30, 120, 110), Fill(SkiaSharp.SKColors.Black));
            c.DrawRect(new SkiaSharp.SKRect(20, 130, 70, 180), Fill(SkiaSharp.SKColors.Black));
        });
        True(Judge(sizes, new VisualCheck("two-size"), sizes).Passed);
        var equal = Render(c =>
        {
            c.DrawRect(new SkiaSharp.SKRect(20, 30, 120, 110), Fill(SkiaSharp.SKColors.Black));
            c.DrawRect(new SkiaSharp.SKRect(20, 130, 120, 210), Fill(SkiaSharp.SKColors.Black));
        });
        True(!Judge(equal, new VisualCheck("two-size"), equal).Passed);
    }

    private static SkiaSharp.SKBitmap Render(Action<SkiaSharp.SKCanvas> draw)
    {
        var bitmap = new SkiaSharp.SKBitmap(400, 300);
        using var canvas = new SkiaSharp.SKCanvas(bitmap);
        canvas.Clear(SkiaSharp.SKColors.White);
        draw(canvas);
        return bitmap;
    }

    private static SkiaSharp.SKPaint Fill(SkiaSharp.SKColor color) =>
        new() { Color = color, Style = SkiaSharp.SKPaintStyle.Fill, IsAntialias = true };

    private static WptRunner.RunResult Judge(SkiaSharp.SKBitmap bitmap, VisualCheck check, SkiaSharp.SKBitmap? render = null)
    {
        var components = VisualChecks.Components(bitmap, 0, out var clipped);
        True(!clipped);
        return VisualChecks.Judge(check, VisualChecks.TopLevel(components), render ?? bitmap, 0, components);
    }

    /// <summary>Judge without the clipped assertion — for checks whose stated condition is
    /// ink at the viewport edge itself (viewport-bottom).</summary>
    private static WptRunner.RunResult JudgeLenient(SkiaSharp.SKBitmap bitmap, VisualCheck check)
    {
        var components = VisualChecks.Components(bitmap, 0, out _);
        return VisualChecks.Judge(check, VisualChecks.TopLevel(components), bitmap, 0, components);
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

    // --- Probe batch 2 (2 November): judges for the next wave of formerly-unsupported cases. ---

    [Test]
    public static void VisualChecks_ShadeAndColouredSizeChecks()
    {
        // "the box below is a shade or shades of green": hue-preserving greens pass, blue fails.
        var shades = Render(c => c.DrawRect(new SkiaSharp.SKRect(20, 30, 120, 130), Fill(new SkiaSharp.SKColor(0, 0x90, 0x20))));
        True(Judge(shades, new VisualCheck("one-shape", "box", "green", Shades: true), shades).Passed);
        True(!Judge(shades, new VisualCheck("one-shape", "box", "blue", Shades: true), shades).Passed);
        True(!Judge(shades, new VisualCheck("one-shape", "box", "green"), shades).Passed);
        // "the blue box is the same size as the orange box".
        var sizes = Render(c =>
        {
            c.DrawRect(new SkiaSharp.SKRect(10, 30, 110, 100), Fill(SkiaSharp.SKColors.Blue));
            c.DrawRect(new SkiaSharp.SKRect(10, 130, 110, 200), Fill(SkiaSharp.SKColors.Orange));
        });
        True(Judge(sizes, new VisualCheck("same-dimension", Dimension: "size", Count: 2, ColorA: "blue", ColorB: "orange"), sizes).Passed);
        var wrong = Render(c =>
        {
            c.DrawRect(new SkiaSharp.SKRect(10, 30, 120, 100), Fill(SkiaSharp.SKColors.Blue));
            c.DrawRect(new SkiaSharp.SKRect(10, 130, 110, 200), Fill(SkiaSharp.SKColors.Orange));
        });
        True(!Judge(wrong, new VisualCheck("same-dimension", Dimension: "size", Count: 2, ColorA: "blue", ColorB: "orange"), wrong).Passed);
    }

    [Test]
    public static void VisualChecks_StackedPaintOrderReadsFromTheOverlap()
    {
        // Blue bottom-left, orange middle, yellow top-right: at each overlap the higher box wins.
        var stacked = Render(c =>
        {
            c.DrawRect(new SkiaSharp.SKRect(0, 0, 96, 96), Fill(SkiaSharp.SKColors.Blue));
            c.DrawRect(new SkiaSharp.SKRect(30, 30, 126, 126), Fill(SkiaSharp.SKColors.Orange));
            c.DrawRect(new SkiaSharp.SKRect(60, 60, 156, 156), Fill(SkiaSharp.SKColors.Yellow));
        });
        True(Judge(stacked, new VisualCheck("stack-order", Colors: ["blue", "orange", "yellow"]), stacked).Passed);
        True(!Judge(stacked, new VisualCheck("stack-order", Colors: ["yellow", "orange", "blue"]), stacked).Passed);
    }

    [Test]
    public static void VisualChecks_RelationsForSpacingAndCorners()
    {
        var blue = SkiaSharp.SKColors.Blue;
        var orange = SkiaSharp.SKColors.Orange;
        var apart = Render(c =>
        {
            c.DrawRect(new SkiaSharp.SKRect(20, 20, 120, 80), Fill(blue));
            c.DrawRect(new SkiaSharp.SKRect(20, 120, 120, 180), Fill(orange));
        });
        True(Judge(apart, new VisualCheck("shape-relation", Relation: "not-touching", ColorA: "blue", ColorB: "orange"), apart).Passed, "not-touching");
        True(!Judge(apart, new VisualCheck("shape-relation", Relation: "touching", ColorA: "blue", ColorB: "orange"), apart).Passed, "touching-neg");
        True(Judge(apart, new VisualCheck("shape-relation", Relation: "vertical-gap", ColorA: "blue", ColorB: "orange"), apart).Passed, "v-gap");
        var adjacent = Render(c =>
        {
            c.DrawRect(new SkiaSharp.SKRect(20, 20, 120, 80), Fill(blue));
            c.DrawRect(new SkiaSharp.SKRect(120, 20, 220, 80), Fill(orange));
        });
        True(Judge(adjacent, new VisualCheck("shape-relation", Relation: "touching", ColorA: "blue", ColorB: "orange"), adjacent).Passed, "touching");
        True(!Judge(adjacent, new VisualCheck("shape-relation", Relation: "not-touching", ColorA: "blue", ColorB: "orange"), adjacent).Passed, "not-touching-neg");
        True(Judge(adjacent, new VisualCheck("shape-relation", Relation: "right-of", ColorA: "orange", ColorB: "blue"), adjacent).Passed, "right-of");
        True(!Judge(adjacent, new VisualCheck("shape-relation", Relation: "right-of", ColorA: "blue", ColorB: "orange"), adjacent).Passed, "right-of-neg");
        // Half-height: the blue and purple boxes are half the black box's height.
        var halves = Render(c =>
        {
            c.DrawRect(new SkiaSharp.SKRect(10, 10, 210, 170), Fill(SkiaSharp.SKColors.Black));
            c.DrawRect(new SkiaSharp.SKRect(220, 10, 320, 90), Fill(blue));
            c.DrawRect(new SkiaSharp.SKRect(330, 10, 395, 90), Fill(SkiaSharp.SKColors.Purple));
        });
        True(Judge(halves, new VisualCheck("shape-relation", Relation: "half-height", ColorA: "blue", ColorB: "purple", ColorC: "black"), halves).Passed, "half");
        True(!Judge(halves, new VisualCheck("shape-relation", Relation: "half-height", ColorA: "blue", ColorB: "purple", ColorC: "blue"), halves).Passed, "half-neg");
        // A filled square flush in the lower-right inner corner of a bordered box.
        var corner = Render(c =>
        {
            c.DrawRect(new SkiaSharp.SKRect(10, 10, 210, 160), Fill(SkiaSharp.SKColors.Black));
            c.DrawRect(new SkiaSharp.SKRect(180, 130, 210, 160), Fill(blue));
        });
        True(Judge(corner, new VisualCheck("shape-relation", Relation: "corner-lower-right", ColorA: "blue", ColorB: "black"), corner).Passed, "corner-lr");
        True(!Judge(corner, new VisualCheck("shape-relation", Relation: "corner-upper-left", ColorA: "blue", ColorB: "black"), corner).Passed, "corner-ul-neg");
    }

    [Test]
    public static void VisualChecks_BoxCountsAndThreeBars()
    {
        var black = SkiaSharp.SKColors.Black;
        var two = Render(c =>
        {
            c.DrawRect(new SkiaSharp.SKRect(10, 10, 110, 60), Fill(black));
            c.DrawRect(new SkiaSharp.SKRect(10, 80, 110, 130), Fill(black));
        });
        True(Judge(two, new VisualCheck("box-count", Count: 2), two).Passed);
        True(!Judge(two, new VisualCheck("box-count", Count: 3), two).Passed);
        var bars = Render(c =>
        {
            c.DrawRect(new SkiaSharp.SKRect(10, 10, 110, 30), Fill(SkiaSharp.SKColors.Green));
            c.DrawRect(new SkiaSharp.SKRect(10, 40, 110, 60), Fill(SkiaSharp.SKColors.Yellow));
            c.DrawRect(new SkiaSharp.SKRect(10, 70, 110, 90), Fill(SkiaSharp.SKColors.Green));
        });
        True(Judge(bars, new VisualCheck("three-bars", ColorA: "green", ColorB: "yellow"), bars).Passed);
        var uneven = Render(c => c.DrawRect(new SkiaSharp.SKRect(10, 40, 90, 60), Fill(SkiaSharp.SKColors.Yellow)));
        True(!Judge(uneven, new VisualCheck("three-bars", ColorA: "green", ColorB: "yellow"), uneven).Passed);
        var stack = Render(c =>
        {
            c.DrawRect(new SkiaSharp.SKRect(10, 10, 110, 50), Fill(SkiaSharp.SKColors.Green));
            c.DrawRect(new SkiaSharp.SKRect(10, 50, 110, 90), Fill(SkiaSharp.SKColors.Blue));
        });
        True(Judge(stack, new VisualCheck("bar-stack", ColorA: "green", ColorB: "blue"), stack).Passed);
        True(!Judge(stack, new VisualCheck("bar-stack", ColorA: "blue", ColorB: "green"), stack).Passed);
        var even = Render(c =>
        {
            c.DrawRect(new SkiaSharp.SKRect(10, 10, 110, 40), Fill(black));
            c.DrawRect(new SkiaSharp.SKRect(10, 60, 110, 90), Fill(black));
            c.DrawRect(new SkiaSharp.SKRect(10, 110, 110, 140), Fill(black));
        });
        True(Judge(even, new VisualCheck("stack-three", Even: true), even).Passed);
        var lopsided = Render(c =>
        {
            c.DrawRect(new SkiaSharp.SKRect(10, 10, 110, 40), Fill(black));
            c.DrawRect(new SkiaSharp.SKRect(10, 45, 110, 75), Fill(black));
            c.DrawRect(new SkiaSharp.SKRect(10, 110, 110, 140), Fill(black));
        });
        True(!Judge(lopsided, new VisualCheck("stack-three", Even: true), lopsided).Passed);
    }

    [Test]
    public static void VisualChecks_BarsMatchBoxesByColourAndHeight()
    {
        var yellow = SkiaSharp.SKColors.Yellow;
        var orange = SkiaSharp.SKColors.Orange;
        var matched = Render(c =>
        {
            c.DrawRect(new SkiaSharp.SKRect(2, 10, 12, 60), Fill(yellow));   // thin bar, 50 tall
            c.DrawRect(new SkiaSharp.SKRect(40, 10, 190, 60), Fill(yellow)); // matching box
            c.DrawRect(new SkiaSharp.SKRect(2, 110, 7, 160), Fill(orange));  // second bar
            c.DrawRect(new SkiaSharp.SKRect(40, 110, 190, 160), Fill(orange));
        });
        True(Judge(matched, new VisualCheck("bars-match"), matched).Passed);
        var unmatched = Render(c =>
        {
            c.DrawRect(new SkiaSharp.SKRect(2, 10, 12, 60), Fill(yellow));
            c.DrawRect(new SkiaSharp.SKRect(40, 10, 190, 80), Fill(yellow));
        });
        True(!Judge(unmatched, new VisualCheck("bars-match"), unmatched).Passed);
    }

    [Test]
    public static void VisualChecks_BarGridAndLinesSpaced()
    {
        var black = SkiaSharp.SKColors.Black;
        var grid = Render(c =>
        {
            c.DrawRect(new SkiaSharp.SKRect(10, 10, 130, 30), Fill(black));
            for (var r = 0; r < 3; r++)
            for (var col = 0; col < 3; col++)
                c.DrawRect(new SkiaSharp.SKRect(10 + col * 40, 40 + r * 40, 46 + col * 40, 76 + r * 40), Fill(black));
        });
        True(Judge(grid, new VisualCheck("bar-grid", Rows: 3, Cols: 3), grid).Passed, "bg-3x3");
        True(!Judge(grid, new VisualCheck("bar-grid", Rows: 2, Cols: 2), grid).Passed, "bg-2x2");
        var spaced = Render(c =>
        {
            c.DrawRect(new SkiaSharp.SKRect(10, 10, 15, 100), Fill(SkiaSharp.SKColors.Blue));
            c.DrawRect(new SkiaSharp.SKRect(65, 10, 70, 100), Fill(SkiaSharp.SKColors.Orange));
        });
        True(Judge(spaced, new VisualCheck("lines-spaced", ColorA: "blue", ColorB: "orange"), spaced).Passed, "ls-pass");
        True(!Judge(spaced, new VisualCheck("lines-spaced", ColorA: "blue", ColorB: "green"), spaced).Passed, "ls-color");
        var adjacent = Render(c =>
        {
            c.DrawRect(new SkiaSharp.SKRect(10, 10, 15, 100), Fill(SkiaSharp.SKColors.Blue));
            c.DrawRect(new SkiaSharp.SKRect(15, 10, 20, 100), Fill(SkiaSharp.SKColors.Orange));
        });
        True(!Judge(adjacent, new VisualCheck("lines-spaced", ColorA: "blue", ColorB: "orange"), adjacent).Passed, "ls-adjacent");
    }

    [Test]
    public static void VisualChecks_LinesSameComparesInkBands()
    {
        var pair = Render(c =>
        {
            c.DrawRect(new SkiaSharp.SKRect(20, 30, 80, 50), Fill(SkiaSharp.SKColors.Black));
            c.DrawRect(new SkiaSharp.SKRect(90, 30, 100, 50), Fill(SkiaSharp.SKColors.Black));
            c.DrawRect(new SkiaSharp.SKRect(20, 90, 80, 110), Fill(SkiaSharp.SKColors.Black));
            c.DrawRect(new SkiaSharp.SKRect(90, 90, 100, 110), Fill(SkiaSharp.SKColors.Black));
        });
        True(Judge(pair, new VisualCheck("lines-same", Count: 2), pair).Passed);
        var different = Render(c =>
        {
            c.DrawRect(new SkiaSharp.SKRect(20, 30, 80, 50), Fill(SkiaSharp.SKColors.Black));
            c.DrawRect(new SkiaSharp.SKRect(20, 90, 70, 110), Fill(SkiaSharp.SKColors.Black));
        });
        True(!Judge(different, new VisualCheck("lines-same", Count: 2), different).Passed);
        var three = Render(c =>
        {
            foreach (var y in new[] { 30, 90, 150 })
                c.DrawRect(new SkiaSharp.SKRect(20, y, 80, y + 20), Fill(SkiaSharp.SKColors.Black));
        });
        True(Judge(three, new VisualCheck("lines-same", Count: 3), three).Passed);
        True(!Judge(three, new VisualCheck("lines-same", Count: 2), three).Passed);
    }

    [Test]
    public static void VisualChecks_FirstLineColorJustifyAndViewportBottom()
    {
        var first = Render(c =>
        {
            c.DrawRect(new SkiaSharp.SKRect(20, 30, 100, 50), Fill(SkiaSharp.SKColors.Green));
            c.DrawRect(new SkiaSharp.SKRect(20, 90, 100, 110), Fill(SkiaSharp.SKColors.Black));
        });
        True(Judge(first, new VisualCheck("first-line-color", Color: "green"), first).Passed);
        True(!Judge(first, new VisualCheck("first-line-color", Color: "black"), first).Passed);
        var justified = Render(c =>
        {
            c.DrawRect(new SkiaSharp.SKRect(20, 30, 180, 50), Fill(SkiaSharp.SKColors.Black));
            c.DrawRect(new SkiaSharp.SKRect(20, 90, 180, 110), Fill(SkiaSharp.SKColors.Black));
            c.DrawRect(new SkiaSharp.SKRect(20, 150, 100, 170), Fill(SkiaSharp.SKColors.Black));
        });
        True(Judge(justified, new VisualCheck("justify"), justified).Passed);
        var ragged = Render(c =>
        {
            c.DrawRect(new SkiaSharp.SKRect(20, 30, 170, 50), Fill(SkiaSharp.SKColors.Black));
            c.DrawRect(new SkiaSharp.SKRect(20, 90, 180, 110), Fill(SkiaSharp.SKColors.Black));
        });
        True(!Judge(ragged, new VisualCheck("justify"), ragged).Passed);
        var bottom = Render(c => c.DrawRect(new SkiaSharp.SKRect(20, 240, 300, 300), Fill(SkiaSharp.SKColors.Blue)));
        True(JudgeLenient(bottom, new VisualCheck("viewport-bottom", Color: "blue")).Passed);
        True(!JudgeLenient(bottom, new VisualCheck("viewport-bottom", Color: "orange")).Passed);
    }

    [Test]
    public static void VisualChecks_BorderSidesReadPerSideStylesAndColours()
    {
        // Four solid sides, each a different colour: the "distinct" mode passes.
        var distinct = Render(c =>
        {
            c.DrawRect(new SkiaSharp.SKRect(10, 10, 210, 18), Fill(SkiaSharp.SKColors.Blue));    // top
            c.DrawRect(new SkiaSharp.SKRect(10, 162, 210, 170), Fill(SkiaSharp.SKColors.Orange)); // bottom
            c.DrawRect(new SkiaSharp.SKRect(10, 10, 18, 170), Fill(SkiaSharp.SKColors.Green));   // left
            c.DrawRect(new SkiaSharp.SKRect(202, 10, 210, 170), Fill(SkiaSharp.SKColors.Purple)); // right
        });
        True(Judge(distinct, new VisualCheck("border-sides", Mode: "distinct"), distinct).Passed);
        var uniform = Render(c =>
        {
            c.DrawRect(new SkiaSharp.SKRect(10, 10, 210, 18), Fill(SkiaSharp.SKColors.Blue));
            c.DrawRect(new SkiaSharp.SKRect(10, 162, 210, 170), Fill(SkiaSharp.SKColors.Blue));
            c.DrawRect(new SkiaSharp.SKRect(10, 10, 18, 170), Fill(SkiaSharp.SKColors.Blue));
            c.DrawRect(new SkiaSharp.SKRect(202, 10, 210, 170), Fill(SkiaSharp.SKColors.Blue));
        });
        True(!Judge(uniform, new VisualCheck("border-sides", Mode: "distinct"), uniform).Passed);
        True(Judge(uniform, new VisualCheck("border-sides", Sides: new Dictionary<string, SideSpec>
        {
            ["top"] = new("solid", "blue"), ["bottom"] = new("solid", "blue"),
            ["left"] = new("solid", "blue"), ["right"] = new("solid", "blue"),
        }), uniform).Passed);
        True(!Judge(uniform, new VisualCheck("border-sides", Sides: new Dictionary<string, SideSpec>
        {
            ["top"] = new("solid", "blue"), ["bottom"] = new("solid", "orange"),
            ["left"] = new("solid", "blue"), ["right"] = new("solid", "blue"),
        }), uniform).Passed);
        // One side missing entirely: not readable as four different edges.
        var threeSides = Render(c =>
        {
            c.DrawRect(new SkiaSharp.SKRect(10, 10, 210, 18), Fill(SkiaSharp.SKColors.Blue));
            c.DrawRect(new SkiaSharp.SKRect(10, 162, 210, 170), Fill(SkiaSharp.SKColors.Orange));
            c.DrawRect(new SkiaSharp.SKRect(10, 10, 18, 170), Fill(SkiaSharp.SKColors.Green));
        });
        True(!Judge(threeSides, new VisualCheck("border-sides", Mode: "distinct"), threeSides).Passed);
    }

    [Test]
    public static void VisualChecks_CornersNeedFourSquaresOnAllCorners()
    {
        var corner = Render(c =>
        {
            c.DrawRect(new SkiaSharp.SKRect(40, 40, 240, 240), Fill(SkiaSharp.SKColors.Yellow));
            foreach (var (x, y) in new[] { (30, 30), (230, 30), (30, 230), (230, 230) })
                c.DrawRect(new SkiaSharp.SKRect(x, y, x + 20, y + 20), Fill(SkiaSharp.SKColors.Blue));
        });
        True(Judge(corner, new VisualCheck("corners", Color: "blue", BoxColor: "yellow"), corner).Passed);
        var missing = Render(c =>
        {
            c.DrawRect(new SkiaSharp.SKRect(40, 40, 240, 240), Fill(SkiaSharp.SKColors.Yellow));
            foreach (var (x, y) in new[] { (30, 30), (230, 30), (30, 230) })
                c.DrawRect(new SkiaSharp.SKRect(x, y, x + 20, y + 20), Fill(SkiaSharp.SKColors.Blue));
        });
        True(!Judge(missing, new VisualCheck("corners", Color: "blue", BoxColor: "yellow"), missing).Passed);
    }

    // ------------------------------------------------------------------
    // Probe batch 3: the scroll protocol, the paint-log judges and the new
    // long-tail component judges.
    // ------------------------------------------------------------------

    private static Lite.Models.LayoutNode ScrollBoxNode()
    {
        var style = Parser.ParseFragment("<div></div>")[0].Style;
        var root = new Lite.Models.LayoutNode(null, "HTML", "", style);
        var body = new Lite.Models.LayoutNode(null, "BODY", "", style);
        var box = new Lite.Models.LayoutNode(null, "DIV", "", style);
        root.Children.Add(body);
        body.Children.Add(box);
        var dimensions = box.Box;
        dimensions.ContentBox = new SkiaSharp.SKRect(100, 100, 300, 300);
        box.Box = dimensions;
        box.ScrollState = new ElementScrollState { ContentHeight = 400, ContainerHeight = 200 };
        return root;
    }

    /// <summary>A cat line-art tile: black strokes on a white body — the corpus's image ink
    /// (the tile's white body is part of what the two-tone detector reads against a colored
    /// box background).</summary>
    private static void DrawCatTiles(SkiaSharp.SKCanvas canvas, int fromY, int toY)
    {
        using var black = Fill(SkiaSharp.SKColors.Black);
        using var white = Fill(SkiaSharp.SKColors.White);
        for (var y = fromY; y < toY; y += 99)
            for (var x = 20; x < 380; x += 98)
            {
                canvas.DrawRect(new SkiaSharp.SKRect(x, y, x + 98, y + 99), white);           // tile body
                canvas.DrawRect(new SkiaSharp.SKRect(x, y, x + 98, y + 8), black);            // ears band
                canvas.DrawRect(new SkiaSharp.SKRect(x, y + 40, x + 98, y + 44), black);      // whiskers band
                canvas.DrawRect(new SkiaSharp.SKRect(x + 10, y + 80, x + 88, y + 84), black); // paws band
            }
    }

    [Test]
    public static void VisualChecks_ScrollJudgesDecideTheCatCorpus()
    {
        // scroll-cats-bottom: the bottom band must gain image ink after scrolling. The box's
        // own background is green — the cat tile is black line art on white, which only reads
        // as image ink against a non-white background (exactly the corpus's construction).
        var root = ScrollBoxNode();
        var before = Render(c => c.DrawRect(new SkiaSharp.SKRect(100, 100, 300, 300), Fill(SkiaSharp.SKColors.Green)));
        var after = Render(c =>
        {
            c.DrawRect(new SkiaSharp.SKRect(100, 100, 300, 300), Fill(SkiaSharp.SKColors.Green));
            DrawCatTiles(c, 230, 300);
        });
        var scrolled = VisualChecks.ApplyScroll(new ScrollSpec("bottom"), root);
        True(scrolled is not null && scrolled.ScrollState!.ScrollY == 200);
        var bottom = VisualChecks.Evaluate(new VisualCheck("scroll-cats-bottom", Scroll: new ScrollSpec("bottom")),
            new RenderSet(after, null, before), root, "x", "p");
        True(bottom.Passed);
        var beforeToo = VisualChecks.Evaluate(new VisualCheck("scroll-cats-bottom", Scroll: new ScrollSpec("bottom")),
            new RenderSet(after, null, after), root, "x", "p");
        True(!beforeToo.Passed);  // ink already present before scrolling fails the "appears" read

        // scroll-cats-moved: ink before and after, and the renders differ.
        var movedBefore = Render(c =>
        {
            c.DrawRect(new SkiaSharp.SKRect(100, 100, 300, 300), Fill(SkiaSharp.SKColors.Green));
            c.Save();
            c.ClipRect(new SkiaSharp.SKRect(100, 100, 300, 300));
            DrawCatTiles(c, 100, 300);
            c.Restore();
        });
        var movedAfter = Render(c =>
        {
            c.DrawRect(new SkiaSharp.SKRect(100, 100, 300, 300), Fill(SkiaSharp.SKColors.Green));
            c.Save();
            c.ClipRect(new SkiaSharp.SKRect(100, 100, 300, 300));
            c.Translate(0, -50);
            DrawCatTiles(c, 100, 350);
            c.Restore();
        });
        var moved = VisualChecks.Evaluate(new VisualCheck("scroll-cats-moved", Scroll: new ScrollSpec("bottom")),
            new RenderSet(movedAfter, null, movedBefore), root, "x", "p");
        True(moved.Passed);
        var unmoved = VisualChecks.Evaluate(new VisualCheck("scroll-cats-moved", Scroll: new ScrollSpec("bottom")),
            new RenderSet(movedBefore, null, movedBefore), root, "x", "p");
        True(!unmoved.Passed);

        // scroll-cats-still: fixed attachment — ink present and the render unchanged.
        var still = VisualChecks.Evaluate(new VisualCheck("scroll-cats-still", Scroll: new ScrollSpec("bottom")),
            new RenderSet(movedBefore, null, movedBefore), root, "x", "p");
        True(still.Passed);
    }

    [Test]
    public static void VisualChecks_PaintedLinesReadBandsTopToBottomFragmentsLeftToRight()
    {
        var paint = new List<Lite.Rendering.PaintLog.Entry>
        {
            new("world", 100, 100, "Ahem", 16, "Left", false, false),
            new("hello ", 20, 100, "Ahem", 16, "Left", false, false),
            new("second", 20, 200, "Ahem", 16, "Left", false, false),
        };
        var lines = VisualChecks.PaintedLines(paint);
        True(lines.Count == 2);
        True(lines[0] == "hello world");
        True(lines[1] == "second");
    }

    [Test]
    public static void VisualChecks_PaintedFontReadsSizeStyleAndPitch()
    {
        var paint = new List<Lite.Rendering.PaintLog.Entry>
        {
            new("dummy", 20, 100, "CSSTest", 18, "Left", true, true),
            new("dummy", 20, 127, "CSSTest", 18, "Left", true, true),
            new("dummy", 20, 154, "CSSTest", 18, "Left", true, true),
        };
        const string sentence = "This text should be 18px. dummy text dummy text dummy text";
        var pass = VisualChecks.Evaluate(new VisualCheck("painted-font", Count: 18, Mode: "italic", Edge: "27"),
            new RenderSet(Render(_ => { }), null, null, paint), ScrollRootWithInstructions(), sentence, "p");
        True(pass.Passed);
        var wrongSize = VisualChecks.Evaluate(new VisualCheck("painted-font", Count: 13),
            new RenderSet(Render(_ => { }), null, null, paint), ScrollRootWithInstructions(), sentence, "p");
        True(!wrongSize.Passed);
        var wrongPitch = VisualChecks.Evaluate(new VisualCheck("painted-font", Edge: "36"),
            new RenderSet(Render(_ => { }), null, null, paint), ScrollRootWithInstructions(), sentence, "p");
        True(!wrongPitch.Passed);
        var wrongStyle = VisualChecks.Evaluate(new VisualCheck("painted-font", Mode: "normal"),
            new RenderSet(Render(_ => { }), null, null, paint), ScrollRootWithInstructions(), sentence, "p");
        True(!wrongStyle.Passed);
    }

    /// <summary>A root whose first paragraph is the instruction text, so the layout-anchored
    /// judges find the region top (the paint-log judges only read entries below it).</summary>
    private static Lite.Models.LayoutNode ScrollRootWithInstructions()
    {
        var root = ScrollBoxNode();
        var p = new Lite.Models.LayoutNode(null, "P", "This text should be 18px. dummy text dummy text dummy text", root.Style);
        p.Box = new BoxDimensions { ContentBox = new SkiaSharp.SKRect(0, 0, 400, 20) };
        root.Children[0].Children.Add(p);
        return root;
    }

    [Test]
    public static void VisualChecks_SlantPairAndLineBelowReadShapeGeometry()
    {
        var slant = Render(c =>
        {
            using var path = new SkiaSharp.SKPath();
            path.MoveTo(20, 30); path.LineTo(120, 30); path.LineTo(110, 80); path.LineTo(10, 80); path.Close();
            c.DrawPath(path, Fill(SkiaSharp.SKColors.Blue));
            c.DrawRect(new SkiaSharp.SKRect(140, 30, 240, 80), Fill(SkiaSharp.SKColors.Blue));
        });
        True(Judge(slant, new VisualCheck("slant-pair")).Passed);
        True(!Judge(Render(c => c.DrawRect(new SkiaSharp.SKRect(20, 30, 120, 80), Fill(SkiaSharp.SKColors.Blue))),
            new VisualCheck("slant-pair")).Passed);

        var lineBelow = Render(c =>
        {
            c.DrawRect(new SkiaSharp.SKRect(40, 40, 160, 140), Fill(SkiaSharp.SKColors.Orange));
            c.DrawRect(new SkiaSharp.SKRect(40, 145, 160, 149), Fill(SkiaSharp.SKColors.Green));
        });
        True(Judge(lineBelow, new VisualCheck("line-below", ColorA: "orange", ColorB: "green")).Passed);
        True(!Judge(lineBelow, new VisualCheck("line-below", ColorA: "orange", ColorB: "blue")).Passed);
    }

    [Test]
    public static void VisualChecks_GradientLighterReadsIncreasingLightness()
    {
        var ramp = Render(c =>
        {
            byte[] tones = [40, 90, 140];
            for (var i = 0; i < 3; i++)
                c.DrawRect(new SkiaSharp.SKRect(20 + i * 60, 30, 70 + i * 60, 90), Fill(new SkiaSharp.SKColor(0, 0, tones[i])));
        });
        True(Judge(ramp, new VisualCheck("gradient-lighter", Color: "blue")).Passed);
        var inverted = Render(c =>
        {
            byte[] tones = [140, 90, 40];
            for (var i = 0; i < 3; i++)
                c.DrawRect(new SkiaSharp.SKRect(20 + i * 60, 30, 70 + i * 60, 90), Fill(new SkiaSharp.SKColor(0, 0, tones[i])));
        });
        True(!Judge(inverted, new VisualCheck("gradient-lighter", Color: "blue")).Passed);
    }

    [Test]
    public static void VisualChecks_SpansStackRequiresCoverageOfTheWholeStack()
    {
        var spans = Render(c =>
        {
            c.DrawRect(new SkiaSharp.SKRect(20, 30, 40, 230), Fill(SkiaSharp.SKColors.Blue));
            foreach (var y in new[] { 30, 110, 190 })
                c.DrawRect(new SkiaSharp.SKRect(120, y, 180, y + 40), Fill(SkiaSharp.SKColors.Orange));
        });
        True(Judge(spans, new VisualCheck("spans-stack", ColorA: "blue", ColorB: "orange")).Passed);
        var shortSpan = Render(c =>
        {
            c.DrawRect(new SkiaSharp.SKRect(20, 30, 40, 120), Fill(SkiaSharp.SKColors.Blue));
            foreach (var y in new[] { 30, 110, 190 })
                c.DrawRect(new SkiaSharp.SKRect(120, y, 180, y + 40), Fill(SkiaSharp.SKColors.Orange));
        });
        True(!Judge(shortSpan, new VisualCheck("spans-stack", ColorA: "blue", ColorB: "orange")).Passed);
    }

    [Test]
    public static void VisualChecks_InsetTopSidesReadsTheInsetFamily()
    {
        var inset = Render(c =>
        {
            c.DrawRect(new SkiaSharp.SKRect(20, 20, 300, 200), Fill(SkiaSharp.SKColors.Blue));
            c.DrawRect(new SkiaSharp.SKRect(24, 24, 150, 60), Fill(SkiaSharp.SKColors.Teal));
            c.DrawRect(new SkiaSharp.SKRect(154, 24, 296, 60), Fill(SkiaSharp.SKColors.Teal));
        });
        True(Judge(inset, new VisualCheck("shape-relation", Relation: "inset-top-sides", ColorA: "teal", ColorB: "blue")).Passed);
        var crossing = Render(c =>
        {
            c.DrawRect(new SkiaSharp.SKRect(20, 20, 300, 200), Fill(SkiaSharp.SKColors.Blue));
            c.DrawRect(new SkiaSharp.SKRect(10, 24, 150, 60), Fill(SkiaSharp.SKColors.Teal));
        });
        True(!Judge(crossing, new VisualCheck("shape-relation", Relation: "inset-top-sides", ColorA: "teal", ColorB: "blue")).Passed);
    }

    [Test]
    public static void VisualChecks_HeightBandRelaxesSimilarityToAFactorOfTwo()
    {
        var band = Render(c =>
        {
            c.DrawRect(new SkiaSharp.SKRect(20, 30, 120, 80), Fill(SkiaSharp.SKColors.Black));
            c.DrawRect(new SkiaSharp.SKRect(140, 30, 200, 110), Fill(SkiaSharp.SKColors.Blue));
        });
        True(Judge(band, new VisualCheck("shape-relation", Relation: "height-band", ColorA: "blue", ColorB: "black")).Passed);
        var far = Render(c =>
        {
            c.DrawRect(new SkiaSharp.SKRect(20, 30, 120, 80), Fill(SkiaSharp.SKColors.Black));
            c.DrawRect(new SkiaSharp.SKRect(140, 30, 200, 200), Fill(SkiaSharp.SKColors.Blue));
        });
        True(!Judge(far, new VisualCheck("shape-relation", Relation: "height-band", ColorA: "blue", ColorB: "black")).Passed);
    }

    [Test]
    public static void ScrollState_ClampsHorizontalScroll()
    {
        var ss = new ElementScrollState { ContentHeight = 400, ContainerHeight = 200, ContentWidth = 600, ContainerWidth = 200 };
        ss.ScrollXTo(1000);
        True(ss.ScrollX == 400);
        ss.ScrollXTo(-5);
        True(ss.ScrollX == 0);
        True(ss.NeedsHorizontalScrollbar && !new ElementScrollState().NeedsHorizontalScrollbar);
    }

    [Test]
    public static void SelfTestOracle_ManualPendingIsASupportedVerdictRoute()
    {
        const string path = "css21-official/html4/example.htm";
        var scanned = new WptRunner.RunResult(WptRunner.Cat.Pass, "Self-describing test shows no failure red (diagnostic)", 1, 0,
            [new("Self-describing test shows no failure red (diagnostic)", 0, null)], 0);
        var manual = SelfTestOracle.Decide(path, "manual", scanned, null, _ => null, manualPending: true);
        True(manual.Cat == WptRunner.Cat.ManualPending);
        True(!manual.Passed);
        // Unreviewed manual stays unsupported, never a silent pass.
        True(SelfTestOracle.Decide(path, "manual", scanned, null, _ => null).Cat == WptRunner.Cat.Unsupported);
    }

    [Test]
    public static void PassConditions_EveryCheckKindHasJudgeAndEveryCaseHasARoute()
    {
        var conditions = JsonNode.Parse(File.ReadAllText(ConformancePaths.Manifest("Css21/css21-pass-conditions.json")))!;
        var applicability = Css21Inventory.Read(Css21Inventory.ApplicabilityFile);
        var reviews = applicability["tests"]!.AsArray().OfType<JsonObject>()
            .ToDictionary(t => Css21Inventory.Text(t, "path"));
        bool ScreenRunnable(string path)
        {
            if (!reviews.TryGetValue(path, out var review)) return true;
            var classification = Css21Inventory.Text(review, "classification");
            return classification == "unreviewed" ||
                classification == "applicable" && Css21Inventory.Strings(review, "media").Contains("screen");
        }
        var known = new HashSet<string>(StringComparer.Ordinal);
        foreach (var kind in new[] { "one-shape", "same-dimension", "bar-gap", "line-style", "look-same", "solid-line",
                     "border-ring", "border-sample", "text-color", "text-color-pairs", "text-decoration", "align-right",
                     "shape-relation", "space-between", "stack-three", "two-size", "line-pair", "marker-bullet",
                     "round-dot", "double-line", "slanted-box", "line-marker", "bullet-beside", "bar-stack", "bars-match",
                     "bar-grid", "viewport-bottom", "cell-border", "corners", "box-count", "three-bars", "lines-same",
                     "first-line-color", "justify", "stack-order", "border-sides", "lines-spaced", "letter-x",
                     "outline-text", "text-baseline", "one-line", "first-line-marker", "numbers-present", "text-ink",
                     "words-and-box", "align-center", "image-tiled", "first-letter-color", "edge-letter", "text-band",
                     "no-ring", "text-below-box", "text-marker-match", "text-marker", "painted-text", "painted-order",
                     "painted-font", "painted-only", "painted-sequence", "striped-fill", "same-alignment", "page-background",
                     "presence", "cats-on-bar", "middle-cell-color", "gradient-lighter", "spans-stack", "text-in-corner",
                     "text-in-box", "box-beside-text", "hanging-indent", "no-scrollbar", "scrollbar-visible",
                     "same-width-corner", "slant-pair", "line-below", "hollow-circle", "tiny-square", "first-letter-color",
                     "scroll-cats-bottom", "scroll-cats-moved", "scroll-cats-bottom-moved", "scroll-cats-top-moved",
                     "scroll-cats-still", "scroll-box-text", "scroll-box-edges-visible", "scroll-no-red",
                     "scroll-page-text", "scroll-page-edges-visible", "scroll-page-always-visible",
                     "scroll-page-stuck-top", "scroll-page-no-red" })
            known.Add(kind);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in conditions["cases"]!.AsArray())
        {
            var path = entry!["path"]!.GetValue<string>();
            True(seen.Add(path), $"duplicate pass-conditions entry {path}");
            var check = entry["check"] as JsonObject;
            var condition = entry["condition"]!.GetValue<string>();
            var reviewed = entry["reviewed"]?.GetValue<bool>() ?? false;
            if (check is not null)
                True(known.Contains(check["kind"]!.GetValue<string>()),
                    $"unknown visual check kind '{check["kind"]}' on {path}");
            // No red-scan-pass can stand without a decided route: a twin, a check, a
            // sufficient scan condition, a reviewed verdict route, or a published
            // applicability exclusion from screen execution (print-only, optional, …).
            var routed = entry["twin"] is not null || check is not null || reviewed ||
                         condition is "no-red" or "same-color" || !ScreenRunnable(path);
            True(routed, $"case without a verdict route: {path} ({condition})");
        }
    }
}
