using System.Diagnostics;
using System.Runtime.InteropServices;
using Lite.Animation;
using Lite.Interaction;
using Lite.Layout;
using Lite.Models;
using Lite.Models.Delegates;
using Lite.Models.Structs;
using Lite.Scripting;
using Lite.Utils;
using SkiaSharp;

namespace Lite;

public class BrowserWindow
{
    private const string CLASS_NAME = "LiteBrowserWindowClass";
    private const int WS_OVERLAPPEDWINDOW = 0xCF0000;
    private const int SW_SHOW = 5;
    private const int WM_PAINT = 0x000F;
    private const int WM_DESTROY = 0x0002;
    private const int WM_SIZE = 0x0005;
    private const int WM_TIMER = 0x0113;
    private const int WM_CHAR = 0x0102;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_MOUSEMOVE = 0x0200;
    private const int WM_MOUSEWHEEL = 0x020A;
    private const int WM_SETCURSOR = 0x0020;
    private const int HTCLIENT = 1;
    private const int IDC_ARROW = 32512;
    private const int IDC_IBEAM = 32513;
    private const int IDC_HAND = 32649;
    private const uint BI_RGB = 0;
    private const uint DIB_RGB_COLORS = 0;

    private readonly string _url;
    private readonly string _title;
    private readonly int _initialWidth;
    private readonly int _initialHeight;

    private int _width;
    private int _height;
    private LayoutNode? _rootNode;
    private IntPtr _pixels;
    private List<HitRegion> _hitRegions = [];
    private readonly Viewport _viewport = new();
    private WndProcDelegate? _wndProcDelegate;

    // Scrollbar drag state
    private bool _draggingScrollbar;
    private float _scrollbarGrabOffset; // Y offset within thumb where drag started

    // Range slider drag state
    private Guid? _draggingRange;
    private SKRect _draggingRangeRect;
    private float _rangeMin, _rangeMax, _rangeStep;

    // Animation timer
    private const uint AnimationTimerMs = 16; // ~60 fps
    private static readonly IntPtr AnimationTimerId = new(1);
    private bool _timerRunning;

    // Change event: snapshot value on focus for comparison on blur
    private string? _focusedValueSnapshot;

    // RAF timing
    private readonly Stopwatch _rafStopwatch = Stopwatch.StartNew();

    public BrowserWindow(string url, string title = "Lite", int width = 800, int height = 600)
    {
        _url = url;
        _title = title;
        _initialWidth = width;
        _initialHeight = height;
    }

    public void Run()
    {
        AnimationEngine.Reset();
        _rootNode = Parser.TraverseHtml(_url, _initialWidth, _initialHeight);
        AnimationEngine.StartAnimations(_rootNode);

        _wndProcDelegate = WndProc;
        var hInstance = Marshal.GetHINSTANCE(typeof(BrowserWindow).Module);

        var wcex = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf(typeof(WNDCLASSEX)),
            style = 0,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
            cbClsExtra = 0,
            cbWndExtra = 0,
            hInstance = hInstance,
            hIcon = IntPtr.Zero,
            hCursor = IntPtr.Zero,
            hbrBackground = IntPtr.Zero,
            lpszMenuName = null,
            lpszClassName = CLASS_NAME,
            hIconSm = IntPtr.Zero
        };

        var regResult = User32.RegisterClassEx(ref wcex);
        if (regResult == 0)
        {
            Console.WriteLine("Window class registration failed!");
            return;
        }

        var hWnd = User32.CreateWindowEx(
            0,
            CLASS_NAME,
            _title,
            WS_OVERLAPPEDWINDOW,
            100, 100, _initialWidth, _initialHeight,
            IntPtr.Zero,
            IntPtr.Zero,
            hInstance,
            IntPtr.Zero
        );

        if (hWnd == IntPtr.Zero)
        {
            Console.WriteLine("Window creation failed!");
            return;
        }

        User32.ShowWindow(hWnd, SW_SHOW);
        User32.UpdateWindow(hWnd);

        // Kick off the animation timer if the page has CSS animations
        if (_rootNode != null)
        {
            AnimationEngine.Tick(_rootNode); // initial frame
            StartAnimationTimer(hWnd);
        }

        while (User32.GetMessage(out var msg, IntPtr.Zero, 0, 0))
        {
            User32.TranslateMessage(ref msg);
            User32.DispatchMessage(ref msg);
        }
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WM_PAINT:
                var hdc = User32.BeginPaint(hWnd, out var ps);

                var bmi = new BITMAPINFO();
                bmi.bmiHeader.biSize = (uint)Marshal.SizeOf(typeof(BITMAPINFOHEADER));
                bmi.bmiHeader.biWidth = _width;
                bmi.bmiHeader.biHeight = -_height;
                bmi.bmiHeader.biPlanes = 1;
                bmi.bmiHeader.biBitCount = 32;
                bmi.bmiHeader.biCompression = BI_RGB;
                bmi.bmiHeader.biSizeImage = (uint)(_width * _height * 4);
                bmi.bmiHeader.biXPelsPerMeter = 0;
                bmi.bmiHeader.biYPelsPerMeter = 0;
                bmi.bmiHeader.biClrUsed = 0;
                bmi.bmiHeader.biClrImportant = 0;

                Gdi32.SetDIBitsToDevice(
                    hdc,
                    0, 0,
                    (uint)_width, (uint)_height,
                    0, 0,
                    0, (uint)_height,
                    _pixels,
                    ref bmi,
                    DIB_RGB_COLORS);

                User32.EndPaint(hWnd, ref ps);

                return IntPtr.Zero;

            case WM_DESTROY:
                User32.PostQuitMessage(0);
                break;

            case WM_TIMER:
                if (_rootNode != null)
                {
                    var stillRunning = AnimationEngine.Tick(_rootNode);

                    // Flush requestAnimationFrame callbacks
                    var engine = JsEngine.Instance;
                    var rafRan = engine?.FlushRAF(_rafStopwatch.Elapsed.TotalMilliseconds) ?? false;

                    (_pixels, _hitRegions) = Drawer.Draw(_width, _height, _rootNode, _viewport);
                    User32.InvalidateRect(hWnd, IntPtr.Zero, false);
                    if (!stillRunning && !(engine?.HasPendingRAF ?? false))
                        StopAnimationTimer(hWnd);
                }
                break;

            case WM_SIZE:
                User32.GetClientRect(hWnd, out var clientRect);
                _width = clientRect.right - clientRect.left;
                _height = clientRect.bottom - clientRect.top;
                _viewport.ViewportHeight = _height;

                // Update JS window.innerWidth / innerHeight
                JsEngine.Instance?.UpdateViewportSize(_width, _height);

                if (_rootNode != null)
                {
                    // Re-evaluate @media queries against the new viewport size
                    _rootNode.ReapplyMediaStyles(_width, _height);
                    (_pixels, _hitRegions) = Drawer.Draw(_width, _height, _rootNode, _viewport);
                    User32.InvalidateRect(hWnd, IntPtr.Zero, false);
                }

                break;

            case WM_MOUSEWHEEL:
                {
                    var delta = (short)((wParam.ToInt64() >> 16) & 0xFFFF);
                    var scrollAmount = -delta / 120f * 60f;

                    // Try scrolling a per-element scrollable container first
                    User32.GetCursorPos(out var wheelPt);
                    User32.ScreenToClient(hWnd, ref wheelPt);
                    var contentPtY = wheelPt.Y + _viewport.ScrollY;
                    var scrollTarget = _rootNode != null
                        ? FindScrollableAncestor(_rootNode, wheelPt.X, contentPtY)
                        : null;

                    if (scrollTarget?.ScrollState != null)
                    {
                        scrollTarget.ScrollState.ScrollBy(scrollAmount);
                    }
                    else
                    {
                        _viewport.ScrollBy(scrollAmount);
                    }

                    if (_rootNode != null)
                    {
                        (_pixels, _hitRegions) = Drawer.Draw(_width, _height, _rootNode, _viewport);
                        User32.InvalidateRect(hWnd, IntPtr.Zero, false);
                    }
                    break;
                }

            case WM_LBUTTONDOWN:
                {
                    var x = (short)(lParam.ToInt32() & 0xFFFF);
                    var y = (short)((lParam.ToInt32() >> 16) & 0xFFFF);

                    if (_viewport.HitThumb(x, y, _width))
                    {
                        _draggingScrollbar = true;
                        _scrollbarGrabOffset = y - _viewport.ThumbTop;
                        User32.SetCapture(hWnd);
                    }
                    else if (_viewport.HitTrack(x, _width))
                    {
                        var targetScrollY = _viewport.ScrollYFromThumbTop(y, _viewport.ThumbHeight / 2f);
                        _viewport.ScrollTo(targetScrollY);
                        if (_rootNode != null)
                        {
                            (_pixels, _hitRegions) = Drawer.Draw(_width, _height, _rootNode, _viewport);
                            User32.InvalidateRect(hWnd, IntPtr.Zero, false);
                        }
                    }
                    else
                    {
                        // Set :active on the node under cursor
                        var contentY = y + _viewport.ScrollY;
                        if (_rootNode != null)
                            AnimationEngine.SnapshotForTransition(_rootNode);
                        if (PseudoClassState.SetActive(_rootNode, x, contentY) && _rootNode != null)
                        {
                            if (AnimationEngine.DetectAndStartTransitions(_rootNode))
                                StartAnimationTimer(hWnd);
                            (_pixels, _hitRegions) = Drawer.Draw(_width, _height, _rootNode, _viewport);
                            User32.InvalidateRect(hWnd, IntPtr.Zero, false);
                        }

                        // Dispatch mousedown event with coordinates
                        if (_rootNode != null)
                        {
                            var jsEngine = JsEngine.Instance;
                            for (int ri = _hitRegions.Count - 1; ri >= 0; ri--)
                            {
                                var region = _hitRegions[ri];
                                if (!region.Bounds.Contains(x, contentY)) continue;
                                if (jsEngine != null)
                                {
                                    var mdNode = FindNodeByKey(_rootNode, region.NodeKey);
                                    if (mdNode != null)
                                        DispatchMouseEvent("mousedown", mdNode, x, y, contentY, jsEngine);
                                }
                                break;
                            }
                        }

                        // Start range drag if clicking on a range slider
                        if (_rootNode != null)
                        {
                            for (int ri = _hitRegions.Count - 1; ri >= 0; ri--)
                            {
                                var region = _hitRegions[ri];
                                if (!region.Bounds.Contains(x, contentY)) continue;
                                if (region.InputAction == InputAction.Range)
                                {
                                    var node = FindNodeByKey(_rootNode, region.NodeKey);
                                    if (node != null)
                                    {
                                        node.Attributes.TryGetValue("min", out var minStr);
                                        node.Attributes.TryGetValue("max", out var maxStr);
                                        node.Attributes.TryGetValue("step", out var stepStr);
                                        float.TryParse(minStr ?? "0", System.Globalization.NumberStyles.Float,
                                            System.Globalization.CultureInfo.InvariantCulture, out var minV);
                                        float.TryParse(maxStr ?? "100", System.Globalization.NumberStyles.Float,
                                            System.Globalization.CultureInfo.InvariantCulture, out var maxV);
                                        float.TryParse(stepStr ?? "1", System.Globalization.NumberStyles.Float,
                                            System.Globalization.CultureInfo.InvariantCulture, out var step);
                                        _draggingRange = node.NodeKey;
                                        _draggingRangeRect = region.Bounds;
                                        _rangeMin = minV;
                                        _rangeMax = maxV;
                                        _rangeStep = step;
                                        // Set initial value at click position
                                        var ratio = Math.Clamp((x - region.Bounds.Left) / region.Bounds.Width, 0f, 1f);
                                        var newVal = minV + ratio * (maxV - minV);
                                        newVal = (float)(Math.Round(newVal / step) * step);
                                        newVal = Math.Clamp(newVal, minV, maxV);
                                        FormState.TextInputValues[node.NodeKey] =
                                            newVal == Math.Floor(newVal)
                                                ? ((int)newVal).ToString()
                                                : newVal.ToString(System.Globalization.CultureInfo.InvariantCulture);
                                        EventDispatcher.Dispatch(node.NodeKey, "input", _rootNode);
                                        (_pixels, _hitRegions) = Drawer.Draw(_width, _height, _rootNode, _viewport);
                                        User32.InvalidateRect(hWnd, IntPtr.Zero, false);
                                    }
                                }
                                break;
                            }
                        }
                    }
                    break;
                }

            case WM_MOUSEMOVE:
                {
                    if (_draggingScrollbar)
                    {
                        var y = (short)((lParam.ToInt32() >> 16) & 0xFFFF);
                        var targetScrollY = _viewport.ScrollYFromThumbTop(y, _scrollbarGrabOffset);
                        _viewport.ScrollTo(targetScrollY);
                        if (_rootNode != null)
                        {
                            (_pixels, _hitRegions) = Drawer.Draw(_width, _height, _rootNode, _viewport);
                            User32.InvalidateRect(hWnd, IntPtr.Zero, false);
                        }
                    }
                    else if (_draggingRange != null && _rootNode != null)
                    {
                        var mx = (short)(lParam.ToInt32() & 0xFFFF);
                        var ratio = Math.Clamp((mx - _draggingRangeRect.Left) / _draggingRangeRect.Width, 0f, 1f);
                        var newVal = _rangeMin + ratio * (_rangeMax - _rangeMin);
                        newVal = (float)(Math.Round(newVal / _rangeStep) * _rangeStep);
                        newVal = Math.Clamp(newVal, _rangeMin, _rangeMax);
                        FormState.TextInputValues[_draggingRange.Value] =
                            newVal == Math.Floor(newVal)
                                ? ((int)newVal).ToString()
                                : newVal.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        EventDispatcher.Dispatch(_draggingRange.Value, "input", _rootNode);
                        (_pixels, _hitRegions) = Drawer.Draw(_width, _height, _rootNode, _viewport);
                        User32.InvalidateRect(hWnd, IntPtr.Zero, false);
                    }
                    else
                    {
                        // Update :hover state
                        var mx = (short)(lParam.ToInt32() & 0xFFFF);
                        var my = (short)((lParam.ToInt32() >> 16) & 0xFFFF);
                        var contentY = my + _viewport.ScrollY;

                        if (_rootNode != null)
                            AnimationEngine.SnapshotForTransition(_rootNode);
                        var hoverChanged = PseudoClassState.UpdateHover(_rootNode, mx, contentY);
                        if (hoverChanged && _rootNode != null)
                        {
                            if (AnimationEngine.DetectAndStartTransitions(_rootNode))
                                StartAnimationTimer(hWnd);
                            (_pixels, _hitRegions) = Drawer.Draw(_width, _height, _rootNode, _viewport);
                            User32.InvalidateRect(hWnd, IntPtr.Zero, false);
                        }
                    }
                    break;
                }

            case WM_LBUTTONUP:
                {
                    // Release range drag
                    _draggingRange = null;

                    if (_draggingScrollbar)
                    {
                        _draggingScrollbar = false;
                        User32.ReleaseCapture();
                        break;
                    }

                    // Clear :active state
                    if (_rootNode != null)
                        AnimationEngine.SnapshotForTransition(_rootNode);
                    var activeChanged = PseudoClassState.ClearActive();
                    if (activeChanged && _rootNode != null && AnimationEngine.DetectAndStartTransitions(_rootNode))
                        StartAnimationTimer(hWnd);

                    var x = (short)(lParam.ToInt32() & 0xFFFF);
                    var y = (short)((lParam.ToInt32() >> 16) & 0xFFFF);
                    var contentY = y + _viewport.ScrollY;
                    var handled = false;
                    var prevFocus = FormState.FocusedInput;

                    // Close any open dropdown if clicking outside it
                    var clickedOnDropdown = false;

                    // Dispatch mouseup event with coordinates
                    if (_rootNode != null)
                    {
                        var jsEngine = JsEngine.Instance;
                        for (int ri = _hitRegions.Count - 1; ri >= 0; ri--)
                        {
                            var region = _hitRegions[ri];
                            if (!region.Bounds.Contains(x, contentY)) continue;
                            if (jsEngine != null)
                            {
                                var node = FindNodeByKey(_rootNode, region.NodeKey);
                                if (node != null)
                                    DispatchMouseEvent("mouseup", node, x, y, contentY, jsEngine);
                            }
                            break;
                        }
                    }

                    for (int ri = _hitRegions.Count - 1; ri >= 0; ri--)
                    {
                        var region = _hitRegions[ri];
                        if (!region.Bounds.Contains(x, contentY)) continue;

                        if (region.Href != null)
                        {
                            Process.Start(new ProcessStartInfo(region.Href) { UseShellExecute = true });
                            handled = true;
                            break;
                        }

                        if (region.InputAction == InputAction.TextInput)
                        {
                            FormState.FocusedInput = region.NodeKey;
                            handled = true;
                            break;
                        }

                        if (region.InputAction == InputAction.Checkbox)
                        {
                            if (!FormState.CheckedBoxes.Remove(region.NodeKey))
                                FormState.CheckedBoxes.Add(region.NodeKey);
                            handled = true;
                            break;
                        }

                        if (region.InputAction == InputAction.Radio)
                        {
                            FormState.SelectRadio(region.NodeKey);
                            handled = true;
                            break;
                        }

                        if (region.InputAction == InputAction.NumberUp || region.InputAction == InputAction.NumberDown)
                        {
                            var node = FindNodeByKey(_rootNode, region.NodeKey);
                            if (node != null)
                            {
                                node.Attributes.TryGetValue("step", out var stepStr);
                                node.Attributes.TryGetValue("min", out var minStr);
                                node.Attributes.TryGetValue("max", out var maxStr);
                                node.Attributes.TryGetValue("value", out var defVal);
                                float.TryParse(stepStr ?? "1", System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture, out var step);
                                float.TryParse(minStr, System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture, out var minV);
                                float.TryParse(maxStr, System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture, out var maxV);
                                var hasMin = !string.IsNullOrEmpty(minStr);
                                var hasMax = !string.IsNullOrEmpty(maxStr);
                                var curText = FormState.GetTextValue(node.NodeKey, defVal ?? "0");
                                float.TryParse(curText, System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture, out var curVal);
                                curVal += region.InputAction == InputAction.NumberUp ? step : -step;
                                if (hasMin && curVal < minV) curVal = minV;
                                if (hasMax && curVal > maxV) curVal = maxV;
                                FormState.TextInputValues[node.NodeKey] =
                                    curVal == Math.Floor(curVal)
                                        ? ((int)curVal).ToString()
                                        : curVal.ToString(System.Globalization.CultureInfo.InvariantCulture);
                                EventDispatcher.Dispatch(node.NodeKey, "input", _rootNode);
                            }
                            handled = true;
                            break;
                        }

                        if (region.InputAction == InputAction.Range)
                        {
                            // Range click is handled in WM_LBUTTONDOWN; just consume
                            handled = true;
                            break;
                        }

                        if (region.InputAction == InputAction.SelectDropdown)
                        {
                            clickedOnDropdown = true;
                            // Check if this is a dropdown option click
                            if (FormState.OpenDropdown == region.NodeKey && Drawer.SelectOptionMap.TryGetValue(ri, out var optIdx))
                            {
                                var node = FindNodeByKey(_rootNode, region.NodeKey);
                                if (node != null)
                                {
                                    node.Attributes.TryGetValue("_optionValues", out var optValStr);
                                    if (optValStr != null)
                                    {
                                        var vals = optValStr.Split('|');
                                        if (optIdx < vals.Length)
                                            FormState.TextInputValues[node.NodeKey] = vals[optIdx];
                                    }
                                }
                                FormState.OpenDropdown = null;
                            }
                            else if (FormState.OpenDropdown == region.NodeKey)
                            {
                                // Clicking the select again closes it
                                FormState.OpenDropdown = null;
                            }
                            else
                            {
                                // Open dropdown
                                FormState.OpenDropdown = region.NodeKey;
                            }
                            handled = true;
                            break;
                        }

                        if (region.InputAction == InputAction.Button)
                        {
                            DispatchClickWithCoords(region.NodeKey, x, y, contentY);
                            handled = true;
                            break;
                        }

                        if (DispatchClickWithCoords(region.NodeKey, x, y, contentY))
                        {
                            handled = true;
                            break;
                        }
                    }

                    // Close dropdown if clicked elsewhere
                    if (!clickedOnDropdown && FormState.OpenDropdown != null)
                    {
                        FormState.OpenDropdown = null;
                        handled = true;
                    }

                    var focusChanged = !handled && FormState.FocusedInput != null;
                    if (focusChanged) FormState.FocusedInput = null;

                    // Fire "change" event if value changed since focus
                    if (prevFocus != null && prevFocus != FormState.FocusedInput && _rootNode != null)
                    {
                        var currentVal = FormState.TextInputValues.GetValueOrDefault(prevFocus.Value, "");
                        if (currentVal != (_focusedValueSnapshot ?? ""))
                            EventDispatcher.Dispatch(prevFocus.Value, "change", _rootNode);
                    }

                    // Snapshot value for newly focused input
                    if (FormState.FocusedInput != null && FormState.FocusedInput != prevFocus)
                        _focusedValueSnapshot = FormState.TextInputValues.GetValueOrDefault(FormState.FocusedInput.Value, "");
                    else if (FormState.FocusedInput == null)
                        _focusedValueSnapshot = null;

                    // Update :focus pseudo-class state
                    if (FormState.FocusedInput != prevFocus)
                    {
                        if (_rootNode != null)
                            AnimationEngine.SnapshotForTransition(_rootNode);
                        PseudoClassState.UpdateFocus(_rootNode, FormState.FocusedInput);
                        if (_rootNode != null && AnimationEngine.DetectAndStartTransitions(_rootNode))
                            StartAnimationTimer(hWnd);
                    }

                    if ((handled || focusChanged || activeChanged) && _rootNode != null)
                    {
                        (_pixels, _hitRegions) = Drawer.Draw(_width, _height, _rootNode, _viewport);
                        User32.InvalidateRect(hWnd, IntPtr.Zero, false);
                    }
                    break;
                }

            case WM_KEYDOWN:
            case WM_KEYUP:
                {
                    if (_rootNode == null) break;
                    var vk = (int)wParam.ToInt64();
                    var keyName = MapVirtualKey(vk);
                    var eventType = msg == WM_KEYDOWN ? "keydown" : "keyup";
                    var modifiers = GetKeyModifiers();

                    // Dispatch to focused element or body
                    var targetNode = FormState.FocusedInput != null
                        ? FindNodeByKey(_rootNode, FormState.FocusedInput.Value)
                        : FindFirst(_rootNode, n => n.TagName == "BODY");
                    if (targetNode != null)
                    {
                        var jsEngine = JsEngine.Instance;
                        if (jsEngine != null)
                        {
                            var evt = new Scripting.Dom.JsEvent();
                            evt.initEvent(eventType, true, true);
                            evt.key = keyName;
                            evt.keyCode = vk;
                            evt.code = MapVirtualKeyToCode(vk);
                            evt.ctrlKey = modifiers.ctrl;
                            evt.shiftKey = modifiers.shift;
                            evt.altKey = modifiers.alt;
                            evt.metaKey = modifiers.meta;
                            evt.target = new Scripting.Dom.JsElement(jsEngine.RawEngine, targetNode);
                            EventDispatcher.DispatchEvent(targetNode, evt, jsEngine);
                        }
                    }

                    // Handle Enter in single-line input → submit
                    if (msg == WM_KEYDOWN && vk == 0x0D && FormState.FocusedInput != null)
                    {
                        var focNode = FindNodeByKey(_rootNode, FormState.FocusedInput.Value);
                        if (focNode != null && focNode.TagName == "INPUT")
                        {
                            // Walk up to find <form>
                            for (var p = focNode.Parent; p != null; p = p.Parent)
                            {
                                if (p.TagName == "FORM")
                                {
                                    EventDispatcher.Dispatch(p.NodeKey, "submit", _rootNode);
                                    break;
                                }
                            }
                        }
                    }
                    break;
                }

            case WM_CHAR:
                {
                    if (FormState.FocusedInput == null || _rootNode == null) break;
                    var c = (char)wParam.ToInt32();
                    if (c == '\b')
                    {
                        var key = FormState.FocusedInput.Value;
                        if (FormState.TextInputValues.TryGetValue(key, out var cur) && cur.Length > 0)
                            FormState.TextInputValues[key] = cur[..^1];
                    }
                    else if (c == '\r')
                    {
                        // Enter key — only allow in textareas
                        var focusedNode = FindNodeByKey(_rootNode, FormState.FocusedInput.Value);
                        if (focusedNode?.TagName == "TEXTAREA")
                        {
                            var key = FormState.FocusedInput.Value;
                            FormState.TextInputValues.TryGetValue(key, out var cur);
                            FormState.TextInputValues[key] = (cur ?? string.Empty) + '\n';
                        }
                    }
                    else if (!char.IsControl(c))
                    {
                        var key = FormState.FocusedInput.Value;
                        FormState.TextInputValues.TryGetValue(key, out var cur);
                        FormState.TextInputValues[key] = (cur ?? string.Empty) + c;
                    }

                    // Fire "input" event on the focused element
                    EventDispatcher.Dispatch(FormState.FocusedInput.Value, "input", _rootNode);

                    (_pixels, _hitRegions) = Drawer.Draw(_width, _height, _rootNode, _viewport);
                    User32.InvalidateRect(hWnd, IntPtr.Zero, false);
                    break;
                }

            case WM_SETCURSOR:
                if ((lParam.ToInt32() & 0xFFFF) == HTCLIENT)
                {
                    User32.GetCursorPos(out var pt);
                    User32.ScreenToClient(hWnd, ref pt);
                    var cursorId = IDC_ARROW;
                    var contentPtY = pt.Y + _viewport.ScrollY;
                    foreach (var region in _hitRegions)
                    {
                        if (region.Bounds.Contains(pt.X, contentPtY))
                        {
                            cursorId = region.Cursor switch
                            {
                                CursorType.Pointer => IDC_HAND,
                                CursorType.Text => IDC_IBEAM,
                                _ => IDC_ARROW
                            };
                        }
                    }
                    User32.SetCursor(User32.LoadCursor(IntPtr.Zero, cursorId));
                    return new IntPtr(1);
                }
                return User32.DefWindowProc(hWnd, msg, wParam, lParam);

            default:
                return User32.DefWindowProc(hWnd, msg, wParam, lParam);
        }
        return IntPtr.Zero;
    }

    private void StartAnimationTimer(IntPtr hWnd)
    {
        if (_timerRunning) return;
        User32.SetTimer(hWnd, AnimationTimerId, AnimationTimerMs, IntPtr.Zero);
        _timerRunning = true;
    }

    private void StopAnimationTimer(IntPtr hWnd)
    {
        if (!_timerRunning) return;
        User32.KillTimer(hWnd, AnimationTimerId);
        _timerRunning = false;
    }

    private static LayoutNode? FindNodeByKey(LayoutNode? root, Guid key)
    {
        if (root == null) return null;
        if (root.NodeKey == key) return root;
        foreach (var child in root.Children)
        {
            var result = FindNodeByKey(child, key);
            if (result != null) return result;
        }
        return null;
    }

    private static LayoutNode? FindFirst(LayoutNode root, Func<LayoutNode, bool> predicate)
    {
        if (predicate(root)) return root;
        foreach (var child in root.Children)
        {
            var result = FindFirst(child, predicate);
            if (result != null) return result;
        }
        return null;
    }

    private void DispatchMouseEvent(string eventType, LayoutNode node, int x, int y, float contentY, JsEngine engine)
    {
        var evt = new Scripting.Dom.JsEvent();
        evt.initEvent(eventType, true, true);
        evt.clientX = x;
        evt.clientY = y;
        evt.pageX = x;
        evt.pageY = (int)contentY;
        evt.button = 0;
        evt.target = new Scripting.Dom.JsElement(engine.RawEngine, node);
        EventDispatcher.DispatchEvent(node, evt, engine);
    }

    private bool DispatchClickWithCoords(Guid nodeKey, int x, int y, float contentY)
    {
        if (_rootNode == null) return false;
        var node = FindNodeByKey(_rootNode, nodeKey);
        if (node == null) return false;
        var engine = JsEngine.Instance;
        if (engine == null) return false;

        var evt = new Scripting.Dom.JsEvent();
        evt.initEvent("click", true, true);
        evt.clientX = x;
        evt.clientY = y;
        evt.pageX = x;
        evt.pageY = (int)contentY;
        evt.button = 0;
        evt.target = new Scripting.Dom.JsElement(engine.RawEngine, node);
        return EventDispatcher.DispatchEvent(node, evt, engine);
    }

    private static string MapVirtualKey(int vk) => vk switch
    {
        0x08 => "Backspace",
        0x09 => "Tab",
        0x0D => "Enter",
        0x10 => "Shift",
        0x11 => "Control",
        0x12 => "Alt",
        0x13 => "Pause",
        0x14 => "CapsLock",
        0x1B => "Escape",
        0x20 => " ",
        0x21 => "PageUp",
        0x22 => "PageDown",
        0x23 => "End",
        0x24 => "Home",
        0x25 => "ArrowLeft",
        0x26 => "ArrowUp",
        0x27 => "ArrowRight",
        0x28 => "ArrowDown",
        0x2D => "Insert",
        0x2E => "Delete",
        >= 0x30 and <= 0x39 => ((char)vk).ToString(),
        >= 0x41 and <= 0x5A => ((char)vk).ToString().ToLower(),
        >= 0x60 and <= 0x69 => (vk - 0x60).ToString(),
        0x6A => "*",
        0x6B => "+",
        0x6D => "-",
        0x6E => ".",
        0x6F => "/",
        >= 0x70 and <= 0x7B => "F" + (vk - 0x70 + 1),
        0xBA => ";",
        0xBB => "=",
        0xBC => ",",
        0xBD => "-",
        0xBE => ".",
        0xBF => "/",
        0xC0 => "`",
        0xDB => "[",
        0xDC => "\\",
        0xDD => "]",
        0xDE => "'",
        _ => "Unidentified"
    };

    private static string MapVirtualKeyToCode(int vk) => vk switch
    {
        0x08 => "Backspace",
        0x09 => "Tab",
        0x0D => "Enter",
        0x10 => "ShiftLeft",
        0x11 => "ControlLeft",
        0x12 => "AltLeft",
        0x13 => "Pause",
        0x14 => "CapsLock",
        0x1B => "Escape",
        0x20 => "Space",
        0x21 => "PageUp",
        0x22 => "PageDown",
        0x23 => "End",
        0x24 => "Home",
        0x25 => "ArrowLeft",
        0x26 => "ArrowUp",
        0x27 => "ArrowRight",
        0x28 => "ArrowDown",
        0x2D => "Insert",
        0x2E => "Delete",
        >= 0x30 and <= 0x39 => "Digit" + (char)vk,
        >= 0x41 and <= 0x5A => "Key" + (char)vk,
        >= 0x60 and <= 0x69 => "Numpad" + (vk - 0x60),
        0x6A => "NumpadMultiply",
        0x6B => "NumpadAdd",
        0x6D => "NumpadSubtract",
        0x6E => "NumpadDecimal",
        0x6F => "NumpadDivide",
        >= 0x70 and <= 0x7B => "F" + (vk - 0x70 + 1),
        0xBA => "Semicolon",
        0xBB => "Equal",
        0xBC => "Comma",
        0xBD => "Minus",
        0xBE => "Period",
        0xBF => "Slash",
        0xC0 => "Backquote",
        0xDB => "BracketLeft",
        0xDC => "Backslash",
        0xDD => "BracketRight",
        0xDE => "Quote",
        _ => "Unidentified"
    };

    private static (bool ctrl, bool shift, bool alt, bool meta) GetKeyModifiers()
    {
        return (
            ctrl: (User32.GetKeyState(0x11) & 0x8000) != 0,
            shift: (User32.GetKeyState(0x10) & 0x8000) != 0,
            alt: (User32.GetKeyState(0x12) & 0x8000) != 0,
            meta: (User32.GetKeyState(0x5B) & 0x8000) != 0 || (User32.GetKeyState(0x5C) & 0x8000) != 0
        );
    }

    /// <summary>
    /// Finds the innermost scrollable ancestor at the given content-space coordinates.
    /// Returns null if no scrollable element contains the point.
    /// </summary>
    private static LayoutNode? FindScrollableAncestor(LayoutNode root, float x, float contentY)
    {
        LayoutNode? best = null;
        FindScrollableAt(root, x, contentY, ref best);
        return best;
    }

    private static void FindScrollableAt(LayoutNode node, float x, float contentY, ref LayoutNode? best)
    {
        var box = node.Box.BorderBox;
        if (box.Width <= 0 || box.Height <= 0) goto children;
        if (!box.Contains(x, contentY)) return;

        if (node.ScrollState != null && node.ScrollState.NeedsScrollbar)
            best = node;

    children:
        foreach (var child in node.Children)
            FindScrollableAt(child, x, contentY, ref best);
    }
}
