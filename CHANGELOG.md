# Changelog

All notable changes to this project will be documented in this file.

## [0.0.10] - 2026-06-28 (current)

### Added

- **`<iframe>` / nested browsing contexts** — an iframe parses its child document (from `srcdoc` or a same-origin `src`) into an independent `Page` with its own layout tree and JS engine, rendered clipped into the frame box (default 300×150). The host event loop pumps the whole page tree so child timers, observers, and messages run (`Parser`, `Drawer`, `BrowserWindow`)
- **Cross-context JS wiring** — `iframe.contentWindow` (a WindowProxy) and `contentDocument` (same-origin); a child's `window.parent` / `top` / `frameElement`; `window.postMessage` round-trips between parent and child, delivering a `message` event with `data` / `origin` / `source` (`JsWindowProxy`, `JsElement`, `JsEngine`)
- **iframe `load` event** — fired on the iframe element after its child document finishes loading (`Parser`, `EventDispatcher`)
- **`Page` abstraction** — bundles a browsing context's root layout tree, JS engine, document, base URL, and viewport; the first step in replacing the Parser/Drawer/JsEngine static singletons. DOM proxies now resolve their owning engine via `JsEngine.For(rawEngine)` instead of the global `Instance`, so multiple pages coexist (`Page`, `JsEngine`)
- **Acid2 (partial) + gate** — the Acid2 test and its `position:fixed` scroll variant render deterministically and are gated against approved baselines (`baselines/acid2.png`, `baselines/acid2-scrolled.png`). The render is a recognizable smiley (head, eyes, scalp, chin); the mouth/nose detail awaits the deferred CSS 2.1 anonymous-box / margin-collapse work. The harness scrolls to `#top` (as following the in-page link would) so the face comes into view (`AcidRunner`, `RefTestRunner`)
- **`<object>` nested fallback** — an `<object>` renders its `data` resource as a replaced image; when the resource can't be displayed it falls through to its child content, which may be a nested `<object>` (Acid2's eyes are a 3-deep chain) (`Parser`, `Drawer`, `BoxEngine`)
- **`background-attachment: fixed`** — fixed backgrounds are positioned relative to the viewport and clipped to the element box, so they stay put as the element scrolls (`Drawer`, `DrawCommandExtensions`)
- **Appendix/alternate stylesheets** — `<link>` elements whose `rel` token set contains `stylesheet` (e.g. `rel="appendix stylesheet"`) are loaded, including `data:` CSS hrefs; `alternate` stylesheets are skipped (`Parser`)
- **min/max-width & min/max-height clamping for absolute/fixed boxes** — `ResolveAbsoluteBox` clamps the resolved width/height to the min/max box (min wins over max, CSS 2.1 §10.4/§10.7), and approximates shrink-to-fit width from the widest explicit child width instead of defaulting to half the containing block (`BoxEngine`)
- **Tests** — new `IframeTests` (srcdoc child page, default sizing, child rendering, contentDocument, postMessage round-trip, parent/frameElement) with a WPT-style `lite/iframe.html` gate and an **Iframes** Example demo page; new `AcidPrereqTests` covering percent-encoded `data:` images, straight-alpha PNG decode, `<object>` nested fallback, `background-attachment: fixed`, and max-width clamping

### Fixed

- **AngleSharp periodic-value crash** — reading certain periodic/invalid declarations (e.g. `border-color: red yellow black yellow`) threw a `NullReferenceException` deep in AngleSharp.Css; all property reads now go through `GetPropertyValueSafe`, which swallows the failure and ignores the declaration (CSS error recovery) (`DrawCommandExtensions` and all callers)
- **Percent-encoded base64 `data:` images** — `data:image/...;base64,` payloads whose `/` and `=` are percent-encoded (`%2F`, `%3D`, as Acid2 encodes them) now decode correctly (`DataUri`, `ResourceLoader`)
- **Alpha PNG compositing** — images decode with straight (un-premultiplied) alpha so partly-transparent PNGs composite correctly with source-over (Acid2's eyes are two offset transparent PNGs that must overlap into solid yellow) (`ResourceLoader`)
- **Debug logging removed** — the parser's per-element and per-property `[CSS]` console spam (previously always on) is gone (`Parser`)

### Known limitations

- iframe hit-testing does not yet route clicks into child frames (parent-level UI works); cross-document navigation does not dispose child pages; a child's *runtime* class-based restyle reads the active page's cascade (its initial render is fully correct); nested-frame `top` is approximated as `parent`

## [0.0.9] - 2026-06-25

### Added

- **Script execution timing** — classic scripts now run in the correct order: in-position (inline + external without `defer`/`async`) scripts execute in document order during parse, `defer` scripts run after parsing in document order, and `async` scripts are queued on the event loop in any order; ES modules remain deferred per spec (`Parser`)
- **`document.write()`** — `document.write` / `writeln` / `open` / `close` parse markup and append the resulting nodes to `<body>` (`JsDocument`)
- **`<template>` element** — parsed as an inert fragment; `template.content` exposes a `DocumentFragment` holding the parsed children, which are not rendered or laid out (`Parser`, `JsElement`)
- **`<dialog>` element** — `show()`, `showModal()`, `close(returnValue)`, `returnValue`, `validationMessage`, and `setCustomValidity()`; the `open` attribute drives visibility (`JsElement`, `BoxEngine`)
- **`FormData` API** — `new FormData(form)` enumerates a form's successful controls; entries, `get`, `getAll`, `append`, `set`, `delete`, `has` (`JsFormData`)
- **`<progress>` / `<meter>` / `<output>`** — rendered form elements with `value`, `min`, `max`, `low`, `high`, `optimum`, and `for` attributes; `<output>` reflects its referenced controls (`Drawer`, `FormLayout`, `JsElement`)
- **`<details>` open/close collapse** — toggling the `open` attribute re-flows the disclosure content in/out of the layout tree (`BoxEngine`, `JsElement`)
- **`ResizeObserver`** — `new ResizeObserver(cb)`, `observe`/`unobserve`/`disconnect`, `contentRect` entries delivered on the event loop (`JsResizeObserver`)
- **`IntersectionObserver`** — `new IntersectionObserver(cb, options)`, `observe`/`unobserve`/`disconnect`, `isIntersecting` / `intersectionRatio` entries (`JsIntersectionObserver`)
- **`WheelEvent` / `PointerEvent`** — `wheel` dispatched with `deltaX/Y/Z`; pointer events (`pointerdown`/`up`/`move`) dispatched alongside mouse events with `pointerId`, `pointerType`, `pressure` (`JsEvent`, `BrowserWindow`)
- **`<input type="file">`** — native Win32 file open dialog (`Comdlg32`); `input.files` (`FileList` with `name`, `size`, `type`) and `input.value` reflect the selection (`JsFileList`)
- **`multipart/form-data` submission** — `enctype="multipart/form-data"` builds an RFC 7578 body with file parts (filename + content-type); `application/x-www-form-urlencoded` and GET query encoding retained (`FormSubmitter`)
- **`Attr.value` mutation** — setting `attr.value` updates the owning element's attribute and triggers mutation observers (`JsNamedNodeMap`)
- **Tests** — expanded `DomTests`, `FormTests`, and `LayoutTests` suites covering template, dialog, FormData, progress/meter/output, details, observers, and file inputs

### Fixed

- **Self-collapsing margins** — the margin-collapse model now follows CSS 2.1 §8.3.1, including the self-collapsing case (margins of a block whose top/bottom margins adjoin collapse through) (`BoxEngine`)
- **Block-in-inline margin suppression** — margins on block boxes split by inline content are suppressed per §9.2.1.1, preventing spurious vertical spacing in split-inline formatting (`BoxEngine`)

## [0.0.8] - 2026-06-23

### Added

- **Conformance test harness** — new `Lite.Conformance` project with runners for CSS 2.1 reftests, Web Platform Tests, test262, and the Acid tests; headless page rendering, pixel-diff comparison against approved baselines, mismatch-reference support, and a survey mode (`AcidRunner`, `RefTestRunner`, `WptRunner`, `Test262Runner`, `HeadlessPage`, `PixelDiff`, `ConformanceServer`)
- **`window.location`** — `href`, `protocol`, `host`, `hostname`, `port`, `pathname`, `search`, `hash`, `origin`, and `assign` / `replace` / `reload` setters; same-document (fragment-only) changes scroll to the fragment and fire `hashchange` without reloading (`JsLocation`)
- **`window.history`** — `length`, `state`, `back` / `forward` / `go`, `pushState` / `replaceState`; `popstate` events dispatched to window listeners (`JsHistory`)
- **URL APIs** — `URL` (parsing, `href`/`origin`/`pathname`/`search`/`hash` components, `searchParams`) and `URLSearchParams` (`get`, `getAll`, `append`, `set`, `delete`, `has`, `entries`, `toString`) (`JsUrl`, `JsUrlSearchParams`)
- **`navigator`** — `userAgent`, `platform`, `language` (`JsNavigator`)
- **All CSS length units** — centralized `CssUnits` resolver supporting `px`, `em`, `rem`, `%`, `vw`, `vh`, `vmin`, `vmax`, `ex`, `ch`, `pt`, `pc`, `in`, `cm`, `mm`, and `q` across all length-resolving properties (`CssUnits`)
- **`currentcolor` keyword** — `color: currentcolor` resolves against the inherited `color` value (`DrawCommandExtensions`)
- **`MutationObserver`** — `observe(target, options)`, `disconnect`, `takeRecords`; records for `childList`, `attributes`, and `characterData` mutations with the spec's microtask delivery (`JsMutationObserver`)
- **Attribute API** — `element.attributes` as a live `NamedNodeMap`, `getAttributeNode` / `setAttributeNode` / `removeAttributeNode`, and `Attr` nodes with mutable `value` / `name` (`JsNamedNodeMap`, `JsElement`)
- **Geometry probing & DOM layout hooks** — layout nodes carry geometry used by `getBoundingClientRect` and the conformance reftest harness; `JsElement` exposes resolved geometry to JavaScript (`JsEngine`, `JsElement`, `BoxEngine`)
- **`data:` URL parsing** — `DataUri` decodes `data:` URLs (base64 and percent-encoded) for use by `fetch` and image loading (`DataUri`)
- **`XMLHttpRequest` improvements** — async send on a background thread with `readyState`, `status`, `statusText`, `responseText`, `responseURL`, and `onload` / `onerror` / `onreadystatechange` callbacks (`JsXmlHttpRequest`)
- **Acid1 compliance** — the Acid1 reference renders correctly and an 800×600 baseline is approved (`baselines/acid1.png`)
- **Tests** — new `AttributeApiTests`, `CssWideKeywordTests`, `HostObjectTests`, `LayoutTests`, `MutationObserverTests`, and `NavigationTests` suites; `Lite.Example` renamed from `Example`

### Fixed

- **Unitless `line-height`** — a bare-number computed `line-height` (e.g. `font: 10px/1`) is now treated as a font-size multiplier instead of a pixel length, which had collapsed every line box to ~1px and overlapped the Acid1 headings (`BoxEngine`)
- **Box / table / CSS parser / drawer** — a series of correctness improvements to the box model, table column sizing, CSS parser, and renderer landed alongside the conformance work

## [0.0.7] - 2026-06-10

### Added

- **In-page navigation** — clicking a same-origin `<a href>` (and submitting a form) now loads the target document _in place_ instead of opening the OS browser; links to a different origin still open in the system default browser, and pure `#` fragment links are ignored. The document load runs on a background thread so the UI stays responsive
- **Navigation loading animation** — a browser-style indeterminate progress bar sweeps across the top of the window over a dimmed snapshot of the outgoing page while the next page is fetched/parsed/rendered; the page is then revealed with a short cross-fade + slide-up. Interaction is frozen for the duration so the (single-threaded) JS engine is never touched concurrently (`LoadingAnimation`, `PageTransition`)
- **`fetch()`** — Promise-based `fetch(url, options)` with `method` and `body`; the response exposes `ok`, `status`, `statusText`, `url`, `.text()`, and `.json()`. The HTTP request runs on a background thread and the callback is marshaled back onto the event loop; `http(s)` and `data:` URLs supported (`JsFetch`)
- **Web Storage** — `localStorage` (persisted per-origin under `%LocalAppData%/Lite/Storage`) and `sessionStorage` (in-memory for the process); `getItem`, `setItem`, `removeItem`, `clear`, `key(i)`, `length` (`JsStorage`)
- **ES modules** — `<script type="module">`, `import` / `export`, for both inline and `src` modules; specifiers resolved relative to the page base URL and fetched over `http(s)` (`HttpModuleLoader`); classic scripts run first, modules deferred per spec
- **JavaScript event loop** — a macrotask queue drained on the UI thread so timers and `fetch` callbacks never touch Jint off-thread; a Promise microtask checkpoint runs after each task (so `.then()` continuations fire); `queueMicrotask` polyfill installed
- **Form submission** — a `<form>` submits on Enter (from a text input) or when a submit control is activated; a cancelable `submit` event is dispatched first, then an `application/x-www-form-urlencoded` query is built from the form's successful controls and the engine navigates to the resolved `action` (GET query appended). `form.submit()` / `form.reset()` from JavaScript (`FormSubmitter`)
- **Constraint validation** — `required`, `type="email"` / `type="url"`, and `pattern` are validated; `element.validity` (`ValidityState` with `valueMissing` / `typeMismatch` / `patternMismatch` / `valid`), `willValidate`, `checkValidity()`, `reportValidity()` (`FormValidation`)
- **Keyboard events** — `keydown` / `keyup` dispatched to the focused element (or `<body>`) with `key`, `keyCode`, `code`, and `ctrlKey` / `shiftKey` / `altKey` / `metaKey` modifiers
- **Mouse events** — `mousedown` / `mouseup` / `mousemove` dispatched with `clientX/Y`, `pageX/Y`, and `button`
- **Real `innerHTML` / `outerHTML`** — the getters serialize the live node subtree (`HtmlSerializer`, with HTML escaping and void-element handling); the setters parse an HTML fragment (`Parser.ParseFragment`) against the page's full stylesheet cascade and rebuild the children. Added **`insertAdjacentHTML(position, html)`** (`beforebegin` / `afterbegin` / `beforeend` / `afterend`)
- **DOM mutation convenience methods** — `append`, `prepend`, `before`, `after`, `replaceWith`, `remove` (accepting `Element`s or strings), with stylesheet cascade re-resolution applied to inserted subtrees via `StyleResolver`
- **`MouseEvent` / `KeyboardEvent` / `CustomEvent` constructors** — `new CustomEvent(type, { detail })`, `new Event(type, { bubbles, cancelable })`; `event.detail` payload
- **Element form members** — `element.type`, `element.name`, and `element.form` (nearest ancestor `<form>`)
- **Document additions** — `document.URL`, `document.domain`, `document.cookie` (single in-memory jar), `createElementNS`
- **Expanded CSS selectors** — `:empty`, `:checked`, `:disabled`, `:enabled`, `:link`, `:visited` (never matched — no history), `:required`, `:optional`, `:valid`, `:invalid`; the dynamic `:hover` / `:focus` / `:active` classes now also match on detached (parentless) elements
- **Tests** — new `Lite.Tests` project with a lightweight probe/runner and cascade, DOM, event-loop, and form suites (`InternalsVisibleTo` granted to the test project)
- **Multi-page Example site** — the single demo page is split into a navigable site (typography, colors, layout, lists & tables, forms, graphics, transforms & animations, JavaScript DOM) that exercises in-page navigation, the loading animation, `fetch`, Web Storage, modules, form submission, and validation

### Fixed

- **Timer thread-safety** — `setTimeout` / `setInterval` previously invoked the JS callback directly from a `System.Threading.Timer` background thread (and `setTimeout(fn, 0)` fired synchronously during bootstrap); callbacks are now queued onto the UI event loop, since Jint is not thread-safe
- **`Jint` version pinned** — the package reference moved from `3.*` to `3.1.6` for reproducible builds

## [0.0.6] - 2026-04-16

### Added

- **`linear-gradient()`** — `background-image: linear-gradient()` support with angle keywords (`to top/right/bottom/left`, diagonals), degree values, and multi-stop color lists; rendered via `SKShader.CreateLinearGradient` with correct CSS angle convention
- **CSS `transform`** — `rotate()`, `scale()`, `scaleX()`, `scaleY()`, `translate()`, `translateX()`, `translateY()`, `skew()`, `skewX()`, `skewY()` parsed into a composed `SKMatrix`; applied around the element's center (transform-origin: center); deg/rad/turn units supported
- **CSS `filter`** — `blur()` (via `SKImageFilter`), `grayscale()`, `sepia()`, `brightness()`, `contrast()`, `saturate()`, `hue-rotate()`, `invert()`, `opacity()` (all via `SKColorFilter` color matrices); multiple filters composed; applied as a `SaveLayer` paint
- **`text-overflow: ellipsis`** — single-line overflow text truncated with `…` when `overflow: hidden` and `white-space: nowrap`; truncation point found via binary search in `TextMeasure`
- **`position: sticky`** — element sticks at its `top` offset within the viewport while remaining within its parent container; sub-pixel clamping prevents sticking beyond the container bottom
- **`aspect-ratio`** — `width / height` and single-value (`1.5`) syntax; height is derived from content width when no explicit height is set
- **`pointer-events: none`** — hit regions are not registered for the element; mouse events pass through to elements underneath
- **`element.dataset`** — `JsDataset` proxy reads and writes `data-*` attributes with camelCase ↔ kebab-case conversion
- **`animation-play-state`** — `running` / `paused`; pausing freezes elapsed time; resuming continues from the frozen point; toggling via JavaScript works correctly
- **`window.scrollTo(x, y)` / `window.scrollBy(dx, dy)`** — programmatic viewport scrolling; `window.scrollY`, `window.scrollX`, `window.pageXOffset`, `window.pageYOffset` read-only properties
- **`autofocus` attribute** — first element with `autofocus` receives focus automatically after page load
- **Animation lifecycle events** — `animationstart`, `animationend`, `animationiteration` fired on the element; `transitionend` fired when a CSS transition completes; events dispatched through the full capture/bubble chain via `EventDispatcher`

## [0.0.5] - 2026-03-23

### Added

- **`text-transform`** — `uppercase`, `lowercase`, `capitalize`, `none`
- **`letter-spacing`** — character-level spacing with custom draw/measure routines
- **`word-spacing`** — additional space between words
- **`text-indent`** — first-line indent for block text
- **`border-style`** — `solid`, `dotted`, `dashed`, `double`, `groove`, `ridge`, `inset`, `outset`, `none` per side
- **`list-style-type`** — `disc`, `circle`, `square`, `decimal`, `lower-alpha`, `upper-alpha`, `lower-roman`, `upper-roman`, `none`
- **`list-style-position`** — `outside` (default) and `inside` with proper text offset
- **`outline`** — `outline-width`, `outline-color`, `outline-style`, `outline-offset` shorthand and individual properties
- **`background-image`** — `url()` references to raster images (PNG, JPEG) with `background-repeat` (`repeat`, `repeat-x`, `repeat-y`, `no-repeat`), `background-position`, and `background-size` (`cover`, `contain`, `auto`, px/%)
- **`vertical-align`** — `baseline`, `top`, `middle`, `bottom`, `text-top`, `text-bottom`, `sub`, `super` for inline elements
- **`::before` / `::after` pseudo-elements** — CSS `content` property with quoted strings, `open-quote`/`close-quote`, and CSS unicode escape sequences (`\201C`, `\25B6`, etc.); pseudo-element styles (color, font-weight, font-size, display) applied via `StyleOverrides`
- **`border-collapse`** — `collapse` and `separate` on tables
- **`border-spacing`** — horizontal and vertical spacing between table cells
- **Form: `input[type=password]`** — masked text display with bullet characters
- **Form: `input[type=number]`** — numeric input with clickable up/down stepper arrows; respects `min`, `max`, `step` attributes
- **Form: `input[type=range]`** — range slider with click-to-set and mouse drag support; respects `min`, `max`, `step`
- **Form: `input[type=radio]`** — radio button circles with group selection logic (only one per `name` group); proper intrinsic sizing in both inline and flex layout
- **Form: `<textarea>`** — multi-line text input with placeholder, monospace font, word wrapping, and Enter key support for new lines
- **Form: `<select>`** — dropdown select with option list overlay drawn on top of all content; click to open/close; option selection updates displayed value
- **CSS shorthand parsing** — `border-style`, `outline`, `list-style` shorthands decomposed into individual properties

### Fixed

- **Pseudo-element text overlap** — `::before`/`::after` content was drawn on top of the parent's text; now the parent's text is moved into a `#text` child node so all content flows together as inline children
- **CSS unicode escapes** — `ParseContentValue` now decodes CSS escape sequences like `\201C` (left quote) and `\25B6` (triangle) into actual characters via `DecodeCssEscapes`
- **Background image loading** — `DrawBackgroundImage` passed `null` as the base URL to `ResourceLoader.FetchImage`, so relative image paths couldn't resolve; now passes `Parser.BaseUrl`
- **Number input steppers** — click hit region for the text area covered the entire input including the arrow buttons; now the text hit region excludes the 16px arrow zone
- **Range slider stuck dragging** — drag was initiated on mouse-up instead of mouse-down, causing the slider to follow the mouse until the next click; moved drag initiation to `WM_LBUTTONDOWN`
- **Radio button sizing in flex containers** — `FlexEngine.MeasureIntrinsicMain/Cross` returned 0 for form elements with no text/children; added `GetFormIntrinsicSize` to return correct intrinsic dimensions for all form element types
- **Select dropdown z-order** — dropdown overlay was drawn during normal tree traversal and could be covered by later-painted elements; now deferred and drawn after all content
- **List inside position** — `list-style-position: inside` marker was drawn at the content edge causing text overlap; now tracks marker width and offsets the text

## [0.0.4] - 2026-03-22

### Added

- **SVG rendering** — full `SvgRenderer` supporting `<rect>`, `<circle>`, `<ellipse>`, `<line>`, `<polyline>`, `<polygon>`, `<path>` (via `SKPath.ParseSvgPathData`), `<text>`, and `<g>` grouping; `viewBox` scaling, `transform` attribute (translate, scale, rotate, skewX, skewY, matrix), fill/stroke with opacity, stroke-linecap/linejoin, and HSL color parsing
- **`<canvas>` element** — `CanvasRenderingContext2D` exposed to JavaScript with rect operations (`fillRect`, `strokeRect`, `clearRect`), path API (`beginPath`, `moveTo`, `lineTo`, `arc`, `arcTo`, `ellipse`, `quadraticCurveTo`, `bezierCurveTo`, `closePath`, `fill`, `stroke`, `clip`), text (`fillText`, `strokeText`, `measureText`), transforms (`save`, `restore`, `translate`, `rotate`, `scale`, `setTransform`, `resetTransform`), `drawImage`, and full paint state (`fillStyle`, `strokeStyle`, `lineWidth`, `globalAlpha`, `lineCap`, `lineJoin`, `font`)
- **CSS selector engine** — `SelectorEngine` supporting compound selectors: `#id`, `.class`, `tag`, `tag.class`, `tag#id`, attribute selectors (`[attr]`, `[attr=val]`, `[attr^=val]`, `[attr$=val]`, `[attr*=val]`, `[attr~=val]`), combinators (descendant, child `>`, adjacent `+`, general sibling `~`), pseudo-classes (`:first-child`, `:last-child`, `:nth-child()`, `:not()`), and comma-separated selector lists
- **`document.querySelectorAll`** — now uses the full selector engine for complex queries
- **`document.createTextNode`** — creates `#text` layout nodes from JavaScript
- **`document.createDocumentFragment`** — lightweight container for batch DOM mutations
- **`window.getComputedStyle`** — returns a `JsComputedStyle` proxy that reads resolved CSS values from the layout node
- **`XMLHttpRequest`** — synchronous `open`/`send` with `responseText`, `status`, `readyState`, and `onload` callback; supports GET requests to the page origin
- **`TreeWalker`** — `document.createTreeWalker` with `NodeFilter.SHOW_ELEMENT`, `currentNode`, `nextNode()`, `previousNode()`, `parentNode()`, `firstChild()`, `lastChild()`
- **`JsEvent` object** — `type`, `target`, `currentTarget`, `preventDefault()`, `stopPropagation()` passed to event handlers; `event` global available inside inline handlers
- **Element DOM API expansions** — `insertBefore`, `replaceChild`, `cloneNode(deep)`, `nextElementSibling`, `previousElementSibling`, `firstElementChild`, `lastElementChild`, `childNodes`, `closest(selector)`, `matches(selector)`, `getBoundingClientRect()`, `contains(node)`, `ownerDocument`, `nodeType`, `nodeName`, `className` (get/set), `dataset` proxy for `data-*` attributes
- **`element.classList`** — proper `add`, `remove`, `contains`, `toggle` via Jint object property (replaces `classList_add`/`classList_remove` workaround)
- **`element.style` improvements** — `setProperty`/`getPropertyValue`/`removeProperty` methods; camelCase ↔ kebab-case conversion
- **`data-*` attributes** — captured during parse and accessible via `element.dataset` and `getAttribute`
- **Event bubbling** — events now propagate up the DOM tree from target to root, checking handlers at each ancestor; `stopPropagation()` halts the walk
- **`setTimeout` / `setInterval` / `clearInterval`** — timer APIs via `JsWindow` driving the Win32 animation timer
- **`window.innerWidth` / `window.innerHeight`** — viewport dimensions accessible from JavaScript
- **`requestAnimationFrame`** — schedules a callback on the next animation frame tick

### Fixed

- **Font crash on missing typeface** — `SKTypeface.FromFamilyName` returning `null` for uninstalled fonts now falls back to `SKTypeface.Default` instead of passing `null` to `SKFont` constructor
- **Animation color parse exceptions** — `SKColor.Parse` in `AnimationEngine.TryParseColor` threw `ArgumentException` for every numeric value (e.g. opacity `"0.35"`) on every animation frame; replaced with `SKColor.TryParse` to avoid first-chance exceptions
- **SVG zero-dimension guards** — `<rect>` with zero width/height, `<circle>` with zero radius, and `<ellipse>` with zero radii now skip rendering instead of throwing
- **SVG font size floor** — `<text>` font size clamped to minimum 1px
- **Canvas arc with zero radius** — `arc()` and `ellipse()` with zero or negative radius no longer throw `ArgumentException` from `SKPath.ArcTo`
- **Canvas font size floor** — `ParseFont` now clamps parsed size to minimum 1px
- **Border drawing on tiny elements** — `SKRect.Inflate` with negative inset producing an invalid rect now skips `DrawRoundRect` instead of throwing
- **Null text in caret measurement** — `MeasureText` for the text input caret now guards against null text value
- **Font size floor** — `TextMeasure.CreateFont` clamps font size to minimum 1px to prevent zero-size font exceptions
- **Tag name normalization** — Parser now normalizes all tag names to uppercase via `ToUpperInvariant()`, fixing SVG elements that AngleSharp returns in lowercase
- **`#text` node tag casing** — synthetic text nodes now use lowercase `#text` consistently across Parser, BoxEngine, and FlexEngine
- **Window title** — `BrowserWindow` now correctly sets the Win32 window title
- **Selector null pointer** — fixed null reference in CSS selector matching

## [0.0.3] - 2026-03-20

### Added

- **`opacity`** — element opacity (0–1) with composited subtree rendering via temporary SkiaSharp layers
- **`border-radius`** — rounded corners on all box types via `SKRoundRect`; supports `px` and `%` units
- **`box-shadow`** — multi-layer box shadows with offset, blur, spread, and color; `inset` keyword parsed
- **`text-shadow`** — single-layer text shadow with offset, blur, and color
- **`float: left` / `float: right`** — floated elements removed from normal flow with shrink-to-fit sizing; subsequent content narrows around floats
- **`clear: left` / `right` / `both`** — clears past floated elements
- **Scrollbar UI** — visual scrollbar track and thumb rendered when content overflows the viewport; thumb draggable with mouse, track click jumps to position
- **`:hover` pseudo-class** — CSS properties applied on mouse hover with interactive re-render
- **`:focus` pseudo-class** — CSS properties applied when a form input is focused
- **`:active` pseudo-class** — CSS properties applied during mouse-down
- **`@media` queries** — responsive design support with `min-width`, `max-width`, `min-height`, `max-height`, `orientation`; media types `screen`, `all`, `print`; combinators `and`, `not`, comma (OR); re-evaluated on window resize
- **CSS transitions** — `transition` property with `property`, `duration`, `delay`, and `timing-function`; triggers on pseudo-class state changes; interpolates numeric (px, em, %) and color (rgba) values
- **CSS `@keyframes` animations** — `animation` shorthand with `name`, `duration`, `delay`, `timing-function`, `iteration-count` (including `infinite`), `direction` (`alternate`, `reverse`), `fill-mode` (`forwards`, `backwards`, `both`); 60fps timer-driven animation loop
- **Easing functions** — `linear`, `ease`, `ease-in`, `ease-out`, `ease-in-out`, `step-start`, `step-end`, `cubic-bezier(...)` with Newton-Raphson solver
- **`calc()` expressions** — recursive descent evaluator for `calc()` in all length-resolving properties; supports `+`, `-`, `*`, `/` operators and `px`, `%`, `em`, `rem`, `vw`, `vh` units; nested `calc()` flattened
- **CSS custom properties (`--*` / `var()`)** — custom properties declared on any element (including `:root`), inherited via ancestor chain walk; `var(--name)` and `var(--name, fallback)` with recursive resolution; nested `var()` in both resolved values and fallbacks; automatic shorthand expansion for `padding`, `margin`, `gap`

### Fixed

- **Button text wrapping** — added `white-space: nowrap` to prevent "Hover me" button text from wrapping in flex containers
- **`var()` values in non-override properties** — properties like `background-color`, `color`, `padding` containing `var()` references were silently dropped because AngleSharp cannot resolve custom properties; now any property with a `var()` value is stored in StyleOverrides regardless of the property whitelist

## [0.0.2] - 2026-03-18

### Added

- **Inline text elements** — `<strong>`, `<b>`, `<em>`, `<i>`, `<u>`, `<ins>`, `<s>`, `<del>`, `<strike>`, `<small>`, `<sub>`, `<sup>`, `<mark>`, `<code>`, `<kbd>`, `<samp>`, `<var>`, `<tt>` now render correctly via UA stylesheet rules
- **`font-style: italic`** — rendered using the italic typeface slant via SkiaSharp
- **`text-decoration: line-through`** — strikethrough line drawn at the correct baseline offset
- **`text-align`** — `left`, `center`, `right`, and `justify` support for block and inline runs
- **`line-height`** — configurable via px, em, percentage, or unitless multiplier; falls back to `1.4`
- **`white-space`** — `normal`, `nowrap`, `pre`, `pre-wrap`, and `pre-line` modes all implemented
- **`margin: auto` horizontal centering** — fixed-width blocks with `margin-left: auto` / `margin-right: auto` are centered in their container
- **Vertical margin collapsing** — adjacent block siblings now use `max(marginBottom, marginTop)` instead of summing both margins (CSS 2.1 §8.3.1)
- **`<br>` line breaks** — forced line break inside inline runs
- **`<hr>` horizontal rule** — renders as a styled horizontal line respecting `border-top-width` and `border-top-color`
- **`<pre>` and `<blockquote>`** — block layout with correct UA stylesheet margins and monospace font
- **`<dl>`, `<dt>`, `<dd>`** — definition list elements with correct block display and indentation
- **List rendering** — `<ul>` and `<ol>` render bullet (•) and ordered (1.) markers; nested lists supported
- **Mixed inline content** — text nodes interleaved with element children (e.g. `text <strong>bold</strong> more`) are now preserved in DOM order using synthetic `#TEXT` layout nodes
- **Inter-element spacing** — whitespace between inline siblings (e.g. `</label> <input>`) correctly produces a single space; whitespace-only nodes between block siblings are suppressed
- **`label` is inline by default** — matches browser UA stylesheet; `button` gets a default `1px` border
- **Button CSS** — background color, text color, and border colors are now read from computed styles instead of hardcoded gray values
- **Background and borders on `<p>`** — `PaintTextBlock` now paints background and borders before text, consistent with block elements
- **Monospace font mapping** — `monospace`, `ui-monospace`, `Courier`, and `Courier New` map to `Consolas`; `system-ui` variants map to `Segoe UI`
- **`box-sizing: border-box`** — explicit `height` (and `width`) now correctly subtract padding and border to get content size, matching the `* { box-sizing: border-box }` CSS reset
- **Viewport canvas background** — body background color is propagated to the canvas clear color, eliminating the bare margin strip visible at the page edges
- **Absolute element shrink-wrap** — absolutely positioned inline elements (e.g. badges) now measure their text content for width instead of defaulting to half the container width
- **`display: flex` / `display: inline-flex`** — full CSS Flexbox Level 1 layout engine (`FlexEngine`) implementing:
  - `flex-direction`: `row`, `row-reverse`, `column`, `column-reverse`
  - `flex-wrap`: `nowrap`, `wrap`, `wrap-reverse`
  - `flex-grow` and `flex-shrink` with iterative frozen-item resolution (CSS §9.7)
  - `flex-basis` in px, %, or `auto`/`content`
  - `justify-content`: `flex-start`, `flex-end`, `center`, `space-between`, `space-around`, `space-evenly`
  - `align-items` / `align-self`: `stretch`, `flex-start`, `flex-end`, `center`, `baseline`
  - `align-content` for multi-line flex containers
  - `order` property for paint and layout ordering
  - `gap`, `row-gap`, `column-gap`
  - `min-width` / `max-width` / `min-height` / `max-height` clamping on flex items
  - Auto-margin absorption on both axes
  - Baseline alignment in row containers
  - Cross-axis stretch re-layout at final size
  - Static position tracking (`FlexStaticX/Y`) so absolutely positioned children inside a flex container use the correct static position fallback
- **`display: table`** — table layout engine (`TableEngine`) supporting `<table>`, `<thead>`, `<tbody>`, `<tfoot>`, `<tr>`, `<td>`, `<th>`:
  - Two-pass layout: measure natural cell heights in pass 1, stretch all cells to the uniform row height in pass 2
  - Column widths: explicit `width` on any cell takes priority; remaining columns share space evenly
  - Explicit row `height` is honoured as a minimum row height
  - Row groups (`thead`/`tbody`/`tfoot`) are transparent wrappers resolved by tag name
  - UA stylesheet defaults: `1px` padding on `td`/`th`, bold font on `th`
- **`z-index`** — stacking context for `position: absolute`, `position: fixed`, and `position: relative` elements with an explicit `z-index`; negative z-index elements paint first, non-negative positioned elements paint last
- **`overflow: hidden`** — clips child painting to the element's padding box
- **`overflow: scroll` / `overflow: auto`** — same clip behaviour as `hidden` (scrollable axis not yet interactive)
- **`position: relative`** — element shifted by `top`/`left`/`right`/`bottom` without affecting normal flow
- **`position: absolute`** — removed from normal flow, resolved against the nearest positioned ancestor; `left + right` computes width, `top + bottom` computes height
- **`position: fixed`** — resolved against the viewport and painted after scroll restore so it stays on screen
- **`visibility: hidden` / `collapse`** — property parsed and stored via `StyleOverrides`
- **Percentage `width` / `height`** — percentage sizes now correctly resolve against the parent's content dimension; `vh`/`vw` units resolve against the viewport
- **Percentage `height` on children** — `parentContentHeight` is threaded through `LayoutBlock` and `LayoutChildren` so children can resolve `height: 50%` against the actual parent content height
- **`min-width` / `max-width` / `min-height` / `max-height`** — resolved correctly for both px and percentage values; auto min-width detection via `IsAutoMinWidth`
- **Flex CSS extraction workaround** — `ExtractMatchedCssProperties` iterates all matching stylesheet rules and copies flex/gap/visibility properties into `StyleOverrides`, working around AngleSharp not cascading these via `ComputeCurrentStyle()`; `flex` and `flex-flow` shorthands are decomposed automatically
- **`inline-flex` in inline runs** — `display: inline-flex` elements participate in inline formatting contexts as inline-block equivalents, with intrinsic sizing from max-content measurement

### Fixed

- `<hr>` was incorrectly matched by the heading paint path (`H` + digit check) and never rendered
- Inline elements (`<strong>`, `<em>`, `<mark>`, `#TEXT`, etc.) had no paint path and were silently skipped
- `#TEXT` nodes inherited `display: block` from parent computed style; now forced to `display: inline`
- `GetLineHeight` switch had an invalid `or` pattern for `Em`/`Percent` units — split into separate cases
- `overflow: hidden` clip was not restored when the painted node used an early-return code path, causing all subsequent siblings to be clipped to the overflow box
- `position: relative` elements without an explicit `z-index` were incorrectly sorted into the z-index paint pass, causing them to render after `overflow: hidden` siblings whose clip had not been restored
- Whitespace-only text nodes between block siblings produced phantom inline runs adding unwanted line height
- Block-level elements (`display: flex`, `display: table`) inside an inline run no longer get collected into the inline formatting context — the run-collection loop now breaks on `Flex` and `Table` display types

## [0.0.1] - 2026-03-17

### Added

- `BrowserWindow` API — create a native Win32 window that renders a web page from a URL
- HTML parser using AngleSharp with CSS style computation
- Custom two-pass CSS box model layout engine (`BoxEngine`) supporting block and inline line boxes
- SkiaSharp-based renderer (`Drawer`) producing a pixel buffer from the layout tree
- JavaScript runtime (`JsEngine`) powered by Jint with a minimal browser-compatible DOM API
  - `document.getElementById`, `querySelector`, `querySelectorAll`, `createElement`
  - Element proxy with `textContent`, `innerHTML`, `value`, `checked`, `style`, `classList`, attributes, children, and event listeners
  - `console.log/error/warn` and `window.alert`
  - Inline event attribute support (`onclick`, `oninput`, `onchange`)
- Support for common HTML elements: `div`, `section`, `header`, `footer`, `main`, `article`, `nav`, `aside`, `ul`, `ol`, `li`, `form`, `span`, `h1`–`h6`, `p`, `a`, `img`, `input` (text & checkbox), `button`, `label`, `script`
- CSS property support: `display`, `width`, `height`, `margin`, `padding`, `border-width/color/radius`, `background-color`, `color`, `font-size/weight/style`, `text-decoration`, `text-align`, `cursor`
- `FormState` for tracking text input values, checkbox state, and focused element
- `EventDispatcher` for routing click/change/input events to JS handlers
- `ResourceLoader` for HTTP image fetching with bitmap cache
- Scroll support
- Static file server (`StaticFileServer`) in the Example project using ASP.NET Core Kestrel
- NuGet package published
- Example project with a demo page featuring typography, buttons, form inputs, a counter, and a todo list
