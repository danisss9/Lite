using Lite.Extensions;
using Lite.Interaction;
using Lite.Layout;
using Lite.Models;
using Lite.Network;
using Lite.Rendering;
using SkiaSharp;

namespace Lite;

internal static partial class Drawer
{
    /// <summary>Draws a block's own text run (the step-5 half of a text-bearing block whose
    /// background painted during the step-3 pass).</summary>
    private static void DrawNodeText(SKCanvas canvas, LayoutNode node)
    {
        if (string.IsNullOrEmpty(node.DisplayText)) return;
        using var font = TextMeasure.CreateFont(node);
        using var paint = new SKPaint { Color = node.GetColor(), IsAntialias = true };
        DrawWrappedText(canvas, node, node.BidiVisualBlockText(node.Box.ContentBox.Width, font),
            node.Box.ContentBox.Left, node.Box.ContentBox.Top, node.Box.ContentBox.Width, font, paint);
    }

    // -------------------------------------------------------------------------
    // Text elements (H1-H6, P, LABEL)
    // -------------------------------------------------------------------------

    private static void PaintTextBlock(SKCanvas canvas, LayoutNode node, int viewportWidth)
    {
        var box = node.Box;

        DrawBoxShadows(canvas, box, node);

        var bgColor = node.GetBackgroundColor();
        if (bgColor != SKColors.Transparent)
        {
            var (rx, ry) = node.GetBorderRadius(box.PaddingBox.Width, box.PaddingBox.Height);
            // Square backgrounds paint unantialiased — see PaintBlockDecorations.
            using var bgPaint = new SKPaint { Color = bgColor, IsAntialias = rx > 0 || ry > 0 };
            if (rx > 0 || ry > 0) canvas.DrawRoundRect(box.PaddingBox, rx, ry, bgPaint);
            else canvas.DrawRect(box.PaddingBox, bgPaint);
        }

        // §14.2: a P/H/LABEL box paints its background layers like any block — without the
        // image pass here, `p { background: url(...) }` painted only the colour half of the
        // shorthand (case-sensitive-001).
        var textGradient = node.GetLinearGradient();
        if (textGradient != null) DrawLinearGradient(canvas, box, node, textGradient);
        DrawBackgroundImage(canvas, node, box);

        DrawBorders(canvas, box, node);
        DrawOutline(canvas, box, node);

        _hitRegions.Add(CursorRegion(box.MarginBox, node, node.Href, node.NodeKey));

        if (string.IsNullOrEmpty(node.DisplayText)) return;

        // Appendix-E step discipline: during the step-3 descent the text runs belong to step 5 —
        // the block was collected there; drawing here would put P/H1/LABEL text under floats.
        if (_stepFloats != null)
        {
            Defer(_stepInlines!, canvas, node);
            return;
        }

        using var font = TextMeasure.CreateFont(node);
        using var paint = new SKPaint { Color = node.GetColor(), IsAntialias = true };

        DrawWrappedText(canvas, node, node.BidiVisualBlockText(box.ContentBox.Width, font),
                        box.ContentBox.Left, box.ContentBox.Top, box.ContentBox.Width, font, paint);
    }

    // -------------------------------------------------------------------------
    // List item
    // -------------------------------------------------------------------------

    private static void PaintListItem(SKCanvas canvas, LayoutNode node, int viewportWidth)
    {
        var box = node.Box;

        DrawBoxShadows(canvas, box, node);

        // Background
        var bgColor = node.GetBackgroundColor();
        if (bgColor != SKColors.Transparent)
        {
            var (rx, ry) = node.GetBorderRadius(box.PaddingBox.Width, box.PaddingBox.Height);
            // Square backgrounds paint unantialiased — see PaintBlockDecorations.
            using var bgPaint = new SKPaint { Color = bgColor, IsAntialias = rx > 0 || ry > 0 };
            if (rx > 0 || ry > 0) canvas.DrawRoundRect(box.PaddingBox, rx, ry, bgPaint);
            else canvas.DrawRect(box.PaddingBox, bgPaint);
        }

        DrawBorders(canvas, box, node);

        // §18.1: a list item's box shows its own cursor like any block (cursor-applies-to-010:
        // 'cursor: pointer' on a display:list-item box showed the default arrow).
        if (!node.GetPointerEventsNone())
            _hitRegions.Add(CursorRegion(box.BorderBox, node, nodeKey: node.NodeKey));

        var listStyleType = node.GetListStyleType();
        var listStylePos = node.GetListStylePosition();
        float insideMarkerWidth = 0f; // track width consumed by inside marker

        // list-style-image: when set and loadable, the image replaces the bullet/number marker.
        var listImageUrl = node.GetListStyleImage();
        SKBitmap? listImage = listImageUrl is null ? null : ResourceLoader.FetchImage(listImageUrl,
            node.DocumentState?.BaseUrl, node.DocumentState?.Session);

        if (listImage is not null)
        {
            var lineH = node.GetLineHeight(node.GetFontSize());
            float imgW = listImage.Width, imgH = listImage.Height;
            var dest = listStylePos == ListStylePosition.Inside
                ? new SKRect(box.ContentBox.Left, box.ContentBox.Top, box.ContentBox.Left + imgW, box.ContentBox.Top + imgH)
                : new SKRect(box.ContentBox.Left - imgW - 4f, box.ContentBox.Top, box.ContentBox.Left - 4f, box.ContentBox.Top + imgH);
            using var imgPaint = new SKPaint { IsAntialias = true };
            canvas.DrawBitmap(listImage, dest, imgPaint);
            if (listStylePos == ListStylePosition.Inside) insideMarkerWidth = imgW + 4f;
        }
        else if (listStyleType != ListStyleType.None)
        {
            using var markerFont = TextMeasure.CreateFont(node);
            using var markerPaint = new SKPaint { Color = node.GetColor(), IsAntialias = true };

            var markerText = GetMarkerText(listStyleType, node);
            var ascent = -markerFont.Metrics.Ascent;

            if (listStylePos == ListStylePosition.Inside)
            {
                // Inside: marker is part of content flow — draw it and offset content
                if (!string.IsNullOrEmpty(markerText))
                {
                    var markerStr = markerText + " ";
                    insideMarkerWidth = markerFont.MeasureText(markerStr);
                    canvas.DrawText(markerStr, box.ContentBox.Left, box.ContentBox.Top + ascent,
                                    SKTextAlign.Left, markerFont, markerPaint);
                    Rendering.PaintLog.Record(markerStr, box.ContentBox.Left, box.ContentBox.Top + ascent, markerFont, SKTextAlign.Left);
                }
                else
                {
                    var bulletSize = markerFont.Size * 0.2f;
                    insideMarkerWidth = bulletSize * 2 + markerFont.Size * 0.3f + 4f;
                    DrawBulletMarker(canvas, listStyleType, box.ContentBox.Left + markerFont.Size * 0.3f,
                                     box.ContentBox.Top + markerFont.Size * 0.55f, markerFont.Size, markerPaint);
                }
            }
            else
            {
                // Outside: marker is drawn to the left of content box
                var markerX = box.ContentBox.Left - 6f;
                var markerY = box.ContentBox.Top;

                if (!string.IsNullOrEmpty(markerText))
                {
                    canvas.DrawText(markerText, markerX, markerY + ascent,
                                    SKTextAlign.Right, markerFont, markerPaint);
                    Rendering.PaintLog.Record(markerText, markerX, markerY + ascent, markerFont, SKTextAlign.Right);
                }
                else
                {
                    DrawBulletMarker(canvas, listStyleType, markerX - markerFont.Size * 0.2f * 2,
                                     markerY + markerFont.Size * 0.55f, markerFont.Size, markerPaint);
                }
            }
        }

        // Paint own text — offset by inside marker width if applicable
        if (!string.IsNullOrEmpty(node.DisplayText) && node.Children.Count == 0)
        {
            using var font = TextMeasure.CreateFont(node);
            using var paint = new SKPaint { Color = node.GetColor(), IsAntialias = true };
            DrawWrappedText(canvas, node, node.RenderedText(),
                            box.ContentBox.Left + insideMarkerWidth, box.ContentBox.Top,
                            box.ContentBox.Width - insideMarkerWidth, font, paint);
        }

        PaintChildrenSorted(canvas, node, viewportWidth);
    }

    private static string? GetMarkerText(ListStyleType type, LayoutNode node)
    {
        var index = GetOrderedIndex(node);
        // All text marker styles take the "." suffix and share the §4.3.1 counter-style
        // table with the content property; the bullets return null and draw as glyphs.
        var style = type switch
        {
            ListStyleType.Decimal => "decimal",
            ListStyleType.DecimalLeadingZero => "decimal-leading-zero",
            ListStyleType.LowerAlpha => "lower-alpha",
            ListStyleType.UpperAlpha => "upper-alpha",
            ListStyleType.LowerRoman => "lower-roman",
            ListStyleType.UpperRoman => "upper-roman",
            ListStyleType.LowerGreek => "lower-greek",
            ListStyleType.Armenian => "armenian",
            ListStyleType.Georgian => "georgian",
            ListStyleType.Hebrew => "hebrew",
            ListStyleType.Hiragana => "hiragana",
            ListStyleType.HiraganaIroha => "hiragana-iroha",
            ListStyleType.Katakana => "katakana",
            ListStyleType.KatakanaIroha => "katakana-iroha",
            ListStyleType.CjkIdeographic => "cjk-ideographic",
            _ => null,
        };
        return style is null ? null : Layout.CssCounterStyles.Format(index, style) + ".";
    }

    private static void DrawBulletMarker(SKCanvas canvas, ListStyleType type, float cx, float cy, float fontSize, SKPaint paint)
    {
        var radius = fontSize * 0.2f;
        switch (type)
        {
            case ListStyleType.Disc:
                canvas.DrawCircle(cx, cy, radius, paint);
                break;
            case ListStyleType.Circle:
                using (var strokePaint = new SKPaint { Color = paint.Color, Style = SKPaintStyle.Stroke, StrokeWidth = 1f, IsAntialias = true })
                    canvas.DrawCircle(cx, cy, radius, strokePaint);
                break;
            case ListStyleType.Square:
                canvas.DrawRect(cx - radius, cy - radius, radius * 2, radius * 2, paint);
                break;
        }
    }


    private static bool IsInsideOrderedList(LayoutNode node)
    {
        var p = node.Parent;
        while (p != null)
        {
            if (p.TagName == "OL") return true;
            if (p.TagName == "UL") return false;
            p = p.Parent;
        }
        return false;
    }

    private static int GetOrderedIndex(LayoutNode node)
    {
        if (node.Parent == null) return 1;
        var index = 1;
        foreach (var sibling in node.Parent.Children)
        {
            if (sibling == node) break;
            if (sibling.TagName == "LI") index++;
        }
        return index;
    }

    /// <summary>The width of line <paramref name="index"/>'s float band, or <paramref name="fallback"/>
    /// when the run has none.</summary>
    private static float LineBandWidth(List<(float X, float Width)>? bands, int index, float fallback)
        => bands is { Count: > 0 } ? bands[Math.Min(index, bands.Count - 1)].Width : fallback;

    /// <summary>True when the node or any ancestor is a floated box (CSS 2.1 §5.12.1 excludes
    /// float content from :first-line styling).</summary>
    private static bool DescendsFromFloat(LayoutNode node)
    {
        for (var n = node; n is not null && n.TagName != "HTML"; n = n.Parent)
            if (n.GetFloat() != FloatType.None) return true;
        return false;
    }

    /// <param name="lineAligned">True when <paramref name="x"/>/<paramref name="maxWidth"/>
    /// describe a rect the line assembler already positioned — the §16.2 align shift was
    /// applied there over the whole line box's items, so painting must not apply it again
    /// inside the rect. Block own-text sites keep the painter-side alignment: their rect is
    /// the block's raw content box and the painter is the only aligner.</param>
    private static void DrawWrappedText(SKCanvas canvas, LayoutNode node, string text,
                                        float x, float y, float maxWidth,
                                        SKFont font, SKPaint paint, bool lineAligned = false)
    {
        var whiteSpace = node.GetWhiteSpace();
        var textAlign = node.GetTextAlign();
        var underline = node.IsUnderline();
        var lineThrough = node.IsLineThrough();
        var overline = node.IsOverline();
        var textTransform = node.GetTextTransform();
        var letterSpacing = node.GetLetterSpacing(font.Size);
        var wordSpacing = node.GetWordSpacing(font.Size);
        var textIndent = node.GetTextIndent(maxWidth, font.Size);
        var autoCursor = !node.GetPointerEventsNone() && node.GetCursor() == CursorType.Auto;

        // ::first-line styles are resolved from the node or its parent block. CSS 2.1 §5.12.1:
        // they do not apply inside descendant floats — a float's content is not part of the
        // block's first formatted line, so `div:first-line` must not style a floated span's text.
        var firstLineStyles = BoxEngine.FirstLineStylesOf(node, out var firstLineAllLines);
        if (firstLineStyles is not null && DescendsFromFloat(node)) firstLineStyles = null;
        var firstLineTransform = firstLineStyles is null ? TextTransform.None : BoxEngine.FirstLineTransform(node);

        // Text-transform (§16.5) is already applied: every caller passes RenderedText or a
        // slice of it. Transforming again re-capitalized a word continued from an earlier
        // inline ('T<span>his</span>' painted 'THis', text-transform-cap-002).

        // Layout may have wrapped this text against a per-line band (text beside a float, CSS 2.1
        // §9.5). Painting has to use exactly the same bands or the lines break in different places
        // than they were measured.
        var bands = node.LineBands;
        // A ::first-line 'text-transform' re-breaks the first line with the transformed text,
        // exactly as layout measured it (BoxEngine's own-text wrap).
        var lines = TextMeasure.WrapTextWithFirstLine(text, firstLineTransform, node.GetLanguage(), maxWidth, font, whiteSpace,
                                          node.GetLineHeight(node.GetFontSize()), bands?.Select(b => b.Width).ToList());

        // text-overflow: ellipsis — when overflow is clipped and only one line, truncate with "…"
        // Check node itself OR its parent block (text-overflow is non-inherited; inline children
        // get the visual effect from the nearest clipping ancestor).
        static bool HasEllipsisClip(LayoutNode n) =>
            n.IsTextOverflowEllipsis() && n.GetOverflow() != OverflowType.Visible;
        var useEllipsis = HasEllipsisClip(node)
            || (node.Parent != null && HasEllipsisClip(node.Parent));
        if (useEllipsis && lines.Count == 1 && lines[0].Width > maxWidth)
        {
            var (truncText, truncW) = TextMeasure.TruncateWithEllipsis(lines[0].Text, maxWidth, font);
            lines = [new TextLine(truncText, truncW, lines[0].Height, lines[0].Ascent)];
        }

        var lineY = y;
        var textShadow = node.GetTextShadow();
        var isFirstLine = true;

        var lastLineIndex = lines.Count - 1;
        var lineIndex = -1;
        foreach (var line in lines)
        {
            lineIndex++;
            // Measure actual draw width accounting for letter-spacing
            var lineWidth = line.Width;
            if (letterSpacing != 0f || wordSpacing != 0f)
                lineWidth = MeasureWithSpacing(line.Text, font, letterSpacing, wordSpacing);

            // text-align: justify — widen the inter-word gaps so every line except the last fills
            // the available width (CSS 2.1 §16.2). Last lines and single-word lines stay unmodified,
            // and so does pre/pre-wrap text: §16.2 forbids any justification from altering it.
            var lineWordSpacing = wordSpacing;
            var justified = false;
            if (textAlign == TextAlign.Justify && whiteSpace is not (WhiteSpace.Pre or WhiteSpace.PreWrap)
                && lineIndex != lastLineIndex && lineWidth < LineBandWidth(bands, lineIndex, maxWidth))
            {
                var gaps = line.Text.Count(c => c == ' ');
                if (gaps > 0)
                {
                    lineWordSpacing = wordSpacing + (LineBandWidth(bands, lineIndex, maxWidth) - lineWidth) / gaps;
                    justified = true;
                }
            }

            // Compute x offset for text-align, within this line's own band when floats gave it
            // one. A line-positioned fragment skips both this shift and the band re-anchor: its
            // rect IS the final line position (see the lineAligned parameter).
            var lineX0 = x;
            var lineMaxW = maxWidth;
            if (!lineAligned && bands is { Count: > 0 })
            {
                var band = bands[Math.Min(lineIndex, bands.Count - 1)];
                lineX0 = x + band.X;
                lineMaxW = band.Width;
            }
            var drawX = lineAligned ? lineX0 : textAlign switch
            {
                TextAlign.Center => lineX0 + (lineMaxW - lineWidth) / 2f,
                TextAlign.Right => lineX0 + lineMaxW - lineWidth,
                _ => lineX0,
            };

            // A justified line visually spans the full width (used for underline/strike extents).
            var drawnWidth = justified ? LineBandWidth(bands, lineIndex, maxWidth) : lineWidth;

            // Apply text-indent on first line. A line-positioned slice (lineAligned) already
            // sits where layout indented its line (§16.1): shifting it again indented the first
            // line of every inline text node, not just the block's first line.
            if (isFirstLine && textIndent != 0f && !lineAligned && node.GetDirection() != "rtl")
            {
                drawX += textIndent;
            }

            // SkiaSharp draws at baseline; add ascent to convert top→baseline. The baseline
            // snaps to a whole DEVICE pixel, as browsers place glyph runs: a line box at a
            // fractional y (a 17.875px paragraph above it) otherwise blended every glyph edge,
            // so Ahem squares never matched a reference built from pixel-aligned images
            // (c5502-imrgn-r-003's "HI").
            var baseline = SnapToDevicePixelY(canvas, lineY + line.Ascent);

            // Determine paint/font for this line (::first-line override)
            var linePaint = paint;
            var lineFont = font;
            SKPaint? tempPaint = null;
            SKFont? tempFont = null;

            if ((isFirstLine || firstLineAllLines) && firstLineStyles != null)
            {
                tempPaint = new SKPaint { Color = paint.Color, IsAntialias = true };
                if (firstLineStyles.TryGetValue("color", out var flColor))
                    tempPaint.Color = Rendering.SvgRenderer.ParseColor(flColor);
                if (firstLineStyles.TryGetValue("font-weight", out var flWeight) && flWeight == "bold")
                {
                    var tf = font.Typeface;
                    var boldTf = SKTypeface.FromFamilyName(tf.FamilyName, (int)SKFontStyleWeight.Bold,
                        (int)tf.FontStyle.Width, tf.FontStyle.Slant) ?? tf;
                    tempFont = new SKFont(boldTf, font.Size);
                    lineFont = tempFont;
                }
                if (firstLineStyles.TryGetValue("font-size", out var flSize))
                {
                    if (flSize.EndsWith("em") && float.TryParse(flSize[..^2],
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var em))
                    {
                        tempFont?.Dispose();
                        tempFont = new SKFont(lineFont.Typeface, font.Size * em);
                        lineFont = tempFont;
                    }
                }
                linePaint = tempPaint;
            }

            // §15.6 'font-variant: small-caps': lowercase letters synthesize to small capitals.
            var smallCaps = IsSmallCaps(node);
            // §15.5 italic synthesis: the face resolved upright while the node requested
            // italic — skew the glyph runs (the oblique every browser falls back to).
            var synthItalic = SynthesizesItalic(node, lineFont);
            if (synthItalic)
            {
                canvas.Save();
                canvas.Translate(drawX, baseline);
                canvas.Skew(-0.25f, 0f);
            }
            var runX = synthItalic ? 0f : drawX;
            var runY = synthItalic ? 0f : baseline;
            // text-shadow drawn before (behind) the main text
            if (textShadow.HasValue)
            {
                var ts = textShadow.Value;
                var sigma = ts.Blur / 2f;
                using var sp = new SKPaint
                {
                    Color = ts.Color,
                    IsAntialias = true,
                    MaskFilter = sigma > 0 ? SKMaskFilter.CreateBlur(SKBlurStyle.Normal, sigma) : null,
                };
                if (smallCaps)
                    DrawTextSmallCaps(canvas, line.Text, runX + ts.OffsetX, runY + ts.OffsetY, lineFont, sp, letterSpacing, lineWordSpacing);
                else
                    DrawTextWithSpacing(canvas, line.Text, runX + ts.OffsetX, runY + ts.OffsetY, lineFont, sp, letterSpacing, lineWordSpacing);
            }

            // ::first-letter is not handled here: the Parser splits it into a real inline box
            // (#pseudo-first-letter) so it takes part in layout, and it paints as ordinary inline
            // content through this same routine.
            if (smallCaps)
                DrawTextSmallCaps(canvas, line.Text, runX, runY, lineFont, linePaint, letterSpacing, lineWordSpacing);
            else
                DrawTextWithSpacing(canvas, line.Text, runX, runY, lineFont, linePaint, letterSpacing, lineWordSpacing);
            if (synthItalic) canvas.Restore();
            // §18.1 'auto': the UA picks the cursor by context — a text cursor over text. The
            // fragment's region only informs the cursor lookup (clicks and hover skip it).
            if (autoCursor && !string.IsNullOrWhiteSpace(line.Text))
                _hitRegions.Add(new HitRegion(new SKRect(drawX, lineY, drawX + drawnWidth, lineY + line.Height),
                    CursorType.Text, NodeKey: node.NodeKey) { CursorOnly = true });

            if (underline)
            {
                var metrics = lineFont.Metrics;
                var uY = baseline + (metrics.UnderlinePosition ?? lineFont.Size * 0.1f);
                var uThick = Math.Max(metrics.UnderlineThickness ?? 1f, 1f);
                using var lp = new SKPaint { Color = linePaint.Color, StrokeWidth = uThick, IsAntialias = true };
                canvas.DrawLine(drawX, uY, drawX + drawnWidth, uY, lp);
            }

            if (overline)
            {
                // §16.3.1 'overline': a line above the text, at the top of the font's ascent.
                var oY = baseline + lineFont.Metrics.Ascent;
                var uThick = Math.Max(lineFont.Metrics.UnderlineThickness ?? 1f, 1f);
                using var lp = new SKPaint { Color = linePaint.Color, StrokeWidth = uThick, IsAntialias = true };
                canvas.DrawLine(drawX, oY, drawX + drawnWidth, oY, lp);
            }

            if (lineThrough)
            {
                var strikY = baseline - lineFont.Size * 0.3f;
                var uThick = Math.Max(lineFont.Metrics.UnderlineThickness ?? 1f, 1f);
                using var lp = new SKPaint { Color = linePaint.Color, StrokeWidth = uThick, IsAntialias = true };
                canvas.DrawLine(drawX, strikY, drawX + drawnWidth, strikY, lp);
            }

            isFirstLine = false;
            tempPaint?.Dispose();
            tempFont?.Dispose();
            lineY += line.Height;
        }
    }

    /// <summary>A hit region showing <paramref name="cursorNode"/>'s cursor (CSS 2.1 §18.1),
    /// including the url() image a custom cursor loaded.</summary>
    private static HitRegion CursorRegion(SKRect bounds, LayoutNode cursorNode, string? href = null, Guid nodeKey = default)
    {
        var (type, image) = cursorNode.GetCursorSpec();
        return new HitRegion(bounds, type, href, nodeKey) { CursorImage = image };
    }

    /// <summary>Draws text with custom letter-spacing and word-spacing.</summary>
    /// <summary>Rounds a y coordinate to the nearest whole device pixel under the canvas's
    /// current transform (translation and scale only; any other transform leaves it as is).</summary>
    private static float SnapToDevicePixelY(SKCanvas canvas, float y)
    {
        var m = canvas.TotalMatrix;
        if (m.SkewX != 0 || m.SkewY != 0 || m.ScaleY == 0 || m.Persp0 != 0 || m.Persp1 != 0) return y;
        var device = y * m.ScaleY + m.TransY;
        return (MathF.Round(device) - m.TransY) / m.ScaleY;
    }

    private static void DrawTextWithSpacing(SKCanvas canvas, string text, float x, float y,
                                            SKFont font, SKPaint paint, float letterSpacing, float wordSpacing)
    {
        if (letterSpacing == 0f && wordSpacing == 0f)
        {
            canvas.DrawText(text, x, y, SKTextAlign.Left, font, paint);
            Rendering.PaintLog.Record(text, x, y, font, SKTextAlign.Left);
            return;
        }

        Rendering.PaintLog.Record(text, x, y, font, SKTextAlign.Left);
        var curX = x;
        foreach (var ch in text)
        {
            var s = ch.ToString();
            canvas.DrawText(s, curX, y, SKTextAlign.Left, font, paint);
            curX += font.MeasureText(s) + letterSpacing;
            if (ch is ' ' or '\u00A0') curX += wordSpacing;
        }
    }

    /// <summary>True when <c>font-variant: small-caps</c> (§15.6) — lowercase letters render
    /// as small capitals.</summary>
    public static bool IsSmallCaps(LayoutNode node)
    {
        var raw = node.TryResolveStyle("font-variant", out var ov) ? ov : node.Style.GetPropertyValueSafe("font-variant");
        return raw.Trim().Equals("small-caps", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when the node requests italic/oblique but the resolved face is upright —
    /// Skia matched the closest face without synthesizing, and §15.5's oblique synthesis is
    /// ours to paint: a skew around the glyph runs (advances unchanged, outline slanted).</summary>
    private static bool SynthesizesItalic(LayoutNode node, SKFont font)
    {
        if (!node.GetFontItalic()) return false;
        return font.Typeface.FontStyle.Slant == SKFontStyleSlant.Upright;
    }

    /// <summary>Synthesized small-caps drawing (§15.6): capitals and non-letters draw at the
    /// full size, LOWERCASE letters draw UPPERCASED at 0.75em on the same baseline — the
    /// synthesis every engine falls back to when the face carries no real small-caps glyphs.
    /// The reference graph of the font-variant-applies-to family turns on exactly this
    /// visible difference (its != edge requires small-caps text to differ from plain).</summary>
    private static void DrawTextSmallCaps(SKCanvas canvas, string text, float x, float y,
                                          SKFont font, SKPaint paint, float letterSpacing, float wordSpacing)
    {
        using var smallFont = new SKFont(font.Typeface, font.Size * 0.75f);
        var curX = x;
        var runStart = -1;
        var runLower = false;
        void FlushRun(int endExclusive)
        {
            if (runStart < 0) return;
            var run = text[runStart..endExclusive];
            var runFont = runLower ? smallFont : font;
            if (runLower) run = run.ToUpperInvariant();
            if (letterSpacing == 0f && wordSpacing == 0f)
            {
                canvas.DrawText(run, curX, y, SKTextAlign.Left, runFont, paint);
                curX += runFont.MeasureText(run);
            }
            else
            {
                foreach (var ch in run)
                {
                    var s = ch.ToString();
                    canvas.DrawText(s, curX, y, SKTextAlign.Left, runFont, paint);
                    curX += runFont.MeasureText(s) + letterSpacing;
                    if (ch is ' ' or '\u00A0') curX += wordSpacing;
                }
            }
            runStart = -1;
        }
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            var lower = char.IsLower(ch);
            if (runStart < 0) { runStart = i; runLower = lower; }
            else if (lower != runLower) { FlushRun(i); runStart = i; runLower = lower; }
        }
        FlushRun(text.Length);
    }

    /// <summary>Measures text width including letter-spacing and word-spacing. Shared with layout
    /// (<see cref="TextMeasure.MeasureWithSpacing"/>) so the two agree on how wide the text is.</summary>
    /// <summary>The width a laid-out line slice paints its text in: never narrower than the
    /// slice's own text. Layout trims the end-of-line collapsible space off a fragment's rect
    /// (§16.6.1: the inline box does not reach over it), but the slice is already one line, so
    /// re-wrapping its spaced text into the trimmed rect would split it and strip the leading
    /// space of " d " (table-anonymous-objects-177). Only a slice whose text fits the rect once
    /// its trailing spaces are dropped is widened.</summary>
    private static float SliceWidth(LayoutNode node, string text, SKFont font, float rectWidth)
    {
        if (text.Length == 0 || !char.IsWhiteSpace(text[^1])) return rectWidth;
        var shown = text; // already transformed (RenderedText or a slice of it)
        var (ls, ws) = (node.GetLetterSpacing(font.Size), node.GetWordSpacing(font.Size));
        // Only the trimmed space is given back: text wider than its rect without it still wraps.
        if (MeasureWithSpacing(shown.TrimEnd(), font, ls, ws) > rectWidth + 0.5f) return rectWidth;
        return Math.Max(rectWidth, MeasureWithSpacing(shown, font, ls, ws));
    }

    private static float MeasureWithSpacing(string text, SKFont font, float letterSpacing, float wordSpacing)
        => TextMeasure.MeasureWithSpacing(text, font, letterSpacing, wordSpacing);
}
