using SkiaSharp;

namespace Lite.Models;

public enum CursorType { Default, Pointer, Text }

public enum InputAction { None, TextInput, Checkbox, Button, Radio, NumberUp, NumberDown, Range, SelectDropdown, FileOpen }

public record HitRegion(SKRect Bounds, CursorType Cursor, string? Href = null, Guid NodeKey = default, InputAction InputAction = InputAction.None);
