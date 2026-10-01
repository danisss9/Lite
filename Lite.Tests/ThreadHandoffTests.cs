using Lite.QuickJs;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>
/// Cross-thread handoff of a page engine: a document parsed on a background thread (the
/// navigation path) is committed to the window thread, which then drives and disposes it.
/// Guards stay in place until the transfer is made, so accidental cross-thread use still throws.
/// </summary>
public static class ThreadHandoffTests
{
    private const string Html = "<!doctype html><p id=a onclick='x=1'>hi</p><script>var x = 0;</script>";

    private static Lite.Models.Page ParseOnBackgroundThread()
    {
        Lite.Models.Page? page = null;
        Exception? failure = null;
        using (var done = new ManualResetEventSlim())
        {
            _ = Task.Run(() =>
            {
                try { page = Parser.ParseChildPage(Html, true, "http://handoff.test/", 800, 600); }
                catch (Exception ex) { failure = ex; }
                finally { done.Set(); }
            });
            done.Wait();
        }
        if (failure is not null) throw failure;
        return page!;
    }

    [Test]
    public static void BackgroundParsedPage_CommitsAndDisposesOnWindowThread()
    {
        var engine = ParseOnBackgroundThread().Engine!;

        // FinalizeNavigation adopts the engine parsed by the load task.
        engine.TransferTreeToCurrentThread();
        Equal(0, (int)engine.RawEngine.Evaluate("x").AsNumber()); // usable from the new owner
        engine.Execute("x = 40 + 2;");
        Equal(42, (int)engine.RawEngine.Evaluate("x").AsNumber());
        engine.DrainTasks();
        engine.Dispose(); // freed on the adopting thread
    }

    [Test]
    public static void EngineWithoutTransfer_RejectsCrossThreadAccess()
    {
        var engine = ParseOnBackgroundThread().Engine!;
        try { engine.RawEngine.Evaluate("1"); True(false, "cross-thread use must stay forbidden"); }
        catch (InvalidOperationException) { }
        try { engine.Dispose(); True(false, "cross-thread dispose must stay forbidden"); }
        catch (InvalidOperationException) { }
    }
}
