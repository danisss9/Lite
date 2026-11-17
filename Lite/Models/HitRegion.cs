using SkiaSharp;

namespace Lite.Models;

/// <summary>The CSS 2.1 §18.1 cursor keywords, plus <see cref="Auto"/> (resolved by context
/// when the pointer position is known: a text cursor over text, otherwise the default arrow)
/// and <see cref="Custom"/> (a loaded url() cursor image, see <see cref="HitRegion.CursorImage"/>).</summary>
public enum CursorType
{
    Default, Pointer, Text, Auto, Custom, Crosshair, Move,
    EResize, NeResize, NwResize, NResize, SeResize, SwResize, SResize, WResize,
    Wait, Help, Progress,
}

public enum InputAction { None, TextInput, Checkbox, Button, Radio, NumberUp, NumberDown, Range, SelectDropdown, FileOpen, MediaToggle }

public record HitRegion(SKRect Bounds, CursorType Cursor, string? Href = null, Guid NodeKey = default, InputAction InputAction = InputAction.None)
{
    internal Page? OwnerPage { get; init; }
    internal float OwnerOffsetX { get; init; }
    internal float OwnerOffsetY { get; init; }

    /// <summary>The absolute URL of the url() cursor image a <see cref="CursorType.Custom"/>
    /// region shows.</summary>
    public string? CursorImage { get; init; }

    /// <summary>A text fragment painted under 'cursor: auto'. It only tells the cursor lookup
    /// that the pointer is over text; clicks, hover and focus skip it.</summary>
    public bool CursorOnly { get; init; }

    /// <summary>The cursor shown at a point: the topmost region containing it decides, an
    /// 'auto' box resolves to the text cursor where a text fragment lies on top and to the
    /// default arrow elsewhere (CSS 2.1 §18.1 'auto').</summary>
    public static (CursorType Type, string? Image) CursorAt(IReadOnlyList<HitRegion> regions, float x, float y)
    {
        for (var i = regions.Count - 1; i >= 0; i--)
        {
            var region = regions[i];
            if (!region.Bounds.Contains(x, y)) continue;
            return region.Cursor switch
            {
                CursorType.Auto => (CursorType.Default, null),
                CursorType.Custom => (CursorType.Custom, region.CursorImage),
                var type => (type, null),
            };
        }
        return (CursorType.Default, null);
    }
}
