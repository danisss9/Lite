using AngleSharp.Io;
using AngleSharp;
using AngleSharp.Css;
using AngleSharp.Dom;
using AngleSharp.Css.Dom;
using AngleSharp.Css.Values;
using AngleSharp.Scripting;
using AngleSharp.Xml;
using Lite.Animation;
using Lite.Extensions;
using Lite.Layout;
using Lite.Models;
using Lite.Network;
using Lite.Scripting;
using Lite.Scripting.Dom;
using System.Text;
using System.Text.RegularExpressions;

namespace Lite;

internal static partial class Parser
{
    /// <summary>Applies counter-reset/counter-increment for <paramref name="node"/> in document order
    /// and snapshots the current counter state onto it. Returns the counter names this node pushed
    /// via counter-reset (to be popped after its subtree). Call BEFORE traversing children.</summary>
    private static void ApplyCounters(LayoutNode node, IElement element, out List<string> pushed)
    {
        pushed = new List<string>();
        // An implicit reset made by this element scopes over its following siblings, so it
        // belongs to the enclosing element's list (document root when there is none).
        var enclosing = Current.CounterScopes.Count > 0 ? Current.CounterScopes.Peek() : null;
        // §12.4: an element that is not displayed cannot increment or reset a counter. The
        // authored keyword decides: table-column(-group) maps to no box internally, yet such
        // elements are displayed and their counters apply (counter-increment-applies-to-005).
        var display = node.TryResolveStyle("display", out var ov) ? ov : node.Style.GetPropertyValueSafe("display");
        if (!display.Trim().Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            // The rule channel only carries matched style rules; an inline declaration outranks
            // every non-important rule (§6.4.3) and lives in the style attribute
            // (content-counter-007's span style="counter-reset: c 48").
            var inline = element.GetAttribute("style") is { Length: > 0 } css ? ParseDeclarations(css).Item1 : null;
            string? Counter(string prop) => inline?.GetValueOrDefault(prop) ?? node.StyleOverrides.GetValueOrDefault(prop);
            var reset = Counter("counter-reset");
            ApplyCounterProperties(reset, Counter("counter-increment"), enclosing);

            // §12.4: "the 'list-item' counter is automatically created and incremented for every
            // list item". It is reset by the item's list — 0, or start-1 from HTML's 'start'
            // hint — unless the author's own counter-reset names it (list-style-type-017's
            // `ol { counter-reset: list-item -3 }` must win).
            if (display.Trim().Equals("list-item", StringComparison.OrdinalIgnoreCase))
                ApplyCounterProperties(null, "list-item", enclosing);
            else if (TagOf(element) is "OL" or "UL" or "DIR" or "MENU")
            {
                // HTML's 'start' hint sets the list's first ordinal (start-1 before the item's
                // increment) and is applied after the author's own reset — list-style-type-017
                // writes both `counter-reset: list-item -3` and `start="-3"` and expects -3.
                // Without 'start', the author's reset (if it names list-item) stands, else the
                // list restarts the counter at 0 for its items (§12.4's implicit reset).
                if (TagOf(element) == "OL" && int.TryParse(element.GetAttribute("start"), out var parsedStart))
                    ApplyCounterProperties($"list-item {parsedStart - 1}", null, enclosing);
                else if (reset is null || !reset.Contains("list-item", StringComparison.OrdinalIgnoreCase))
                    ApplyCounterProperties("list-item 0", null, enclosing);
            }
        }
        var (values, stacks) = SnapshotCounters();
        node.CounterValues = values;
        node.CounterStacks = stacks;
    }

    /// <summary>Applies a pseudo-element's counter-reset/counter-increment (§12.4: they apply
    /// to pseudo-elements too) and snapshots the counters its content reads. Instances it
    /// creates — explicit or implicit — join <paramref name="scope"/>.</summary>
    private static (Dictionary<string, int>? Values, Dictionary<string, List<int>>? Stacks) PseudoCounters(
        LayoutNode node, Dictionary<string, string>? pseudoStyles, List<string> scope)
    {
        if (pseudoStyles is null || !pseudoStyles.ContainsKey("content") ||
            ResolvePseudoDisplay(node, pseudoStyles).Equals("none", StringComparison.OrdinalIgnoreCase))
            return (node.CounterValues, node.CounterStacks);
        ApplyCounterProperties(pseudoStyles.GetValueOrDefault("counter-reset"),
            pseudoStyles.GetValueOrDefault("counter-increment"), scope);
        return SnapshotCounters();
    }

    /// <summary>§12.4.1: a counter-reset's scope is the element, its descendants AND its following
    /// siblings with theirs, so the instance joins <paramref name="scope"/> — the enclosing
    /// element's list, popped when the PARENT ends (never popped when null: the root). A reset of
    /// a name this scope level already holds (a previous sibling's, or this element's own)
    /// replaces that instance instead of nesting under it; popping it at the resetting element's
    /// own end made the next sibling count on from the outer counter (content-counter-007: 49,
    /// then 13 instead of 50). An increment of a counter in no scope is an implicit reset to 0
    /// on the same box, scoped the same way.</summary>
    private static void ApplyCounterProperties(string? reset, string? increment, List<string>? scope)
    {
        if (reset is not null && !reset.Equals("none", StringComparison.OrdinalIgnoreCase))
            foreach (var part in ParseCounterSpec(reset, 0))
            {
                if (!_counters.TryGetValue(part.Name, out var stack))
                    _counters[part.Name] = stack = new List<int>();
                if (scope is not null && scope.Contains(part.Name) && stack.Count > 0)
                    stack[^1] = part.Value;         // a later sibling's reset replaces the instance
                else
                {
                    stack.Add(part.Value);          // push a new nested instance
                    scope?.Add(part.Name);
                }
            }

        if (increment is not null && !increment.Equals("none", StringComparison.OrdinalIgnoreCase))
            foreach (var part in ParseCounterSpec(increment, 1))
            {
                if (!_counters.TryGetValue(part.Name, out var stack))
                    _counters[part.Name] = stack = new List<int>();
                if (stack.Count == 0)
                {
                    stack.Add(0);
                    scope?.Add(part.Name);
                }
                stack[^1] += part.Value;
            }
    }

    private static (Dictionary<string, int>? Values, Dictionary<string, List<int>>? Stacks) SnapshotCounters()
    {
        Dictionary<string, int>? values = null;
        Dictionary<string, List<int>>? stacks = null;
        foreach (var (name, stack) in _counters)
        {
            if (stack.Count == 0) continue;
            (values ??= new())[name] = stack[^1];
            (stacks ??= new())[name] = new List<int>(stack);
        }
        return (values, stacks);
    }

    /// <summary>Pops the counter instances pushed by a node's counter-reset when its subtree ends.</summary>
    private static void PopCounters(List<string> pushed)
    {
        foreach (var name in pushed)
            if (_counters.TryGetValue(name, out var stack) && stack.Count > 0)
                stack.RemoveAt(stack.Count - 1);
    }

    /// <summary>
    /// If the node has ::before or ::after styles with a content property,
    /// creates synthetic inline children at the start/end of the children list.
    /// </summary>
    /// <param name="directTextRaw">The element's own text before it was trimmed, so a boundary
    /// space that sits between the text and an adjacent pseudo-element can be restored — that
    /// space is mid-line, not end-of-line, so §16.6.1 keeps it.</param>
    /// <param name="beforeCounters">Counter snapshot ::before's content reads (after its own
    /// counter properties, before the children).</param>
    /// <param name="afterCounters">Counter snapshot ::after's content reads (after the children
    /// and its own counter properties).</param>
    private static void CreatePseudoElementChildren(LayoutNode node, string directTextRaw,
        (Dictionary<string, int>? Values, Dictionary<string, List<int>>? Stacks) beforeCounters,
        (Dictionary<string, int>? Values, Dictionary<string, List<int>>? Stacks) afterCounters,
        int beforeQuoteDepth = 0, int afterQuoteDepth = 0)
    {
        string? ContentWith((Dictionary<string, int>? Values, Dictionary<string, List<int>>? Stacks) counters, string content,
            Dictionary<string, string> pseudoStyles, int quoteDepth)
        {
            var (values, stacks) = (node.CounterValues, node.CounterStacks);
            (node.CounterValues, node.CounterStacks) = counters;
            try { return ParseContentValue(content, node, new QuoteState(QuotePairs(node, pseudoStyles), quoteDepth)); }
            finally { (node.CounterValues, node.CounterStacks) = (values, stacks); }
        }

        // The has-content pre-check runs at each pseudo's OWN quote depth with the element's
        // own quote pairs — a depth-0 evaluation drops a :after made purely of close-quote
        // keywords (they emit nothing un-nested), though at its real depth (after :before's
        // opens) the same content renders the closing text (the quotes family's "PASS" =
        // "PA" | "SS" from open-quote/close-quote pairs).
        bool hasBefore = node.BeforeStyles != null && node.BeforeStyles.TryGetValue("content", out var beforeContent)
                         && ParseContentValue(beforeContent!, node,
                             new QuoteState(QuotePairs(node, node.BeforeStyles!), beforeQuoteDepth)) != null;
        bool hasAfter = node.AfterStyles != null && node.AfterStyles.TryGetValue("content", out var afterContent)
                        && ParseContentValue(afterContent!, node,
                            new QuoteState(QuotePairs(node, node.AfterStyles!), afterQuoteDepth)) != null;

        if (!hasBefore && !hasAfter) return;

        // If the parent node has its own text, move it into a #text child so that
        // pseudo-elements and the original text flow together as inline children.
        if (!string.IsNullOrEmpty(node.DisplayText))
        {
            // Put back the single collapsed space on whichever side a pseudo-element continues the
            // line, e.g. "<div>Filler text </div>" + a ::after must render "Filler text Filler text".
            var keepLead = hasBefore && directTextRaw.Length > 0 && char.IsWhiteSpace(directTextRaw[0]);
            var keepTrail = hasAfter && directTextRaw.Length > 0 && char.IsWhiteSpace(directTextRaw[^1]);
            var ownText = (keepLead ? " " : "") + node.DisplayText + (keepTrail ? " " : "");
            var textChild = new LayoutNode(null, "#text", ownText, node.Style);
            textChild.ResetNonInheritedStyles();
            textChild.StyleOverrides["display"] = "inline";
            textChild.Parent = node;
            node.Children.Add(textChild);
            node.TextOverride = "";
        }

        if (hasBefore)
        {
            var text = ContentWith(beforeCounters, node.BeforeStyles!["content"], node.BeforeStyles!, beforeQuoteDepth);
            var pseudoNode = new LayoutNode(null, "#pseudo-before", text!, node.Style);
            // 'content: url(...)': the box is the image (§12.2) — empty text, fetched bitmap.
            if (TryParseContentUrl(node.BeforeStyles!["content"]) is { } beforeUrl)
                pseudoNode.Image = ResourceLoader.FetchImage(beforeUrl, _documentBaseUrl ?? _baseUrl, Session);
            pseudoNode.StyleOverrides["display"] = ResolvePseudoDisplay(node, node.BeforeStyles!);
            foreach (var (p, v) in node.BeforeStyles!)
            {
                // 'display' is skipped: it was already resolved above (css-wide keywords), and
                // copying the raw value here would overwrite that with e.g. the literal "inherit".
                if (p != "content" && p != "display")
                    pseudoNode.StyleOverrides[p] = ResolvePseudoValue(node, p, v);
            }
            // CSS 2.1 §10.8.1: 'vertical-align' does not inherit and its initial value is
            // 'baseline'. The generated box borrows the originating element's style object, so an
            // element whose own value is 'middle' — every table cell through the UA sheet — leaked
            // it into its ::before and the first line grew by the disagreement with the strut
            // (content-037's green "PASS PASS" sat 2px below the same text in the reference cell).
            pseudoNode.StyleOverrides.TryAdd("vertical-align", "baseline");
            NormalizeGeneratedWhitespace(pseudoNode);
            pseudoNode.Parent = node;
            node.Children.Insert(0, pseudoNode);
        }

        if (hasAfter)
        {
            var text = ContentWith(afterCounters, node.AfterStyles!["content"], node.AfterStyles!, afterQuoteDepth);
            var pseudoNode = new LayoutNode(null, "#pseudo-after", text!, node.Style);
            if (TryParseContentUrl(node.AfterStyles!["content"]) is { } afterUrl)
                pseudoNode.Image = ResourceLoader.FetchImage(afterUrl, _documentBaseUrl ?? _baseUrl, Session);
            pseudoNode.StyleOverrides["display"] = ResolvePseudoDisplay(node, node.AfterStyles!);
            foreach (var (p, v) in node.AfterStyles!)
            {
                // 'display' is skipped: it was already resolved above (css-wide keywords), and
                // copying the raw value here would overwrite that with e.g. the literal "inherit".
                if (p != "content" && p != "display")
                    pseudoNode.StyleOverrides[p] = ResolvePseudoValue(node, p, v);
            }
            // §10.8.1: 'vertical-align' does not inherit; the generated box starts at 'baseline',
            // not at the originating element's value (see the ::before case above).
            pseudoNode.StyleOverrides.TryAdd("vertical-align", "baseline");
            NormalizeGeneratedWhitespace(pseudoNode);
            pseudoNode.Parent = node;
            node.Children.Add(pseudoNode);
        }
    }

    /// <summary>
    /// CSS 2.1 §12.5.1: the list-item marker is an inline box — "the first inline box in the
    /// principal block box" for 'list-style-position: inside', and a box outside the principal
    /// block box for 'outside'. Generating it as a real child (rather than painting it over the
    /// content box at a reserved indent) is what gives an empty item the marker's line box, puts
    /// an inside marker on a line of its own above a block child (list-style-position-005/023),
    /// and lets the item's text wrap after it so continuation lines start at the content edge —
    /// the hanging indent of list-style-position-020/022.
    /// <para>An 'outside' marker is painted by the list-item painter, so its box is zero-width
    /// and only added when the item has no other in-flow content: the item still gets the marker's
    /// line (an empty <c>&lt;li&gt;</c> is one line tall), which places the marker of an empty item
    /// where the reference puts it (list-style-applies-to-009/012).</para>
    /// </summary>
    private static void CreateListMarkerChild(LayoutNode node, IElement element)
    {
        if (node.GetDisplay() != DisplayType.ListItem) return;

        // Idempotent across re-projections: replace any marker from an earlier pass.
        node.Children.RemoveAll(c => c.TagName == "#pseudo-marker");

        var position = node.GetListStylePosition();
        var type = node.GetListStyleType();
        var imageUrl = node.GetListStyleImage();
        var image = imageUrl is null or "none" ? null
            : ResourceLoader.FetchImage(imageUrl, _documentBaseUrl ?? _baseUrl, Session);
        // A loadable 'list-style-image' replaces the type's marker; 'none' without an image has
        // no marker box at all (§12.6.2).
        if (image is null && type == Lite.Extensions.ListStyleType.None) return;

        var marker = new LayoutNode(null, "#pseudo-marker", "", node.Style);
        marker.ResetNonInheritedStyles();
        marker.StyleOverrides["display"] = "inline";
        // §10.8.1: the generated box is its own inline box and 'vertical-align' does not inherit.
        marker.StyleOverrides["vertical-align"] = "baseline";

        if (position == ListStylePosition.Inside)
        {
            if (image is not null)
            {
                marker.Image = image;
                marker.StyleOverrides["margin-right"] = "4px";
            }
            else
            {
                // §12.4: the ordinal is the implicit 'list-item' counter (falling back to the
                // sibling position for a node with no snapshot).
                var index = 1;
                if (node.CounterValues?.TryGetValue("list-item", out var count) == true) index = count;
                else
                    for (var sibling = element.PreviousElementSibling; sibling is not null;
                         sibling = sibling.PreviousElementSibling)
                        if (TagOf(sibling) == "LI") index++;
                var keyword = CssCounterStyles.Keyword(type);
                if (CssCounterStyles.InsideMarkerText(index, keyword) is not { } text) return;
                marker.TextOverride = text;
                // The bullet styles are drawn as shapes by the list-item painter (§12.6.2's
                // disc/circle/square are figures, not glyphs). Its shape is larger and rounder
                // than the font's bullet character, so the glyph keeps the line's width while
                // staying invisible under the drawn figure.
                if (!CssCounterStyles.HasTextMarker(keyword))
                    marker.StyleOverrides["color"] = "transparent";
            }

            // The item's own (folded) text becomes a #text child after the marker — the same
            // representation ::before uses — so the marker and content share the first line.
            if (!string.IsNullOrEmpty(node.DisplayText))
            {
                var textChild = new LayoutNode(null, "#text", node.DisplayText, node.Style);
                textChild.ResetNonInheritedStyles();
                textChild.StyleOverrides["display"] = "inline";
                textChild.Parent = node;
                node.Children.Add(textChild);
                node.TextOverride = "";
            }
        }
        else
        {
            if (HasInFlowContent(node)) return;
            // The marker paints outside the box, so its line slot is zero-width; a zero-width
            // space is content that creates the line (§9.4.2: a line exists only if something
            // on it renders) without advancing the pen or painting ink.
            marker.TextOverride = "\uFEFF";
        }

        marker.Parent = node;
        node.Children.Insert(0, marker);
    }

    /// <summary>True when the node or a descendant contributes an in-flow box (text, a normal
    /// element, ::before/::after content). Floats, absolutely positioned boxes and display:none
    /// subtrees do not: an item holding only those is still "empty" for its marker line.</summary>
    private static bool HasInFlowContent(LayoutNode node)
    {
        if (node.GetDisplay() == DisplayType.None) return false;
        if (node.GetFloat() != FloatType.None) return false;
        if (node.GetPosition() is PositionType.Absolute or PositionType.Fixed) return false;
        if (!string.IsNullOrEmpty(node.DisplayText)) return true;
        return node.Children.Any(HasInFlowContent);
    }

    /// <summary>
    /// CSS 2.1 §5.12.1: splits off the first letter of a block's first line into a real inline
    /// box carrying the <c>::first-letter</c> declarations, so it takes part in layout — it
    /// contributes its own font metrics and 'line-height' to the line box, its width to line
    /// breaking, and it paints backgrounds, borders and every other property through the ordinary
    /// inline path. Styling it at paint time instead (re-drawing the first characters in another
    /// font) left the line box sized for the parent's font, so a larger first letter overlapped
    /// the line above and the whole block sat at the wrong height.
    /// <para>The "first letter" is the first letter plus any Ps/Pe/Pi/Pf/Po punctuation that
    /// precedes or follows it. Text that is only punctuation yields no first-letter box.</para>
    /// </summary>
    private static void CreateFirstLetterChild(LayoutNode node)
    {
        if (node.FirstLetterStyles is not { Count: > 0 } styles) return;
        // Only a block container gets a ::first-letter; an inline one inherits its block's.
        if (node.StyleOverrides.TryGetValue("display", out var d) && d is "none" or "inline") return;

        // The first letter comes from the first in-flow text in the block: the node's own text, or
        // the first descendant that has some (a leading ::before box, or an inline child).
        var host = FindFirstTextHost(node);
        if (host is null) return;

        var text = host.DisplayText;
        var len = FirstLetterLength(text);
        if (len <= 0) return;

        var letter = new LayoutNode(null, "#pseudo-first-letter", text[..len], host.Style);
        letter.ResetNonInheritedStyles();
        letter.StyleOverrides["display"] = "inline";
        foreach (var (p, v) in styles)
            if (p != "content" && p != "display")
                letter.StyleOverrides[p] = ResolvePseudoValue(node, p, v);
        // §10.8.1: the ::first-letter box is its own inline box, so its undeclared
        // 'vertical-align' is the initial 'baseline', not the host element's value.
        letter.StyleOverrides.TryAdd("vertical-align", "baseline");

        var rest = new LayoutNode(null, "#text", text[len..], host.Style);
        rest.ResetNonInheritedStyles();
        rest.StyleOverrides["display"] = "inline";
        // The tail is an inline child once the letter is split off, so it starts from the
        // initial baseline rather than reading the host's non-inherited 'vertical-align'
        // (a table cell's UA 'middle' pushed the rest of the text 2px down:
        // first-letter-selector-007).
        rest.StyleOverrides["vertical-align"] = "baseline";

        // The host keeps its box (and its own styles) but hands its text to the two new children,
        // which flow inside it as ordinary inline content.
        host.TextOverride = "";
        letter.Parent = host;
        rest.Parent = host;
        host.Children.Insert(0, rest);
        host.Children.Insert(0, letter);
    }

    /// <summary>The nearest node in document order whose own text would start
    /// <paramref name="node"/>'s first line: the node itself, or its first inline descendant with
    /// text. Stops at anything that is not inline-level — a block child starts its own first line
    /// and gets its own ::first-letter.</summary>
    private static LayoutNode? FindFirstTextHost(LayoutNode node)
    {
        if (!string.IsNullOrEmpty(node.DisplayText)) return node;
        foreach (var child in node.Children)
        {
            // The list marker is not part of the item's first letter (§12.5.1 places it before
            // the content): `li::first-letter` styles the content, not the bullet.
            if (child.TagName == "#pseudo-marker") continue;
            // Floats and absolutely positioned boxes are not on the first line; an atomic inline
            // (inline-block/-table) or a block ends the search — ::first-letter never reaches into
            // them (§5.12.2; first-letter-selector-017's red inline-block p).
            if (child.TagName != "#text" && (child.GetFloat() != FloatType.None ||
                child.GetPosition() is PositionType.Absolute or PositionType.Fixed))
                continue;
            // Replaced content and a forced break are CONTENT on the line: the first letter
            // matches only when nothing precedes it (§5.12.2 — an <img> or <br> before the text
            // stops the match entirely; first-letter-selector-002/005/016's red first letter).
            if (child.TagName is "BR" || BoxEngine.IsReplacedImage(child)) return null;
            if (child.TagName != "#text" && child.GetDisplay() != DisplayType.Inline)
                return null;
            if (!string.IsNullOrWhiteSpace(child.DisplayText)) return child;
            if (child.Children.Count > 0 && FindFirstTextHost(child) is { } inner) return inner;
        }
        return null;
    }

    /// <summary>Length of the ::first-letter run at the start of <paramref name="text"/>: any
    /// leading Ps/Pe/Pi/Pf/Po punctuation, the letter itself, and any punctuation directly after
    /// it (CSS 2.1 §5.12.1). Zero when there is no letter at all. The walk is per CODE POINT:
    /// astral punctuation (first-letter-punctuation-334's U+10100 word separators) is a
    /// surrogate pair, and a code-unit walk split half a pair, corrupting the text.</summary>
    internal static int FirstLetterLength(string text)
    {
        static bool IsFirstLetterPunctuation(int codePoint) =>
            System.Globalization.CharUnicodeInfo.GetUnicodeCategory(codePoint) is
                System.Globalization.UnicodeCategory.OpenPunctuation or
                System.Globalization.UnicodeCategory.ClosePunctuation or
                System.Globalization.UnicodeCategory.InitialQuotePunctuation or
                System.Globalization.UnicodeCategory.FinalQuotePunctuation or
                System.Globalization.UnicodeCategory.OtherPunctuation;
        static int LengthOf(char high, char next) => char.IsHighSurrogate(high) && char.IsLowSurrogate(next) ? 2 : 1;
        static int CodePointAt(string s, int i) => char.IsSurrogatePair(s[i], i + 1 < s.Length ? s[i + 1] : ' ')
            ? char.ConvertToUtf32(s, i) : s[i];

        var i = 0;
        while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
        while (i < text.Length && IsFirstLetterPunctuation(CodePointAt(text, i)))
            i += LengthOf(text[i], i + 1 < text.Length ? text[i + 1] : ' ');
        if (i >= text.Length) return 0;   // punctuation only: no first letter to style
        i += LengthOf(text[i], i + 1 < text.Length ? text[i + 1] : ' ');   // the letter itself
        while (i < text.Length && IsFirstLetterPunctuation(CodePointAt(text, i)))
            i += LengthOf(text[i], i + 1 < text.Length ? text[i + 1] : ' ');
        return i;
    }

    /// <summary>
    /// The 'display' a ::before/::after box should use. Defaults to 'inline' (the initial value for
    /// generated content). 'inherit' has to be resolved here against the originating element: these
    /// pseudo styles are written straight into StyleOverrides, so the cascade's css-wide-keyword
    /// resolution never sees them and the literal string "inherit" would reach GetDisplay, which
    /// does not recognise it and would silently fall back to 'inline'.
    /// </summary>
    private static string ResolvePseudoDisplay(LayoutNode host, Dictionary<string, string> pseudoStyles)
    {
        var display = pseudoStyles.GetValueOrDefault("display", "inline").Trim();
        if (!display.Equals("inherit", StringComparison.OrdinalIgnoreCase)) return display;

        var hostDisplay = host.TryResolveStyle(AngleSharp.Css.PropertyNames.Display, out var ov)
            ? ov : host.Style.GetPropertyValueSafe(AngleSharp.Css.PropertyNames.Display);
        hostDisplay = hostDisplay?.Trim();
        // Guard against the host itself computing to 'inherit' (or to nothing) — fall back to the
        // initial value rather than looping.
        return string.IsNullOrEmpty(hostDisplay)
               || hostDisplay.Equals("inherit", StringComparison.OrdinalIgnoreCase)
            ? "inline"
            : hostDisplay;
    }

    /// <summary>CSS 2.1 §6.2.1: 'inherit' on a generated box takes the originating element's
    /// computed value. Pseudo declarations are copied straight into StyleOverrides, so leaving
    /// the literal token there reached the property readers as an unknown value — `white-space:
    /// inherit` on `:before` collapsed a 'pre' item's "\A" to a space instead of a forced break
    /// (content-172), and `float: inherit` on `:first-letter` never floated.</summary>
    private static string ResolvePseudoValue(LayoutNode host, string property, string value)
    {
        if (!value.Trim().Equals("inherit", StringComparison.OrdinalIgnoreCase)) return value;
        var inherited = host.TryResolveStyle(property, out var ov)
            ? ov : host.Style.GetPropertyValueSafe(property);
        inherited = inherited?.Trim();
        return string.IsNullOrEmpty(inherited) ||
               inherited.Equals("inherit", StringComparison.OrdinalIgnoreCase)
            ? value
            : inherited;
    }

    /// <summary>The image URL when a content value is a single url(...) token (§12.2), else
    /// null. The pseudo-element box carries the fetched bitmap instead of text.</summary>
    private static string? TryParseContentUrl(string value)
    {
        value = value.Trim();
        if (value.Length < 5 || !value.StartsWith("url(", StringComparison.OrdinalIgnoreCase) || !value.EndsWith(")"))
            return null;
        var inner = value[4..^1].Trim();
        if ((inner.StartsWith('"') && inner.EndsWith('"') && inner.Length >= 2) ||
            (inner.StartsWith('\'') && inner.EndsWith('\'') && inner.Length >= 2))
            inner = inner[1..^1];
        return inner.Length > 0 ? inner : null;
    }

    /// <summary>Parses a CSS content property value, stripping quotes and handling basic values.
    /// Supports concatenated tokens like: "Chapter " counter(section) ". "</summary>
    private static string? ParseContentValue(string value, LayoutNode? node = null, QuoteState? quotes = null)
    {
        quotes ??= new QuoteState(DefaultQuotes, 0);
        value = value.Trim();
        if (value is "none" or "normal" or "") return null;

        // 'content: url(...)': the generated box is the image, not text (TryParseContentUrl
        // hands the URL to the pseudo-element creation, which fetches it into node.Image).
        if (TryParseContentUrl(value) is not null) return "";

        // Check if this is a simple single-token value
        // More than one token ('close-quote "x"', '"a" "b"') is a concatenation: tokenize it.
        if (!value.Contains("counter(") && !value.Contains("counters(") && !value.Contains("attr(") &&
            IsSingleContentToken(value))
        {
            // Simple single value. The length check matters: a lone quote character both starts
            // and ends with a quote, and stripping one off each end of it asks for a -1 slice.
            if (value.Length >= 2 &&
                ((value.StartsWith('"') && value.EndsWith('"')) ||
                 (value.StartsWith('\'') && value.EndsWith('\''))))
            {
                var inner = value[1..^1];
                return DecodeCssEscapes(inner);
            }
            if (QuoteKeyword(value) is { } keyword) return quotes.Apply(keyword);
            return value;
        }

        // Simple attr(name) \u2014 the named attribute's value (CSS 2.1 \u00A712.2).
        if (value.StartsWith("attr(", StringComparison.OrdinalIgnoreCase) && value.EndsWith(")") &&
            !value[5..].Contains("attr(") && !value.Contains(' '))
        {
            var attrName = value[5..^1].Trim();
            return node?.Attributes.GetValueOrDefault(attrName) ?? "";
        }

        // Tokenize concatenated content value: "text" counter(name) "more"
        var sb = new System.Text.StringBuilder();
        int i = 0;
        while (i < value.Length)
        {
            if (char.IsWhiteSpace(value[i])) { i++; continue; }

            // Quoted string
            if (value[i] == '"' || value[i] == '\'')
            {
                var quote = value[i];
                int end = value.IndexOf(quote, i + 1);
                if (end < 0) break;
                sb.Append(DecodeCssEscapes(value[(i + 1)..end]));
                i = end + 1;
                continue;
            }

            // counter(name) or counter(name, style)
            if (value[i..].StartsWith("counter("))
            {
                var paren = value.IndexOf(')', i);
                if (paren < 0) break;
                var args = SplitCounterArgs(value[(i + 8)..paren]);
                var counterVal = 0;
                if (args.Count > 0) node?.CounterValues?.TryGetValue(args[0], out counterVal);
                sb.Append(FormatCounter(counterVal, args.Count > 1 ? args[1] : "decimal"));
                i = paren + 1;
                continue;
            }

            // counters(name, separator) or counters(name, separator, style) — joins the whole
            // nested scope stack with the separator (CSS 2.1 §12.4.1, e.g. nested-list "1.2.1").
            if (value[i..].StartsWith("counters("))
            {
                var paren = value.IndexOf(')', i);
                if (paren < 0) break;
                var args = SplitCounterArgs(value[(i + 9)..paren]);
                var counterName = args.Count > 0 ? args[0] : "";
                var sep = args.Count > 1 ? args[1] : "";
                var style = args.Count > 2 ? args[2] : "decimal";
                if (node?.CounterStacks != null && node.CounterStacks.TryGetValue(counterName, out var stack))
                    sb.Append(string.Join(sep, stack.Select(v => FormatCounter(v, style))));
                else
                {
                    var counterVal = 0;
                    node?.CounterValues?.TryGetValue(counterName, out counterVal);
                    sb.Append(FormatCounter(counterVal, style));
                }
                i = paren + 1;
                continue;
            }

            // attr(name) \u2014 the named attribute's value
            if (value[i..].StartsWith("attr(", StringComparison.OrdinalIgnoreCase))
            {
                var paren = value.IndexOf(')', i);
                if (paren < 0) break;
                var attrName = value[(i + 5)..paren].Trim();
                if (node?.Attributes.TryGetValue(attrName, out var av) == true) sb.Append(av);
                i = paren + 1;
                continue;
            }

            // open-quote / close-quote / no-open-quote / no-close-quote (§12.3.2)
            var quoteWord = QuoteKeywords.FirstOrDefault(k => string.CompareOrdinal(value, i, k, 0, k.Length) == 0 &&
                (i + k.Length == value.Length || char.IsWhiteSpace(value[i + k.Length])));
            if (quoteWord is not null) { sb.Append(quotes.Apply(quoteWord)); i += quoteWord.Length; continue; }

            // Skip unknown token until whitespace
            while (i < value.Length && !char.IsWhiteSpace(value[i])) i++;
        }

        return sb.Length > 0 ? sb.ToString() : null;
    }

    /// <summary>Splits counter()/counters() arguments. AngleSharp normalizes
    /// <c>counters(item, ".")</c> to <c>counters(item .)</c> (comma dropped, separator unquoted),
    /// so we split on commas OR whitespace while keeping quoted tokens intact and dropping quotes.</summary>
    private static List<string> SplitCounterArgs(string inner)
    {
        var args = new List<string>();
        var sb = new System.Text.StringBuilder();
        char quote = '\0';
        foreach (var c in inner)
        {
            if (quote != '\0')
            {
                if (c == quote) quote = '\0';
                else sb.Append(c);
            }
            else if (c is '"' or '\'') quote = c;
            else if (c == ',' || char.IsWhiteSpace(c))
            {
                if (sb.Length > 0) { args.Add(sb.ToString()); sb.Clear(); }
            }
            else sb.Append(c);
        }
        if (sb.Length > 0) args.Add(sb.ToString());
        return args;
    }

    private static string FormatCounter(int value, string style) => Layout.CssCounterStyles.Content(value, style);

    /// <summary>Parses "counter-reset: name value name2 value2" or "counter-increment: name value" specs.</summary>
    /// <param name="defaultValue">The value an omitted integer means: 0 for counter-reset,
    /// 1 for counter-increment (an explicit "x 0" increments by zero).</param>
    private static List<(string Name, int Value)> ParseCounterSpec(string spec, int defaultValue)
    {
        var result = new List<(string, int)>();
        var tokens = spec.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int i = 0;
        while (i < tokens.Length)
        {
            if (tokens[i] == "none") break;
            var name = tokens[i];
            int val = defaultValue;
            if (i + 1 < tokens.Length && int.TryParse(tokens[i + 1], out var parsed))
            {
                val = parsed;
                i += 2;
            }
            else
            {
                i++;
            }
            result.Add((name, val));
        }
        return result;
    }

    /// <summary>Decodes CSS unicode escape sequences like \201C into actual characters.</summary>
    private static readonly string[] QuoteKeywords = ["no-open-quote", "no-close-quote", "open-quote", "close-quote"];
    private static readonly List<(string Open, string Close)> DefaultQuotes = [("\u201C", "\u201D"), ("\u2018", "\u2019")];

    private static string? QuoteKeyword(string token) => Array.IndexOf(QuoteKeywords, token) >= 0 ? token : null;

    /// <summary>§12.3.2: open-quote emits the 'quotes' pair for the current depth (the last pair
    /// beyond the list) and nests one level; close-quote un-nests and emits the matching close —
    /// nothing at depth 0; the no- forms change the depth without emitting.</summary>
    private sealed class QuoteState(List<(string Open, string Close)> pairs, int depth)
    {
        public int Depth { get; private set; } = depth;

        public string Apply(string keyword)
        {
            switch (keyword)
            {
                case "open-quote":
                    var open = pairs.Count == 0 ? "" : pairs[Math.Min(Depth, pairs.Count - 1)].Open;
                    Depth++;
                    return open;
                case "no-open-quote":
                    Depth++;
                    return "";
                case "close-quote":
                    if (Depth == 0) return "";
                    Depth--;
                    return pairs.Count == 0 ? "" : pairs[Math.Min(Depth, pairs.Count - 1)].Close;
                default: // no-close-quote
                    if (Depth > 0) Depth--;
                    return "";
            }
        }
    }

    /// <summary>Advances the document's quote depth by a pseudo-element's content (when it is
    /// generated) and returns the depth the content starts at.</summary>
    private static int AdvanceQuoteDepth(LayoutNode node, Dictionary<string, string>? pseudoStyles)
    {
        var start = Current.QuoteDepth;
        if (pseudoStyles is null || !pseudoStyles.TryGetValue("content", out var content) ||
            ResolvePseudoDisplay(node, pseudoStyles).Equals("none", StringComparison.OrdinalIgnoreCase))
            return start;
        var state = new QuoteState(DefaultQuotes, start);
        foreach (var token in ContentTokens(content))
            if (QuoteKeyword(token) is { } keyword) state.Apply(keyword);
        Current.QuoteDepth = state.Depth;
        return start;
    }

    /// <summary>True when a 'content' value is one string or one bare word (no whitespace
    /// outside a string, at most one string).</summary>
    private static bool IsSingleContentToken(string value)
    {
        if (value.Length > 0 && value[0] is '"' or '\'')
        {
            var j = 1;
            while (j < value.Length && value[j] != value[0]) j += value[j] == '\\' ? 2 : 1;
            return j >= value.Length - 1;
        }
        return !value.Any(char.IsWhiteSpace);
    }

    /// <summary>The bare (unquoted, non-function) words of a 'content' value, in order.</summary>
    private static IEnumerable<string> ContentTokens(string value)
    {
        var i = 0;
        while (i < value.Length)
        {
            var c = value[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c is '"' or '\'')
            {
                var j = i + 1;
                while (j < value.Length && value[j] != c) j += value[j] == '\\' ? 2 : 1;
                i = j + 1;
                continue;
            }
            var start = i;
            while (i < value.Length && !char.IsWhiteSpace(value[i]) && value[i] != '(') i++;
            if (i < value.Length && value[i] == '(')
            {
                var close = value.IndexOf(')', i);
                i = close < 0 ? value.Length : close + 1;
                continue;
            }
            yield return value[start..i];
        }
    }

    /// <summary>The 'quotes' pairs in effect for a pseudo-element: its own declaration, else the
    /// value its element computes (inherited). 'none' is no pairs; unset uses the UA's curly quotes.</summary>
    private static List<(string Open, string Close)> QuotePairs(LayoutNode node, Dictionary<string, string> pseudoStyles)
    {
        var raw = pseudoStyles.TryGetValue("quotes", out var own) ? own
            : node.TryResolveStyle("quotes", out var ov) ? ov : node.Style.GetPropertyValueSafe("quotes");
        raw = raw?.Trim() ?? "";
        if (raw.Equals("none", StringComparison.OrdinalIgnoreCase)) return [];
        var strings = new List<string>();
        for (var i = 0; i < raw.Length; i++)
        {
            var q = raw[i];
            if (q is not ('"' or '\'')) continue;
            var j = i + 1;
            var sb = new System.Text.StringBuilder();
            while (j < raw.Length && raw[j] != q)
            {
                if (raw[j] == '\\' && j + 1 < raw.Length) { sb.Append(raw, j, 2); j += 2; continue; }
                sb.Append(raw[j++]);
            }
            strings.Add(DecodeCssEscapes(sb.ToString()));
            i = j;
        }
        if (strings.Count < 2) return DefaultQuotes;
        var pairs = new List<(string, string)>();
        for (var k = 0; k + 1 < strings.Count; k += 2) pairs.Add((strings[k], strings[k + 1]));
        return pairs;
    }

    private static string DecodeCssEscapes(string s)
    {
        if (!s.Contains('\\')) return s;
        var sb = new System.Text.StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 1 < s.Length)
            {
                // Collect up to 6 hex digits
                int start = i + 1;
                int end = start;
                while (end < s.Length && end - start < 6 && Uri.IsHexDigit(s[end])) end++;
                if (end > start)
                {
                    var codePoint = Convert.ToInt32(s[start..end], 16);
                    sb.Append(char.ConvertFromUtf32(codePoint));
                    i = end - 1;
                    // Skip optional single trailing space after hex escape
                    if (i + 1 < s.Length && s[i + 1] == ' ') i++;
                }
                else
                {
                    // Not a hex escape — literal escaped char
                    sb.Append(s[i + 1]);
                    i++;
                }
            }
            else
            {
                sb.Append(s[i]);
            }
        }
        return sb.ToString();
    }
}

