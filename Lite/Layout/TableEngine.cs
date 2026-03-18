using Lite.Extensions;
using Lite.Models;
using SkiaSharp;

namespace Lite.Layout;

/// <summary>
/// Lays out CSS tables: TABLE, TR, TD/TH.
/// Implements a simplified fixed-layout table model:
///   - Column widths: explicit px width on any cell wins; remaining columns share space evenly.
///   - Row heights: max cell outer height, then all cells in the row stretch to that height.
/// </summary>
internal static class TableEngine
{
    /// <summary>
    /// Lays out all rows and cells of a table within the given content area.
    /// Returns the total height consumed.
    /// </summary>
    public static float LayoutTable(
        LayoutNode table,
        float contentX, float contentY,
        float contentW,
        float viewportW, float viewportH)
    {
        var rows = CollectRows(table);
        if (rows.Count == 0) return 0f;

        var colCount = rows.Max(r => r.Cells.Count);
        if (colCount == 0) return 0f;

        var colWidths = ComputeColumnWidths(rows, colCount, contentW, viewportW, viewportH);

        var cursorY = contentY;

        foreach (var (rowNode, cells) in rows)
        {
            var rowFontSize = rowNode.GetFontSize();
            var rowPad  = rowNode.GetPadding(contentW, viewportH, rowFontSize);
            var rowBord = rowNode.GetBorderWidth();
            var rowMarg = rowNode.GetMargin(contentW, viewportH, rowFontSize);

            // Y of the cells' outer margin-top inside this row
            var rowInnerTop = cursorY + rowMarg.Top + rowBord.Top + rowPad.Top;

            // ── Pass 1: measure each cell's natural content height ──────────────
            float maxCellContrib = 0f;
            var n = Math.Min(cells.Count, colWidths.Length);
            var pass1 = new CellData[n];
            var cursorX = contentX;

            for (var c = 0; c < n; c++)
            {
                var cell         = cells[c];
                var cellFontSize = cell.GetFontSize();
                var cellPad      = cell.GetPadding(colWidths[c], viewportH, cellFontSize);
                var cellBord     = cell.GetBorderWidth();
                var cellMarg     = cell.GetMargin(colWidths[c], viewportH, cellFontSize);

                var cx = cursorX + cellMarg.Left + cellBord.Left + cellPad.Left;
                var cw = Math.Max(0f, colWidths[c]
                    - cellMarg.Left - cellMarg.Right
                    - cellBord.Left - cellBord.Right
                    - cellPad.Left  - cellPad.Right);
                var cy = rowInnerTop + cellMarg.Top + cellBord.Top + cellPad.Top;

                var contentH = BoxEngine.LayoutChildrenPublic(cell.Children, cx, cy, cw, viewportW, viewportH);

                // Cells with direct text (no element children)
                if (contentH == 0f && !string.IsNullOrEmpty(cell.DisplayText))
                {
                    using var font = TextMeasure.CreateFont(cell);
                    var lines = TextMeasure.WrapText(cell.DisplayText, Math.Max(cw, 1f), font, cell.GetWhiteSpace());
                    contentH = lines.Sum(l => l.Height);
                }

                // Cell's outer height contribution to the row
                var cellContrib = cellMarg.Top + cellBord.Top + cellPad.Top
                                + contentH
                                + cellPad.Bottom + cellBord.Bottom + cellMarg.Bottom;
                maxCellContrib = Math.Max(maxCellContrib, cellContrib);

                pass1[c] = new CellData(cell, cx, cy, cw, cellPad, cellBord, cellMarg);
                cursorX += colWidths[c];
            }

            // Honour explicit height on the row (e.g. tr { height: 40px })
            var explicitRowH = rowNode.GetHeight(0f, 0f, viewportH);
            if (explicitRowH > 0f) maxCellContrib = Math.Max(maxCellContrib, explicitRowH);

            var rowH_outer = rowMarg.Top + rowBord.Top + rowPad.Top
                           + maxCellContrib
                           + rowPad.Bottom + rowBord.Bottom + rowMarg.Bottom;

            // ── Pass 2: commit uniform row height to every cell ──────────────────
            for (var c = 0; c < n; c++)
            {
                var (cell, cx, cy, cw, cellPad, cellBord, cellMarg) = pass1[c];

                var finalH = Math.Max(0f,
                    maxCellContrib
                    - cellMarg.Top - cellBord.Top - cellPad.Top
                    - cellPad.Bottom - cellBord.Bottom - cellMarg.Bottom);

                // Re-layout children so % heights resolve correctly at the final size
                BoxEngine.LayoutChildrenPublic(cell.Children, cx, cy, cw, viewportW, viewportH, finalH);

                cell.Box = new BoxDimensions
                {
                    ContentBox = new SKRect(cx, cy, cx + cw, cy + finalH),
                    Padding    = cellPad,
                    Border     = cellBord,
                    Margin     = cellMarg,
                };
            }

            // ── Row box ──────────────────────────────────────────────────────────
            var rowCX = contentX + rowMarg.Left + rowBord.Left + rowPad.Left;
            var rowCY = cursorY  + rowMarg.Top  + rowBord.Top  + rowPad.Top;
            var rowCW = Math.Max(0f,
                contentW - rowMarg.Left - rowMarg.Right
                         - rowBord.Left - rowBord.Right
                         - rowPad.Left  - rowPad.Right);

            rowNode.Box = new BoxDimensions
            {
                ContentBox = new SKRect(rowCX, rowCY, rowCX + rowCW, rowCY + maxCellContrib),
                Padding    = rowPad,
                Border     = rowBord,
                Margin     = rowMarg,
            };

            cursorY += rowH_outer;
        }

        return cursorY - contentY;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private record struct CellData(
        LayoutNode Cell,
        float CX, float CY, float CW,
        EdgeSizes Pad, EdgeSizes Bord, EdgeSizes Marg);

    private record RowInfo(LayoutNode Row, List<LayoutNode> Cells);

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
                // Transparent row-group containers — look inside for rows
                CollectRowsFrom(child.Children, rows);
            }
        }
    }

    /// <summary>
    /// Determines pixel width for each column.
    /// Explicit widths on any cell in the column take priority;
    /// remaining space is divided evenly among auto columns.
    /// </summary>
    private static float[] ComputeColumnWidths(
        List<RowInfo> rows,
        int colCount,
        float availableW,
        float viewportW, float viewportH)
    {
        var widths = new float[colCount];

        // Gather explicit widths (first row wins per column)
        foreach (var (_, cells) in rows)
        {
            for (var c = 0; c < cells.Count && c < colCount; c++)
            {
                if (widths[c] == 0f)
                {
                    var w = cells[c].GetWidth(availableW);
                    if (w > 0f) widths[c] = w;
                }
            }
        }

        // Evenly distribute remaining width among auto columns
        var fixedSum = widths.Sum();
        var autoCnt  = widths.Count(w => w == 0f);
        var autoW    = autoCnt > 0 ? Math.Max(0f, availableW - fixedSum) / autoCnt : 0f;

        for (var c = 0; c < colCount; c++)
            if (widths[c] == 0f) widths[c] = autoW;

        return widths;
    }
}
