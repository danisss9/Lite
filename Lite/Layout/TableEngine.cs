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

        var colWidths = ComputeColumnWidths(placements, colCount, contentW - spacing * (colCount + 1), viewportW, viewportH);
        var rowHeights = new float[rowCount];

        // Caption (CSS 2.1 §17.4): a block spanning the table width, placed above (default) or
        // below the table box per caption-side.
        var caption = table.Children.FirstOrDefault(c => c.TagName == "CAPTION" && c.GetDisplay() != DisplayType.None);
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
                var lines = TextMeasure.WrapText(cell.DisplayText, Math.Max(cw, 1f), font, cell.GetWhiteSpace(), lh);
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

        if (caption is not null && captionBottom)
            ry += LayoutCaptionBlock(caption, contentX, ry, contentW, viewportW, viewportH);

        return ry - contentY;
    }

    /// <summary>Lays out a table CAPTION as a block spanning the table width and returns its
    /// margin-box height. Sets caption.Box so it paints in the normal child pass.</summary>
    private static float LayoutCaptionBlock(LayoutNode caption, float x, float y, float width,
        float viewportW, float viewportH)
    {
        // A caption is a block-level box; mark it so the painter draws its background/borders
        // (an empty inline caption would otherwise paint nothing).
        caption.StyleOverrides["display"] = "block";
        var fs = caption.GetFontSize();
        var pad = caption.GetPadding(width, viewportH, fs);
        var bord = caption.GetBorderWidth();
        var marg = caption.GetMargin(width, viewportH, fs);
        var cw = Math.Max(0f, width - marg.Left - marg.Right - bord.Left - bord.Right - pad.Left - pad.Right);
        var cx = x + marg.Left + bord.Left + pad.Left;
        var cy = y + marg.Top + bord.Top + pad.Top;

        var contentH = BoxEngine.LayoutChildrenPublic(caption.Children, cx, cy, cw, viewportW, viewportH);
        if (contentH == 0f && !string.IsNullOrEmpty(caption.DisplayText))
        {
            using var font = TextMeasure.CreateFont(caption);
            var lh = caption.GetLineHeight(fs);
            var lines = TextMeasure.WrapText(caption.DisplayText, Math.Max(cw, 1f), font, caption.GetWhiteSpace(), lh);
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

        foreach (var p in placements)
        {
            var own = NodeEdgeBorders(p.Cell);
            var result = new Dictionary<string, EdgeBorder>(4, StringComparer.Ordinal);

            var left = own["left"];
            if (byPosition.TryGetValue((p.Row, p.Col - 1), out var west))
                left = Dominant(NodeEdgeBorders(west.Cell)["right"], left); // left over right
            else
                left = Dominant(left, tableBorders["left"]);               // cell over table
            result["left"] = left;

            var right = own["right"];
            if (byPosition.TryGetValue((p.Row, p.Col + p.ColSpan), out var east))
                right = Dominant(right, NodeEdgeBorders(east.Cell)["left"]);
            else
                right = Dominant(right, tableBorders["right"]);
            result["right"] = right;

            var top = own["top"];
            if (p.Row > 0 && byPosition.TryGetValue((p.Row - 1, p.Col), out var north))
                top = Dominant(NodeEdgeBorders(north.Cell)["bottom"], top); // top over bottom
            else
                top = Dominant(top, tableBorders["top"]);
            result["top"] = top;

            var bottom = own["bottom"];
            if (byPosition.TryGetValue((p.Row + p.RowSpan, p.Col), out var south))
                bottom = Dominant(bottom, NodeEdgeBorders(south.Cell)["top"]);
            else
                bottom = Dominant(bottom, tableBorders["bottom"]);
            result["bottom"] = bottom;

            p.Cell.CollapsedEdgeBorders = result;
        }
    }

    private static Dictionary<string, EdgeBorder> NodeEdgeBorders(LayoutNode node)
    {
        var widths = node.GetBorderWidth();
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
            if (rowBaseline is null) continue;

            var growth = 0f;
            var shifts = new List<(CellPlacement P, float Delta)>();
            foreach (var (p, b, contentH) in baselineCells)
            {
                var delta = rowBaseline.Value - b;
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
                var contentH = p.Cell.Box.ContentBox.Height;
                var rowH = rowHeights[r] - p.Pad.Top - p.Bord.Top - p.Pad.Bottom - p.Bord.Bottom;
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
        p.Cell.Box = new BoxDimensions
        {
            ContentBox = new SKRect(p.Cell.Box.ContentBox.Left, cy,
                p.Cell.Box.ContentBox.Right, p.Cell.Box.ContentBox.Bottom),
            Padding = p.Cell.Box.Padding,
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
        var rows = CollectRows(table);
        if (rows.Count == 0) return Math.Max(0f, explicitTableW);

        var spacing = IsBorderCollapse(table) ? 0f : GetBorderSpacing(table).Horizontal;
        var placements = BuildGrid(rows, out var colCount, out _);
        if (colCount == 0) return Math.Max(0f, explicitTableW);

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

        var spacingTotal = spacing * (colCount + 1);
        if (explicitTableW > 0f) return explicitTableW;
        var sumMax = colMax.Sum() + spacingTotal;
        var sumMin = colMin.Sum() + spacingTotal;
        return Math.Max(sumMin, Math.Min(sumMax, availW));
    }

    /// <summary>
    /// Determines pixel width for each column (CSS 2.1 §17.5.2.2 automatic layout).
    /// Cells with an explicit width (colspan=1) fix their column; the remaining width is
    /// distributed to the auto columns according to their measured content min/max widths
    /// (a column with short content stays narrow; one with long content takes more), falling
    /// back to an even split for columns whose content width can't be measured (e.g. empty cells).
    /// </summary>
    private static float[] ComputeColumnWidths(
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
                var w = p.Cell.GetWidth(availableW);
                if (w > 0f) widths[p.Col] = w;
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
        if (!string.IsNullOrEmpty(node.DisplayText))
        {
            using var font = TextMeasure.CreateFont(node);
            max = font.MeasureText(node.DisplayText);
            foreach (var word in node.DisplayText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                min = Math.Max(min, font.MeasureText(word));
        }
        float runMin = 0f, runMax = 0f;

        void FlushInlineRun()
        {
            if (runMax <= 0f && runMin <= 0f) return;
            max = Math.Max(max, runMax);
            min = Math.Max(min, runMin);
            runMin = runMax = 0f;
        }

        foreach (var ch in node.Children)
        {
            var d = ch.GetDisplay();
            if (d == DisplayType.None) continue;
            if (ch.TagName == "BR") { FlushInlineRun(); continue; }
            // An inline-table child is consumed ATOMICALLY by the inline-run collector (sized
            // by MeasureTableWidth, cell padding and border-spacing included), so its intrinsic
            // measure must come from the same table model — recursing into it as plain inline
            // text under-measures the cell and clamps the inner table narrower than its content.
            var (cMin, cMax) = d == DisplayType.InlineTable
                ? IntrinsicSizer.ContentMinMax(ch, viewportH)
                : MeasureIntrinsic(ch, viewportH);
            var fs = ch.GetFontSize();
            var pad = ch.GetPadding(0f, viewportH, fs);
            var bord = ch.GetBorderWidth();
            var marg = ch.GetMargin(0f, viewportH, fs);
            var boxExtra = pad.Left + pad.Right + bord.Left + bord.Right + marg.Left + marg.Right;
            var w = ch.GetWidth(0f);  // explicit px/em width (0 for auto/percent)
            if (w > 0f) { cMin = Math.Max(cMin, w); cMax = Math.Max(cMax, w); }
            var inline = d is DisplayType.Inline or DisplayType.InlineBlock
                or DisplayType.InlineTable or DisplayType.InlineFlex;
            if (inline)
            {
                runMin = Math.Max(runMin, cMin + boxExtra);
                runMax += cMax + boxExtra;
                continue;
            }
            FlushInlineRun();
            min = Math.Max(min, cMin + boxExtra);
            max = Math.Max(max, cMax + boxExtra);
        }
        FlushInlineRun();
        return (min, max);
    }
}
