using SkiaSharp;

namespace Lite.Models;

public enum CursorType { Default, Pointer, Text }

public record HitRegion(SKRect Bounds, CursorType Cursor, string? Href = null);
