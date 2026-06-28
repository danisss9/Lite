# Lite

Lite is a lightweight HTML/CSS/JS rendering engine for Windows, written in C#. It parses HTML and CSS, runs a custom layout engine, renders to a native Win32 window using SkiaSharp, and executes JavaScript via Jint. Same-origin links and form submissions navigate in-page — complete with a browser-style loading animation — backed by an event loop with `fetch`, Web Storage, and ES module support.

![Lite App Demo](https://github.com/user-attachments/assets/994d4334-e96a-483a-94c6-b23fc5f0454b)

---

## Projects

| Project    | Type          | Description                                              |
| ---------- | ------------- | -------------------------------------------------------- |
| `Lite/`    | Class library | The rendering engine and `BrowserWindow` API             |
| `Example/` | Executable    | Demo app using the library with a local HTML/CSS/JS site |

---

## Getting Started

### Install

Reference the `Lite` project or build it as a DLL and add a project reference:

```xml
<ItemGroup>
  <ProjectReference Include="..\Lite\Lite.csproj" />
</ItemGroup>
```

### Basic Usage

```csharp
using Lite;

var window = new BrowserWindow("http://localhost:4444");
window.Run();
```

`Run()` is blocking — it parses the URL, creates a native window, and enters the Win32 message loop until the window is closed.

---

## API Reference

### `BrowserWindow`

The main entry point for the library. Creates a native Win32 window that renders a web page.

```csharp
public class BrowserWindow
```

#### Constructor

```csharp
public BrowserWindow(string url, string title = "Lite Browser", int width = 800, int height = 600)
```

| Parameter | Description                                                 |
| --------- | ----------------------------------------------------------- |
| `url`     | The URL to load and render (e.g. `"http://localhost:4444"`) |
| `title`   | Window title bar text                                       |
| `width`   | Initial window width in pixels                              |
| `height`  | Initial window height in pixels                             |

#### Methods

```csharp
public void Run()
```

Loads the URL, creates the window, and starts the message loop. Blocks until the window is closed.

---

## Architecture

```
BrowserWindow          Native window + message loop; in-page navigation (background-thread
│                       document load), keyboard/mouse event dispatch, and the JS event loop pump
│   ├── LoadingAnimation   Indeterminate loading bar shown while the next page loads
│   └── PageTransition     Cross-fade + slide-up reveal of the loaded page
│
├── Parser              Fetches HTML, parses via AngleSharp, builds LayoutNode tree,
│                       loads external CSS/modules, executes scripts via JsEngine;
│                       ParseFragment() backs innerHTML/insertAdjacentHTML
│
├── StyleResolver       Re-applies the stylesheet cascade to nodes inserted at runtime
│
├── FormSubmitter       Builds the submission query + action URL for form navigation
├── FormValidation      HTML5 constraint validation (required, email/url, pattern)
├── HtmlSerializer      Serializes a node subtree for innerHTML/outerHTML
│
├── Drawer              Runs BoxEngine layout, then renders the tree to a SkiaSharp
│                       bitmap; returns the pixel buffer and hit regions
│
├── BoxEngine           Two-pass CSS box model layout (block + inline line boxes,
│                       absolute/fixed positioning, floats)
│
├── FlexEngine          CSS Flexbox Level 1 layout (flex-grow/shrink, wrapping,
│                       alignment, gap, order, baseline)
│
├── TableEngine         Table layout (display:table, tr, td/th — two-pass row sizing)
│
├── AnimationEngine     CSS transitions and @keyframes animations with easing
│   └── AnimationRegistry  Global @keyframes store
│
├── MediaQueryEvaluator Evaluates @media conditions against viewport size
│
├── PseudoClassState    Tracks :hover, :focus, :active state per element
│
├── SvgRenderer         Inline SVG rendering (rect, circle, path, text, etc.)
├── CanvasRenderer      Blits Canvas2D bitmaps onto the page
│
├── JsEngine            Jint-based JavaScript runtime with DOM API; macrotask event loop
│   │                   (timers/fetch) + Promise microtask checkpoint; ES module host
│   ├── JsDocument      document.getElementById / querySelector / createElement /
│   │                   createTextNode / createDocumentFragment / createTreeWalker;
│   │                   URL / domain / cookie
│   ├── JsElement       Element proxy: textContent, innerHTML/outerHTML, value, style,
│   │                   events, classList, DOM traversal + mutation (append/before/after),
│   │                   form association, validity
│   ├── JsStyle         Inline style property get/set / setProperty / removeProperty
│   ├── JsComputedStyle Read-only computed style proxy for getComputedStyle()
│   ├── JsCanvasContext2D  CanvasRenderingContext2D (paths, rects, text, transforms)
│   ├── JsEvent         Event / MouseEvent / KeyboardEvent / CustomEvent (target,
│   │                   bubbling, preventDefault, coordinates, key/modifiers, detail)
│   ├── JsTreeWalker    DOM tree traversal (nextNode, previousNode, parentNode)
│   ├── JsXmlHttpRequest  Synchronous XMLHttpRequest (GET)
│   ├── JsFetch         fetch() backing — background HTTP, resolved on the event loop
│   ├── JsStorage       localStorage (persisted) / sessionStorage (in-memory)
│   ├── HttpModuleLoader  Resolves & fetches ES modules over http(s)
│   ├── JsConsole       console.log / error / warn
│   ├── JsWindow        window.alert / setTimeout / setInterval / requestAnimationFrame
│   └── SelectorEngine  CSS selector matching (compound, combinators, pseudo-classes)
│
├── FormState           Tracks text input values, checkbox/radio state, dropdowns, and focus
├── EventDispatcher     Dispatches click/change/input events to JS handlers
├── ResourceLoader      HTTP image fetching with bitmap cache
└── StaticFileServer    (Example project) ASP.NET Core Kestrel server for local files
```

---

## Supported HTML Elements

| Element                                                                                 | Behaviour                                                                                                            |
| --------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------- |
| `div`, `section`, `header`, `footer`, `main`, `article`, `nav`, `aside`, `form`, `span` | Generic block or inline containers                                                                                   |
| `h1`–`h6`                                                                               | Headings with computed font size and weight                                                                          |
| `p`                                                                                     | Paragraph with block layout                                                                                          |
| `a`                                                                                     | Link — same-origin links navigate in-page (with a loading animation); cross-origin links open in the system browser  |
| `img`                                                                                   | Image loaded via HTTP or `data:` URI (incl. percent-encoded base64); straight-alpha compositing; placeholder + alt   |
| `object`                                                                                | Renders its `data` resource as a replaced image; falls back to child content (incl. nested `<object>`) on failure    |
| `input` (text)                                                                          | Focusable text field with keyboard input and backspace                                                               |
| `input` (password)                                                                      | Masked text field with bullet characters                                                                             |
| `input` (number)                                                                        | Numeric input with up/down stepper arrows; `min`, `max`, `step`                                                      |
| `input` (range)                                                                         | Range slider with click and drag support; `min`, `max`, `step`                                                       |
| `input` (checkbox)                                                                      | Toggle on click                                                                                                      |
| `input` (radio)                                                                         | Radio button with group selection logic by `name` attribute                                                          |
| `textarea`                                                                              | Multi-line text input with placeholder, Enter key, monospace font                                                    |
| `select`                                                                                | Dropdown with option list overlay, click to select                                                                   |
| `button`, `input[type=submit]`                                                          | Triggers `click` handlers; submit controls submit the containing `<form>`                                            |
| `form`                                                                                  | Submits on Enter / submit button — builds a query and navigates to `action` (cancelable `submit` event)             |
| `label`                                                                                 | Inline by default                                                                                                    |
| `strong`, `b`                                                                           | Bold text                                                                                                            |
| `em`, `i`, `cite`, `dfn`                                                                | Italic text                                                                                                          |
| `u`, `ins`                                                                              | Underline                                                                                                            |
| `s`, `del`, `strike`                                                                    | Strikethrough                                                                                                        |
| `small`, `sub`, `sup`                                                                   | Smaller text / subscript / superscript                                                                               |
| `mark`                                                                                  | Yellow highlight                                                                                                     |
| `code`, `kbd`, `samp`, `var`, `tt`                                                      | Monospace font                                                                                                       |
| `pre`                                                                                   | Preformatted block with preserved whitespace                                                                         |
| `blockquote`                                                                            | Indented block quote                                                                                                 |
| `hr`                                                                                    | Horizontal rule                                                                                                      |
| `br`                                                                                    | Forced line break                                                                                                    |
| `ul`, `ol`, `li`                                                                        | Unordered (bullet) and ordered (numbered) lists                                                                      |
| `dl`, `dt`, `dd`                                                                        | Definition lists                                                                                                     |
| `table`, `thead`, `tbody`, `tfoot`, `tr`, `td`, `th`                                    | Table layout                                                                                                         |
| `svg`                                                                                   | Inline SVG with `rect`, `circle`, `ellipse`, `line`, `polyline`, `polygon`, `path`, `text`, `g`; viewBox, transforms |
| `canvas`                                                                                | HTML5 Canvas with `CanvasRenderingContext2D` via JavaScript                                                          |
| `script`                                                                                | Inline and `src` scripts executed after parse                                                                        |

---

## Supported CSS Properties

| Property                                             | Values                                                                                                                  |
| ---------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------- |
| `display`                                            | `block`, `inline`, `inline-block`, `list-item`, `flex`, `inline-flex`, `table`, `table-row`, `table-cell`, `none`       |
| `width`, `height`                                    | `px`, `%`, `vh`, `vw`, `auto`, `calc()`                                                                                 |
| `min-width`, `max-width`, `min-height`, `max-height` | `px`, `%`, `calc()`                                                                                                     |
| `margin`, `padding`                                  | Shorthand and individual sides; `px`, `%`, `em`, `auto`, `calc()`                                                       |
| `border-width`                                       | `px` per side                                                                                                           |
| `border-color`                                       | Any CSS color per side                                                                                                  |
| `border-style`                                       | `solid`, `dotted`, `dashed`, `double`, `groove`, `ridge`, `inset`, `outset`, `none` per side                            |
| `border-radius`                                      | `px`, `%`                                                                                                               |
| `box-sizing`                                         | `border-box`, `content-box`                                                                                             |
| `box-shadow`                                         | Multi-layer; offset, blur, spread, color, `inset`                                                                       |
| `text-shadow`                                        | Offset, blur, color                                                                                                     |
| `background-color`                                   | Any CSS color                                                                                                           |
| `background-image`                                   | `url()`; raster formats (PNG, JPEG)                                                                                     |
| `background-repeat`                                  | `repeat`, `repeat-x`, `repeat-y`, `no-repeat`                                                                           |
| `background-position`                                | Keywords, `px`, `%`                                                                                                     |
| `background-size`                                    | `cover`, `contain`, `auto`, `px`, `%`                                                                                   |
| `background-attachment`                              | `scroll`, `fixed` (fixed backgrounds pin to the viewport)                                                              |
| `color`                                              | Any CSS color                                                                                                           |
| `opacity`                                            | `0`–`1`                                                                                                                 |
| `font-size`                                          | `px`, `em`, keyword sizes                                                                                               |
| `font-weight`                                        | `bold` / normal                                                                                                         |
| `font-style`                                         | `italic` / normal                                                                                                       |
| `font-family`                                        | Named families; `monospace` → Consolas, `system-ui` → Segoe UI                                                          |
| `line-height`                                        | `px`, `em`, `%`, unitless multiplier                                                                                    |
| `text-decoration`                                    | `underline`, `line-through`                                                                                             |
| `text-transform`                                     | `uppercase`, `lowercase`, `capitalize`, `none`                                                                          |
| `text-align`                                         | `left`, `center`, `right`, `justify`                                                                                    |
| `text-indent`                                        | `px`, `em`, `%`                                                                                                         |
| `letter-spacing`                                     | `px`, `em`                                                                                                              |
| `word-spacing`                                       | `px`, `em`                                                                                                              |
| `vertical-align`                                     | `baseline`, `top`, `middle`, `bottom`, `text-top`, `text-bottom`, `sub`, `super`                                        |
| `white-space`                                        | `normal`, `nowrap`, `pre`, `pre-wrap`, `pre-line`                                                                       |
| `position`                                           | `static`, `relative`, `absolute`, `fixed`, `sticky`                                                                     |
| `top`, `right`, `bottom`, `left`                     | `px`, `%`, `calc()`                                                                                                     |
| `z-index`                                            | Integer                                                                                                                 |
| `overflow`                                           | `visible`, `hidden`, `scroll`, `auto`                                                                                   |
| `visibility`                                         | `visible`, `hidden`, `collapse`                                                                                         |
| `float`                                              | `none`, `left`, `right`                                                                                                 |
| `clear`                                              | `none`, `left`, `right`, `both`                                                                                         |
| `flex-direction`                                     | `row`, `row-reverse`, `column`, `column-reverse`                                                                        |
| `flex-wrap`                                          | `nowrap`, `wrap`, `wrap-reverse`                                                                                        |
| `flex-grow`, `flex-shrink`                           | Number                                                                                                                  |
| `flex-basis`                                         | `px`, `%`, `auto`, `calc()`                                                                                             |
| `flex`                                               | Shorthand                                                                                                               |
| `justify-content`                                    | `flex-start`, `flex-end`, `center`, `space-between`, `space-around`, `space-evenly`                                     |
| `align-items`, `align-self`                          | `stretch`, `flex-start`, `flex-end`, `center`, `baseline`                                                               |
| `align-content`                                      | `stretch`, `flex-start`, `flex-end`, `center`, `space-between`, `space-around`                                          |
| `flex-flow`                                          | Shorthand                                                                                                               |
| `gap`, `row-gap`, `column-gap`                       | `px`, `em`, `%`, `calc()`                                                                                               |
| `order`                                              | Integer                                                                                                                 |
| `outline`                                            | `outline-width`, `outline-color`, `outline-style`, `outline-offset`                                                     |
| `list-style-type`                                    | `disc`, `circle`, `square`, `decimal`, `lower-alpha`, `upper-alpha`, `lower-roman`, `upper-roman`, `none`               |
| `list-style-position`                                | `outside`, `inside`                                                                                                     |
| `border-collapse`                                    | `collapse`, `separate`                                                                                                  |
| `border-spacing`                                     | `px`                                                                                                                    |
| `cursor`                                             | `pointer`, `text`, `default`                                                                                            |
| `text-overflow`                                      | `ellipsis`, `clip`                                                                                                      |
| `aspect-ratio`                                       | `width / height`, single value                                                                                          |
| `pointer-events`                                     | `none`, `auto`                                                                                                          |
| `transform`                                          | `rotate()`, `scale()`, `scaleX/Y()`, `translate()`, `translateX/Y()`, `skew()`, `skewX/Y()`; deg/rad/turn               |
| `filter`                                             | `blur()`, `grayscale()`, `sepia()`, `brightness()`, `contrast()`, `saturate()`, `hue-rotate()`, `invert()`, `opacity()` |
| `background-image` (gradient)                        | `linear-gradient()` with angle keywords, `Ndeg`, and multi-stop color lists                                             |
| `animation-play-state`                               | `running`, `paused`                                                                                                     |
| `transition`                                         | `property`, `duration`, `delay`, `timing-function`                                                                      |
| `animation`                                          | `name`, `duration`, `delay`, `timing-function`, `iteration-count`, `direction`, `fill-mode`                             |
| `calc()`                                             | `+`, `-`, `*`, `/`; `px`, `%`, `em`, `rem`, `vw`, `vh`                                                                  |
| `--*` custom properties                              | Declared on any element; inherited via ancestor chain                                                                   |
| `var()`                                              | `var(--name)`, `var(--name, fallback)`; recursive resolution                                                            |
| `@media`                                             | `min-width`, `max-width`, `min-height`, `max-height`, `orientation`; `and`, `not`, comma                                |
| `@keyframes`                                         | `from`/`to`, percentage offsets                                                                                         |
| `:hover`, `:focus`, `:active`                        | Pseudo-class state with interactive re-render                                                                           |
| Structural / form pseudo-classes                     | `:first-child`, `:last-child`, `:nth-child()`, `:not()`, `:empty`, `:checked`, `:disabled`, `:enabled`, `:link`, `:required`, `:optional`, `:valid`, `:invalid` |
| `::before`, `::after`                                | Pseudo-elements with `content` property (strings, `open-quote`/`close-quote`, unicode escapes)                          |

---

## JavaScript DOM API

Scripts run in a Jint sandbox with a minimal browser-compatible API.

### `document`

```js
document.getElementById(id); // → Element | null
document.querySelector(selector); // → Element | null
document.querySelectorAll(selector); // → Element[]
document.createElement(tagName); // → Element
document.createElementNS(ns, tagName); // → Element
document.createTextNode(text); // → Element (#text node)
document.createDocumentFragment(); // → Element (lightweight container)
document.createTreeWalker(root, whatToShow); // → TreeWalker

document.URL; // current page URL
document.domain; // host of the current page
document.cookie; // get/set (single in-memory cookie jar)
```

Selectors support: `#id`, `.class`, `tag`, compound (`tag.class#id`), attribute (`[attr=val]`, `[attr^=val]`, `[attr$=val]`, `[attr*=val]`, `[attr~=val]`), combinators (descendant, `>`, `+`, `~`), pseudo-classes (`:first-child`, `:last-child`, `:nth-child()`, `:not()`, `:empty`, `:checked`, `:disabled`, `:enabled`, `:required`, `:optional`, `:valid`, `:invalid`, `:hover`, `:focus`, `:active`), and comma-separated lists.

### Element

```js
element.id; // string (read-only)
element.tagName; // string (read-only)
element.className; // get/set class attribute
element.textContent; // get/set displayed text
element.innerHTML; // get serializes the subtree; set parses an HTML fragment
element.outerHTML; // get/set including the element's own tag
element.insertAdjacentHTML(position, html); // beforebegin | afterbegin | beforeend | afterend
element.value; // get/set text input value
element.checked; // get/set checkbox state
element.type; // get/set (input type; "text" default for <input>)
element.name; // get/set name attribute
element.nodeType; // 1 (Element)
element.nodeName; // same as tagName
element.ownerDocument; // → document
element.dataset; // proxy for data-* attributes

element.style.color = 'red'; // set any CSS property as camelCase or kebab-case
element.style.setProperty(name, val);
element.style.getPropertyValue(name);
element.style.removeProperty(name);

element.getAttribute(name); // → string | null
element.setAttribute(name, value);
element.hasAttribute(name); // → boolean

element.children; // → Element[]
element.childNodes; // → Element[]
element.parentElement; // → Element | null
element.firstElementChild; // → Element | null
element.lastElementChild; // → Element | null
element.nextElementSibling; // → Element | null
element.previousElementSibling; // → Element | null
element.appendChild(child); // → child
element.removeChild(child); // → child
element.insertBefore(newNode, ref); // → newNode
element.replaceChild(newNode, old); // → old
element.append(...nodesOrStrings); // append children
element.prepend(...nodesOrStrings); // prepend children
element.before(...nodesOrStrings); // insert into parent before this
element.after(...nodesOrStrings); // insert into parent after this
element.replaceWith(...nodesOrStrings); // replace this element
element.remove(); // detach from parent
element.cloneNode(deep); // → Element
element.contains(node); // → boolean
element.closest(selector); // → Element | null
element.matches(selector); // → boolean
element.getBoundingClientRect(); // → { x, y, width, height, top, left, right, bottom }

// Form association & constraint validation
element.form; // → containing <form> | null
element.willValidate; // → boolean
element.validity; // → { valueMissing, typeMismatch, patternMismatch, valid }
element.checkValidity(); // → boolean
element.reportValidity(); // → boolean
form.submit(); // submit the form (navigates to its action)
form.reset(); // reset controls to defaults

element.classList.add(cls);
element.classList.remove(cls);
element.classList.contains(cls); // → boolean
element.classList.toggle(cls);

element.addEventListener(type, fn);
element.removeEventListener(type, fn);
element.click();
```

### Canvas

```js
var canvas = document.getElementById('myCanvas');
var ctx = canvas.getContext('2d');

// Drawing: fillRect, strokeRect, clearRect, fillText, strokeText, measureText
// Paths: beginPath, moveTo, lineTo, arc, arcTo, ellipse, quadraticCurveTo,
//        bezierCurveTo, closePath, rect, fill, stroke, clip
// Transforms: save, restore, translate, rotate, scale, setTransform, resetTransform
// Images: drawImage
// State: fillStyle, strokeStyle, lineWidth, globalAlpha, font, lineCap, lineJoin
```

### `console`

```js
console.log(...)
console.error(...)
console.warn(...)
```

### `window`

```js
window.alert(message);
alert(message); // shorthand
window.getComputedStyle(element); // → CSSStyleDeclaration proxy
window.innerWidth; // viewport width
window.innerHeight; // viewport height
window.scrollTo(x, y); // scroll viewport to absolute position
window.scrollBy(dx, dy); // scroll viewport by relative amount
window.scrollY; // current vertical scroll offset (read-only)
window.scrollX; // current horizontal scroll offset (read-only)
window.pageXOffset; // alias for scrollX
window.pageYOffset; // alias for scrollY
setTimeout(fn, ms);
setInterval(fn, ms);
clearInterval(id);
requestAnimationFrame(fn);
```

### `XMLHttpRequest`

```js
var xhr = new XMLHttpRequest();
xhr.open('GET', '/api/data');
xhr.onload = function () {
  console.log(xhr.responseText);
};
xhr.send();
// Properties: responseText, status, readyState
```

### `fetch`

```js
fetch('/api/data', { method: 'GET' })
  .then(function (res) {
    // res.ok, res.status, res.statusText, res.url
    return res.json(); // or res.text()
  })
  .then(function (data) {
    console.log(data);
  });
```

The request runs on a background thread and the Promise resolves on the event loop. `http(s)` and `data:` URLs are supported. `queueMicrotask(fn)` is also available.

### Web Storage

```js
localStorage.setItem('key', 'value'); // persisted per-origin to disk
localStorage.getItem('key'); // → "value" | null
localStorage.removeItem('key');
localStorage.clear();
localStorage.key(0); // → key name at index | null
localStorage.length; // → number

sessionStorage.setItem('key', 'value'); // in-memory for the process
```

> Property-style access (`localStorage.foo`) is not supported — use `getItem` / `setItem`.

### ES Modules

```html
<script type="module">
  import { greet } from './greet.js';
  greet('world');
</script>
```

`import` / `export` work for inline and `src` modules. Specifiers resolve relative to the page URL and are fetched over `http(s)`. Classic scripts run first; modules are deferred.

### Event object

```js
element.addEventListener('click', function (event) {
  event.type; // "click"
  event.target; // Element that triggered the event
  event.currentTarget; // Element the handler is attached to
  event.preventDefault();
  event.stopPropagation(); // stop bubbling up the DOM tree
  event.stopImmediatePropagation();

  // MouseEvent: event.clientX/clientY, event.pageX/pageY, event.button
  // KeyboardEvent: event.key, event.keyCode, event.code,
  //                event.ctrlKey/shiftKey/altKey/metaKey
});

// Constructors
new Event('my-event', { bubbles: true, cancelable: true });
new CustomEvent('my-event', { detail: { count: 1 } }); // event.detail
```

Dispatched DOM events include `click`, `mousedown` / `mouseup` / `mousemove`, `keydown` / `keyup`, `input`, `change`, and `submit`.

### Inline event attributes

Standard HTML inline handlers are supported:

```html
<button onclick="handleClick()">Click me</button>
<input oninput="handleInput()" onchange="handleChange()" />
```

---

## Running the Example

```bash
dotnet run --project Example
```

The example serves the `Example/resources/` folder on `http://localhost:4444` and opens it in a `BrowserWindow`. It is a multi-page site — typography, colors, layout, lists & tables, forms, graphics, transforms & animations, and JavaScript DOM — linked by an in-app nav bar, so clicking through it exercises in-page navigation and the loading animation. Between them the pages cover inline text elements, lists, forms (text, password, number, range, radio, checkbox, textarea, select) with submission and validation, flexbox layouts, tables, positioning, z-index, overflow clipping, percentage sizing, pseudo-classes (:hover/:focus/:active and structural/form), pseudo-elements (::before/::after), responsive design (@media), CSS animations/transitions (with lifecycle events), calc() expressions, CSS custom properties (var()), text transforms, letter/word spacing, border styles, outlines, background images, vertical alignment, table border collapse, linear gradients, CSS 2D transforms, CSS filters, text-overflow ellipsis, position:sticky, aspect-ratio, pointer-events, dataset, animation-play-state, programmatic scrolling, fetch, Web Storage, and ES modules.

---

## Dependencies

| Package                                                        | Purpose                                     |
| -------------------------------------------------------------- | ------------------------------------------- |
| [AngleSharp](https://github.com/AngleSharp/AngleSharp)         | HTML parsing and CSS style computation      |
| [AngleSharp.Css](https://github.com/AngleSharp/AngleSharp.Css) | CSS extension for AngleSharp                |
| [Jint](https://github.com/sebastienros/jint)                   | JavaScript engine                           |
| [Esprima](https://github.com/sebastienros/esprima-dotnet)      | JavaScript parser (used by Jint)            |
| [SkiaSharp](https://github.com/mono/SkiaSharp)                 | 2D rendering                                |
| Microsoft.AspNetCore.App                                       | Static file server _(Example project only)_ |

---

## Platform

Windows only — the renderer uses Win32 APIs (User32, GDI32) for window creation and bitmap blitting.
