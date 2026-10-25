using AngleSharp.Css;
using Lite.Extensions;
using Lite.Models;
using SkiaSharp;

namespace Lite.Layout;

/// <summary>
/// Lays out CSS tables: TABLE, TR, TD/TH.
/// Supports colspan and rowspan via a 2D grid placement model.
///   - Column widths: explicit px width on any cell (colspan=1) wins; remaining columns share space evenly.
///   - Row heights: max cell outer height per row, cells with rowspan stretch across multiple rows.
/// </summary>
internal static class TableEngine
{
    /// <summary>Returns true if the table uses border-collapse: collapse.</summary>
    internal static bool IsBorderCollapse(LayoutNode table)
    {
        var raw = table.TryResolveStyle("border-collapse", out var ov)
            ? ov : table.Style.GetPropertyValueSafe("border-collapse");
        return raw?.Trim() == "collapse";
    }

    /// <summary>
    /// Returns the border-spacing value in px. CSS 2.1 §17.6.1 gives 'border-spacing' an initial
    /// value of 0; the familiar 2px comes from the HTML UA stylesheet's <c>table { border-spacing:
    /// 2px }</c>, which matches the TABLE *element* only. A box that is merely
    /// <c>display: table</c> (including an anonymous table box, or a <c>::after</c> with
    /// <c>display:table</c>) therefore gets 0 — defaulting it to 2px indents its content.
    /// </summary>
    internal static (float Horizontal, float Vertical) GetBorderSpacing(LayoutNode table)
    {
        var uaDefault = table.TagName == "TABLE" ? 2f : 0f;
        var raw = table.TryResolveStyle("border-spacing", out var ov)
            ? ov : table.Style.GetPropertyValueSafe("border-spacing");
        if (string.IsNullOrWhiteSpace(raw)) return (uaDefault, uaDefault);
        var tokens = raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var fontSize = table.GetFontSize();
        bool Length(string token, out float px)
        {
            px = 0;
            return !token.Contains('%') && CssUnits.TryParse(token, fontSize, 0, 0, 0, out px) && float.IsFinite(px) && px >= 0;
        }
        // The whole declaration is invalid if either component is invalid.
        if (tokens.Length is < 1 or > 2 || !Length(tokens[0], out var horizontal)) return (uaDefault, uaDefault);
        if (tokens.Length == 1) return (horizontal, horizontal);
        return Length(tokens[1], out var vertical) ? (horizontal, vertical) : (uaDefault, uaDefault);
    }

    public static float LayoutTable(
        LayoutNode table,
        float contentX, float contentY,
        float contentW,
        float viewportW, float viewportH)
    {
        var rows = CollectRows(table);
        if (rows.Count == 0) return 0f;

        var collapse = IsBorderCollapse(table);
        var (spacing, verticalSpacing) = collapse ? (0f, 0f) : GetBorderSpacing(table);

        // Build 2D grid placement with colspan/rowspan support
        var placements = BuildGrid(rows, out var colCount, out var rowCount);
        if (colCount == 0) return 0f;

        var colWidths = IsFixedLayout(table)
            ? ComputeFixedColumnWidths(table, placements, colCount, contentW - spacing * (colCount + 1), viewportH)
            : ComputeColumnWidths(table, placements, colCount, contentW - spacing * (colCount + 1), viewportW, viewportH);
        var rowHeights = new float[rowCount];

        // Caption (CSS 2.1 §17.4): a block spanning the table width, placed above (default) or
        // below the table box per caption-side.
        var caption = table.Children.FirstOrDefault(c => IsCaption(c) && c.GetDisplay() != DisplayType.None);
        bool captionBottom = false;
        float captionTopH = 0f;
        if (caption is not null)
        {
            var side = caption.TryResolveStyle("caption-side", out var cs)
                ? cs : caption.Style.GetPropertyValueSafe("caption-side");
            captionBottom = side?.Trim() == "bottom";
            if (!captionBottom)
                captionTopH = LayoutCaptionBlock(caption, contentX, contentY, contentW, viewportW, viewportH);
        }

        var cursorY = contentY + captionTopH + verticalSpacing;

        // ── Pass 1: measure each cell's natural content height ──────────────
        foreach (var p in placements)
        {
            var cell = p.Cell;
            var cellFontSize = cell.GetFontSize();
            var cellW = CellSpanWidth(colWidths, p.Col, p.ColSpan, spacing);
            var cellPad = cell.GetPadding(cellW, viewportH, cellFontSize);
            var cellBord = cell.GetBorderWidth();
            var cellMarg = cell.GetMargin(cellW, viewportH, cellFontSize);

            var cx = CellX(contentX, colWidths, p.Col, spacing) + cellMarg.Left + cellBord.Left + cellPad.Left;
            var cw = Math.Max(0f, cellW
                - cellMarg.Left - cellMarg.Right
                - cellBord.Left - cellBord.Right
                - cellPad.Left - cellPad.Right);
            var cy = 0f; // Temporary, will be set in pass 2

            var contentH = BoxEngine.LayoutChildrenPublic(cell.Children, cx, cy, cw, viewportW, viewportH);

            if (contentH == 0f && !string.IsNullOrEmpty(cell.DisplayText))
            {
                using var font = TextMeasure.CreateFont(cell);
                var lh = cell.GetLineHeight(cell.GetFontSize());
                var lines = TextMeasure.WrapText(cell.RenderedText(), Math.Max(cw, 1f), font, cell.GetWhiteSpace(), lh);
                contentH = lines.Sum(l => l.Height);
            }
            // A cell's explicit height acts as a minimum for its content height.
            if (!cell.IsAutoHeight())
            {
                var explicitCellH = cell.GetHeight(0f, 0f, viewportH);
                if (explicitCellH > contentH) contentH = explicitCellH;
            }

            var cellOuterH = cellMarg.Top + cellBord.Top + cellPad.Top
                           + contentH
                           + cellPad.Bottom + cellBord.Bottom + cellMarg.Bottom;

            p.MeasuredOuterH = cellOuterH;
            p.Pad = cellPad;
            p.Bord = cellBord;
            p.Marg = cellMarg;
            p.MeasuredCW = cw;

            // Cells with rowspan=1 contribute to their row's height
            if (p.RowSpan == 1)
                rowHeights[p.Row] = Math.Max(rowHeights[p.Row], cellOuterH);
        }

        // Honour explicit row heights
        for (int r = 0; r < rowCount; r++)
        {
            if (r < rows.Count)
            {
                var explicitH = rows[r].Row.GetHeight(0f, 0f, viewportH);
                if (explicitH > 0f) rowHeights[r] = Math.Max(rowHeights[r], explicitH);
            }
        }

        // Distribute rowspan cells: if their measured height exceeds the sum of spanned rows, grow last row
        foreach (var p in placements)
        {
            if (p.RowSpan <= 1) continue;
            var spannedH = 0f;
            for (int r = p.Row; r < p.Row + p.RowSpan && r < rowCount; r++)
                spannedH += rowHeights[r] + (r > p.Row ? verticalSpacing : 0f);
            if (p.MeasuredOuterH > spannedH)
            {
                var lastRow = Math.Min(p.Row + p.RowSpan - 1, rowCount - 1);
                rowHeights[lastRow] += p.MeasuredOuterH - spannedH;
            }
        }

        // §17.5.3: a specified table height is a minimum; the extra goes to the rows in
        // proportion to their heights (evenly when they are all empty). Ignoring it collapsed a
        // 206px table to its content (background-043-ref's bottom-aligned stripe sat at the top).
        // Like browsers, the height covers the table's border box. Percentages need a definite
        // containing block and are left alone.
        if (rowCount > 0 && !table.IsAutoHeight() &&
            !(table.TryResolveStyle("height", out var tableHeightRaw) ? tableHeightRaw : table.Style.GetPropertyValueSafe("height")).Contains('%'))
        {
            var tableBorder = table.GetBorderWidth();
            var tablePadding = table.GetPadding(contentW, viewportH, table.GetFontSize());
            var target = table.GetHeight(0f, 0f, viewportH) - tableBorder.Top - tableBorder.Bottom
                         - tablePadding.Top - tablePadding.Bottom - captionTopH;
            var used = rowHeights.Sum() + verticalSpacing * (rowCount + 1);
            if (target > used + 0.01f)
            {
                var extra = target - used;
                var total = rowHeights.Sum();
                for (int r = 0; r < rowCount; r++)
                    rowHeights[r] += total > 0f ? extra * rowHeights[r] / total : extra / rowCount;
            }
        }

        // ── Compute row Y positions ──────────────────────────────────────
        var rowYs = new float[rowCount];
        var ry = cursorY;
        for (int r = 0; r < rowCount; r++)
        {
            rowYs[r] = ry;
            ry += rowHeights[r] + verticalSpacing;
        }

        // ── Pass 2: commit final positions to every cell ──────────────────
        void CommitPlacements()
        {
            foreach (var p in placements)
            {
                var cell = p.Cell;
                var cellW = CellSpanWidth(colWidths, p.Col, p.ColSpan, spacing);
                var cellH = 0f;
                for (int r = p.Row; r < p.Row + p.RowSpan && r < rowCount; r++)
                    cellH += rowHeights[r] + (r > p.Row ? verticalSpacing : 0f);

                var cx = CellX(contentX, colWidths, p.Col, spacing) + p.Marg.Left + p.Bord.Left + p.Pad.Left;
                var cy = rowYs[p.Row] + p.Marg.Top + p.Bord.Top + p.Pad.Top;
                var cw = p.MeasuredCW;
                var finalH = Math.Max(0f,
                    cellH - p.Marg.Top - p.Bord.Top - p.Pad.Top
                          - p.Pad.Bottom - p.Bord.Bottom - p.Marg.Bottom);

                BoxEngine.LayoutChildrenPublic(cell.Children, cx, cy, cw, viewportW, viewportH, finalH);

                cell.Box = new BoxDimensions
                {
                    ContentBox = new SKRect(cx, cy, cx + cw, cy + finalH),
                    Padding = p.Pad,
                    Border = p.Bord,
                    Margin = p.Marg,
                };
            }
        }

        CommitPlacements();

        // CSS 2.1 §17.5.3 vertical alignment of cells within their rows: 'baseline' cells share
        // the row baseline (the lowest first-baseline among them, which may grow the row so the
        // shifted content fits); 'middle' centres the content box; 'bottom' pins it to the row
        // bottom. 'top' (and the placement above) is the default geometry already committed.
        AlignCellVerticalAlignment(placements, rowYs, rowHeights, rowCount, viewportW, viewportH, CommitPlacements);

        // CSS 2.1 §17.6.2.1: in the collapsing model each shared edge is painted with the
        // dominant border among all boxes adjoining it; store the winners on the cells for
        // the painter.
        if (collapse)
            ResolveCollapsedBorders(table, placements);

        // ── Row boxes ──────────────────────────────────────────────────────
        for (int r = 0; r < rows.Count && r < rowCount; r++)
        {
            var rowNode = rows[r].Row;
            var rowFontSize = rowNode.GetFontSize();
            var rowPad = rowNode.GetPadding(contentW, viewportH, rowFontSize);
            var rowBord = rowNode.GetBorderWidth();
            var rowMarg = rowNode.GetMargin(contentW, viewportH, rowFontSize);

            var rowCX = contentX + rowMarg.Left + rowBord.Left + rowPad.Left;
            var rowCY = rowYs[r] + rowMarg.Top + rowBord.Top + rowPad.Top;
            var rowCW = Math.Max(0f,
                contentW - rowMarg.Left - rowMarg.Right
                         - rowBord.Left - rowBord.Right
                         - rowPad.Left - rowPad.Right);

            rowNode.Box = new BoxDimensions
            {
                ContentBox = new SKRect(rowCX, rowCY, rowCX + rowCW, rowCY + rowHeights[r]),
                Padding = rowPad,
                Border = rowBord,
                Margin = rowMarg,
            };
        }

        // ── Column / column-group boxes (§17.5.1 layers 2 and 3) ──────────────
        // A column spans the rows top to bottom; a group spans its columns. The painter draws
        // their backgrounds above the table's and below the rows' (they had no box at all, so a
        // col { background: orange } painted nothing — fixed-table-layout-017).
        if (rowCount > 0)
        {
            var gridTop = rowYs[0];
            var gridBottom = rowYs[rowCount - 1] + rowHeights[rowCount - 1];
            var col = 0;
            void ColumnBoxes(LayoutNode container)
            {
                foreach (var child in container.Children)
                {
                    var group = IsColumnGroup(child);
                    if (!group && !IsColumn(child)) continue;
                    var first = col;
                    if (group && child.Children.Any(IsColumn)) ColumnBoxes(child);
                    else
                    {
                        var span = 1;
                        if (child.Attributes.TryGetValue("span", out var raw) && int.TryParse(raw, out var n)) span = Math.Clamp(n, 1, 1000);
                        col += span;
                    }
                    var last = Math.Min(col, colCount) - 1;
                    if (last < first) continue;
                    var left = CellX(contentX, colWidths, first, spacing);
                    var right = CellX(contentX, colWidths, last, spacing) + colWidths[last];
                    child.Box = new BoxDimensions { ContentBox = new SKRect(left, gridTop, right, gridBottom) };
                }
            }
            ColumnBoxes(table);
        }

        // ── Row-group boxes (§17.5.1: a group spans its rows) ─────────────────
        // Without a box of its own a row group painted nothing — its outline (§18.4 applies
        // to every element; outline-applies-to-001's blue frame) and its background layer.
        foreach (var group in rows.Take(rowCount).Select(r => r.Row.Parent).OfType<LayoutNode>()
                     .Where(p => !ReferenceEquals(p, table) && p.GetDisplay() == DisplayType.TableRowGroup).Distinct())
        {
            var spanned = rows.Take(rowCount).Where(r => ReferenceEquals(r.Row.Parent, group)).Select(r => r.Row.Box.BorderBox).ToList();
            group.Box = new BoxDimensions
            {
                ContentBox = new SKRect(spanned.Min(b => b.Left), spanned.Min(b => b.Top),
                    spanned.Max(b => b.Right), spanned.Max(b => b.Bottom)),
            };
        }

        if (caption is not null && captionBottom)
            ry += LayoutCaptionBlock(caption, contentX, ry, contentW, viewportW, viewportH);

        return ry - contentY;
    }

    /// <summary>Lays out a table CAPTION as a block spanning the table width and returns its
    /// margin-box height. Sets caption.Box so it paints in the normal child pass.</summary>
    private static string? RawDisplay(LayoutNode node) =>
        (node.TryResolveStyle("display", out var d) ? d : node.Style.GetPropertyValueSafe("display"))?.Trim();

    /// <summary>A column: the COL element or any 'display: table-column' box (GetDisplay maps
    /// both column keywords to None since they generate no box of their own).</summary>
    internal static bool IsColumn(LayoutNode node) => node.TagName == "COL" || RawDisplay(node) == "table-column";

    /// <summary>A column group: the COLGROUP element or any 'display: table-column-group' box.</summary>
    internal static bool IsColumnGroup(LayoutNode node) => node.TagName == "COLGROUP" || RawDisplay(node) == "table-column-group";

    /// <summary>The table's caption: the HTML CAPTION element or any 'display: table-caption' box.</summary>
    private static bool IsCaption(LayoutNode node) => node.TagName == "CAPTION" || RawDisplay(node) == "table-caption";

    private static float LayoutCaptionBlock(LayoutNode caption, float x, float y, float width,
        float viewportW, float viewportH)
    {
        // A caption is a block-level box. The HTML CAPTION element needs that said explicitly
        // (an empty inline caption would otherwise paint nothing); a CSS 'table-caption' box
        // already lays out as a block and must keep its keyword, or the next layout would no
        // longer recognise it as the caption.
        if (caption.TagName == "CAPTION") caption.StyleOverrides["display"] = "block";
        var fs = caption.GetFontSize();
        var pad = caption.GetPadding(width, viewportH, fs);
        var bord = caption.GetBorderWidth();
        var marg = caption.GetMargin(width, viewportH, fs);
        // Its containing block is the table wrapper (§17.4): an explicit width is its own,
        // 'auto' fills the wrapper's width.
        var cw = caption.IsAutoWidth()
            ? Math.Max(0f, width - marg.Left - marg.Right - bord.Left - bord.Right - pad.Left - pad.Right)
            : caption.GetWidth(width, fs);
        var cx = x + marg.Left + bord.Left + pad.Left;
        var cy = y + marg.Top + bord.Top + pad.Top;

        var contentH = BoxEngine.LayoutChildrenPublic(caption.Children, cx, cy, cw, viewportW, viewportH);
        if (contentH == 0f && !string.IsNullOrEmpty(caption.DisplayText))
        {
            using var font = TextMeasure.CreateFont(caption);
            var lh = caption.GetLineHeight(fs);
            var lines = TextMeasure.WrapText(caption.RenderedText(), Math.Max(cw, 1f), font, caption.GetWhiteSpace(), lh);
            contentH = lines.Sum(l => l.Height);
        }
        // Honour an explicit height on the caption.
        if (!caption.IsAutoHeight())
        {
            var h = caption.GetHeight(0f, 0f, viewportH);
            if (h > 0f) contentH = h;
        }

        caption.Box = new BoxDimensions
        {
            ContentBox = new SKRect(cx, cy, cx + cw, cy + contentH),
            Padding = pad,
            Border = bord,
            Margin = marg,
        };
        return marg.Top + bord.Top + pad.Top + contentH + pad.Bottom + bord.Bottom + marg.Bottom;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private sealed record CollapsedGeometry(int Generation, Dictionary<LayoutNode, EdgeSizes> Cells, EdgeSizes Table);

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<LayoutNode, CollapsedGeometry> s_collapsed = new();

    /// <summary>§17.6.2 border widths that box geometry uses in the collapsing model, or null
    /// when <paramref name="node"/> is not part of a collapsed table. A cell gets half of each
    /// resolved (dominant) edge — borders are centred on the grid lines; the table gets half of
    /// its first row's outer left/right cells' borders and half of the widest top/bottom outer
    /// border; rows, row groups and columns get none (their borders take part only in the
    /// conflict). Using the cells' own full widths put a row's 3px collapsed bottom border on a
    /// 0px row — painted above the table (the border-*-applies-to-00x row/group family).</summary>
    internal static EdgeSizes? CollapsedBorderWidth(LayoutNode node)
    {
        if (node.TagName.StartsWith('#') && node.TagName is not ("#anon-cell" or "#anon-row" or "#anon-table")) return null;
        var display = node.GetDisplay();
        if (display is DisplayType.Table or DisplayType.InlineTable)
            return IsBorderCollapse(node) ? Collapsed(node).Table : null;
        if (display is not (DisplayType.TableCell or DisplayType.TableRow or DisplayType.TableRowGroup)
            && !IsColumn(node) && !IsColumnGroup(node))
            return null;
        LayoutNode? table = null;
        for (var a = node.Parent; a is not null; a = a.Parent)
            if (a.GetDisplay() is DisplayType.Table or DisplayType.InlineTable) { table = a; break; }
        if (table is null || !IsBorderCollapse(table)) return null;
        if (display != DisplayType.TableCell) return default(EdgeSizes);
        return Collapsed(table).Cells.TryGetValue(node, out var half) ? half : null;
    }

    private static CollapsedGeometry Collapsed(LayoutNode table)
    {
        if (s_collapsed.TryGetValue(table, out var cached) && cached.Generation == BoxEngine.Generation)
            return cached;
        var cells = new Dictionary<LayoutNode, EdgeSizes>(ReferenceEqualityComparer.Instance);
        var tableEdges = default(EdgeSizes);
        var rows = CollectRows(table);
        if (rows.Count > 0)
        {
            var placements = BuildGrid(rows, out var colCount, out var rowCount);
            ResolveCollapsedBorders(table, placements);
            static float W(Dictionary<string, EdgeBorder>? e, string side) =>
                e is not null && e.TryGetValue(side, out var b) && b.Style is not (BorderStyle.None or BorderStyle.Hidden)
                    ? b.Width : 0f;
            foreach (var p in placements)
            {
                var e = p.Cell.CollapsedEdgeBorders;
                cells[p.Cell] = new EdgeSizes
                {
                    Top = W(e, "top") / 2f, Right = W(e, "right") / 2f,
                    Bottom = W(e, "bottom") / 2f, Left = W(e, "left") / 2f,
                };
                if (p.Row == 0) tableEdges.Top = Math.Max(tableEdges.Top, W(e, "top") / 2f);
                if (p.Row + p.RowSpan >= rowCount) tableEdges.Bottom = Math.Max(tableEdges.Bottom, W(e, "bottom") / 2f);
                if (p.Row == 0 && p.Col == 0) tableEdges.Left = W(e, "left") / 2f;
                if (p.Row == 0 && p.Col + p.ColSpan >= colCount) tableEdges.Right = W(e, "right") / 2f;
            }
        }
        var geometry = new CollapsedGeometry(BoxEngine.Generation, cells, tableEdges);
        s_collapsed.AddOrUpdate(table, geometry);
        return geometry;
    }

    private class CellPlacement
    {
        public required LayoutNode Cell;
        public int Row, Col, ColSpan, RowSpan;
        public float MeasuredOuterH;
        public float MeasuredCW;
        public EdgeSizes Pad, Bord, Marg;
    }

    private record RowInfo(LayoutNode Row, List<LayoutNode> Cells);

    // ─────────────────────────────────────────────────────────────────────────
    // Collapsing border conflict resolution (CSS 2.1 §17.6.2.1)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Resolves every cell edge's dominant border among the adjoining cell (when a
    /// shared edge) and the table box (at the outer boundary). Row, row-group, column, and
    /// column-group borders join the candidates in a later pass. Tie-breaking follows the
    /// specification: left over right, top over bottom, and cells over the table.</summary>
    private static void ResolveCollapsedBorders(LayoutNode table, List<CellPlacement> placements)
    {
        var byPosition = new Dictionary<(int Row, int Col), CellPlacement>(placements.Count);
        foreach (var p in placements)
            byPosition[(p.Row, p.Col)] = p;
        var tableBorders = NodeEdgeBorders(table);

        // Column and column-group borders conflict with the cell edges inside their span
        // (§17.6.2.1: cell > row > row group > column > column group > table). A column's
        // left/right edges run the full table height — they conflict with every cell edge in
        // the column's span — and its top/bottom edges coincide with the table's top/bottom
        // over that column, so a 'border' set on a col beats the table's own border around
        // the whole frame (border-conflict-element-032). A column group's edges do the same
        // over its first/last column (border-conflict-element-039).
        var columnByIndex = new Dictionary<int, Dictionary<string, EdgeBorder>>();
        var groupByIndex = new Dictionary<int, Dictionary<string, EdgeBorder>>();
        var firstInGroup = new HashSet<int>();
        var lastInGroup = new HashSet<int>();
        var columnIndex = 0;
        int SpanOf(LayoutNode node)
        {
            var span = 1;
            if (node.Attributes.TryGetValue("span", out var raw))
                int.TryParse(raw, out span);
            return Math.Clamp(span, 1, 1000);
        }
        void CollectColumns(LayoutNode container)
        {
            foreach (var child in container.Children)
            {
                if (IsColumn(child))
                {
                    var borders = NodeEdgeBorders(child);
                    for (var s = 0; s < SpanOf(child); s++)
                        columnByIndex[columnIndex++] = borders;
                }
                else if (IsColumnGroup(child))
                {
                    var start = columnIndex;
                    if (child.Children.Count == 0)
                    {
                        var borders = NodeEdgeBorders(child);
                        for (var s = 0; s < SpanOf(child); s++)
                            columnByIndex[columnIndex++] = borders;
                    }
                    else CollectColumns(child);
                    // The group's own borders join the conflict at the covered columns' outer
                    // edges; the column wins element ties (column > column group).
                    var gborders = NodeEdgeBorders(child);
                    for (var i = start; i < columnIndex; i++)
                        groupByIndex[i] = gborders;
                    if (columnIndex > start) { firstInGroup.Add(start); lastInGroup.Add(columnIndex - 1); }
                }
            }
        }
        CollectColumns(table);

        foreach (var p in placements)
        {
            var own = NodeEdgeBorders(p.Cell);
            var columnBorders = columnByIndex.GetValueOrDefault(p.Col);
            var colGroupBorders = groupByIndex.GetValueOrDefault(p.Col);
            var westColumn = columnByIndex.GetValueOrDefault(p.Col - 1);
            var westColGroup = groupByIndex.GetValueOrDefault(p.Col - 1);
            var eastColumn = columnByIndex.GetValueOrDefault(p.Col + p.ColSpan);
            var eastColGroup = groupByIndex.GetValueOrDefault(p.Col + p.ColSpan);
            var result = new Dictionary<string, EdgeBorder>(4, StringComparer.Ordinal);
            // §17.6.2.1 element precedence on equal width and style: cell > row > row group >
            // table. The embedded-element tests (border-conflict-element-*) turn exactly on the
            // row and row-group borders joining the conflict at every edge they share.
            var row = RowOf(p.Cell);
            var rowBorders = row is null ? null : NodeEdgeBorders(row);
            if (Environment.GetEnvironmentVariable("LITE_TRACE_BC") == "1")
                Console.WriteLine($"[BC] cell({p.Row},{p.Col}) row={(row is null ? "null" : row.TagName)} rowTop={(rowBorders is null ? "-" : rowBorders["top"].Width + "/" + rowBorders["top"].Style)} tableTop={tableBorders["top"].Width}");
            var rowGroup = row?.Parent?.GetDisplay() == DisplayType.TableRowGroup ? row.Parent : null;
            var groupBorders = rowGroup is null ? null : NodeEdgeBorders(rowGroup);

            var left = own["left"];
            if (byPosition.TryGetValue((p.Row, p.Col - 1), out var west))
            {
                left = Dominant(NodeEdgeBorders(west.Cell)["right"], left); // left over right
                var westRow = RowOf(west.Cell);
                if (rowBorders is not null)
                    left = Dominant(left, westRow is not null
                        ? Dominant(NodeEdgeBorders(westRow)["right"], rowBorders["left"])
                        : rowBorders["left"]);                              // left over right
                // Column borders join every vertical edge they span, the groups' facing edges
                // join at a group boundary — candidates in precedence order (§17.6.2.1 column >
                // column group), and same-type ties go to the left, or to the right under
                // 'direction: rtl' (border-conflict-element-029).
                var rtl = table.GetDirection() == "rtl";
                if (columnBorders is not null)
                    left = Dominant(left, westColumn is not null
                        ? (rtl ? Dominant(columnBorders["left"], westColumn["right"])
                               : Dominant(westColumn["right"], columnBorders["left"]))
                        : columnBorders["left"]);
                if (westColGroup is not null && lastInGroup.Contains(p.Col - 1))
                    left = Dominant(left, rtl
                        ? Dominant(Bd(colGroupBorders, "left"), westColGroup["right"])
                        : Dominant(westColGroup["right"], Bd(colGroupBorders, "left")));
                else if (colGroupBorders is not null && firstInGroup.Contains(p.Col))
                    left = Dominant(left, colGroupBorders["left"]);
            }
            else
            {
                left = Dominant(left, Bd(rowBorders, "left"));
                left = Dominant(left, Bd(groupBorders, "left"));
                left = Dominant(left, Bd(columnBorders, "left"));
                left = Dominant(left, firstInGroup.Contains(p.Col)
                    ? Dominant(Bd(columnBorders, "left"), Bd(colGroupBorders, "left"))
                    : Bd(columnBorders, "left"));
                left = Dominant(left, tableBorders["left"]);                // cell over table
            }
            result["left"] = left;

            var right = own["right"];
            if (byPosition.TryGetValue((p.Row, p.Col + p.ColSpan), out var east))
            {
                right = Dominant(right, NodeEdgeBorders(east.Cell)["left"]);
                var eastRow = RowOf(east.Cell);
                if (rowBorders is not null)
                    right = Dominant(right, eastRow is not null
                        ? Dominant(rowBorders["right"], NodeEdgeBorders(eastRow)["left"])
                        : rowBorders["right"]);
                var eastColSpanIndex = p.Col + p.ColSpan;
                var rtl2 = table.GetDirection() == "rtl";
                if (columnBorders is not null)
                    right = Dominant(right, eastColumn is not null
                        ? (rtl2 ? Dominant(eastColumn["left"], columnBorders["right"])
                                : Dominant(columnBorders["right"], eastColumn["left"]))
                        : columnBorders["right"]);
                if (eastColGroup is not null && firstInGroup.Contains(eastColSpanIndex))
                    right = Dominant(right, rtl2
                        ? Dominant(eastColGroup["left"], Bd(colGroupBorders, "right"))
                        : Dominant(Bd(colGroupBorders, "right"), eastColGroup["left"]));
                else if (colGroupBorders is not null && lastInGroup.Contains(p.Col + p.ColSpan - 1))
                    right = Dominant(right, colGroupBorders["right"]);
            }
            else
            {
                right = Dominant(right, Bd(rowBorders, "right"));
                right = Dominant(right, Bd(groupBorders, "right"));
                right = Dominant(right, Bd(columnBorders, "right"));
                right = Dominant(right, lastInGroup.Contains(p.Col + p.ColSpan - 1)
                    ? Dominant(Bd(columnBorders, "right"), Bd(colGroupBorders, "right"))
                    : Bd(columnBorders, "right"));
                right = Dominant(right, tableBorders["right"]);
            }
            result["right"] = right;

            var top = own["top"];
            if (p.Row > 0 && byPosition.TryGetValue((p.Row - 1, p.Col), out var north))
            {
                top = Dominant(NodeEdgeBorders(north.Cell)["bottom"], top); // top over bottom
                var northRow = RowOf(north.Cell);
                if (rowBorders is not null)
                    top = Dominant(top, northRow is not null
                        ? Dominant(NodeEdgeBorders(northRow)["bottom"], rowBorders["top"])
                        : rowBorders["top"]);                               // top over bottom
                // The row groups' facing borders join the shared horizontal edge too: a group's
                // top at its first row, the north group's bottom at its last (§17.6.2.1 —
                // border-conflict-style-105/106's hidden tfoot/thead border suppresses the
                // red cells' edges on BOTH sides of the boundary).
                var northRowGroup = northRow?.Parent?.GetDisplay() == DisplayType.TableRowGroup
                    ? northRow.Parent : null;
                if (rowGroup is not null && ReferenceEquals(FirstRowOf(rowGroup), row))
                    top = Dominant(top, Bd(groupBorders, "top"));
                if (northRowGroup is not null && ReferenceEquals(LastRowOf(northRowGroup), northRow))
                    top = Dominant(top, NodeEdgeBorders(northRowGroup)["bottom"]);
            }
            else
            {
                top = Dominant(top, Bd(rowBorders, "top"));
                top = Dominant(top, Bd(groupBorders, "top"));
                // A column's top edge coincides with the table's top over its span (§17.6.2.1:
                // column above column group above table on ties) — a border set on a col/colgroup
                // beats the table's own border along the top of its columns (032/039).
                top = Dominant(top, Bd(columnBorders, "top"));
                top = Dominant(top, Bd(colGroupBorders, "top"));
                top = Dominant(top, tableBorders["top"]);
            }
            result["top"] = top;

            var bottom = own["bottom"];
            if (byPosition.TryGetValue((p.Row + p.RowSpan, p.Col), out var south))
            {
                bottom = Dominant(bottom, NodeEdgeBorders(south.Cell)["top"]);
                var southRow = RowOf(south.Cell);
                if (rowBorders is not null)
                    bottom = Dominant(bottom, southRow is not null
                        ? Dominant(rowBorders["bottom"], NodeEdgeBorders(southRow)["top"])
                        : rowBorders["bottom"]);
                // Same at the south boundary: the own group's bottom at its last row, the
                // south group's top at its first row.
                var southRowGroup = southRow?.Parent?.GetDisplay() == DisplayType.TableRowGroup
                    ? southRow.Parent : null;
                if (rowGroup is not null && ReferenceEquals(LastRowOf(rowGroup), row))
                    bottom = Dominant(bottom, Bd(groupBorders, "bottom"));
                if (southRowGroup is not null && ReferenceEquals(FirstRowOf(southRowGroup), southRow))
                    bottom = Dominant(bottom, NodeEdgeBorders(southRowGroup)["top"]);
            }
            else
            {
                bottom = Dominant(bottom, Bd(rowBorders, "bottom"));
                bottom = Dominant(bottom, Bd(groupBorders, "bottom"));
                bottom = Dominant(bottom, Bd(columnBorders, "bottom"));
                bottom = Dominant(bottom, Bd(colGroupBorders, "bottom"));
                bottom = Dominant(bottom, tableBorders["bottom"]);
            }
            result["bottom"] = bottom;

            p.Cell.CollapsedEdgeBorders = result;

            var rowCount = 0;
            var colCount = 0;
            foreach (var q in placements)
            {
                rowCount = Math.Max(rowCount, q.Row + q.RowSpan);
                colCount = Math.Max(colCount, q.Col + q.ColSpan);
            }
            HashSet<string>? outer = null;
            void Mark(string edge)
            {
                (outer ??= new HashSet<string>(StringComparer.Ordinal)).Add(edge);
            }
            if (p.Row == 0) Mark("top");
            if (p.Col == 0) Mark("left");
            if (p.Row + p.RowSpan >= rowCount) Mark("bottom");
            if (p.Col + p.ColSpan >= colCount) Mark("right");
            p.Cell.CollapsedOuterEdges = outer;
        }
    }


    private static EdgeBorder Bd(Dictionary<string, EdgeBorder>? borders, string edge)
        => borders is not null ? borders[edge] : default;

    private static LayoutNode? RowOf(LayoutNode cell)
        => cell.Parent?.GetDisplay() == DisplayType.TableRow ? cell.Parent : null;

    /// <summary>The group's first/last ROW child, skipping the whitespace text nodes the
    /// projection keeps between the row elements (a colgroup/row-group's Children[0] can be
    /// a #text node, so the raw ends misidentify the boundary rows).</summary>
    private static LayoutNode? FirstRowOf(LayoutNode group)
        => group.Children.FirstOrDefault(c => c.GetDisplay() == DisplayType.TableRow);

    private static LayoutNode? LastRowOf(LayoutNode group)
        => group.Children.LastOrDefault(c => c.GetDisplay() == DisplayType.TableRow);

    private static Dictionary<string, EdgeBorder> NodeEdgeBorders(LayoutNode node)
    {
        // The DECLARED widths: the conflict is resolved between the boxes' own borders
        // (GetBorderWidth answers with the collapsed result this resolution produces).
        var widths = node.GetOwnBorderWidth();
        return new Dictionary<string, EdgeBorder>(4, StringComparer.Ordinal)
        {
            ["top"] = new(widths.Top, node.GetBorderStyleTop(), node.GetBorderTopColor()),
            ["right"] = new(widths.Right, node.GetBorderStyleRight(), node.GetBorderRightColor()),
            ["bottom"] = new(widths.Bottom, node.GetBorderStyleBottom(), node.GetBorderBottomColor()),
            ["left"] = new(widths.Left, node.GetBorderStyleLeft(), node.GetBorderLeftColor()),
        };
    }

    /// <summary>§17.6.2.1 dominance: hidden suppresses everything, then the widest border,
    /// then the style order double > solid > dashed > dotted > ridge > outset > groove >
    /// inset > none; 'a' wins exact ties (callers pass candidates in tie-winning order).</summary>
    private static EdgeBorder Dominant(EdgeBorder a, EdgeBorder b)
    {
        if (a.Style == BorderStyle.Hidden) return a;
        if (b.Style == BorderStyle.Hidden) return b;
        if (a.Width != b.Width) return a.Width > b.Width ? a : b;
        if (a.Style != b.Style) return StyleRank(a.Style) >= StyleRank(b.Style) ? a : b;
        return a;
    }

    private static int StyleRank(BorderStyle style) => style switch
    {
        BorderStyle.Double => 9,
        BorderStyle.Solid => 8,
        BorderStyle.Dashed => 7,
        BorderStyle.Dotted => 6,
        BorderStyle.Ridge => 5,
        BorderStyle.Outset => 4,
        BorderStyle.Groove => 3,
        BorderStyle.Inset => 2,
        _ => 1,
    };

    /// <summary>
    /// Builds a 2D grid placement list, handling colspan and rowspan.
    /// </summary>
    private static List<CellPlacement> BuildGrid(List<RowInfo> rows, out int colCount, out int rowCount)
    {
        rowCount = rows.Count;
        // First pass: determine column count considering colspan
        var maxCol = 0;
        foreach (var r in rows)
        {
            var cols = 0;
            foreach (var cell in r.Cells)
            {
                cell.Attributes.TryGetValue("colspan", out var csStr);
                int cs = 1;
                if (csStr != null) int.TryParse(csStr, out cs);
                if (cs < 1) cs = 1;
                cols += cs;
            }
            maxCol = Math.Max(maxCol, cols);
        }
        colCount = maxCol;
        if (colCount == 0) return [];

        // 2D occupied grid (grown as rowspan might extend beyond initial row count)
        var occupied = new bool[rowCount + 20, colCount];
        var placements = new List<CellPlacement>();

        for (int r = 0; r < rows.Count; r++)
        {
            int col = 0;
            foreach (var cell in rows[r].Cells)
            {
                // Skip occupied slots (from previous rowspan)
                while (col < colCount && occupied[r, col]) col++;
                if (col >= colCount) break;

                cell.Attributes.TryGetValue("colspan", out var csStr);
                cell.Attributes.TryGetValue("rowspan", out var rsStr);
                int cs = 1, rs = 1;
                if (csStr != null) int.TryParse(csStr, out cs);
                if (rsStr != null) int.TryParse(rsStr, out rs);
                if (cs < 1) cs = 1;
                if (rs < 1) rs = 1;

                // Clamp to available
                if (col + cs > colCount) cs = colCount - col;

                // Extend rowCount if needed
                if (r + rs > rowCount)
                    rowCount = r + rs;

                // Mark occupied
                for (int dr = 0; dr < rs; dr++)
                    for (int dc = 0; dc < cs; dc++)
                    {
                        var gr = r + dr;
                        var gc = col + dc;
                        if (gr < occupied.GetLength(0) && gc < occupied.GetLength(1))
                            occupied[gr, gc] = true;
                    }

                placements.Add(new CellPlacement { Cell = cell, Row = r, Col = col, ColSpan = cs, RowSpan = rs });
                col += cs;
            }
        }

        return placements;
    }

    /// <summary>Computes the X position for a cell starting at column col.</summary>
    private static float CellX(float contentX, float[] colWidths, int col, float spacing)
    {
        var x = contentX + spacing;
        for (int c = 0; c < col; c++)
            x += colWidths[c] + spacing;
        return x;
    }

    /// <summary>Computes the total width for a cell spanning colSpan columns.</summary>
    private static float CellSpanWidth(float[] colWidths, int startCol, int colSpan, float spacing)
    {
        float w = 0f;
        for (int c = startCol; c < startCol + colSpan && c < colWidths.Length; c++)
        {
            w += colWidths[c];
            if (c > startCol) w += spacing;
        }
        return w;
    }

    /// <summary>CSS 2.1 §17.5.3: "the baseline of a table is the baseline of the first row".
    /// Must be called after <see cref="LayoutTable"/> has positioned the table's rows/cells.
    /// Returns null if the table has no rows or the first row has no baseline-contributing
    /// content (e.g. all cells empty) — callers fall back to the table's bottom margin edge.</summary>
    public static float? GetFirstRowBaseline(LayoutNode table)
    {
        var rows = CollectRows(table);
        if (rows.Count == 0) return null;

        // A row's baseline is the lowest baseline among its cells (CSS 2.1 §17.5.3: the cell
        // whose content needs the most room above the shared baseline sets it).
        float? best = null;
        foreach (var cell in rows[0].Cells)
        {
            var y = BoxEngine.FindFirstBaselineY(cell);
            if (y.HasValue && (!best.HasValue || y.Value > best.Value)) best = y.Value;
        }
        return best;
    }

    /// <summary>CSS 2.1 §17.5.3 vertical alignment of cells within their rows. Baseline-aligned
    /// cells (the initial value) share the row's baseline — the LOWEST first-line baseline among
    /// them; a cell whose baseline sits above it shifts its content down, which may require the
    /// row to grow (the caller re-commits all placements when that happens, because later rows
    /// move). 'middle' centres the content box in the row; 'bottom' pins it to the row bottom.</summary>
    private static void AlignCellVerticalAlignment(List<CellPlacement> placements,
        float[] rowYs, float[] rowHeights, int rowCount, float viewportW, float viewportH, Action recommit)
    {
        var byPosition = new Dictionary<(int Row, int Col), CellPlacement>(placements.Count);
        foreach (var p in placements) byPosition[(p.Row, p.Col)] = p;

        for (var r = 0; r < rowCount; r++)
        {
            var rowCells = placements.Where(p => p.Row == r && p.RowSpan == 1).ToList();
            if (rowCells.Count == 0) continue;

            float? rowBaseline = null;
            var baselineCells = new List<(CellPlacement P, float BaselineY, float ContentH)>();
            foreach (var p in rowCells)
            {
                var va = p.Cell.GetVerticalAlign();
                var contentTop = p.Cell.Box.ContentBox.Top;
                var contentH = p.Cell.Box.ContentBox.Height;
                switch (va)
                {
                    case VerticalAlignType.Baseline or VerticalAlignType.Length or VerticalAlignType.Percentage:
                        if (BoxEngine.FindFirstBaselineY(p.Cell) is { } b)
                        {
                            // The offset kinds shift the first line within the cell too.
                            b += p.Cell.GetVerticalAlignOffset();
                            baselineCells.Add((p, b, contentH));
                            rowBaseline = rowBaseline is { } cur ? Math.Max(cur, b) : b;
                        }
                        break;
                }
            }
            var growth = 0f;
            var shifts = new List<(CellPlacement P, float Delta)>();
            foreach (var (p, b, contentH) in baselineCells)
            {
                var delta = rowBaseline!.Value - b;
                if (delta > 0f) shifts.Add((p, delta));
                // The shifted content must fit inside the row: content top + shift + height
                // (plus the cell's bottom padding/border) may exceed the committed row height.
                var cellBottomEdge = p.Cell.Box.ContentBox.Top + delta + contentH + p.Pad.Bottom + p.Bord.Bottom;
                var rowBottom = rowYs[r] + rowHeights[r];
                if (r + 1 >= rowCount || p.Row + p.RowSpan >= rowCount)
                    growth = Math.Max(growth, cellBottomEdge - rowBottom);
            }
            if (growth > 0.5f)
            {
                rowHeights[r] += growth;
                // Rows below this one move down.
                for (var rr = r + 1; rr < rowCount; rr++) rowYs[rr] += growth;
                recommit();
            }
            foreach (var (p, delta) in shifts)
            {
                if (delta <= 0f) continue;
                ShiftCellContent(p, delta, viewportW, viewportH);
            }

            // middle / bottom cells
            foreach (var p in rowCells)
            {
                var va = p.Cell.GetVerticalAlign();
                if (va is not (VerticalAlignType.Middle or VerticalAlignType.Bottom)) continue;
                // The content's NATURAL height: the committed content box already fills the row,
                // which made every shift zero (background-043-ref's bottom-aligned stripe).
                var contentH = p.MeasuredOuterH - p.Marg.Top - p.Bord.Top - p.Pad.Top
                               - p.Pad.Bottom - p.Bord.Bottom - p.Marg.Bottom;
                var rowH = rowHeights[r] - p.Pad.Top - p.Bord.Top - p.Pad.Bottom - p.Bord.Bottom
                           - p.Marg.Top - p.Marg.Bottom;
                var delta = va == VerticalAlignType.Middle ? (rowH - contentH) / 2f : rowH - contentH;
                if (delta <= 0.5f) continue;
                ShiftCellContent(p, delta, viewportW, viewportH);
            }
        }
    }

    /// <summary>Re-lays a cell's children at a vertically shifted content top and moves the
    /// cell's content box down by the same delta (the cell's outer geometry is unchanged).</summary>
    private static void ShiftCellContent(CellPlacement p, float delta, float viewportW, float viewportH)
    {
        var cy = p.Cell.Box.ContentBox.Top + delta;
        BoxEngine.LayoutChildrenPublic(p.Cell.Children, p.Cell.Box.ContentBox.Left, cy,
            p.Cell.Box.ContentBox.Width, viewportW, viewportH, p.Cell.Box.ContentBox.Height);
        // The shift becomes used top padding: the cell box still fills its row and only the
        // content moves (moving the content box alone dragged the cell's border down with it).
        var padding = p.Cell.Box.Padding;
        padding.Top += delta;
        p.Cell.Box = new BoxDimensions
        {
            ContentBox = new SKRect(p.Cell.Box.ContentBox.Left, cy,
                p.Cell.Box.ContentBox.Right, p.Cell.Box.ContentBox.Bottom),
            Padding = padding,
            Border = p.Cell.Box.Border,
            Margin = p.Cell.Box.Margin,
        };
    }

    private static List<RowInfo> CollectRows(LayoutNode table)
    {
        // §17.2.1: a table-header-group presents its rows above all other rows and a
        // table-footer-group below them — the same order an explicit thead/tbody/tfoot table
        // uses — regardless of DOM order. The infer-cells reftests author row-group,
        // header-group and footer-group spans in interleaved order and expect the anonymous
        // table to present header rows first.
        var headers = new List<LayoutNode>();
        var body = new List<LayoutNode>();
        var footers = new List<LayoutNode>();
        foreach (var child in table.Children)
        {
            switch (GroupKind(child))
            {
                case RowGroupKind.Header: headers.Add(child); break;
                case RowGroupKind.Footer: footers.Add(child); break;
                default: body.Add(child); break;
            }
        }
        var rows = new List<RowInfo>();
        CollectRowsFrom(headers, rows);
        CollectRowsFrom(body, rows);
        CollectRowsFrom(footers, rows);
        return rows;
    }

    private enum RowGroupKind { Header, Footer, Other }

    /// <summary>Header/footer classification by the RESOLVED display value (all three group
    /// kinds compute to DisplayType.TableRowGroup, so the tag and raw value decide).</summary>
    private static RowGroupKind GroupKind(LayoutNode node)
    {
        if (node.TagName is "THEAD") return RowGroupKind.Header;
        if (node.TagName is "TFOOT") return RowGroupKind.Footer;
        var raw = node.TryResolveStyle(PropertyNames.Display, out var ov)
            ? ov : node.Style.GetPropertyValueSafe(PropertyNames.Display);
        return raw?.Trim() switch
        {
            "table-header-group" => RowGroupKind.Header,
            "table-footer-group" => RowGroupKind.Footer,
            _ => RowGroupKind.Other,
        };
    }

    private static void CollectRowsFrom(IEnumerable<LayoutNode> children, List<RowInfo> rows)
    {
        foreach (var child in children)
        {
            if (child.GetDisplay() == DisplayType.TableRow)
            {
                var cells = child.Children
                    .Where(c => c.GetDisplay() == DisplayType.TableCell)
                    .ToList();
                rows.Add(new RowInfo(child, cells));
            }
            else if (child.GetDisplay() == DisplayType.TableRowGroup || child.TagName is "TBODY" or "THEAD" or "TFOOT")
            {
                CollectRowsFrom(child.Children, rows);
            }
        }
    }

    /// <summary>
    /// Shrink-to-fit content width of a table/inline-table (CSS 2.1 §17.5.2): the sum of the
    /// columns' max-content (preferred) widths plus border-spacing, clamped to
    /// <paramref name="availW"/> but never below the columns' min-content sum. An explicit table
    /// <c>width</c> wins. Used to size an inline-table (which shrink-wraps like inline-block).
    /// </summary>
    public static float MeasureTableWidth(LayoutNode table, float availW, float viewportW, float viewportH)
    {
        var explicitTableW = table.GetWidth(availW);
        if (explicitTableW > 0f) return explicitTableW;
        var (sumMin, sumMax, colCount) = MeasureTableContentWidths(table, availW, viewportH);
        var width = colCount == 0 ? 0f : Math.Max(sumMin, Math.Min(sumMax, availW));
        // §10.4 on the shrink-wrapped width: max-width caps it, min-width (which wins) floors it
        // (min-width-applies-to-013: an empty-celled table with min-width 1in is 1in wide).
        var fs = table.GetFontSize();
        return Math.Max(Math.Min(width, table.GetMaxWidth(availW, fs)), table.GetMinWidth(availW, fs));
    }

    /// <summary>The table's intrinsic content width: the PER-COLUMN max across rows, summed
    /// over the columns (§17.5.2.1's preferred-width definition), border-spacing included.
    /// Shared with IntrinsicSizer.TableMinMax so a table's shrink-to-fit measure can never
    /// disagree with the width the layout algorithm actually gives it.</summary>
    internal static (float SumMin, float SumMax, int ColCount) MeasureTableContentWidths(
        LayoutNode table, float availW, float viewportH)
    {
        var rows = CollectRows(table);
        if (rows.Count == 0) return (0f, 0f, 0);

        var spacing = IsBorderCollapse(table) ? 0f : GetBorderSpacing(table).Horizontal;
        var placements = BuildGrid(rows, out var colCount, out _);
        if (colCount == 0) return (0f, 0f, 0);

        var colMin = new float[colCount];
        var colMax = new float[colCount];
        foreach (var p in placements)
        {
            if (p.ColSpan != 1) continue;
            var explicitCol = p.Cell.GetWidth(availW);
            float cMin, cMax;
            if (explicitCol > 0f)
            {
                var fs = p.Cell.GetFontSize();
                var pad = p.Cell.GetPadding(availW, viewportH, fs);
                var bord = p.Cell.GetBorderWidth();
                cMin = cMax = explicitCol + pad.Left + pad.Right + bord.Left + bord.Right;
            }
            else
                (cMin, cMax) = MeasureCellIntrinsic(p.Cell, availW, viewportH);
            colMin[p.Col] = Math.Max(colMin[p.Col], cMin);
            colMax[p.Col] = Math.Max(colMax[p.Col], cMax);
        }
        var elementWidths = ColumnElementWidths(table, colCount, availW);
        for (var c = 0; c < colCount; c++)
        {
            colMin[c] = Math.Max(colMin[c], elementWidths[c]);
            colMax[c] = Math.Max(colMax[c], elementWidths[c]);
        }

        var spacingTotal = spacing * (colCount + 1);
        return (colMin.Sum() + spacingTotal, colMax.Sum() + spacingTotal, colCount);
    }

    /// <summary>
    /// Determines pixel width for each column (CSS 2.1 §17.5.2.2 automatic layout).
    /// Cells with an explicit width (colspan=1) fix their column; the remaining width is
    /// distributed to the auto columns according to their measured content min/max widths
    /// (a column with short content stays narrow; one with long content takes more), falling
    /// back to an even split for columns whose content width can't be measured (e.g. empty cells).
    /// </summary>
    /// <summary>The 'width' each column receives from a column element, or from a column
    /// group with no column children (applied to every column it spans); 0 where none.
    /// §17.5.2.2 makes it the column's minimum in automatic layout (width-applies-to-005's
    /// 1in column group around an empty cell must still produce a 1in square).</summary>
    private static float[] ColumnElementWidths(LayoutNode table, int colCount, float availableW)
    {
        var widths = new float[colCount];
        var index = 0;
        void Walk(LayoutNode container, float inherited)
        {
            foreach (var child in container.Children)
            {
                var group = IsColumnGroup(child);
                if (!group && !IsColumn(child)) continue;
                var own = child.IsAutoWidth() ? 0f : child.GetWidth(availableW);
                // §10.4 applies to columns too: max-width caps the width, min-width floors it.
                var fs = child.GetFontSize();
                own = Math.Max(Math.Min(own, child.GetMaxWidth(availableW, fs)), child.GetMinWidth(availableW, fs));
                if (group && child.Children.Any(IsColumn)) { Walk(child, own); continue; }
                var span = 1;
                if (child.Attributes.TryGetValue("span", out var raw) && int.TryParse(raw, out var n)) span = Math.Clamp(n, 1, 1000);
                var w = own > 0f ? own : inherited;
                for (var k = 0; k < span && index < colCount; k++, index++)
                    widths[index] = Math.Max(widths[index], w);
            }
        }
        Walk(table, 0f);
        return widths;
    }

    /// <summary>§17.5.2.1: 'table-layout: fixed' applies when the table's width is not auto.</summary>
    private static bool IsFixedLayout(LayoutNode table) =>
        !table.IsAutoWidth() &&
        (table.TryResolveStyle("table-layout", out var v) ? v : table.Style.GetPropertyValueSafe("table-layout"))?.Trim() == "fixed";

    /// <summary>§17.5.2.1 fixed table layout: a column element's width sets its column; otherwise
    /// a first-row cell's width does (a spanning cell's is shared over its columns); the columns
    /// left over divide the remaining space equally whatever their content, and space the
    /// columns do not use is spread over all of them. Only auto layout existed, so content
    /// minimums sized fixed tables (fixed-table-layout-003*: a 120px-padded empty cell took
    /// 120px instead of its equal share).</summary>
    private static float[] ComputeFixedColumnWidths(LayoutNode table, List<CellPlacement> placements, int colCount,
        float availableW, float viewportH)
    {
        var widths = new float[colCount];
        var known = new bool[colCount];

        var columnIndex = 0;
        void Columns(LayoutNode container)
        {
            foreach (var child in container.Children)
            {
                if (IsColumnGroup(child) && child.Children.Count > 0) { Columns(child); continue; }
                if (!IsColumn(child) && !IsColumnGroup(child)) continue;
                var span = 1;
                if (child.Attributes.TryGetValue("span", out var raw) && int.TryParse(raw, out var n)) span = Math.Clamp(n, 1, 1000);
                var w = child.IsAutoWidth() ? 0f : child.GetWidth(availableW);
                for (var k = 0; k < span && columnIndex < colCount; k++, columnIndex++)
                    if (w > 0f) { widths[columnIndex] = w; known[columnIndex] = true; }
            }
        }
        Columns(table);

        foreach (var p in placements.Where(p => p.Row == 0))
        {
            if (p.Cell.IsAutoWidth()) continue;
            var cols = Enumerable.Range(p.Col, Math.Min(p.ColSpan, colCount - p.Col)).Where(c => !known[c]).ToList();
            if (cols.Count == 0) continue;
            var pad = p.Cell.GetPadding(availableW, viewportH, p.Cell.GetFontSize());
            var bord = p.Cell.GetBorderWidth();
            var total = p.Cell.GetWidth(availableW) + pad.Left + pad.Right + bord.Left + bord.Right;
            foreach (var c in cols) { widths[c] = total / cols.Count; known[c] = true; }
        }

        var rest = Enumerable.Range(0, colCount).Where(c => !known[c]).ToList();
        var remaining = Math.Max(0f, availableW - widths.Sum());
        if (rest.Count > 0)
            foreach (var c in rest) widths[c] = remaining / rest.Count;
        else if (remaining > 0f && colCount > 0)
            for (var c = 0; c < colCount; c++) widths[c] += remaining / colCount;
        return widths;
    }

    private static float[] ComputeColumnWidths(
        LayoutNode table,
        List<CellPlacement> placements,
        int colCount,
        float availableW,
        float viewportW, float viewportH)
    {
        var widths = new float[colCount];

        // Gather explicit widths from cells with colspan=1 (these columns are fixed).
        foreach (var p in placements)
        {
            if (p.ColSpan != 1) continue;
            if (widths[p.Col] == 0f)
            {
                // A cell's 'width' is its content width: the column also holds the cell's
                // padding and borders (§17.5.2.2) — a td { width: 200px; border: 3px } is 206px.
                var w = p.Cell.GetWidth(availableW);
                if (w > 0f)
                {
                    var pad = p.Cell.GetPadding(availableW, viewportH, p.Cell.GetFontSize());
                    var bord = p.Cell.GetBorderWidth();
                    widths[p.Col] = w + pad.Left + pad.Right + bord.Left + bord.Right;
                }
            }
        }

        var autoCols = Enumerable.Range(0, colCount).Where(c => widths[c] == 0f).ToList();
        if (autoCols.Count == 0) return widths;

        // Measure intrinsic content min/max for each auto column from its colspan=1 cells.
        var colMin = new float[colCount];
        var colMax = new float[colCount];
        var hasContent = new bool[colCount];
        foreach (var p in placements)
        {
            if (p.ColSpan != 1 || widths[p.Col] != 0f) continue;
            var (cMin, cMax) = MeasureCellIntrinsic(p.Cell, availableW, viewportH);
            colMin[p.Col] = Math.Max(colMin[p.Col], cMin);
            colMax[p.Col] = Math.Max(colMax[p.Col], cMax);
            hasContent[p.Col] = true;
        }
        // A column (group) 'width' is a minimum for its column in automatic layout.
        var elementWidths = ColumnElementWidths(table, colCount, availableW);
        foreach (var c in autoCols)
            if (elementWidths[c] > 0f)
            {
                colMin[c] = Math.Max(colMin[c], elementWidths[c]);
                colMax[c] = Math.Max(colMax[c], elementWidths[c]);
                hasContent[c] = true;
            }

        var remaining = Math.Max(0f, availableW - widths.Sum());

        // No measurable content (e.g. all-empty cells) → preserve the legacy even split.
        if (!autoCols.Any(c => hasContent[c] && colMax[c] > 0f))
        {
            var even = remaining / autoCols.Count;
            foreach (var c in autoCols) widths[c] = even;
            return widths;
        }

        var sumMin = autoCols.Sum(c => colMin[c]);
        var sumMax = autoCols.Sum(c => colMax[c]);

        if (sumMax <= remaining)
        {
            // Room for every preferred width; give each its max and share the leftover equally.
            var extra = (remaining - sumMax) / autoCols.Count;
            foreach (var c in autoCols) widths[c] = colMax[c] + extra;
        }
        else if (sumMin >= remaining || sumMax <= sumMin)
        {
            // Not even room for the minimums (table overflows) — use minimums.
            foreach (var c in autoCols) widths[c] = colMin[c];
        }
        else
        {
            // Distribute the slack between min and max proportionally to each column's flexibility.
            var slack = (remaining - sumMin) / (sumMax - sumMin);
            foreach (var c in autoCols) widths[c] = colMin[c] + (colMax[c] - colMin[c]) * slack;
        }

        return widths;
    }

    /// <summary>Measures a table cell's intrinsic min (longest unbreakable unit) and max
    /// (preferred, no-wrap) content widths, including the cell's own padding+border.</summary>
    private static (float Min, float Max) MeasureCellIntrinsic(LayoutNode cell, float availableW, float viewportH)
    {
        var fs = cell.GetFontSize();
        var pad = cell.GetPadding(availableW, viewportH, fs);
        var bord = cell.GetBorderWidth();
        var extra = pad.Left + pad.Right + bord.Left + bord.Right;
        var (min, max) = MeasureIntrinsic(cell, viewportH);
        return (min + extra, max + extra);
    }

    /// <summary>Recursively measures intrinsic content widths (excluding the node's own box model).
    /// Block children stack, so the column needs the widest child; consecutive INLINE-level children
    /// share one line box (§10.3.5 max-content), so a run of them sums its members' widths — the
    /// old per-child max measured "b" + "c" as 9px instead of 17px and wrapped every multi-run
    /// anonymous cell. A run's min-content stays the widest member (breaks may occur between
    /// inline boxes); a BR ends the run.</summary>
    private static (float Min, float Max) MeasureIntrinsic(LayoutNode node, float viewportH)
    {
        if (FormLayout.IntrinsicWidth(node) is { } controlWidth)
        {
            var width = node.IsAutoWidth() ? controlWidth : node.GetWidth(0);
            return (width, width);
        }
        float min = 0f, max = 0f;
        // A whitespace-only text leaf arms the run's boundary flag instead of measuring: the
        // space between two rendered atoms counts exactly once (by the atom that follows, when
        // it has a rendered predecessor). Measuring each whitespace leaf at its raw or even
        // collapsed length double-counted every interior boundary — with the spaces restored
        // to cell content, the infer-cells columns came out one to two characters too wide.
        var pendingBoundarySpace = false;
        if (!string.IsNullOrEmpty(node.DisplayText))
        {
            using var font = TextMeasure.CreateFont(node);
            var ws = node.GetWhiteSpace();
            var preserves = ws is WhiteSpace.Pre or WhiteSpace.PreWrap;
            var raw = node.RenderedText();
            if (string.IsNullOrWhiteSpace(raw) && !preserves)
            {
                pendingBoundarySpace = true;
            }
            else
            {
                var text = preserves ? raw
                    : string.Join(' ', raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
                max = font.MeasureText(text);
                foreach (var word in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                    min = Math.Max(min, font.MeasureText(word));
            }
        }
        float runMin = 0f, runMax = 0f;
        var runHasContent = false;

        void FlushInlineRun()
        {
            if (runMax <= 0f && runMin <= 0f) { runHasContent = false; return; }
            max = Math.Max(max, runMax);
            min = Math.Max(min, runMin);
            runMin = runMax = 0f;
            runHasContent = false;
        }

        foreach (var ch in node.Children)
        {
            var d = ch.GetDisplay();
            if (d == DisplayType.None) continue;
            if (ch.TagName == "BR") { FlushInlineRun(); pendingBoundarySpace = false; continue; }
            // A whitespace-only child arms the run boundary (its space renders only when the
            // run already has content, exactly once); under pre/pre-wrap it measures as text.
            if (ch.TagName == "#text" && string.IsNullOrWhiteSpace(ch.DisplayText))
            {
                var chWs = ch.GetWhiteSpace();
                if (chWs is not (WhiteSpace.Pre or WhiteSpace.PreWrap))
                { pendingBoundarySpace = true; continue; }
            }
            // An inline-table child is consumed ATOMICALLY by the inline-run collector (sized
            // by MeasureTableWidth, cell padding and border-spacing included), so its intrinsic
            // measure must come from the same table model — recursing into it as plain inline
            // text under-measures the cell and clamps the inner table narrower than its content.
            var (cMin, cMax) = d == DisplayType.InlineTable
                ? IntrinsicSizer.ContentMinMax(ch, viewportH)
                : MeasureIntrinsic(ch, viewportH);
            // Anonymous nodes (#text, generated content, anonymous table parts) resolve their
            // style from the element they came from, so they have no box edges of their own —
            // exactly as InlineBoxEdges lays them out. Reading the host's border/padding here
            // widened every cell holding text or ::before content by its own edges again
            // (content-037/080: cell 2-4 border widths too wide). A #text never takes the host's
            // display either: it is inline content.
            var anonymous = ch.TagName.StartsWith('#');
            var fs = ch.GetFontSize();
            var boxExtra = 0f;
            if (!anonymous)
            {
                var pad = ch.GetPadding(0f, viewportH, fs);
                var bord = ch.GetBorderWidth();
                var marg = ch.GetMargin(0f, viewportH, fs);
                boxExtra = pad.Left + pad.Right + bord.Left + bord.Right + marg.Left + marg.Right;
            }
            var w = ch.TagName == "#text" ? 0f : ch.GetWidth(0f);  // explicit px/em width (0 for auto/percent)
            if (w > 0f) { cMin = Math.Max(cMin, w); cMax = Math.Max(cMax, w); }
            var inline = ch.TagName == "#text" || d is DisplayType.Inline or DisplayType.InlineBlock
                or DisplayType.InlineTable or DisplayType.InlineFlex;
            if (inline)
            {
                if (pendingBoundarySpace && runHasContent)
                {
                    using var spaceFont = TextMeasure.CreateFont(ch);
                    runMax += spaceFont.MeasureText(" ");
                }
                pendingBoundarySpace = false;
                runMin = Math.Max(runMin, cMin + boxExtra);
                runMax += cMax + boxExtra;
                runHasContent = true;
                continue;
            }
            FlushInlineRun();
            pendingBoundarySpace = false;
            min = Math.Max(min, cMin + boxExtra);
            max = Math.Max(max, cMax + boxExtra);
        }
        FlushInlineRun();
        return (min, max);
    }
}
