# Lite

Lite is a lightweight HTML/CSS/JS rendering engine for Windows, written in C#. It parses HTML and CSS, runs a custom layout engine, renders to a native Win32 window using SkiaSharp, and executes JavaScript via Jint.

![Lite App Demo](https://github.com/user-attachments/assets/994d4334-e96a-483a-94c6-b23fc5f0454b)

---

## Projects

| Project | Type | Description |
|---|---|---|
| `Lite/` | Class library | The rendering engine and `BrowserWindow` API |
| `Example/` | Executable | Demo app using the library with a local HTML/CSS/JS site |

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

| Parameter | Description |
|---|---|
| `url` | The URL to load and render (e.g. `"http://localhost:4444"`) |
| `title` | Window title bar text |
| `width` | Initial window width in pixels |
| `height` | Initial window height in pixels |

#### Methods

```csharp
public void Run()
```

Loads the URL, creates the window, and starts the message loop. Blocks until the window is closed.

---

## Architecture

```
BrowserWindow
│
├── Parser              Fetches HTML, parses via AngleSharp, builds LayoutNode tree,
│                       loads external CSS, executes scripts via JsEngine
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
├── JsEngine            Jint-based JavaScript runtime with DOM API
│   ├── JsDocument      document.getElementById / querySelector / createElement /
│   │                   createTextNode / createDocumentFragment / createTreeWalker
│   ├── JsElement       Element proxy: textContent, value, style, events, classList,
│   │                   DOM traversal, cloneNode, closest, matches, getBoundingClientRect
│   ├── JsStyle         Inline style property get/set / setProperty / removeProperty
│   ├── JsComputedStyle Read-only computed style proxy for getComputedStyle()
│   ├── JsCanvasContext2D  CanvasRenderingContext2D (paths, rects, text, transforms)
│   ├── JsEvent         Event object with target, bubbling, preventDefault
│   ├── JsTreeWalker    DOM tree traversal (nextNode, previousNode, parentNode)
│   ├── JsXmlHttpRequest  Synchronous XMLHttpRequest (GET)
│   ├── JsConsole       console.log / error / warn
│   ├── JsWindow        window.alert / setTimeout / setInterval / requestAnimationFrame
│   └── SelectorEngine  CSS selector matching (compound, combinators, pseudo-classes)
│
├── FormState           Tracks text input values, checkbox state, and focused element
├── EventDispatcher     Dispatches click/change/input events to JS handlers
├── ResourceLoader      HTTP image fetching with bitmap cache
└── StaticFileServer    (Example project) ASP.NET Core Kestrel server for local files
```

---

## Supported HTML Elements

| Element | Behaviour |
|---|---|
| `div`, `section`, `header`, `footer`, `main`, `article`, `nav`, `aside`, `form`, `span` | Generic block or inline containers |
| `h1`–`h6` | Headings with computed font size and weight |
| `p` | Paragraph with block layout |
| `a` | Link — opens in the system browser on click |
| `img` | Image loaded via HTTP; falls back to a placeholder with alt text |
| `input` (text) | Focusable text field with keyboard input and backspace |
| `input` (checkbox) | Toggle on click |
| `button`, `input[type=submit]` | Triggers `click` event handlers |
| `label` | Inline by default |
| `strong`, `b` | Bold text |
| `em`, `i`, `cite`, `dfn` | Italic text |
| `u`, `ins` | Underline |
| `s`, `del`, `strike` | Strikethrough |
| `small`, `sub`, `sup` | Smaller text / subscript / superscript |
| `mark` | Yellow highlight |
| `code`, `kbd`, `samp`, `var`, `tt` | Monospace font |
| `pre` | Preformatted block with preserved whitespace |
| `blockquote` | Indented block quote |
| `hr` | Horizontal rule |
| `br` | Forced line break |
| `ul`, `ol`, `li` | Unordered (bullet) and ordered (numbered) lists |
| `dl`, `dt`, `dd` | Definition lists |
| `table`, `thead`, `tbody`, `tfoot`, `tr`, `td`, `th` | Table layout |
| `svg` | Inline SVG with `rect`, `circle`, `ellipse`, `line`, `polyline`, `polygon`, `path`, `text`, `g`; viewBox, transforms |
| `canvas` | HTML5 Canvas with `CanvasRenderingContext2D` via JavaScript |
| `script` | Inline and `src` scripts executed after parse |

---

## Supported CSS Properties

| Property | Values |
|---|---|
| `display` | `block`, `inline`, `inline-block`, `list-item`, `flex`, `inline-flex`, `table`, `table-row`, `table-cell`, `none` |
| `width`, `height` | `px`, `%`, `vh`, `vw`, `auto`, `calc()` |
| `min-width`, `max-width`, `min-height`, `max-height` | `px`, `%`, `calc()` |
| `margin`, `padding` | Shorthand and individual sides; `px`, `%`, `em`, `auto`, `calc()` |
| `border-width` | `px` per side |
| `border-color` | Any CSS color per side |
| `border-radius` | `px`, `%` |
| `box-sizing` | `border-box`, `content-box` |
| `box-shadow` | Multi-layer; offset, blur, spread, color, `inset` |
| `text-shadow` | Offset, blur, color |
| `background-color` | Any CSS color |
| `color` | Any CSS color |
| `opacity` | `0`–`1` |
| `font-size` | `px`, `em`, keyword sizes |
| `font-weight` | `bold` / normal |
| `font-style` | `italic` / normal |
| `font-family` | Named families; `monospace` → Consolas, `system-ui` → Segoe UI |
| `line-height` | `px`, `em`, `%`, unitless multiplier |
| `text-decoration` | `underline`, `line-through` |
| `text-align` | `left`, `center`, `right`, `justify` |
| `white-space` | `normal`, `nowrap`, `pre`, `pre-wrap`, `pre-line` |
| `position` | `static`, `relative`, `absolute`, `fixed` |
| `top`, `right`, `bottom`, `left` | `px`, `%`, `calc()` |
| `z-index` | Integer |
| `overflow` | `visible`, `hidden`, `scroll`, `auto` |
| `visibility` | `visible`, `hidden`, `collapse` |
| `float` | `none`, `left`, `right` |
| `clear` | `none`, `left`, `right`, `both` |
| `flex-direction` | `row`, `row-reverse`, `column`, `column-reverse` |
| `flex-wrap` | `nowrap`, `wrap`, `wrap-reverse` |
| `flex-grow`, `flex-shrink` | Number |
| `flex-basis` | `px`, `%`, `auto`, `calc()` |
| `flex` | Shorthand |
| `justify-content` | `flex-start`, `flex-end`, `center`, `space-between`, `space-around`, `space-evenly` |
| `align-items`, `align-self` | `stretch`, `flex-start`, `flex-end`, `center`, `baseline` |
| `align-content` | `stretch`, `flex-start`, `flex-end`, `center`, `space-between`, `space-around` |
| `flex-flow` | Shorthand |
| `gap`, `row-gap`, `column-gap` | `px`, `em`, `%`, `calc()` |
| `order` | Integer |
| `cursor` | `pointer`, `text`, `default` |
| `transition` | `property`, `duration`, `delay`, `timing-function` |
| `animation` | `name`, `duration`, `delay`, `timing-function`, `iteration-count`, `direction`, `fill-mode` |
| `calc()` | `+`, `-`, `*`, `/`; `px`, `%`, `em`, `rem`, `vw`, `vh` |
| `--*` custom properties | Declared on any element; inherited via ancestor chain |
| `var()` | `var(--name)`, `var(--name, fallback)`; recursive resolution |
| `@media` | `min-width`, `max-width`, `min-height`, `max-height`, `orientation`; `and`, `not`, comma |
| `@keyframes` | `from`/`to`, percentage offsets |
| `:hover`, `:focus`, `:active` | Pseudo-class state with interactive re-render |

---

## JavaScript DOM API

Scripts run in a Jint sandbox with a minimal browser-compatible API.

### `document`

```js
document.getElementById(id)          // → Element | null
document.querySelector(selector)     // → Element | null
document.querySelectorAll(selector)  // → Element[]
document.createElement(tagName)      // → Element
document.createTextNode(text)        // → Element (#text node)
document.createDocumentFragment()    // → Element (lightweight container)
document.createTreeWalker(root, whatToShow) // → TreeWalker
```

Selectors support: `#id`, `.class`, `tag`, compound (`tag.class#id`), attribute (`[attr=val]`, `[attr^=val]`, `[attr$=val]`, `[attr*=val]`, `[attr~=val]`), combinators (descendant, `>`, `+`, `~`), pseudo-classes (`:first-child`, `:last-child`, `:nth-child()`, `:not()`), and comma-separated lists.

### Element

```js
element.id                           // string (read-only)
element.tagName                      // string (read-only)
element.className                    // get/set class attribute
element.textContent                  // get/set displayed text
element.innerHTML                    // get/set (simplified — same as textContent)
element.value                        // get/set text input value
element.checked                      // get/set checkbox state
element.nodeType                     // 1 (Element)
element.nodeName                     // same as tagName
element.ownerDocument                // → document
element.dataset                      // proxy for data-* attributes

element.style.color = "red"          // set any CSS property as camelCase or kebab-case
element.style.setProperty(name, val)
element.style.getPropertyValue(name)
element.style.removeProperty(name)

element.getAttribute(name)           // → string | null
element.setAttribute(name, value)
element.hasAttribute(name)           // → boolean

element.children                     // → Element[]
element.childNodes                   // → Element[]
element.parentElement                // → Element | null
element.firstElementChild            // → Element | null
element.lastElementChild             // → Element | null
element.nextElementSibling           // → Element | null
element.previousElementSibling       // → Element | null
element.appendChild(child)           // → child
element.removeChild(child)           // → child
element.insertBefore(newNode, ref)   // → newNode
element.replaceChild(newNode, old)   // → old
element.cloneNode(deep)              // → Element
element.contains(node)               // → boolean
element.closest(selector)            // → Element | null
element.matches(selector)            // → boolean
element.getBoundingClientRect()       // → { x, y, width, height, top, left, right, bottom }

element.classList.add(cls)
element.classList.remove(cls)
element.classList.contains(cls)      // → boolean
element.classList.toggle(cls)

element.addEventListener(type, fn)
element.removeEventListener(type, fn)
element.click()
```

### Canvas

```js
var canvas = document.getElementById("myCanvas");
var ctx = canvas.getContext("2d");

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
window.alert(message)
alert(message)                       // shorthand
window.getComputedStyle(element)     // → CSSStyleDeclaration proxy
window.innerWidth                    // viewport width
window.innerHeight                   // viewport height
setTimeout(fn, ms)
setInterval(fn, ms)
clearInterval(id)
requestAnimationFrame(fn)
```

### `XMLHttpRequest`

```js
var xhr = new XMLHttpRequest();
xhr.open("GET", "/api/data");
xhr.onload = function() { console.log(xhr.responseText); };
xhr.send();
// Properties: responseText, status, readyState
```

### Event object

```js
element.addEventListener("click", function(event) {
    event.type              // "click"
    event.target            // Element that triggered the event
    event.currentTarget     // Element the handler is attached to
    event.preventDefault()
    event.stopPropagation() // stop bubbling up the DOM tree
});
```

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

The example serves the `Example/resources/` folder on `http://localhost:4444` and opens it in a `BrowserWindow`. The demo page covers typography, inline text elements, lists, forms, flexbox layouts, tables, positioning, z-index, overflow clipping, percentage sizing, pseudo-classes (:hover/:focus/:active), responsive design (@media), CSS animations/transitions, calc() expressions, and CSS custom properties (var()).

---

## Dependencies

| Package | Purpose |
|---|---|
| [AngleSharp](https://github.com/AngleSharp/AngleSharp) | HTML parsing and CSS style computation |
| [AngleSharp.Css](https://github.com/AngleSharp/AngleSharp.Css) | CSS extension for AngleSharp |
| [Jint](https://github.com/sebastienros/jint) | JavaScript engine |
| [Esprima](https://github.com/sebastienros/esprima-dotnet) | JavaScript parser (used by Jint) |
| [SkiaSharp](https://github.com/mono/SkiaSharp) | 2D rendering |
| Microsoft.AspNetCore.App | Static file server *(Example project only)* |

---

## Platform

Windows only — the renderer uses Win32 APIs (User32, GDI32) for window creation and bitmap blitting.
