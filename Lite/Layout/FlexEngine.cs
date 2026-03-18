using Lite.Extensions;
using Lite.Models;
using SkiaSharp;

namespace Lite.Layout;

/// <summary>
/// Implements the CSS Flexible Box Layout algorithm (CSS Flexbox Level 1).
/// Supports row/column direction, wrap, justify-content, align-items,
/// align-self, flex-grow, flex-shrink, flex-basis, and gap.
/// </summary>
internal static class FlexEngine
{
    /// <summary>
    /// Lays out the flex children of <paramref name="container"/>.
    /// Returns the total content height consumed.
    /// </summary>
    public static float LayoutFlex(
        LayoutNode container,
        float contentX, float contentY,
        float contentW, float containerH,
        float viewportWidth, float viewportHeight)
    {
        var dir       = container.GetFlexDirection();
        var wrap      = container.GetFlexWrap();
        var justify   = container.GetJustifyContent();
        var alignI    = container.GetAlignItems();
        var alignC    = container.GetAlignContent();
        var fontSize  = container.GetFontSize();

        var isRow     = dir == FlexDirection.Row    || dir == FlexDirection.RowReverse;
        var isReverse = dir == FlexDirection.RowReverse || dir == FlexDirection.ColumnReverse;

        var gapMain  = isRow ? container.GetGapColumn(contentW, fontSize) : container.GetGapRow(containerH, fontSize);
        var gapCross = isRow ? container.GetGapRow(containerH, fontSize)  : container.GetGapColumn(contentW, fontSize);

        var mainSize  = isRow ? contentW : (containerH > 0 ? containerH : float.MaxValue);
        var crossSize = isRow ? (containerH > 0 ? containerH : 0f) : contentW;

        // ── §9.1 Collect in-flow items ──────────────────────────────────────
        // §4.1: Abs-pos children participate in reordering but not flex layout.
        // Compute their static position as if they were the sole flex item at flex-start.
        var absPosStaticX = isRow ? contentX : (isReverse ? contentX + mainSize : contentX);
        var absPosStaticY = isRow ? contentY : contentY;
        foreach (var c in container.Children)
        {
            var pos = c.GetPosition();
            if (pos == PositionType.Absolute || pos == PositionType.Fixed)
            {
                // Static position = flex-start of main axis, cross-start
                c.FlexStaticX = isRow ? (isReverse ? contentX + contentW : contentX) : contentX;
                c.FlexStaticY = isRow ? contentY : (isReverse ? contentY + mainSize : contentY);
            }
        }

        var items = container.Children
            .Select((c, i) => (child: c, index: i))
            .Where(t => t.child.GetDisplay() != DisplayType.None &&
                        t.child.GetPosition() != PositionType.Absolute &&
                        t.child.GetPosition() != PositionType.Fixed &&
                        !(t.child.TagName == "#TEXT" && string.IsNullOrWhiteSpace(t.child.DisplayText)))
            .OrderBy(t => t.child.GetOrder())
            .ThenBy(t => t.index)
            .Select(t => t.child)
            .ToList();

        if (items.Count == 0) return 0f;

        // ── §9.2 Compute each item's flex data ─────────────────────────────
        var data = items.Select(item => new FlexItem(item,
            contentW, containerH, viewportWidth, viewportHeight, isRow)).ToList();

        // ── §9.3 Wrap into lines ────────────────────────────────────────────
        var lines = WrapItems(data, mainSize, gapMain, wrap != FlexWrap.NoWrap);

        // ── §9.7 Resolve flex-grow / flex-shrink per line (iterative) ───────
        foreach (var line in lines)
            ResolveFlexibility(line, mainSize, gapMain);

        // ── Re-measure cross size after grow/shrink (row only) ──────────────
        if (isRow)
            foreach (var line in lines)
                foreach (var d in line)
                    d.RemeasureCross(viewportWidth, viewportHeight);

        // ── §9.4 Compute line cross sizes ───────────────────────────────────
        var lineCrossSizes = new float[lines.Count];
        for (int i = 0; i < lines.Count; i++)
            lineCrossSizes[i] = lines[i].Max(d => d.OuterCross);

        // Single-line: use container cross size if available
        if (lines.Count == 1 && crossSize > 0)
            lineCrossSizes[0] = crossSize;

        // ── §9.4 align-content: distribute lines along cross axis ───────────
        var totalLineCross = lineCrossSizes.Sum() + Math.Max(0, lines.Count - 1) * gapCross;
        var freeCross = crossSize > 0 ? crossSize - totalLineCross : 0f;

        float crossOffset = 0f, crossSpacing = 0f;
        if (lines.Count > 1 || (lines.Count == 1 && crossSize > 0))
        {
            if (alignC == AlignContent.Stretch && freeCross > 0)
            {
                // Distribute extra space equally among lines
                var extra = freeCross / lines.Count;
                for (int i = 0; i < lines.Count; i++)
                    lineCrossSizes[i] += extra;
                freeCross = 0;
            }
            else if (freeCross > 0)
            {
                ComputeJustify(alignC switch
                {
                    AlignContent.FlexStart    => JustifyContent.FlexStart,
                    AlignContent.FlexEnd      => JustifyContent.FlexEnd,
                    AlignContent.Center       => JustifyContent.Center,
                    AlignContent.SpaceBetween => JustifyContent.SpaceBetween,
                    AlignContent.SpaceAround  => JustifyContent.SpaceAround,
                    _                         => JustifyContent.FlexStart,
                }, lines.Count, freeCross, out crossOffset, out crossSpacing);
            }
        }

        // ── wrap-reverse: reverse the line order for cross-axis placement ───
        if (wrap == FlexWrap.WrapReverse)
        {
            lines.Reverse();
            Array.Reverse(lineCrossSizes);
        }

        // ── §9.5/§9.6 Position items ───────────────────────────────────────
        var crossCursor = (isRow ? contentY : contentX) + crossOffset;

        for (int li = 0; li < lines.Count; li++)
        {
            var line = lines[li];
            var lineCrossSize = lineCrossSizes[li];

            var lineMainUsed = line.Sum(d => d.OuterMain) + Math.Max(0, line.Count - 1) * gapMain;

            // ── Auto margins on main axis absorb free space before justify-content ──
            var freeMain = mainSize == float.MaxValue ? 0 : mainSize - lineMainUsed;
            var autoMainMarginCount = 0;
            foreach (var d in line)
            {
                if (isRow)
                {
                    if (d.Node.IsAutoMarginLeft())  autoMainMarginCount++;
                    if (d.Node.IsAutoMarginRight()) autoMainMarginCount++;
                }
                else
                {
                    if (d.Node.IsAutoMarginTop())    autoMainMarginCount++;
                    if (d.Node.IsAutoMarginBottom()) autoMainMarginCount++;
                }
            }

            float mainOffset2, mainSpacing;
            if (autoMainMarginCount > 0 && freeMain > 0)
            {
                var perAutoMargin = freeMain / autoMainMarginCount;
                foreach (var d in line)
                {
                    float addStart = 0, addEnd = 0;
                    if (isRow)
                    {
                        if (d.Node.IsAutoMarginLeft())  addStart = perAutoMargin;
                        if (d.Node.IsAutoMarginRight()) addEnd   = perAutoMargin;
                    }
                    else
                    {
                        if (d.Node.IsAutoMarginTop())    addStart = perAutoMargin;
                        if (d.Node.IsAutoMarginBottom()) addEnd   = perAutoMargin;
                    }
                    d.Margin = new FlexEdge(
                        d.Margin.MainStart + addStart, d.Margin.MainEnd + addEnd,
                        d.Margin.CrossStart, d.Margin.CrossEnd);
                }
                mainOffset2 = 0;
                mainSpacing = 0;
                // Recalculate after auto margins applied
                freeMain = 0;
            }
            else
            {
                ComputeJustify(justify, line.Count, Math.Max(0, freeMain), out mainOffset2, out mainSpacing);
            }

            if (isReverse)
            {
                mainOffset2  = (isRow ? contentX : contentY) + mainSize - mainOffset2 - line[0].OuterMain;
                mainSpacing = -mainSpacing;
            }
            else
            {
                mainOffset2 += isRow ? contentX : contentY;
            }

            // ── Compute baseline alignment for this line (row only) ────────
            float lineBaseline = 0;
            if (isRow)
            {
                foreach (var d in line)
                {
                    var sa = d.Node.GetAlignSelf();
                    var ea = sa == AlignSelf.Auto ? (alignI == AlignItems.Baseline ? AlignSelf.Baseline : AlignSelf.Stretch) : sa;
                    if (ea == AlignSelf.Baseline)
                    {
                        var itemBaseline = GetFirstBaseline(d);
                        var outerBaseline = d.Margin.CrossStart + d.Border.CrossStart + d.Padding.CrossStart + itemBaseline;
                        lineBaseline = Math.Max(lineBaseline, outerBaseline);
                    }
                }
            }

            // Place each item along the main axis
            foreach (var d in line)
            {
                // ── Cross-axis auto margins ─────────────────────────────────
                bool crossAutoStart, crossAutoEnd;
                if (isRow)
                {
                    crossAutoStart = d.Node.IsAutoMarginTop();
                    crossAutoEnd   = d.Node.IsAutoMarginBottom();
                }
                else
                {
                    crossAutoStart = d.Node.IsAutoMarginLeft();
                    crossAutoEnd   = d.Node.IsAutoMarginRight();
                }

                // Cross-axis alignment
                var selfAlign = d.Node.GetAlignSelf();
                AlignSelf effectiveAlign;
                if (crossAutoStart || crossAutoEnd)
                {
                    // Auto margins override align-self
                    effectiveAlign = AlignSelf.FlexStart; // placeholder, handled below
                }
                else if (selfAlign == AlignSelf.Auto)
                {
                    effectiveAlign = alignI switch
                    {
                        AlignItems.FlexStart => AlignSelf.FlexStart,
                        AlignItems.FlexEnd   => AlignSelf.FlexEnd,
                        AlignItems.Center    => AlignSelf.Center,
                        AlignItems.Baseline  => AlignSelf.Baseline,
                        _                    => AlignSelf.Stretch,
                    };
                }
                else
                {
                    effectiveAlign = selfAlign;
                }

                float itemCrossOffset;
                float itemCrossSize;

                if (crossAutoStart || crossAutoEnd)
                {
                    // Distribute free cross space to auto margins
                    itemCrossSize = d.ContentCross;
                    var freeCrossItem = lineCrossSize - d.OuterCross;
                    if (freeCrossItem < 0) freeCrossItem = 0;
                    float addCrossStart = 0;
                    if (crossAutoStart && crossAutoEnd) addCrossStart = freeCrossItem / 2f;
                    else if (crossAutoStart) addCrossStart = freeCrossItem;
                    // else crossAutoEnd: addCrossStart = 0
                    itemCrossOffset = crossCursor + addCrossStart
                        + d.Margin.CrossStart + d.Border.CrossStart + d.Padding.CrossStart;
                }
                else if (effectiveAlign == AlignSelf.Stretch && d.CrossSizeAuto)
                {
                    itemCrossSize = Math.Max(0, lineCrossSize - d.Margin.CrossStart - d.Margin.CrossEnd
                                                              - d.Border.CrossStart - d.Border.CrossEnd
                                                              - d.Padding.CrossStart - d.Padding.CrossEnd);
                    // Clamp stretched size by min/max
                    itemCrossSize = Math.Max(d.MinCross, Math.Min(d.MaxCross, itemCrossSize));
                    itemCrossOffset = crossCursor + d.Margin.CrossStart + d.Border.CrossStart + d.Padding.CrossStart;
                }
                else
                {
                    itemCrossSize = d.ContentCross;
                    if (effectiveAlign == AlignSelf.Baseline && isRow)
                    {
                        var itemBaseline = GetFirstBaseline(d);
                        var outerBaseline = d.Margin.CrossStart + d.Border.CrossStart + d.Padding.CrossStart + itemBaseline;
                        var shift = lineBaseline - outerBaseline;
                        itemCrossOffset = crossCursor + shift
                            + d.Margin.CrossStart + d.Border.CrossStart + d.Padding.CrossStart;
                    }
                    else
                    {
                        itemCrossOffset = effectiveAlign switch
                        {
                            AlignSelf.FlexEnd => crossCursor + lineCrossSize - d.OuterCross
                                                + d.Margin.CrossStart + d.Border.CrossStart + d.Padding.CrossStart,
                            AlignSelf.Center  => crossCursor + (lineCrossSize - d.OuterCross) / 2f
                                                + d.Margin.CrossStart + d.Border.CrossStart + d.Padding.CrossStart,
                            _                 => crossCursor + d.Margin.CrossStart + d.Border.CrossStart + d.Padding.CrossStart,
                        };
                    }
                }

                // Clamp cross size by min/max
                itemCrossSize = Math.Max(d.MinCross, Math.Min(d.MaxCross, itemCrossSize));

                float absX, absY, w, h;
                if (isRow)
                {
                    absX = mainOffset2 + d.Margin.MainStart + d.Border.MainStart + d.Padding.MainStart;
                    absY = itemCrossOffset;
                    w    = d.ContentMain;
                    h    = itemCrossSize;
                }
                else
                {
                    absX = itemCrossOffset;
                    absY = mainOffset2 + d.Margin.MainStart + d.Border.MainStart + d.Padding.MainStart;
                    w    = itemCrossSize;
                    h    = d.ContentMain;
                }

                // Lay out children inside this flex item.
                // If the item is itself a flex/inline-flex container, invoke FlexEngine directly
                // so inner flex properties (grow, shrink, align-items) are respected.
                var nodeDisplay = d.Node.GetDisplay();
                var isInnerFlex = nodeDisplay == DisplayType.Flex || nodeDisplay == DisplayType.InlineFlex;
                float childH;
                if (isInnerFlex)
                    childH = FlexEngine.LayoutFlex(d.Node, absX, absY, w, 0, viewportWidth, viewportHeight);
                else
                    childH = BoxEngine.LayoutChildrenPublic(d.Node.Children, absX, absY, w, viewportWidth, viewportHeight);

                if (childH == 0 && !string.IsNullOrEmpty(d.Node.DisplayText))
                {
                    using var font = TextMeasure.CreateFont(d.Node);
                    var lines2 = TextMeasure.WrapText(d.Node.DisplayText, Math.Max(w, 1f), font, d.Node.GetWhiteSpace());
                    childH = lines2.Sum(l => l.Height);
                }

                // Apply explicit height if present (border-box aware)
                var explicitH = d.Node.GetHeight(viewportHeight);
                if (explicitH > 0)
                {
                    var isBorderBox = d.Node.Style.GetPropertyValue("box-sizing") == "border-box";
                    h = isBorderBox
                        ? Math.Max(0, explicitH - d.Border.CrossStart - d.Border.CrossEnd
                                                - d.Padding.CrossStart - d.Padding.CrossEnd)
                        : explicitH;
                }
                else if (effectiveAlign != AlignSelf.Stretch || !d.CrossSizeAuto)
                {
                    if (!isRow) h = d.ContentMain;
                    else        h = childH > 0 ? Math.Max(d.ContentCross, childH) : d.ContentCross;
                }

                // §9.4 Re-layout after stretch: if item cross size changed (was stretched),
                // re-invoke layout so inner content (especially inner flex containers) can
                // fill the new cross size with a known containerH.
                var finalCross = isRow ? h : w;
                var initialCross = isRow ? d.ContentCross : d.ContentCross;
                if (effectiveAlign == AlignSelf.Stretch && d.CrossSizeAuto && Math.Abs(finalCross - initialCross) > 0.5f)
                {
                    if (isInnerFlex)
                        FlexEngine.LayoutFlex(d.Node, absX, absY, w, isRow ? h : w, viewportWidth, viewportHeight);
                    else if (isRow)
                        BoxEngine.LayoutChildrenPublic(d.Node.Children, absX, absY, w, viewportWidth, viewportHeight);
                    else
                        BoxEngine.LayoutChildrenPublic(d.Node.Children, absX, absY, w, viewportWidth, viewportHeight);
                }

                d.Node.Box = new BoxDimensions
                {
                    ContentBox = new SKRect(absX, absY, absX + w, absY + (isRow ? h : d.ContentMain)),
                    Margin     = EdgeFromFlex(d.Margin,  isRow),
                    Padding    = EdgeFromFlex(d.Padding, isRow),
                    Border     = EdgeFromFlex(d.Border,  isRow),
                };

                // Advance main-axis cursor
                if (isReverse)
                    mainOffset2 -= d.OuterMain + gapMain + mainSpacing;
                else
                    mainOffset2 += d.OuterMain + gapMain + mainSpacing;
            }

            crossCursor += lineCrossSize + gapCross + crossSpacing;
        }

        // Return total height consumed
        if (isRow)
        {
            var totalCross = lines.Sum(l =>
            {
                return l.Max(d =>
                {
                    var box = d.Node.Box;
                    return d.Margin.CrossStart + d.Border.CrossStart + d.Padding.CrossStart
                         + box.ContentBox.Height
                         + d.Padding.CrossEnd + d.Border.CrossEnd + d.Margin.CrossEnd;
                });
            }) + Math.Max(0, lines.Count - 1) * gapCross;
            return totalCross;
        }
        else
        {
            var totalMain = lines.Max(l =>
                l.Sum(d => d.OuterMain) + Math.Max(0, l.Count - 1) * gapMain);
            return totalMain;
        }
    }

    // ── §9.9 Intrinsic sizing ─────────────────────────────────────────────────

    /// <summary>
    /// Computes the max-content main size of a flex container.
    /// Used to size inline-flex containers that have no explicit width/height.
    /// </summary>
    public static float MeasureMaxContentMain(
        LayoutNode container,
        float availableMain, float availableCross,
        float viewportWidth, float viewportHeight)
    {
        var dir   = container.GetFlexDirection();
        var isRow = dir == FlexDirection.Row || dir == FlexDirection.RowReverse;
        var fontSize = container.GetFontSize();
        var gap   = isRow
            ? container.GetGapColumn(availableMain, fontSize)
            : container.GetGapRow(availableCross, fontSize);

        var items = container.Children
            .Where(c => c.GetDisplay() != DisplayType.None &&
                        c.GetPosition() != PositionType.Absolute &&
                        c.GetPosition() != PositionType.Fixed &&
                        !(c.TagName == "#TEXT" && string.IsNullOrWhiteSpace(c.DisplayText)))
            .Select(c => new FlexItem(c, availableMain, availableCross, viewportWidth, viewportHeight, isRow))
            .ToList();

        if (items.Count == 0) return 0f;

        return items.Sum(d => d.OuterMain) + Math.Max(0, items.Count - 1) * gap;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static List<List<FlexItem>> WrapItems(List<FlexItem> data, float mainSize, float gap, bool wrap)
    {
        var lines = new List<List<FlexItem>>();
        if (!wrap)
        {
            lines.Add(data);
            return lines;
        }

        var current = new List<FlexItem>();
        var used    = 0f;
        foreach (var d in data)
        {
            var needed = d.OuterMain + (current.Count > 0 ? gap : 0);
            if (current.Count > 0 && used + needed > mainSize)
            {
                lines.Add(current);
                current = [];
                used    = 0;
            }
            current.Add(d);
            used += d.OuterMain + (current.Count > 1 ? gap : 0);
        }
        if (current.Count > 0) lines.Add(current);
        return lines;
    }

    /// <summary>
    /// Implements the §9.7 "Resolving Flexible Lengths" algorithm with iterative
    /// freeze-and-redistribute to handle min/max constraints correctly.
    /// </summary>
    private static void ResolveFlexibility(List<FlexItem> line, float mainSize, float gap)
    {
        if (mainSize == float.MaxValue) return; // indefinite container — no flex resolution

        var gapTotal = Math.Max(0, line.Count - 1) * gap;

        // Step 1: Determine used space and initial free space
        var usedMain = line.Sum(d => d.OuterMain) + gapTotal;
        var initialFreeSpace = mainSize - usedMain;
        var isGrowing = initialFreeSpace > 0;

        // Step 2: Initialize — all items unfrozen
        foreach (var d in line) d.Frozen = false;

        // Step 3: Freeze inflexible items
        foreach (var d in line)
        {
            if (isGrowing)
            {
                if (d.Grow == 0) d.Frozen = true;
            }
            else
            {
                if (d.Shrink == 0) d.Frozen = true;
            }
        }

        // Also freeze collapsed items
        foreach (var d in line)
            if (d.IsCollapsed) d.Frozen = true;

        // Step 4: Iterative loop
        for (int iteration = 0; iteration < line.Count + 1; iteration++)
        {
            // Recalculate free space among unfrozen items
            var frozenOuterMain = line.Where(d => d.Frozen).Sum(d => d.OuterMain);
            var unfrozen = line.Where(d => !d.Frozen).ToList();
            if (unfrozen.Count == 0) break;

            var unfrozenOuterExtra = unfrozen.Sum(d =>
                d.Margin.MainStart + d.Border.MainStart + d.Padding.MainStart +
                d.Margin.MainEnd + d.Border.MainEnd + d.Padding.MainEnd);
            var freeSpace = mainSize - frozenOuterMain - unfrozenOuterExtra - gapTotal;

            // Subtract unfrozen items' flex base sizes
            freeSpace -= unfrozen.Sum(d => d.FlexBasis);

            if (isGrowing)
            {
                var totalGrow = unfrozen.Sum(d => d.Grow);
                if (totalGrow == 0) break;

                // If total flex factor < 1, clamp free space
                if (totalGrow < 1)
                    freeSpace = Math.Min(freeSpace, initialFreeSpace * totalGrow);

                foreach (var d in unfrozen)
                    d.ContentMain = d.FlexBasis + freeSpace * (d.Grow / totalGrow);
            }
            else
            {
                var totalScaledShrink = unfrozen.Sum(d => d.Shrink * d.FlexBasis);
                if (totalScaledShrink == 0) break;

                var totalShrinkFactor = unfrozen.Sum(d => d.Shrink);
                if (totalShrinkFactor < 1)
                    freeSpace = Math.Max(freeSpace, initialFreeSpace * totalShrinkFactor);

                foreach (var d in unfrozen)
                {
                    var ratio = d.FlexBasis > 0 ? (d.Shrink * d.FlexBasis / totalScaledShrink) : 0;
                    d.ContentMain = d.FlexBasis + freeSpace * ratio;
                }
            }

            // Step 5: Fix min/max violations and freeze violating items
            var anyFrozen = false;
            foreach (var d in unfrozen)
            {
                if (d.ContentMain < d.MinMain)
                {
                    d.ContentMain = d.MinMain;
                    d.Frozen = true;
                    anyFrozen = true;
                }
                else if (d.ContentMain > d.MaxMain)
                {
                    d.ContentMain = d.MaxMain;
                    d.Frozen = true;
                    anyFrozen = true;
                }

                // Ensure non-negative
                d.ContentMain = Math.Max(0, d.ContentMain);
            }

            if (!anyFrozen)
            {
                // No violations — we're done. Freeze all.
                foreach (var d in unfrozen) d.Frozen = true;
                break;
            }
            // Otherwise loop again with frozen items clamped
        }
    }

    private static void ComputeJustify(JustifyContent justify, int count, float free,
        out float offset, out float spacing)
    {
        offset  = 0;
        spacing = 0;
        if (free <= 0) return;
        switch (justify)
        {
            case JustifyContent.FlexEnd:      offset  = free; break;
            case JustifyContent.Center:       offset  = free / 2f; break;
            case JustifyContent.SpaceBetween: spacing = count > 1 ? free / (count - 1) : 0; break;
            case JustifyContent.SpaceAround:
                spacing = free / count;
                offset  = spacing / 2f;
                break;
            case JustifyContent.SpaceEvenly:
                spacing = free / (count + 1);
                offset  = spacing;
                break;
        }
    }

    /// <summary>
    /// Computes the first baseline of a flex item (distance from content-box top to baseline).
    /// For text-bearing items, this is the font ascent. For items with children, it recurses.
    /// Falls back to the item's content cross size (bottom edge).
    /// </summary>
    private static float GetFirstBaseline(FlexItem d)
    {
        // If the item has direct text, use font metrics
        if (!string.IsNullOrEmpty(d.Node.DisplayText) && !d.Node.Children.Any())
        {
            using var font = TextMeasure.CreateFont(d.Node);
            // The baseline is the ascent (distance from top to alphabetic baseline)
            var metrics = font.Metrics;
            return -metrics.Ascent; // Ascent is negative in Skia
        }

        // Otherwise, use the content cross size as a fallback (bottom of content)
        return d.ContentCross;
    }

    private static EdgeSizes EdgeFromFlex(FlexEdge fe, bool isRow) => isRow
        ? new EdgeSizes { Left = fe.MainStart, Right = fe.MainEnd, Top = fe.CrossStart, Bottom = fe.CrossEnd }
        : new EdgeSizes { Top = fe.MainStart, Bottom = fe.MainEnd, Left = fe.CrossStart, Right = fe.CrossEnd };

    // ── FlexItem ──────────────────────────────────────────────────────────────

    private sealed class FlexItem
    {
        public LayoutNode Node;
        public float ContentMain;   // mutable — updated by grow/shrink
        public float ContentCross;
        public bool  CrossSizeAuto;
        public float Grow;
        public float Shrink;
        public FlexEdge Margin;
        public FlexEdge Padding;
        public FlexEdge Border;
        public float FlexBasis;     // original flex base size before clamping
        public float MinMain;       // min-width (row) or min-height (column)
        public float MaxMain;       // max-width (row) or max-height (column)
        public float MinCross;
        public float MaxCross;
        public bool  Frozen;        // used by iterative resolve algorithm
        public bool  IsCollapsed;   // visibility: collapse

        public float OuterMain  => Margin.MainStart + Border.MainStart + Padding.MainStart
                                 + ContentMain
                                 + Padding.MainEnd + Border.MainEnd + Margin.MainEnd;
        public float OuterCross => Margin.CrossStart + Border.CrossStart + Padding.CrossStart
                                  + ContentCross
                                  + Padding.CrossEnd + Border.CrossEnd + Margin.CrossEnd;

        public FlexItem(LayoutNode node, float containerW, float containerH,
                        float viewportWidth, float viewportHeight, bool isRow)
        {
            Node   = node;
            Grow   = node.GetFlexGrow();
            Shrink = node.GetFlexShrink();
            IsCollapsed = node.GetVisibility() == Visibility.Collapse;

            var fontSize = node.GetFontSize();
            var m = node.GetMargin(containerW, containerH, fontSize);
            var p = node.GetPadding(containerW, containerH, fontSize);
            var b = node.GetBorderWidth();

            Margin  = isRow ? new FlexEdge(m.Left, m.Right, m.Top, m.Bottom)
                            : new FlexEdge(m.Top, m.Bottom, m.Left, m.Right);
            Padding = isRow ? new FlexEdge(p.Left, p.Right, p.Top, p.Bottom)
                            : new FlexEdge(p.Top, p.Bottom, p.Left, p.Right);
            Border  = isRow ? new FlexEdge(b.Left, b.Right, b.Top, b.Bottom)
                            : new FlexEdge(b.Top, b.Bottom, b.Left, b.Right);

            var mainContainer  = isRow ? containerW : containerH;
            var crossContainer = isRow ? containerH : containerW;

            // Compute min/max constraints (content-box values)
            float rawMinMain  = isRow ? node.GetMinWidth(containerW, fontSize)  : node.GetMinHeight(containerH, fontSize);
            float rawMaxMain  = isRow ? node.GetMaxWidth(containerW, fontSize)  : node.GetMaxHeight(containerH, fontSize);
            float rawMinCross = isRow ? node.GetMinHeight(containerH, fontSize) : node.GetMinWidth(containerW, fontSize);
            float rawMaxCross = isRow ? node.GetMaxHeight(containerH, fontSize) : node.GetMaxWidth(containerW, fontSize);

            var isBorderBoxNode = node.Style.GetPropertyValue("box-sizing") == "border-box";
            var mainPB  = Border.MainStart + Border.MainEnd + Padding.MainStart + Padding.MainEnd;
            var crossPB = Border.CrossStart + Border.CrossEnd + Padding.CrossStart + Padding.CrossEnd;

            MinMain  = isBorderBoxNode && rawMinMain > 0  ? Math.Max(0, rawMinMain - mainPB) : rawMinMain;
            MaxMain  = isBorderBoxNode && !float.IsPositiveInfinity(rawMaxMain) ? Math.Max(0, rawMaxMain - mainPB) : rawMaxMain;
            MinCross = isBorderBoxNode && rawMinCross > 0 ? Math.Max(0, rawMinCross - crossPB) : rawMinCross;
            MaxCross = isBorderBoxNode && !float.IsPositiveInfinity(rawMaxCross) ? Math.Max(0, rawMaxCross - crossPB) : rawMaxCross;
            if (MaxMain < MinMain)   MaxMain  = MinMain;
            if (MaxCross < MinCross) MaxCross = MinCross;

            // flex-basis → main size
            var basis = node.GetFlexBasis(mainContainer);
            if (!float.IsNaN(basis))
            {
                ContentMain = isBorderBoxNode
                    ? Math.Max(0, basis - mainPB)
                    : basis;
            }
            else
            {
                // Fall back to width (row) or height (column), then intrinsic content size
                var explicitMain = isRow ? node.GetWidth(containerW) : node.GetHeight(containerH);
                if (explicitMain > 0)
                {
                    ContentMain = isBorderBoxNode
                        ? Math.Max(0, explicitMain - mainPB)
                        : explicitMain;
                }
                else
                {
                    // Measure intrinsic content size
                    ContentMain = MeasureIntrinsicMain(node, isRow, containerW, viewportWidth, viewportHeight);
                }
            }

            // §4.5: Automatic minimum size — when min-main is auto (unset), compute
            // the content-based minimum to prevent items shrinking below their content.
            bool autoMinMain = isRow ? node.IsAutoMinWidth() : node.IsAutoMinHeight();
            if (autoMinMain && MinMain == 0)
            {
                var contentMin = ComputeContentBasedMin(node, isRow, containerW, viewportWidth, viewportHeight);
                // Content-based minimum = min(content-size, specified-size-if-definite)
                var specifiedMain = isRow ? node.GetWidth(containerW) : node.GetHeight(containerH);
                MinMain = specifiedMain > 0 ? Math.Min(contentMin, specifiedMain) : contentMin;
                if (MaxMain < MinMain) MaxMain = MinMain;
            }

            // Store original flex base size, then clamp by min/max (Section 9.2)
            FlexBasis = ContentMain;
            ContentMain = Math.Max(MinMain, Math.Min(MaxMain, ContentMain));

            // visibility: collapse → zero main size
            if (IsCollapsed) ContentMain = 0;

            // Cross size
            var explicitCross = isRow ? node.GetHeight(crossContainer) : node.GetWidth(crossContainer);
            if (explicitCross > 0)
            {
                ContentCross = isBorderBoxNode
                    ? Math.Max(0, explicitCross - crossPB)
                    : explicitCross;
                CrossSizeAuto = false;
            }
            else
            {
                ContentCross = MeasureIntrinsicCross(node, isRow, ContentMain, viewportWidth, viewportHeight);
                CrossSizeAuto = true;
            }
            ContentCross = Math.Max(MinCross, Math.Min(MaxCross, ContentCross));

            // Aspect ratio: for replaced elements (IMG) with intrinsic dimensions,
            // compute cross size from main size via the ratio when cross is auto.
            if (CrossSizeAuto && node.TagName == "IMG")
            {
                var iW = node.IntrinsicWidth  > 0 ? (float)node.IntrinsicWidth  : node.Image?.Width  ?? 0f;
                var iH = node.IntrinsicHeight > 0 ? (float)node.IntrinsicHeight : node.Image?.Height ?? 0f;
                if (iW > 0 && iH > 0)
                {
                    var ratio = isRow ? iH / iW : iW / iH; // cross / main
                    ContentCross = Math.Max(MinCross, Math.Min(MaxCross, ContentMain * ratio));
                }
            }
        }

        /// <summary>
        /// Re-measures cross size after flex-grow/shrink has updated ContentMain.
        /// Only affects auto-cross items; call for row direction where height depends on width.
        /// </summary>
        public void RemeasureCross(float viewportWidth, float viewportHeight)
        {
            if (!CrossSizeAuto) return;
            ContentCross = MeasureIntrinsicCross(Node, isRow: true, ContentMain, viewportWidth, viewportHeight);
        }

        /// <summary>
        /// §4.5: Computes the content-based minimum size (the min-content size).
        /// For text: longest word width (can't break words shorter).
        /// For images: intrinsic size. For others: 0.
        /// </summary>
        private static float ComputeContentBasedMin(LayoutNode node, bool isRow,
            float containerW, float viewportWidth, float viewportHeight)
        {
            // Images: intrinsic dimension
            if (node.TagName == "IMG")
            {
                return isRow
                    ? (node.IntrinsicWidth  > 0 ? node.IntrinsicWidth  : node.Image?.Width  ?? 0f)
                    : (node.IntrinsicHeight > 0 ? node.IntrinsicHeight : node.Image?.Height ?? 0f);
            }

            // Text-bearing leaf nodes: longest word width
            if (!string.IsNullOrEmpty(node.DisplayText) && !node.Children.Any())
            {
                using var font = TextMeasure.CreateFont(node);
                if (isRow)
                {
                    var words = node.DisplayText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    return words.Length > 0 ? words.Max(w => font.MeasureText(w)) : 0f;
                }
                return font.Size * 1.4f;
            }

            // Form controls have intrinsic sizes
            if (node.TagName is "INPUT" or "BUTTON" or "SELECT" or "TEXTAREA")
                return isRow ? FormLayout.TextInputWidth : FormLayout.TextInputHeight;

            // Nested flex/block containers: recurse to deepest min-content
            // (simplified: just use 0 to avoid infinite recursion)
            return 0f;
        }

        private static float MeasureIntrinsicMain(LayoutNode node, bool isRow,
            float containerW, float viewportWidth, float viewportHeight)
        {
            if (!string.IsNullOrEmpty(node.DisplayText) && !node.Children.Any())
            {
                using var font = TextMeasure.CreateFont(node);
                return isRow ? font.MeasureText(node.DisplayText) : font.Size * 1.4f;
            }
            var visible = node.Children.Where(c => c.GetDisplay() != DisplayType.None).ToList();
            if (visible.Count == 0) return 0f;
            if (isRow)
            {
                return visible.Sum(c => MeasureChildWidth(c, containerW, viewportWidth, viewportHeight));
            }
            // Column: main axis is vertical — sum children's heights
            return visible.Sum(c => MeasureChildHeight(c, containerW, viewportWidth, viewportHeight));
        }

        private static float MeasureIntrinsicCross(LayoutNode node, bool isRow,
            float contentMain, float viewportWidth, float viewportHeight)
        {
            if (!string.IsNullOrEmpty(node.DisplayText) && !node.Children.Any())
            {
                using var font = TextMeasure.CreateFont(node);
                if (isRow)
                {
                    var lines = TextMeasure.WrapText(node.DisplayText, Math.Max(contentMain, 1f), font);
                    return lines.Sum(l => l.Height);
                }
                return font.MeasureText(node.DisplayText);
            }
            var visible = node.Children.Where(c => c.GetDisplay() != DisplayType.None).ToList();
            if (visible.Count == 0) return 0f;
            if (isRow)
            {
                // Cross axis for row = height — sum children's heights
                return visible.Sum(c => MeasureChildHeight(c, contentMain, viewportWidth, viewportHeight));
            }
            // Cross axis for column = width — widest child determines container width
            return visible.Max(c => MeasureChildWidth(c, contentMain, viewportWidth, viewportHeight));
        }

        private static float MeasureChildWidth(LayoutNode c, float containerW,
            float viewportWidth, float viewportHeight)
        {
            var w = c.GetWidth(containerW);
            if (w > 0) return w;
            if (!string.IsNullOrEmpty(c.DisplayText))
            {
                using var f = TextMeasure.CreateFont(c);
                return f.MeasureText(c.DisplayText);
            }
            return 0f;
        }

        private static float MeasureChildHeight(LayoutNode c, float containerW,
            float viewportWidth, float viewportHeight)
        {
            var h = c.GetHeight(viewportHeight);
            if (h > 0) return h;
            if (!string.IsNullOrEmpty(c.DisplayText))
            {
                using var f = TextMeasure.CreateFont(c);
                return f.Size * 1.4f;
            }
            return 0f;
        }
    }

    private readonly struct FlexEdge(float mainStart, float mainEnd, float crossStart, float crossEnd)
    {
        public readonly float MainStart  = mainStart;
        public readonly float MainEnd    = mainEnd;
        public readonly float CrossStart = crossStart;
        public readonly float CrossEnd   = crossEnd;
    }
}
