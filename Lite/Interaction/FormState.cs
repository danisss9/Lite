namespace Lite.Interaction;

/// <summary>A file chosen for an &lt;input type=file&gt;. <see cref="TextContent"/> is the file's
/// content decoded as text (best-effort; binary content is not modeled).</summary>
internal sealed record SelectedFile(string Name, long Size, string Type, string TextContent, double LastModified);

internal static class FormState
{
    public static Dictionary<Guid, string> TextInputValues { get; } = [];
    public static HashSet<Guid> CheckedBoxes { get; } = [];
    public static Guid? FocusedInput { get; set; }

    /// <summary>Files selected for each &lt;input type=file&gt; (by NodeKey).</summary>
    public static Dictionary<Guid, List<SelectedFile>> Files { get; } = [];

    /// <summary>The files chosen for a file input (empty list if none).</summary>
    public static IReadOnlyList<SelectedFile> GetFiles(Guid key) =>
        Files.TryGetValue(key, out var f) ? f : (IReadOnlyList<SelectedFile>)Array.Empty<SelectedFile>();

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
