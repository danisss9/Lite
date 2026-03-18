# Changelog

All notable changes to this project will be documented in this file.

## [0.0.2] - 2026-03-18 (current)

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
