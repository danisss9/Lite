using System.Text.RegularExpressions;
using Lite.Models;

namespace Lite.Interaction;

/// <summary>Constraint validation (HTML5) for form controls.</summary>
public class ValidityState
{
    public bool valueMissing { get; internal set; }
    public bool typeMismatch { get; internal set; }
    public bool patternMismatch { get; internal set; }
    public bool valid => !(valueMissing || typeMismatch || patternMismatch);
}

internal static class FormValidation
{
    // Pragmatic email check (not the full WHATWG grammar, but close enough for validation).
    private static readonly Regex EmailRegex =
        new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

    /// <summary>Whether the element is a candidate for constraint validation (willValidate).</summary>
    internal static bool IsCandidate(LayoutNode node)
    {
        if (node.TagName is not ("INPUT" or "SELECT" or "TEXTAREA")) return false;
        if (node.Attributes.ContainsKey("disabled")) return false;
        if (node.Attributes.ContainsKey("readonly")) return false;
        var type = node.Attributes.GetValueOrDefault("type", "text").ToLowerInvariant();
        return type is not ("hidden" or "submit" or "reset" or "button");
    }

    internal static ValidityState GetValidity(LayoutNode node)
    {
        var state = new ValidityState();
        var type = node.Attributes.GetValueOrDefault("type", "text").ToLowerInvariant();
        bool required = node.Attributes.ContainsKey("required");

        if (type is "checkbox" or "radio")
        {
            state.valueMissing = required && !FormState.IsChecked(node.NodeKey, node.Attributes.ContainsKey("checked"));
            return state;
        }

        var value = FormState.GetTextValue(node.NodeKey, node.Attributes.GetValueOrDefault("value"));
        state.valueMissing = required && string.IsNullOrEmpty(value);

        if (!string.IsNullOrEmpty(value))
        {
            if (type == "email") state.typeMismatch = !EmailRegex.IsMatch(value);
            else if (type == "url") state.typeMismatch = !Uri.TryCreate(value, UriKind.Absolute, out _);

            if (node.Attributes.TryGetValue("pattern", out var pattern) && !string.IsNullOrEmpty(pattern))
            {
                try { state.patternMismatch = !Regex.IsMatch(value, "^(?:" + pattern + ")$"); }
                catch { /* invalid author pattern — treated as no constraint */ }
            }
        }
        return state;
    }
}
