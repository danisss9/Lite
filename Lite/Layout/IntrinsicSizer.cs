using Lite.Extensions;
using Lite.Models;

namespace Lite.Layout;

/// <summary>
/// Computes CSS intrinsic widths — the <c>min-content</c> and <c>max-content</c> sizes of a box —
/// so auto-width boxes can be sized by shrink-to-fit (CSS 2.1 §10.3.5 / §10.3.7) rather than the
/// old "widest explicit child" approximation.
///
/// <para><b>max-content</b> is the width the box would take if it never wrapped: text laid out on a
/// single line, inline siblings summed across the line, forced breaks (<c>&lt;br&gt;</c>) starting a
/// fresh line and the widest line winning. <b>min-content</b> is the narrowest the box can get by
/// taking every allowed soft-wrap opportunity: the widest single unbreakable unit (a word, a
/// replaced element, an atomic inline).</para>
///
/// <para>Percentages resolve against an as-yet-unknown containing block during intrinsic sizing, so
/// they compute to their indefinite basis (0 for padding/margin, ignored for min/max-width) — the
/// same convention <see cref="TableEngine"/> uses. All widths returned are content-box widths unless
/// the method name says <c>Outer</c>, which folds in the node's own horizontal margin+border+padding.</para>
/// </summary>
internal static class IntrinsicSizer
{
    /// <summary>
    /// Shrink-to-fit content width (CSS 2.1 §10.3.5): <c>min(max(min-content, available), max-content)</c>.
    /// <paramref name="availableWidth"/> is the space left for content after the box's own margin,
    /// border and padding have been removed from the containing block.
    /// </summary>
    public static float ShrinkToFit(LayoutNode node, float availableWidth, float viewportHeight)
    {
        var (min, max) = ContentMinMax(node, viewportHeight);
        return Math.Min(Math.Max(min, availableWidth), max);
    }

    /// <summary>
    /// The node's intrinsic content-box (min, max) widths. Honors an explicit <c>width</c> (which
    /// fixes both), replaced-element intrinsic width, and otherwise derives them from the flow of the
    /// node's children/text. Does NOT include the node's own margin/border/padding.
    /// </summary>
    public static (float Min, float Max) ContentMinMax(LayoutNode node, float viewportHeight)
    {
        // Replaced element (image / object with a decoded bitmap): intrinsic pixel width.
        if (node.TagName == "IMG" || (node.TagName == "OBJECT" && node.Image != null))
        {
            var iw = node.IntrinsicWidth > 0 ? node.IntrinsicWidth : node.Image?.Width ?? 0;
            var explicitImg = node.GetWidth(0);
            var w = explicitImg > 0 ? explicitImg : iw;
            return (w, w);
        }

        // An explicit (non-auto, non-percent) width fixes both intrinsic sizes to the content width.
        var explicitW = node.GetWidth(0);
        if (explicitW > 0)
        {
            var fs = node.GetFontSize();
            var isBorderBox = node.Style.GetPropertyValueSafe("box-sizing") == "border-box";
            var contentW = explicitW;
            if (isBorderBox)
            {
                var pad = node.GetPadding(0, viewportHeight, fs);
                var bord = node.GetBorderWidth();
                contentW = Math.Max(0f, explicitW - pad.Left - pad.Right - bord.Left - bord.Right);
            }
            return (contentW, contentW);
        }

        // Leaf text: measure directly.
        if (node.Children.Count == 0 && !string.IsNullOrEmpty(node.DisplayText))
            return TextMinMax(node);

        // Container: block children stack (widest wins); consecutive inline children flow into a run
        // (min = widest atomic unit, max = widest line). Mirrors BoxEngine.LayoutChildrenImpl grouping.
        float min = 0f, max = 0f;
        var children = node.Children;
        var i = 0;
        while (i < children.Count)
        {
            var child = children[i];
            var display = child.GetDisplay();
            if (display == DisplayType.None) { i++; continue; }

            var pos = child.GetPosition();
            if (pos == PositionType.Absolute || pos == PositionType.Fixed) { i++; continue; }

            var floatSide = child.TagName == "#text" ? FloatType.None : child.GetFloat();
            var isBlockLevel = display is DisplayType.Block or DisplayType.ListItem
                            or DisplayType.Flex or DisplayType.Table;

            if (isBlockLevel || floatSide != FloatType.None)
            {
                // Block-level (and floats, which shrink-to-fit): stacked — the container must be at
                // least as wide as each one's outer intrinsic width.
                var (cMin, cMax) = OuterMinMax(child, viewportHeight);
                min = Math.Max(min, cMin);
                max = Math.Max(max, cMax);
                i++;
            }
            else
            {
                // Gather a maximal inline run, then measure it as a line.
                var toks = new List<InlineTok>();
                while (i < children.Count)
                {
                    var c = children[i];
                    var d = c.GetDisplay();
                    if (d is DisplayType.Block or DisplayType.ListItem or DisplayType.Flex or DisplayType.Table) break;
                    if (c.TagName != "#text" && c.GetFloat() != FloatType.None) break;
                    FlattenInline(c, viewportHeight, toks);
                    i++;
                }
                var (rMin, rMax) = RunMinMax(toks);
                min = Math.Max(min, rMin);
                max = Math.Max(max, rMax);
            }
        }
        return (min, max);
    }

    /// <summary>
    /// The node's intrinsic (min, max) widths including its own horizontal margin, border and
    /// padding, and clamped by an explicit <c>min-width</c>/<c>max-width</c> (percentages ignored —
    /// the containing block is indefinite here).
    /// </summary>
    public static (float Min, float Max) OuterMinMax(LayoutNode node, float viewportHeight)
    {
        if (node.GetDisplay() == DisplayType.None) return (0f, 0f);

        var (cMin, cMax) = ContentMinMax(node, viewportHeight);

        var fs = node.GetFontSize();
        // min-width / max-width clamp the content box. Skip percentage bases (indefinite CB).
        if (!IsPercent(node, "max-width"))
        {
            var maxW = node.GetMaxWidth(0, fs);
            if (maxW < float.PositiveInfinity) { cMin = Math.Min(cMin, maxW); cMax = Math.Min(cMax, maxW); }
        }
        if (!IsPercent(node, "min-width"))
        {
            var minW = node.GetMinWidth(0, fs);
            if (minW > 0f) { cMin = Math.Max(cMin, minW); cMax = Math.Max(cMax, minW); }
        }

        var pad = node.GetPadding(0, viewportHeight, fs);
        var bord = node.GetBorderWidth();
        var marg = node.GetMargin(0, viewportHeight, fs);
        var extra = pad.Left + pad.Right + bord.Left + bord.Right
                    + Math.Max(0f, marg.Left) + Math.Max(0f, marg.Right);
        return (cMin + extra, cMax + extra);
    }

    // ── Inline flattening ─────────────────────────────────────────────────────

    /// <summary>An atomic inline contribution (min == max for unbreakable units) or a forced break.</summary>
    private readonly record struct InlineTok(bool IsBreak, float Min, float Max);

    /// <summary>
    /// Flattens an inline subtree into a token stream: text words/atomic inlines become
    /// (min, max) atoms, <c>&lt;br&gt;</c> becomes a break, and an inline element's own horizontal
    /// padding/border/margin are emitted as unbreakable atoms around its content (they never wrap).
    /// </summary>
    private static void FlattenInline(LayoutNode node, float viewportHeight, List<InlineTok> toks)
    {
        var display = node.GetDisplay();
        if (display == DisplayType.None) return;
        var pos = node.GetPosition();
        if (pos == PositionType.Absolute || pos == PositionType.Fixed) return;

        if (node.TagName == "BR") { toks.Add(new InlineTok(true, 0f, 0f)); return; }

        // Atomic inlines (inline-block, inline-flex, images, or anything with an explicit width) do
        // not break internally — they contribute their full outer width to both min and max.
        if (display is DisplayType.InlineBlock or DisplayType.InlineFlex
            || node.TagName == "IMG" || (node.TagName == "OBJECT" && node.Image != null)
            || node.GetWidth(0) > 0)
        {
            var (m, x) = OuterMinMax(node, viewportHeight);
            toks.Add(new InlineTok(false, m, x));
            return;
        }

        if (node.Children.Count == 0 && !string.IsNullOrEmpty(node.DisplayText))
        {
            var (m, x) = TextMinMax(node);
            toks.Add(new InlineTok(false, m, x));
            return;
        }

        // Non-replaced inline element with children: its horizontal box model is unbreakable and
        // brackets the content on the same line.
        var fs = node.GetFontSize();
        var pad = node.GetPadding(0, viewportHeight, fs);
        var bord = node.GetBorderWidth();
        var marg = node.GetMargin(0, viewportHeight, fs);
        var left = pad.Left + bord.Left + Math.Max(0f, marg.Left);
        var right = pad.Right + bord.Right + Math.Max(0f, marg.Right);
        if (left > 0f) toks.Add(new InlineTok(false, left, left));
        foreach (var child in node.Children)
            FlattenInline(child, viewportHeight, toks);
        if (right > 0f) toks.Add(new InlineTok(false, right, right));
    }

    /// <summary>
    /// Reduces a flattened inline run to (min, max): min-content is the widest single atom (everything
    /// else can wrap away); max-content is the widest line, where lines are delimited by forced breaks
    /// and each atom on a line is summed.
    /// </summary>
    private static (float Min, float Max) RunMinMax(List<InlineTok> toks)
    {
        float min = 0f, maxLine = 0f, maxBest = 0f;
        foreach (var t in toks)
        {
            if (t.IsBreak) { maxBest = Math.Max(maxBest, maxLine); maxLine = 0f; continue; }
            min = Math.Max(min, t.Min);
            maxLine += t.Max;
        }
        maxBest = Math.Max(maxBest, maxLine);
        return (min, maxBest);
    }

    // ── Text ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Intrinsic widths of a text leaf. max-content is the text on one line; min-content is the widest
    /// word. When white-space forbids wrapping (nowrap/pre) the two are equal; when it preserves line
    /// breaks (pre/pre-wrap/pre-line) max-content is the widest hard line.
    /// </summary>
    private static (float Min, float Max) TextMinMax(LayoutNode node)
    {
        var text = node.DisplayText;
        if (string.IsNullOrEmpty(text)) return (0f, 0f);

        using var font = TextMeasure.CreateFont(node);
        var ws = node.GetWhiteSpace();
        var preservesLines = ws is WhiteSpace.Pre or WhiteSpace.PreWrap or WhiteSpace.PreLine;
        var noWrap = ws is WhiteSpace.NoWrap or WhiteSpace.Pre;

        // max-content: widest hard line (collapsing runs of whitespace into a single space, unless
        // pre keeps them — but pre's newlines are the only breaks that matter for width here).
        float max = 0f;
        var rawLines = preservesLines ? text.Split('\n') : [text];
        foreach (var raw in rawLines)
        {
            var line = ws is WhiteSpace.Pre or WhiteSpace.PreWrap
                ? raw
                : string.Join(' ', raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            max = Math.Max(max, font.MeasureText(line));
        }

        if (noWrap) return (max, max);

        float min = 0f;
        foreach (var word in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            min = Math.Max(min, font.MeasureText(word));
        return (min, max);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>True when the property's resolved value is a percentage (indefinite here).</summary>
    private static bool IsPercent(LayoutNode node, string prop)
    {
        var raw = node.TryResolveStyle(prop, out var ov) ? ov : node.Style.GetPropertyValueSafe(prop);
        return raw?.TrimEnd().EndsWith('%') == true;
    }
}
