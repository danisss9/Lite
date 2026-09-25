using Lite.Models;

namespace Lite.Layout;

internal static class FormLayout
{
    public const float TextInputWidth  = 150f;
    public const float TextInputHeight = 20f;
    public const float CheckboxSize    = 13f;
    public const float RadioSize       = 13f;
    public const float ButtonPaddingX  = 8f;
    public const float ButtonPaddingY  = 4f;
    public const float ElementGap      = 4f;
    public const float TextareaWidth   = 300f;
    public const float TextareaHeight  = 80f;
    public const float SelectWidth     = 150f;
    public const float SelectHeight    = 22f;
    public const float RangeWidth      = 150f;
    public const float RangeHeight     = 20f;
    public const float ProgressWidth   = 160f;
    public const float ProgressHeight  = 16f;
    public const float MeterWidth      = 80f;
    public const float MeterHeight     = 16f;

    /// <summary>Intrinsic content width for a native form control with auto CSS width.
    /// Table and shrink-to-fit sizing need the same value that inline layout paints.</summary>
    internal static float? IntrinsicWidth(LayoutNode node)
    {
        if (node.TagName == "INPUT")
        {
            var type = node.Attributes.GetValueOrDefault("type", "text").ToLowerInvariant();
            if (type == "hidden") return 0f;
            if (type == "checkbox") return CheckboxSize;
            if (type == "radio") return RadioSize;
            if (type == "range") return RangeWidth;
            if (type is "submit" or "reset" or "button" or "image")
            {
                var label = node.Attributes.GetValueOrDefault("value", type == "submit" ? "Submit" : "Reset");
                return ButtonWidth(node, label);
            }
            if (int.TryParse(node.Attributes.GetValueOrDefault("size"), out var columns) && columns > 0)
            {
                using var font = TextMeasure.CreateFont(node);
                return Math.Clamp(columns, 1, 1000) * font.MeasureText("0") + 4f;
            }
            return TextInputWidth;
        }
        if (node.TagName == "BUTTON") return ButtonWidth(node, node.DisplayText);
        return node.TagName switch
        {
            "SELECT" => SelectWidth,
            "TEXTAREA" => TextareaWidth,
            "PROGRESS" => ProgressWidth,
            "METER" => MeterWidth,
            _ => null
        };
    }

    private static float ButtonWidth(LayoutNode node, string? label)
    {
        using var font = TextMeasure.CreateFont(node);
        return font.MeasureText(string.IsNullOrEmpty(label) ? "Button" : label) + ButtonPaddingX * 2;
    }
}
