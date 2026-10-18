using System.Diagnostics;
using Lite.Animation;
using Lite.Interaction;
using Lite.Layout;
using Lite.Models;
using Lite.Network;
using Lite.Platform;
using Lite.Scripting;
using SkiaSharp;

namespace Lite;

public class BrowserWindow
{
    private string _url;
    private readonly string _title;
    private readonly int _initialWidth;
    private readonly int _initialHeight;
    private readonly BrowserSession _session = new();

    private int _width;
    private int _height;
    private LayoutNode? _rootNode;
    private IntPtr _pixels;
    private List<HitRegion> _hitRegions = [];
    private readonly Viewport _viewport = new();
    private IWindowHost _host = null!;

    // Scrollbar drag state
    private bool _draggingScrollbar;
    private float _scrollbarGrabOffset; // Y offset within thumb where drag started

    // Range slider drag state
    private Guid? _draggingRange;
    private SKRect _draggingRangeRect;
    private float _rangeMin, _rangeMax, _rangeStep;

    // Animation timer
    private const uint AnimationTimerMs = 16; // ~60 fps
    private bool _timerRunning;

    // Browser-style loading indicator shown while a navigation loads on a background thread,
    // followed by a short cross-fade that reveals the rendered page.
    private LoadingAnimation? _loading;
    private PageTransition? _pageTransition;
    private SKBitmap? _transitionFrame; // keeps the on-screen composite's pixels alive

    // Background navigation handoff: written by the load task, read on the UI thread.
    // _loadReady is volatile so the non-volatile fields written before it are visible once set.
    private volatile bool _loadReady;
    private Page? _loadedPage;
    private Exception? _loadError;
    private string? _pendingUrl;
    private const long MinLoadingMs = 450; // keep the loading indicator visible at least this long

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
        var initialPage = Parser.TraversePage(new NavigationRequest(_url), _initialWidth, _initialHeight, _session);
        _rootNode = initialPage.Root;
        _url = initialPage.Root.DocumentState?.Address ?? _url;
        AnimationEngine.StartAnimations(_rootNode);

        // Bind viewport to JS engine for window.scrollTo/scrollBy
        JsEngine.Instance?.SetViewport(_viewport);

        _host = CreateHost();
        if (!_host.Create(_title, _initialWidth, _initialHeight))
        {
            Console.WriteLine("Window creation failed!");
            _host.Dispose();
            return;
        }

        // autofocus: focus first element with the attribute
        if (_rootNode != null)
        {
            var af = FindFirst(_rootNode, n => n.Attributes.ContainsKey("autofocus"));
            if (af != null) FormState.FocusedInput = af.NodeKey;
        }

        // Kick off the animation timer if the page has CSS animations
        if (_rootNode != null)
        {
            AnimationEngine.Tick(_rootNode); // initial frame
            StartAnimationTimer();
        }

        // Wake the message loop whenever the JS event loop queues work (timers, fetch, etc.).
        // PostTask is thread-safe, so this is safe to call from background timer threads.
        if (JsEngine.Instance is { } jsEngine)
        {
            jsEngine.TaskEnqueued += () => _host.PostTask();
            // Form submission (and other JS-driven navigation) routes through here.
            jsEngine.OnNavigate = request => Navigate(request);
            // document.title updates the window caption.
            jsEngine.OnTitleChange = t => _host.SetTitle(t.Length == 0 ? _title : t);
            // Reflect the parsed document's <title> in the caption.
            if (Parser.Document?.Title is { Length: > 0 } docTitle)
                _host.SetTitle(docTitle);
            // Flush any tasks already queued during initial script execution (incl. iframe load).
            if (jsEngine.HasPendingTreeTasks)
                _host.PostTask();
        }

        _host.RunLoop();
        _host.Dispose();
        _rootNode?.DocumentState?.Engine?.Dispose();
        _session.Dispose();
    }

    /// <summary>Picks the native windowing backend for the current OS.</summary>
    private IWindowHost CreateHost()
    {
        if (OperatingSystem.IsWindows()) return new Win32WindowHost(this);
        if (OperatingSystem.IsLinux()) return new X11WindowHost(this);
        throw new PlatformNotSupportedException("Lite supports Windows (Win32) and Linux (X11) windows.");
    }

    // ---- Host callbacks -------------------------------------------------------------------
    // Each native window host (Win32, X11) translates its events into these calls on the UI
    // thread. While a navigation is loading, interaction with the (soon-to-be-replaced) page
    // and its JS engine is frozen; paint, sizing, the timer (which drives the loading
    // animation) and close still run.

    /// <summary>The current frame: a BGRA8888 premultiplied buffer of <c>Width*Height</c> pixels.</summary>
    internal (IntPtr Pixels, int Width, int Height) Frame => (_pixels, _width, _height);

    /// <summary>Drains the JS event loop after <see cref="IWindowHost.PostTask"/> woke the UI thread.</summary>
    internal void OnTask()
    {
        if (_loading != null) return;
        if (_rootNode != null && JsEngine.Instance is { } taskEngine)
        {
            // A drained task may itself start a navigation (e.g. location.href = …).
            // Skip the live redraw while a loading/reveal overlay owns the screen so we
            // don't clobber its frame; the animation timer keeps it updated instead.
            if (taskEngine.DrainTree() && _loading == null && _pageTransition == null)
            {
                (_pixels, _hitRegions) = Drawer.Draw(_width, _height, _rootNode, _viewport);
                _host.Invalidate();
            }
            // If a drained task queued more work, make sure we come back for it.
            if (taskEngine.HasPendingTreeTasks)
                _host.PostTask();
        }
    }

    /// <summary>Animation timer tick: loading indicator, reveal cross-fade, then CSS animations and rAF.</summary>
    internal void OnTimer()
    {
        // Phase 1: a navigation is loading on a background thread. Animate the loading
        // indicator and commit the new page once it's ready (and has shown long enough).
        if (_loading != null)
        {
            if (_loadReady && _loading.ElapsedMs >= MinLoadingMs)
            {
                FinalizeNavigation();
            }
            else
            {
                var prevFrame = _transitionFrame;
                _pixels = _loading.RenderFrame(_width, _height, out _transitionFrame);
                prevFrame?.Dispose();
                _host.Invalidate();
            }
            return;
        }

        // Phase 2: reveal cross-fade out of the loading screen after a load completed.
        if (_pageTransition != null)
        {
            if (!_pageTransition.IsComplete)
            {
                var prevFrame = _transitionFrame;
                _pixels = _pageTransition.RenderFrame(_width, _height, out _transitionFrame);
                prevFrame?.Dispose();
                _host.Invalidate();
                return;
            }

            // Reveal finished — drop the snapshots and resume live rendering below.
            _pageTransition.Dispose();
            _pageTransition = null;
            _transitionFrame?.Dispose();
            _transitionFrame = null;
        }

        // Phase 3: normal CSS animation / requestAnimationFrame tick.
        if (_rootNode != null)
        {
            var stillRunning = AnimationEngine.Tick(_rootNode);

            // Dispatch animation/transition lifecycle events
            foreach (var ev in AnimationEngine.DrainEvents())
                EventDispatcher.Dispatch(ev.Node.NodeKey, ev.EventType, _rootNode);

            // Flush requestAnimationFrame callbacks
            var engine = JsEngine.Instance;
            var rafRan = engine?.FlushRAFTree(_rafStopwatch.Elapsed.TotalMilliseconds) ?? false;

            (_pixels, _hitRegions) = Drawer.Draw(_width, _height, _rootNode, _viewport);
            _host.Invalidate();
            if (!stillRunning && !(engine?.HasPendingTreeRAF ?? false))
                StopAnimationTimer();
        }
    }

    /// <summary>The client area changed size; re-lays out and repaints at the new size.</summary>
    internal void OnResize(int width, int height)
    {
        _width = width;
        _height = height;
        _viewport.ViewportHeight = _height;

        // While loading, only re-render the loading frame at the new size (re-sizing the
        // pixel buffer to match the client area) and leave the in-flight engine untouched.
        if (_loading != null)
        {
            var prevFrame = _transitionFrame;
            _pixels = _loading.RenderFrame(_width, _height, out _transitionFrame);
            prevFrame?.Dispose();
            _host.Invalidate();
            return;
        }

        // Update JS window.innerWidth / innerHeight
        JsEngine.Instance?.UpdateViewportSize(_width, _height);

        if (_pageTransition != null)
        {
            var prevFrame = _transitionFrame;
            _pixels = _pageTransition.RenderFrame(_width, _height, out _transitionFrame);
            prevFrame?.Dispose();
            _host.Invalidate();
            return;
        }

        if (_rootNode != null)
        {
            // Re-evaluate @media queries against the new viewport size
            _rootNode.ReapplyMediaStyles(_width, _height);
            (_pixels, _hitRegions) = Drawer.Draw(_width, _height, _rootNode, _viewport);
            _host.Invalidate();
        }
    }

    /// <summary>Mouse wheel at client point (x, y); <paramref name="delta"/> is in WHEEL_DELTA units (120 per notch, positive away from the user).</summary>
    internal void OnWheel(int x, int y, int delta)
    {
        if (_loading != null) return;
        var scrollAmount = -delta / 120f * 60f;

        // Try scrolling a per-element scrollable container first
        var contentPtY = y + _viewport.ScrollY;
        var scrollTarget = _rootNode != null
            ? FindScrollableAncestor(_rootNode, x, contentPtY)
            : null;

        // Dispatch a cancelable wheel event; preventDefault() suppresses the scroll.
        var wheelPrevented = DispatchWheelEvent(scrollTarget ?? _rootNode, x, y, contentPtY, -delta / 120f * 60f);

        if (!wheelPrevented)
        {
            if (scrollTarget?.ScrollState != null)
                scrollTarget.ScrollState.ScrollBy(scrollAmount);
            else
                _viewport.ScrollBy(scrollAmount);
        }

        if (_rootNode != null)
        {
            (_pixels, _hitRegions) = Drawer.Draw(_width, _height, _rootNode, _viewport);
            _host.Invalidate();
        }
    }

    /// <summary>Primary button pressed at client point (x, y).</summary>
    internal void OnMouseDown(int x, int y)
    {
        if (_loading != null) return;
        if (HandleFramePointer("mousedown", x, y)) return;

        if (_viewport.HitThumb(x, y, _width))
        {
            _draggingScrollbar = true;
            _scrollbarGrabOffset = y - _viewport.ThumbTop;
            _host.CaptureMouse();
        }
        else if (_viewport.HitTrack(x, _width))
        {
            var targetScrollY = _viewport.ScrollYFromThumbTop(y, _viewport.ThumbHeight / 2f);
            _viewport.ScrollTo(targetScrollY);
            if (_rootNode != null)
            {
                (_pixels, _hitRegions) = Drawer.Draw(_width, _height, _rootNode, _viewport);
                _host.Invalidate();
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
                    StartAnimationTimer();
                (_pixels, _hitRegions) = Drawer.Draw(_width, _height, _rootNode, _viewport);
                _host.Invalidate();
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
                            _host.Invalidate();
                        }
                    }
                    else if (region.InputAction == InputAction.MediaToggle)
                    {
                        var node = FindNodeByKey(_rootNode, region.NodeKey);
                        if (node != null && JsEngine.Instance is { } mediaEngine)
                        {
                            var el = Scripting.Dom.JsElement.For(mediaEngine.RawEngine, node);
                            if (el.paused) el.play(); else el.pause();
                            _host.PostTask();
                            (_pixels, _hitRegions) = Drawer.Draw(_width, _height, _rootNode, _viewport);
                            _host.Invalidate();
                        }
                    }
                    break;
                }
            }
        }
    }

    /// <summary>Pointer moved to client point (x, y).</summary>
    internal void OnMouseMove(int x, int y)
    {
        if (_loading != null) return;
        if (_draggingScrollbar)
        {
            var targetScrollY = _viewport.ScrollYFromThumbTop(y, _scrollbarGrabOffset);
            _viewport.ScrollTo(targetScrollY);
            if (_rootNode != null)
            {
                (_pixels, _hitRegions) = Drawer.Draw(_width, _height, _rootNode, _viewport);
                _host.Invalidate();
            }
        }
        else if (_draggingRange != null && _rootNode != null)
        {
            var ratio = Math.Clamp((x - _draggingRangeRect.Left) / _draggingRangeRect.Width, 0f, 1f);
            var newVal = _rangeMin + ratio * (_rangeMax - _rangeMin);
            newVal = (float)(Math.Round(newVal / _rangeStep) * _rangeStep);
            newVal = Math.Clamp(newVal, _rangeMin, _rangeMax);
            FormState.TextInputValues[_draggingRange.Value] =
                newVal == Math.Floor(newVal)
                    ? ((int)newVal).ToString()
                    : newVal.ToString(System.Globalization.CultureInfo.InvariantCulture);
            EventDispatcher.Dispatch(_draggingRange.Value, "input", _rootNode);
            (_pixels, _hitRegions) = Drawer.Draw(_width, _height, _rootNode, _viewport);
            _host.Invalidate();
        }
        else
        {
            // Update :hover state
            var contentY = y + _viewport.ScrollY;

            if (_rootNode != null)
                AnimationEngine.SnapshotForTransition(_rootNode);
            var hoverChanged = PseudoClassState.UpdateHover(_rootNode, x, contentY);
            if (hoverChanged && _rootNode != null)
            {
                if (AnimationEngine.DetectAndStartTransitions(_rootNode))
                    StartAnimationTimer();
                (_pixels, _hitRegions) = Drawer.Draw(_width, _height, _rootNode, _viewport);
                _host.Invalidate();
            }
        }
    }

    /// <summary>Primary button released at client point (x, y).</summary>
    internal void OnMouseUp(int x, int y)
    {
        if (_loading != null) return;
        if (HandleFramePointer("mouseup", x, y)) return;
        // Release range drag
        _draggingRange = null;

        if (_draggingScrollbar)
        {
            _draggingScrollbar = false;
            _host.ReleaseMouse();
            return;
        }

        // Clear :active state
        if (_rootNode != null)
            AnimationEngine.SnapshotForTransition(_rootNode);
        var activeChanged = PseudoClassState.ClearActive();
        if (activeChanged && _rootNode != null && AnimationEngine.DetectAndStartTransitions(_rootNode))
            StartAnimationTimer();

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
                Navigate(region.Href);
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
                // Range click is handled in OnMouseDown; just consume
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
                var btn = FindNodeByKey(_rootNode, region.NodeKey);
                if (btn != null && IsSubmitControl(btn) && FindAncestorForm(btn) is { } submitForm)
                    SubmitForm(submitForm, btn);
                handled = true;
                break;
            }

            if (region.InputAction == InputAction.FileOpen)
            {
                OpenFilePicker(region.NodeKey);
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
                StartAnimationTimer();
        }

        if ((handled || focusChanged || activeChanged) && _rootNode != null)
        {
            (_pixels, _hitRegions) = Drawer.Draw(_width, _height, _rootNode, _viewport);
            _host.Invalidate();
        }
    }

    /// <summary>Key pressed or released; <paramref name="vk"/> is a Win32 virtual-key code (other hosts translate to it).</summary>
    internal void OnKey(bool down, int vk, KeyModifiers modifiers)
    {
        if (_loading != null) return;
        if (_rootNode == null) return;
        var keyName = MapVirtualKey(vk);
        var eventType = down ? "keydown" : "keyup";

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
                evt.Init(eventType, true, true);
                evt.key = keyName;
                evt.keyCode = vk;
                evt.code = MapVirtualKeyToCode(vk);
                evt.ctrlKey = modifiers.Ctrl;
                evt.shiftKey = modifiers.Shift;
                evt.altKey = modifiers.Alt;
                evt.metaKey = modifiers.Meta;
                evt.target = new Scripting.Dom.JsElement(jsEngine.RawEngine, targetNode);
                EventDispatcher.DispatchEvent(targetNode, evt, jsEngine);
            }
        }

        // Handle Enter in single-line input → submit
        if (down && vk == 0x0D && FormState.FocusedInput != null)
        {
            var focNode = FindNodeByKey(_rootNode, FormState.FocusedInput.Value);
            if (focNode != null && focNode.TagName == "INPUT")
            {
                // Walk up to find <form>, then submit (dispatch + navigate).
                if (FindAncestorForm(focNode) is { } formNode)
                    SubmitForm(formNode);
            }
        }
    }

    /// <summary>A character was typed ('\b' for Backspace, '\r' for Enter).</summary>
    internal void OnChar(char c)
    {
        if (_loading != null) return;
        if (FormState.FocusedInput == null || _rootNode == null) return;
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
        _host.Invalidate();
    }

    /// <summary>The cursor to show over client point (x, y): the topmost hit region's cursor.</summary>
    internal CursorType CursorAt(int x, int y)
    {
        var cursor = CursorType.Default;
        var contentPtY = y + _viewport.ScrollY;
        foreach (var region in _hitRegions)
        {
            if (region.Bounds.Contains(x, contentPtY))
                cursor = region.Cursor;
        }
        return cursor;
    }

    /// <summary>
    /// Handles a link click or scripted navigation. http(s) URLs load inside this window;
    /// non-http schemes (mailto:, etc.) open in the OS default browser. Pure fragment links
    /// (same document, differing only by #hash) scroll to the target and fire hashchange
    /// without a reload.
    /// </summary>
    private void Navigate(string href) => Navigate(new NavigationRequest(href));

    private void Navigate(NavigationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Url)) return;

        if (!Uri.TryCreate(new Uri(_url), request.Url, out var target)) return;

        // Same-document fragment navigation: scroll + hashchange, no reload.
        if (request.Method.Equals("GET", StringComparison.OrdinalIgnoreCase) &&
            DiffersOnlyByFragment(_url, target.AbsoluteUri))
        {
            if (JsEngine.Instance is { } navEngine)
            {
                var oldUrl = _url;
                _url = target.AbsoluteUri;
                navEngine.NotifyNavigated(_url);
                var frag = target.Fragment.TrimStart('#');
                navEngine.ScrollToFragment(frag);
                if (!string.Equals(oldUrl, _url, StringComparison.Ordinal))
                    navEngine.FireHashChange(oldUrl, _url);
            }
            if (_rootNode != null)
            {
                (_pixels, _hitRegions) = Drawer.Draw(_width, _height, _rootNode, _viewport);
                _host.Invalidate();
            }
            return;
        }

        // Non-http(s) schemes (mailto:, tel:, file:…) open in the OS default browser.
        if (target.Scheme is not ("http" or "https"))
        {
            Process.Start(new ProcessStartInfo(target.ToString()) { UseShellExecute = true });
            return;
        }

        // A navigation is already in flight — ignore re-entrant requests until it settles.
        if (_loading != null) return;

        // Internal link: load the new document on a background thread so the UI thread stays
        // free to animate a browser-style loading indicator while it fetches/parses/renders.
        var url = target.ToString();
        request = request with { Url = url };
        var navigationRequest = request;
        var w = _width;
        var h = _height;

        _pendingUrl = url;
        _loadedPage = null;
        _loadError = null;
        _loadReady = false;

        // Freeze the page we're leaving as a dimmed backdrop behind the loading bar, and show
        // the first loading frame immediately so there's no flash of the old page.
        _loading = new LoadingAnimation(CaptureFrame());
        _transitionFrame?.Dispose();
        _pixels = _loading.RenderFrame(w, h, out _transitionFrame);

        StartAnimationTimer(); // drives the loading animation + the completion check
        _host.Invalidate();

        Task.Run(() =>
        {
            try { _loadedPage = Parser.TraversePage(navigationRequest, w, h, _session); }
            catch (Exception ex) { _loadError = ex; }
            finally { _loadReady = true; } // volatile write — publishes the fields above
        });
    }

    /// <summary>
    /// Commits a finished background navigation: swaps in the new document, rebinds the JS
    /// engine to this window, and reveals the page with a short cross-fade out of the loading
    /// screen. Runs on the UI thread (from the animation timer) once the load is ready and the
    /// loading indicator has been shown for at least <see cref="MinLoadingMs"/>.
    /// </summary>
    private void FinalizeNavigation()
    {
        // The last loading frame doubles as the "from" image for the reveal fade.
        var fromImage = CaptureFrame();
        var newPage = _loadedPage;
        var newRoot = newPage?.Root;
        var error = _loadError;

        _loading?.Dispose();
        _loading = null;
        _loadedPage = null;
        _loadError = null;
        _loadReady = false;

        if (error != null || newRoot == null)
        {
            Console.WriteLine($"[Navigation] failed to load {_pendingUrl}: {error?.Message}");
            // Keep the page we were on; just repaint it live.
            if (_rootNode != null)
                (_pixels, _hitRegions) = Drawer.Draw(_width, _height, _rootNode, _viewport);
            fromImage?.Dispose();
            _host.Invalidate();
            return;
        }

        // Commit the new document (mirrors the setup in Run()). The outgoing engine may have
        // been created on a previous navigation's background thread — adopt it onto this thread
        // so its native runtime can be freed here.
        _rootNode?.DocumentState?.Engine?.TransferTreeToCurrentThread();
        _rootNode?.DocumentState?.Engine?.Dispose();
        _url = newRoot.DocumentState?.Address ?? _pendingUrl!;
        FormState.FocusedInput = null;
        FormState.OpenDropdown = null;
        _viewport.ScrollTo(0);

        _rootNode = newRoot;
        AnimationEngine.Reset();
        AnimationEngine.StartAnimations(_rootNode);

        // Rebind the freshly created JS engine (Parser.TraverseHtml replaced JsEngine.Instance).
        if (JsEngine.Instance is { } engine)
        {
            // Adopt the engine the background load created: from here on the UI thread drives
            // its event loop.
            engine.TransferTreeToCurrentThread();
            engine.SetViewport(_viewport);
            engine.UpdateViewportSize(_width, _height);
            engine.TaskEnqueued += () => _host.PostTask();
            engine.OnNavigate = request => Navigate(request);
            engine.OnTitleChange = t => _host.SetTitle(t.Length == 0 ? _title : t);
            engine.NotifyNavigated(_url);
            _host.SetTitle(newPage?.Document?.Title is { Length: > 0 } t2 ? t2 : _title);
        }

        // Re-apply autofocus for the freshly loaded page.
        var af = FindFirst(_rootNode, n => n.Attributes.ContainsKey("autofocus"));
        if (af != null) FormState.FocusedInput = af.NodeKey;

        AnimationEngine.Tick(_rootNode); // initial frame
        (_pixels, _hitRegions) = Drawer.Draw(_width, _height, _rootNode, _viewport);

        // Reveal the rendered page by fading out of the loading screen.
        var toImage = CaptureFrame();
        _pageTransition?.Dispose();
        _pageTransition = null;
        if (fromImage != null && toImage != null)
        {
            _pageTransition = new PageTransition(fromImage, toImage);
            _transitionFrame?.Dispose();
            _pixels = _pageTransition.RenderFrame(_width, _height, out _transitionFrame);
        }
        else
        {
            fromImage?.Dispose();
            toImage?.Dispose();
        }

        // Run any tasks queued while scripts executed during the load (incl. iframe load events).
        if (JsEngine.Instance is { HasPendingTreeTasks: true })
            _host.PostTask();

        _host.Invalidate();
    }

    /// <summary>
    /// Copies the currently rendered pixel buffer into a standalone <see cref="SKImage"/>
    /// for use as a navigation snapshot. Returns null if nothing is drawn yet.
    /// </summary>
    private SKImage? CaptureFrame()
    {
        if (_pixels == IntPtr.Zero || _width <= 0 || _height <= 0) return null;
        var info = new SKImageInfo(_width, _height, SKColorType.Bgra8888, SKAlphaType.Premul);
        return SKImage.FromPixelCopy(info, _pixels, _width * 4);
    }

    private void StartAnimationTimer()
    {
        if (_timerRunning) return;
        _host.StartTimer(AnimationTimerMs);
        _timerRunning = true;
    }

    private void StopAnimationTimer()
    {
        if (!_timerRunning) return;
        _host.StopTimer();
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

    /// <summary>Dispatches a submit event for the form; if not prevented, navigates to the action URL.</summary>
    private void SubmitForm(LayoutNode form, LayoutNode? submitter = null)
    {
        var engine = JsEngine.Instance;
        if (engine == null || _rootNode == null) return;
        if (Interaction.FormSubmitter.PrepareNavigation(form, engine, fireSubmitEvent: true, submitter: submitter)
            is { } request)
            Navigate(request);
    }

    /// <summary>Shows the native Open dialog for an &lt;input type=file&gt;, records the chosen files
    /// in <see cref="FormState"/>, and fires an "input"/"change" event.</summary>
    private void OpenFilePicker(Guid nodeKey)
    {
        if (_rootNode == null) return;
        var node = FindNodeByKey(_rootNode, nodeKey);
        if (node == null) return;

        var multiple = node.Attributes.ContainsKey("multiple");
        var accept = node.Attributes.GetValueOrDefault("accept", "");
        var paths = _host.ShowOpenFileDialog(accept, multiple);
        if (paths.Length == 0) return;

        var selected = new List<Interaction.SelectedFile>();
        foreach (var path in paths)
        {
            try
            {
                var info = new FileInfo(path);
                var text = info.Length <= 5_000_000 ? File.ReadAllText(path) : "";  // text-only model
                var modified = new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeMilliseconds();
                selected.Add(new Interaction.SelectedFile(info.Name, info.Length, GuessMimeType(path), text, modified));
            }
            catch { /* skip unreadable files */ }
        }
        FormState.Files[nodeKey] = selected;

        // Notify listeners that the control's value changed.
        EventDispatcher.Dispatch(nodeKey, "input", _rootNode);
        EventDispatcher.Dispatch(nodeKey, "change", _rootNode);
    }

    private static string GuessMimeType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".svg" => "image/svg+xml",
        ".txt" => "text/plain",
        ".html" or ".htm" => "text/html",
        ".css" => "text/css",
        ".js" => "text/javascript",
        ".json" => "application/json",
        ".pdf" => "application/pdf",
        _ => "application/octet-stream",
    };

    /// <summary>True when two URLs are identical except for the #fragment.</summary>
    private static bool DiffersOnlyByFragment(string a, string b)
    {
        static string StripFragment(string u)
        {
            var i = u.IndexOf('#');
            return i < 0 ? u : u[..i];
        }
        return StripFragment(a) == StripFragment(b);
    }

    /// <summary>Walks up from a node to find its containing &lt;form&gt;.</summary>
    private static LayoutNode? FindAncestorForm(LayoutNode? node)
    {
        for (var p = node?.Parent; p != null; p = p.Parent)
            if (p.TagName == "FORM") return p;
        return null;
    }

    /// <summary>Whether a control submits its form when activated (button/input submit types).</summary>
    private static bool IsSubmitControl(LayoutNode node)
    {
        var type = node.Attributes.GetValueOrDefault("type", "").ToLowerInvariant();
        return node.TagName switch
        {
            "BUTTON" => type is "" or "submit",
            "INPUT" => type is "submit" or "image",
            _ => false,
        };
    }

    private void DispatchMouseEvent(string eventType, LayoutNode node, int x, int y, float contentY, JsEngine engine)
    {
        // Pointer event fires before its compatibility mouse event (mousedown→pointerdown, etc.).
        if (PointerTypeFor(eventType) is { } pointerType)
            DispatchPointerOrMouse(pointerType, node, x, y, contentY, engine, isPointer: true);
        DispatchPointerOrMouse(eventType, node, x, y, contentY, engine, isPointer: false);
    }

    private bool HandleFramePointer(string eventType, int x, int y)
    {
        if (_rootNode is null) return false;
        var contentY = y + _viewport.ScrollY;
        var region = _hitRegions.LastOrDefault(r => r.OwnerPage is not null && r.Bounds.Contains(x, contentY));
        if (region?.OwnerPage is not { } page) return false;
        var node = FindNodeByKey(page.Root, region.NodeKey);
        if (node is null) return false;
        var localX = (int)(x - region.OwnerOffsetX);
        var localY = (int)(contentY - region.OwnerOffsetY);
        var pageY = localY + page.Viewport.ScrollY;
        DispatchMouseEvent(eventType, node, localX, localY, pageY, page.Engine);
        if (eventType == "mouseup")
        {
            var click = new Scripting.Dom.JsEvent();
            click.Init("click", true, true);
            click.clientX = localX;
            click.clientY = localY;
            click.pageX = localX;
            click.pageY = (int)pageY;
            click.button = 0;
            click.isTrusted = true;
            click.target = Scripting.Dom.JsElement.For(page.Engine.RawEngine, node);
            EventDispatcher.DispatchEvent(node, click, page.Engine);
            _host.PostTask();
        }
        (_pixels, _hitRegions) = Drawer.Draw(_width, _height, _rootNode, _viewport);
        _host.Invalidate();
        return true;
    }

    /// <summary>Dispatches a cancelable "wheel" event to the node under the cursor. Returns true if
    /// a listener called preventDefault() (so the host should not scroll).</summary>
    private bool DispatchWheelEvent(LayoutNode? node, int x, int y, float contentY, float deltaY)
    {
        if (node is null || JsEngine.Instance is not { } engine) return false;
        var evt = new Scripting.Dom.JsEvent();
        evt.Init("wheel", true, true);
        evt.clientX = x;
        evt.clientY = y;
        evt.pageX = x;
        evt.pageY = (int)contentY;
        evt.deltaY = deltaY;
        evt.target = Scripting.Dom.JsElement.For(engine.RawEngine, node);
        EventDispatcher.DispatchEvent(node, evt, engine);
        return evt.defaultPrevented;
    }

    private static string? PointerTypeFor(string mouseType) => mouseType switch
    {
        "mousedown" => "pointerdown",
        "mouseup" => "pointerup",
        "mousemove" => "pointermove",
        _ => null,
    };

    private void DispatchPointerOrMouse(string eventType, LayoutNode node, int x, int y, float contentY, JsEngine engine, bool isPointer)
    {
        var evt = new Scripting.Dom.JsEvent();
        evt.Init(eventType, true, true);
        evt.clientX = x;
        evt.clientY = y;
        evt.pageX = x;
        evt.pageY = (int)contentY;
        evt.button = 0;
        evt.isTrusted = true;
        if (isPointer) { evt.pointerId = 1; evt.pointerType = "mouse"; evt.isPrimary = true; }
        evt.target = Scripting.Dom.JsElement.For(engine.RawEngine, node);
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
        evt.Init("click", true, true);
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
