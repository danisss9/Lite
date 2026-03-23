# Changelog

All notable changes to this project will be documented in this file.

## [0.0.5] - 2026-03-23 (current)

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
