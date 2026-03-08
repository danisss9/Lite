namespace Lite.Interaction;

internal static class FormState
{
    public static Dictionary<Guid, string> TextInputValues { get; } = [];
    public static HashSet<Guid> CheckedBoxes { get; } = [];
    public static Guid? FocusedInput { get; set; }

    private static readonly HashSet<Guid> _initialized = [];

    public static string GetTextValue(Guid key, string? defaultValue)
    {
        if (_initialized.Add(key) && !TextInputValues.ContainsKey(key))
            TextInputValues[key] = defaultValue ?? string.Empty;
        return TextInputValues.GetValueOrDefault(key, string.Empty);
    }

    public static bool IsChecked(Guid key, bool defaultChecked)
    {
        if (_initialized.Add(key) && defaultChecked)
            CheckedBoxes.Add(key);
        return CheckedBoxes.Contains(key);
    }
}
