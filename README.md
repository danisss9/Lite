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
├── BoxEngine           Two-pass CSS box model layout (block + inline line boxes)
│
├── JsEngine            Jint-based JavaScript runtime with DOM API
│   ├── JsDocument      document.getElementById / querySelector / createElement
│   ├── JsElement       Element proxy: textContent, value, style, events, classList
│   ├── JsStyle         Inline style property get/set
│   ├── JsConsole       console.log / error / warn
│   └── JsWindow        window.alert
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
| `div`, `section`, `header`, `footer`, `main`, `article`, `nav`, `aside`, `ul`, `ol`, `li`, `form`, `span` | Generic block or inline containers |
| `h1`–`h6` | Headings with computed font size |
| `p` | Paragraph |
| `a` | Link — opens in the system browser on click |
| `img` | Image loaded via HTTP; falls back to a placeholder |
| `input` (text) | Focusable text field with keyboard input and backspace |
| `input` (checkbox) | Toggle on click |
| `button`, `input[type=submit]` | Triggers `click` event handlers |
| `label` | Rendered as text |
| `script` | Inline and `src` scripts executed after parse |

---

## Supported CSS Properties

Styles are computed by AngleSharp and read at paint time.

| Property | Notes |
|---|---|
| `display` | `block`, `inline`, `inline-block`, `none` |
| `width`, `height` | `px`, `%`, `auto` |
| `margin`, `padding` | Shorthand and individual sides; `px`, `%`, `em`, `rem`, `auto` |
| `border-width` | `px` |
| `border-color` | Any CSS color |
| `border-radius` | `px` |
| `background-color` | Any CSS color |
| `color` | Any CSS color |
| `font-size` | `px`, `em`, `rem`, keyword sizes |
| `font-weight` | `bold` / normal |
| `font-style` | `italic` / normal |
| `text-decoration` | `underline` |
| `text-align` | `left`, `center`, `right` |
| `cursor` | `pointer`, `text`, `default` |

---

## JavaScript DOM API

Scripts run in a Jint sandbox with a minimal browser-compatible API.

### `document`

```js
document.getElementById(id)          // → Element | null
document.querySelector(selector)     // → Element | null   supports: #id, tag, tag#id
document.querySelectorAll(selector)  // → Element[]        supports: #id, tag
document.createElement(tagName)      // → Element
```

### Element

```js
element.id                           // string (read-only)
element.tagName                      // lowercase string (read-only)
element.textContent                  // get/set displayed text
element.innerHTML                    // get/set (simplified — same as textContent)
element.value                        // get/set text input value
element.checked                      // get/set checkbox state

element.style.color = "red"          // set any CSS property as camelCase or kebab-case
element.getAttribute(name)           // → string | null
element.setAttribute(name, value)
element.hasAttribute(name)           // → boolean

element.children                     // → Element[]
element.parentElement                // → Element | null
element.appendChild(child)          // → child
element.removeChild(child)          // → child

element.classList_add(cls)
element.classList_remove(cls)
element.classList_contains(cls)      // → boolean
element.classList_toggle(cls)

element.addEventListener(type, fn)
element.removeEventListener(type, fn)
element.click()
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

The example serves the `Example/resources/` folder on `http://localhost:4444` and opens it in a `BrowserWindow`. The demo page includes typography, buttons, form inputs, a counter, and a todo list.

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
