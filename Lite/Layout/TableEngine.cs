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
    private static bool IsBorderCollapse(LayoutNode table)
    {
        var raw = table.TryResolveStyle("border-collapse", out var ov)
            ? ov : table.Style.GetPropertyValueSafe("border-collapse");
        return raw?.Trim() == "collapse";
    }

    /// <summary>Returns the border-spacing value in px (default 2px).</summary>
    private static float GetBorderSpacing(LayoutNode table)
    {
        var raw = table.TryResolveStyle("border-spacing", out var ov)
            ? ov : table.Style.GetPropertyValueSafe("border-spacing");
        if (string.IsNullOrWhiteSpace(raw)) return 2f;
        raw = raw.Trim().Split(' ')[0]; // Use first value (horizontal)
        if (raw.EndsWith("px") && float.TryParse(raw[..^2],
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var px))
            return px;
        if (float.TryParse(raw, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var plain))
            return plain;
        return 2f;
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
        var spacing = collapse ? 0f : GetBorderSpacing(table);

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

        var cursorY = contentY + captionTopH + spacing;

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
                spannedH += rowHeights[r] + (r > p.Row ? spacing : 0f);
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
            ry += rowHeights[r] + spacing;
        }

        // ── Pass 2: commit final positions to every cell ──────────────────
        foreach (var p in placements)
        {
            var cell = p.Cell;
            var cellW = CellSpanWidth(colWidths, p.Col, p.ColSpan, spacing);
            var cellH = 0f;
            for (int r = p.Row; r < p.Row + p.RowSpan && r < rowCount; r++)
                cellH += rowHeights[r] + (r > p.Row ? spacing : 0f);

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
        public LayoutNode Cell;
        public int Row, Col, ColSpan, RowSpan;
        public float MeasuredOuterH;
        public float MeasuredCW;
        public EdgeSizes Pad, Bord, Marg;
    }

    private record RowInfo(LayoutNode Row, List<LayoutNode> Cells);

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

    private static List<RowInfo> CollectRows(LayoutNode table)
    {
        var rows = new List<RowInfo>();
        CollectRowsFrom(table.Children, rows);
        return rows;
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
            else if (child.TagName is "TBODY" or "THEAD" or "TFOOT")
            {
                CollectRowsFrom(child.Children, rows);
            }
        }
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
    /// Block children stack, so the column needs the widest child; text gives max = one-line width
    /// and min = the widest single word.</summary>
    private static (float Min, float Max) MeasureIntrinsic(LayoutNode node, float viewportH)
    {
        float min = 0f, max = 0f;
        if (!string.IsNullOrEmpty(node.DisplayText))
        {
            using var font = TextMeasure.CreateFont(node);
            max = font.MeasureText(node.DisplayText);
            foreach (var word in node.DisplayText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                min = Math.Max(min, font.MeasureText(word));
        }
        foreach (var ch in node.Children)
        {
            if (ch.GetDisplay() == DisplayType.None) continue;
            var (cMin, cMax) = MeasureIntrinsic(ch, viewportH);
            var fs = ch.GetFontSize();
            var pad = ch.GetPadding(0f, viewportH, fs);
            var bord = ch.GetBorderWidth();
            var marg = ch.GetMargin(0f, viewportH, fs);
            var boxExtra = pad.Left + pad.Right + bord.Left + bord.Right + marg.Left + marg.Right;
            var w = ch.GetWidth(0f);  // explicit px/em width (0 for auto/percent)
            if (w > 0f) { cMin = Math.Max(cMin, w); cMax = Math.Max(cMax, w); }
            min = Math.Max(min, cMin + boxExtra);
            max = Math.Max(max, cMax + boxExtra);
        }
        return (min, max);
    }
}
