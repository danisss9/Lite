using System.Collections.Concurrent;
using Lite.Models;

namespace Lite.Scripting.Dom;

/// <summary>Resolves the Web IDL interface name a host wrapper must present to script. Names
/// feed <c>__lite_brand</c> (see the JsEngine bootstrap), which chains each wrapper's prototype
/// into the per-interface hierarchy (toStringTag, constructor identity, instanceof).</summary>
internal static class WebIdlInterfaces
{
    private static readonly ConcurrentDictionary<string, string> TagCache = new(StringComparer.Ordinal);

    private static readonly Dictionary<Type, string> TypeMap = new()
    {
        [typeof(JsDocument)] = "HTMLDocument",
        [typeof(JsTreeWalker)] = "TreeWalker",
        [typeof(JsNodeIterator)] = "NodeIterator",
        [typeof(JsNamedNodeMap)] = "NamedNodeMap",
        [typeof(JsAttr)] = "Attr",
        [typeof(JsClassList)] = "DOMTokenList",
        [typeof(JsComputedStyle)] = "CSSStyleDeclaration",
        [typeof(JsStyle)] = "CSSStyleDeclaration",
        [typeof(JsHistory)] = "History",
        [typeof(JsLocation)] = "Location",
        [typeof(JsStorage)] = "Storage",
        [typeof(JsNavigator)] = "Navigator",
        [typeof(JsWindow)] = "Window",
        [typeof(JsWindowProxy)] = "Window",
        [typeof(JsEvent)] = "Event",
        [typeof(JsEventTarget)] = "EventTarget",
        [typeof(JsNodeList)] = "NodeList",
        [typeof(JsHtmlCollection)] = "HTMLCollection",
        [typeof(JsHtmlAllCollection)] = "HTMLAllCollection",
        [typeof(JsBlob)] = "Blob",
        [typeof(JsFile)] = "File",
        [typeof(JsFileList)] = "FileList",
        [typeof(JsUrl)] = "URL",
        [typeof(JsUrlSearchParams)] = "URLSearchParams",
        [typeof(JsFormData)] = "FormData",
        [typeof(JsCanvas)] = "HTMLCanvasElement",
        [typeof(JsCanvasContext2D)] = "CanvasRenderingContext2D",
        [typeof(JsTextMetrics)] = "TextMetrics",
        [typeof(JsBoundingClientRect)] = "DOMRect",
        [typeof(JsMessageChannel)] = "MessageChannel",
        [typeof(JsMessagePort)] = "MessagePort",
        [typeof(JsXmlHttpRequest)] = "XMLHttpRequest",
        [typeof(JsMutationObserver)] = "MutationObserver",
        [typeof(JsMutationRecord)] = "MutationRecord",
        [typeof(JsResizeObserver)] = "ResizeObserver",
        [typeof(JsIntersectionObserver)] = "IntersectionObserver",
        [typeof(JsIntersectionObserverEntry)] = "IntersectionObserverEntry",
    };

    public static string? For(object value)
    {
        if (value is JsElement element) return ForNode(element.Node);
        return TypeMap.TryGetValue(value.GetType(), out var name) ? name : null;
    }

    internal static string ForNode(LayoutNode node) => node.TagName switch
    {
        "#text" => "Text",
        "#comment" => "Comment",
        "#document-type" => "DocumentType",
        "#document-fragment" => "DocumentFragment",
        "#pi" => "ProcessingInstruction",
        _ => TagCache.GetOrAdd(node.TagName, ElementInterface),
    };

    private static string ElementInterface(string tagName)
    {
        // SVG content keeps one interface in the 2014 snapshot; MathML nodes have none
        // (they are plain Elements). Tags are stored uppercased by the projection.
        if (SvgTags.Contains(tagName.ToLowerInvariant())) return "SVGElement";
        return ElementTags.TryGetValue(tagName.ToLowerInvariant(), out var name) ? name : "HTMLElement";
    }

    private static readonly HashSet<string> SvgTags = new(StringComparer.Ordinal)
    {
        "svg", "g", "defs", "symbol", "use", "switch", "view", "desc",
        "path", "rect", "circle", "ellipse", "line", "polyline", "polygon", "text", "tspan",
        "textpath", "marker", "clippath", "mask", "pattern", "lineargradient", "radialgradient",
        "stop", "filter", "foreignobject", "image", "animate", "animatemotion", "animatetransform",
        "set", "feblend", "fecolormatrix", "fecomponenttransfer", "fecomposite", "feconvolvematrix",
        "fediffuselighting", "fedisplacementmap", "fedistantlight", "feflood", "fefunca", "fefuncb",
        "fefuncg", "fefuncr", "fegaussianblur", "femerge", "femergenode", "femorphology",
        "feoffset", "fepointlight", "fespecularlighting", "fespotlight", "fetile", "feturbulence",
    };

    private static readonly Dictionary<string, string> ElementTags = new(StringComparer.Ordinal)
    {
        ["html"] = "HTMLHtmlElement", ["head"] = "HTMLHeadElement", ["body"] = "HTMLBodyElement",
        ["frameset"] = "HTMLFrameSetElement", ["title"] = "HTMLTitleElement",
        ["base"] = "HTMLBaseElement", ["link"] = "HTMLLinkElement", ["meta"] = "HTMLMetaElement",
        ["style"] = "HTMLStyleElement", ["script"] = "HTMLScriptElement",
        ["iframe"] = "HTMLIFrameElement", ["frame"] = "HTMLFrameElement",
        ["div"] = "HTMLDivElement", ["p"] = "HTMLParagraphElement", ["img"] = "HTMLImageElement",
        ["input"] = "HTMLInputElement", ["button"] = "HTMLButtonElement", ["form"] = "HTMLFormElement",
        ["label"] = "HTMLLabelElement", ["fieldset"] = "HTMLFieldSetElement", ["legend"] = "HTMLLegendElement",
        ["select"] = "HTMLSelectElement", ["datalist"] = "HTMLDataListElement",
        ["optgroup"] = "HTMLOptGroupElement", ["option"] = "HTMLOptionElement",
        ["textarea"] = "HTMLTextAreaElement", ["output"] = "HTMLOutputElement",
        ["progress"] = "HTMLProgressElement", ["meter"] = "HTMLMeterElement",
        ["a"] = "HTMLAnchorElement", ["area"] = "HTMLAreaElement", ["map"] = "HTMLMapElement",
        ["table"] = "HTMLTableElement", ["caption"] = "HTMLTableCaptionElement",
        ["colgroup"] = "HTMLTableColElement", ["col"] = "HTMLTableColElement",
        ["thead"] = "HTMLTableSectionElement", ["tbody"] = "HTMLTableSectionElement",
        ["tfoot"] = "HTMLTableSectionElement", ["tr"] = "HTMLTableRowElement",
        ["td"] = "HTMLTableCellElement", ["th"] = "HTMLTableCellElement",
        ["ul"] = "HTMLUListElement", ["ol"] = "HTMLOListElement", ["dl"] = "HTMLDListElement",
        ["li"] = "HTMLLIElement", ["br"] = "HTMLBRElement", ["hr"] = "HTMLHRElement",
        ["pre"] = "HTMLPreElement", ["listing"] = "HTMLPreElement", ["plaintext"] = "HTMLPreElement",
        ["blockquote"] = "HTMLQuoteElement", ["q"] = "HTMLQuoteElement",
        ["canvas"] = "HTMLCanvasElement", ["audio"] = "HTMLAudioElement",
        ["video"] = "HTMLVideoElement", ["source"] = "HTMLSourceElement", ["track"] = "HTMLTrackElement",
        ["param"] = "HTMLParamElement", ["object"] = "HTMLObjectElement", ["embed"] = "HTMLEmbedElement",
        ["applet"] = "HTMLAppletElement", ["marquee"] = "HTMLMarqueeElement",
        ["template"] = "HTMLTemplateElement", ["time"] = "HTMLTimeElement", ["data"] = "HTMLDataElement",
        ["ins"] = "HTMLModElement", ["del"] = "HTMLModElement", ["menu"] = "HTMLMenuElement",
        ["details"] = "HTMLDetailsElement", ["dialog"] = "HTMLDialogElement",
        ["keygen"] = "HTMLKeygenElement", ["font"] = "HTMLFontElement",
        ["h1"] = "HTMLHeadingElement", ["h2"] = "HTMLHeadingElement", ["h3"] = "HTMLHeadingElement",
        ["h4"] = "HTMLHeadingElement", ["h5"] = "HTMLHeadingElement", ["h6"] = "HTMLHeadingElement",
        ["dir"] = "HTMLDirectoryElement",
    };
}
