using Lite;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>HTML5 §7.3.3 named access on the Window object and programmatic scrolling of
/// positioned / overflow:hidden scroll containers.</summary>
public static class WindowNamedPropertiesTests
{
    [Test]
    public static void NamedWindowProperties_ResolveSingleIdAndMultipleCollection()
    {
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><body>" +
            "<div id=\"solo\"></div>" +
            "<div id=\"dup\">a</div><div id=\"dup\">b</div>" +
            "<form name=\"myForm\"></form>" +
            "<script>" +
            "window.__soloOk = (solo === document.getElementById('solo'));" +
            "window.__dupLen = dup.length;" +
            "window.__formOk = (myForm.tagName === 'FORM');" +
            "window.__missingThrows = false;" +
            "window.__missingInfo = 'no-throw:' + String(typeof nonexistentName123) + ':in=' + ('nonexistentName123' in window);" +
            "try { var _x = nonexistentName123; } catch (e) { window.__missingThrows = e instanceof ReferenceError; window.__missingInfo = 'threw:' + e; }" +
            "window.__shadowBefore = (solo === document.getElementById('solo'));" +
            "solo = 42;" +
            "window.__shadowAfter = (solo === 42);" +
            "</script></body></html>", isSrcdoc: true, "http://test/", 800, 600);

        True(page.Engine.RawEngine.GetValue("__soloOk").AsBoolean(), "single id resolves to element");
        Equal(2, (int)page.Engine.RawEngine.GetValue("__dupLen").AsNumber());
        True(page.Engine.RawEngine.GetValue("__formOk").AsBoolean(), "named form resolves via name");
        True(page.Engine.RawEngine.GetValue("__missingThrows").AsBoolean(),
            "missing identifier throws ReferenceError (got: " + page.Engine.RawEngine.GetValue("__missingInfo") + ")");
        True(page.Engine.RawEngine.GetValue("__shadowBefore").AsBoolean(), "element resolved before assignment");
        True(page.Engine.RawEngine.GetValue("__shadowAfter").AsBoolean(), "assignment shadows named property");
    }

    [Test]
    public static void OverflowHidden_AbsoluteRelposChild_ScrollTopClampsToOverflow()
    {
        // relpos-percentage-top-in-scrollable: an abspos overflow:hidden container with a
        // position:relative; top:100% child is programmatically scrollable via scrollTop.
        var page = Parser.ParseChildPage(
            "<!DOCTYPE html><html><body>" +
            "<div style=\"position:relative; width:200px; height:200px;\">" +
            "  <div id=\"container\" style=\"overflow:hidden; position:absolute; top:0; left:0; bottom:0; right:0;\">" +
            "    <div style=\"position:relative; top:100%; width:100px; height:100px; background:green;\"></div>" +
            "  </div>" +
            "</div>" +
            "<script>" +
            "container.scrollTop = 123456;" +
            "window.__scrollTop = container.scrollTop;" +
            "</script></body></html>", isSrcdoc: true, "http://test/", 800, 600);

        Equal(100, (int)page.Engine.RawEngine.GetValue("__scrollTop").AsNumber());
    }
}
