namespace Lite.Interaction;

internal static class FormState
{
    public static Dictionary<Guid, string> TextInputValues { get; } = [];
    public static HashSet<Guid> CheckedBoxes { get; } = [];
    public static Guid? FocusedInput { get; set; }

    /// <summary>Maps radio NodeKey → group name for radio button group logic.</summary>
    public static Dictionary<Guid, string> RadioGroups { get; } = [];
    /// <summary>Maps radio group name → list of NodeKeys in that group.</summary>
    public static Dictionary<string, List<Guid>> RadioGroupMembers { get; } = [];

    /// <summary>Tracks the currently open select dropdown NodeKey, if any.</summary>
    public static Guid? OpenDropdown { get; set; }

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

    /// <summary>Registers a radio button in a named group.</summary>
    public static void RegisterRadio(Guid key, string groupName)
    {
        RadioGroups[key] = groupName;
        if (!RadioGroupMembers.TryGetValue(groupName, out var members))
        {
            members = [];
            RadioGroupMembers[groupName] = members;
        }
        if (!members.Contains(key)) members.Add(key);
    }

    /// <summary>Selects a radio button, deselecting others in the same group.</summary>
    public static void SelectRadio(Guid key)
    {
        if (!RadioGroups.TryGetValue(key, out var group)) return;
        if (!RadioGroupMembers.TryGetValue(group, out var members)) return;
        foreach (var member in members)
            CheckedBoxes.Remove(member);
        CheckedBoxes.Add(key);
    }
}
