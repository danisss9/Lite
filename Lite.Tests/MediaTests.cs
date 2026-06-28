using Lite;
using Lite.Models;
using Lite.Scripting;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>
/// Phase D — HTMLMediaElement. The decoder-free <see cref="Lite.Media.SimulatedMediaBackend"/>
/// drives a deterministic timeline so the API surface, attribute reflection, source selection,
/// and event ordering are testable without native codecs.
/// </summary>
public static class MediaTests
{
    // Parses a full top page inline (so the engine + event loop run) and returns its engine.
    private static (JsEngine engine, LayoutNode root) Load(string body)
        => LoadPage("<body>" + body + "</body>");

    private static (JsEngine engine, LayoutNode root) LoadPage(string html)
    {
        var page = Parser.ParseChildPage(html, isSrcdoc: true, "http://test/", 640, 480);
        return (page.Engine, page.Root);
    }

    private static void Pump(JsEngine e)
    {
        for (int i = 0; i < 50; i++) { e.DrainTree(); e.FlushMicrotasksTree(); }
    }

    private static object? Val(JsEngine e, string name) => e.RawEngine.GetValue(name).ToObject();

    [Test]
    public static void CanPlayType_ReportsSupport()
    {
        var (e, _) = Load("<video id='v'></video>");
        e.Execute(@"
            var v = document.getElementById('v');
            globalThis.r1 = v.canPlayType('video/mp4');
            globalThis.r2 = v.canPlayType('video/mp4; codecs=""avc1.42E01E""');
            globalThis.r3 = v.canPlayType('application/x-unknown');");
        Equal("maybe", (string?)Val(e, "r1"));
        Equal("probably", (string?)Val(e, "r2"));
        Equal("", (string?)Val(e, "r3"));
    }

    [Test]
    public static void Media_ReflectsAttributes()
    {
        var (e, _) = Load("<audio id='a' controls loop src='song.mp3'></audio>");
        e.Execute(@"
            var a = document.getElementById('a');
            globalThis.controls = a.controls;
            globalThis.loop = a.loop;
            globalThis.autoplay = a.autoplay;
            globalThis.src = a.src;
            a.autoplay = true;
            globalThis.autoplay2 = a.autoplay;
            a.controls = false;
            globalThis.controls2 = a.controls;");
        Equal(true, (bool)Val(e, "controls")!);
        Equal(true, (bool)Val(e, "loop")!);
        Equal(false, (bool)Val(e, "autoplay")!);
        Equal("song.mp3", (string?)Val(e, "src"));
        Equal(true, (bool)Val(e, "autoplay2")!);
        Equal(false, (bool)Val(e, "controls2")!);
    }

    [Test]
    public static void Media_SourceSelectionPicksFirstPlayable()
    {
        var (e, _) = Load(
            "<video id='v'>" +
            "<source src='bad.xyz' type='application/x-unknown'>" +
            "<source src='good.webm' type='video/webm'>" +
            "<source src='other.mp4' type='video/mp4'>" +
            "</video>");
        e.Execute("globalThis.cs = document.getElementById('v').currentSrc;");
        Equal("good.webm", (string?)Val(e, "cs"));
    }

    [Test]
    public static void Media_PlayFiresEventsInOrderThroughEnded()
    {
        var (e, _) = Load(
            "<video id='v' data-duration='2' src='movie.mp4'></video>" +
            "<script>window.__ev=[];var v=document.getElementById('v');" +
            "['loadedmetadata','canplay','play','playing','timeupdate','pause','ended']" +
            ".forEach(function(t){v.addEventListener(t,function(){window.__ev.push(t);});});" +
            "v.play();</script>");
        Pump(e);

        var ev = ((object[])Val(e, "__ev")!).Select(o => (string)o).ToList();
        var order = string.Join(",", ev);
        int iMeta = ev.IndexOf("loadedmetadata");
        int iPlay = ev.IndexOf("play");
        int iPlaying = ev.IndexOf("playing");
        int iTime = ev.IndexOf("timeupdate");
        int iEnded = ev.IndexOf("ended");
        True(iMeta >= 0 && iPlay >= 0 && iPlaying >= 0 && iTime >= 0 && iEnded >= 0,
            $"all key events fire: {order}");
        True(iMeta < iPlay && iPlay < iPlaying && iPlaying < iTime && iTime < iEnded,
            $"event order loadedmetadata<play<playing<timeupdate<ended: {order}");

        e.Execute(@"var v=document.getElementById('v');
            globalThis.ended=v.ended; globalThis.paused=v.paused;
            globalThis.atEnd = Math.abs(v.currentTime - v.duration) < 0.001;");
        Equal(true, (bool)Val(e, "ended")!);
        Equal(true, (bool)Val(e, "paused")!);
        Equal(true, (bool)Val(e, "atEnd")!);
    }

    [Test]
    public static void Media_PausePreventsEnded()
    {
        var (e, _) = Load(
            "<video id='v' data-duration='100' src='movie.mp4'></video>" +
            "<script>window.__paused=0;var v=document.getElementById('v');" +
            "v.addEventListener('pause',function(){window.__paused++;});" +
            "v.play();</script>");
        // One drain turn: metadata + play/playing + a tick or two, then pause.
        e.DrainTree();
        e.Execute("document.getElementById('v').pause();");
        Pump(e);
        e.Execute("globalThis.p=document.getElementById('v').paused; globalThis.en=document.getElementById('v').ended;");
        Equal(true, (bool)Val(e, "p")!);
        Equal(false, (bool)Val(e, "en")!);
        True((double)System.Convert.ToDouble(Val(e, "__paused")) >= 1, "pause event fired");
    }

    [Test]
    public static void Media_AutoplayStartsPlayback()
    {
        var (e, root) = Load("<video id='v' autoplay data-duration='2' src='m.mp4'></video>");
        Pump(e);
        e.Execute("globalThis.ended = document.getElementById('v').ended;");
        Equal(true, (bool)Val(e, "ended")!);
    }

    [Test]
    public static void Media_VideoRendersBoxAndControls()
    {
        var (_, root) = Load("<video id='v' width='200' height='120' controls src='m.mp4'></video>");
        var viewport = new Lite.Layout.Viewport { ViewportHeight = 400 };
        using var bmp = Lite.Drawer.DrawToBitmap(400, 400, root, viewport);

        var v = FindByTag(root, "VIDEO")!;
        var box = v.Box.ContentBox;
        // The video box (no frame/poster) is dark, not the white page background.
        var mid = bmp.GetPixel((int)box.MidX, (int)box.Top + 10);
        True(mid.Red < 80 && mid.Green < 80 && mid.Blue < 80, $"video box should be dark, got {mid}");
        // The controls bar sits at the bottom and is also dark.
        var bar = bmp.GetPixel((int)box.MidX, (int)box.Bottom - 8);
        True(bar.Red < 80 && bar.Green < 120 && bar.Blue < 160, $"controls bar should be dark, got {bar}");
    }

    private static LayoutNode? FindByTag(LayoutNode node, string tag)
    {
        if (node.TagName == tag) return node;
        foreach (var c in node.Children) { var f = FindByTag(c, tag); if (f is not null) return f; }
        return null;
    }
}
