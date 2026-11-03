using System.Text.Json;
using Lite.Platform;
using Lite.Scripting;

namespace Lite;

/// <summary>
/// Wires a headless <see cref="BrowserWindow"/> to the WPT testdriver vendor script: the page's
/// <c>test_driver_internal</c> calls the <c>__lite_testdriver</c> host function with one JSON
/// operation per gesture, and the bridge drives the window's real input pipeline — the same
/// OnMouseMove/OnMouseDown/OnMouseUp/SendKey/OnChar callbacks the native window hosts translate
/// OS input into, so injected input exercises focus, form controls, activation and event
/// dispatch, not a parallel simulation. Operations: <c>refresh</c> (re-render hit regions after
/// script-driven DOM changes), <c>click {x,y}</c>, <c>pointer {kind,x,y}</c> (move/down/up),
/// <c>key {kind,key}</c> (down/up), <c>char {ch}</c> (text insertion at the focused control).
/// </summary>
internal static class TestDriverBridge
{
    public static void Register(JsEngine engine, BrowserWindow window)
    {
        engine.RawEngine.SetValue("__lite_testdriver", new Action<string>(op => Execute(window, op)));
    }

    private static void Execute(BrowserWindow window, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        switch (root.GetProperty("op").GetString())
        {
            case "refresh":
                window.RefreshFrame();
                break;
            case "click":
            {
                var x = root.GetProperty("x").GetInt32();
                var y = root.GetProperty("y").GetInt32();
                window.OnMouseMove(x, y);
                window.OnMouseDown(x, y);
                window.OnMouseUp(x, y);
                break;
            }
            case "pointer":
                Pointer(window, root);
                break;
            case "key":
                // Non-printable keys still dispatch keydown/keyup at the focused element;
                // printable text insertion is a separate "char" op so key order is explicit.
                window.SendKey(root.GetProperty("key").GetString() ?? "", root.GetProperty("kind").GetString() == "down");
                break;
            case "char":
                window.OnChar((char)root.GetProperty("ch").GetInt32());
                break;
        }
    }

    private static void Pointer(BrowserWindow window, JsonElement root)
    {
        root.TryGetProperty("x", out var xe);
        root.TryGetProperty("y", out var ye);
        var x = xe.ValueKind == JsonValueKind.Number ? xe.GetInt32() : 0;
        var y = ye.ValueKind == JsonValueKind.Number ? ye.GetInt32() : 0;
        switch (root.GetProperty("kind").GetString())
        {
            case "move": window.OnMouseMove(x, y); break;
            case "down": window.OnMouseDown(x, y); break;
            case "up": window.OnMouseUp(x, y); break;
        }
    }
}
