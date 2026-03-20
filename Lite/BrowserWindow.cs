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

    // Animation timer
    private const uint  AnimationTimerMs = 16; // ~60 fps
    private static readonly IntPtr AnimationTimerId = new(1);
    private bool _timerRunning;

    public BrowserWindow(string url, string title = "Lite Browser", int width = 800, int height = 600)
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
                    (_pixels, _hitRegions) = Drawer.Draw(_width, _height, _rootNode, _viewport);
                    User32.InvalidateRect(hWnd, IntPtr.Zero, false);
                    if (!stillRunning)
                        StopAnimationTimer(hWnd);
                }
                break;

            case WM_SIZE:
                User32.GetClientRect(hWnd, out var clientRect);
                _width = clientRect.right - clientRect.left;
                _height = clientRect.bottom - clientRect.top;
                _viewport.ViewportHeight = _height;

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
                _viewport.ScrollBy(-delta / 120f * 60f);
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

                foreach (var region in _hitRegions)
                {
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

                    if (region.InputAction == InputAction.Button)
                    {
                        EventDispatcher.Dispatch(region.NodeKey, "click", _rootNode);
                        handled = true;
                        break;
                    }

                    if (EventDispatcher.Dispatch(region.NodeKey, "click", _rootNode))
                    {
                        handled = true;
                        break;
                    }
                }

                var focusChanged = !handled && FormState.FocusedInput != null;
                if (focusChanged) FormState.FocusedInput = null;

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
                else if (!char.IsControl(c))
                {
                    var key = FormState.FocusedInput.Value;
                    FormState.TextInputValues.TryGetValue(key, out var cur);
                    FormState.TextInputValues[key] = (cur ?? string.Empty) + c;
                }
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
                                CursorType.Text    => IDC_IBEAM,
                                _                  => IDC_ARROW
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
}
