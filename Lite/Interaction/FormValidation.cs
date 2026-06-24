using System.Text.RegularExpressions;
using Lite.Models;

namespace Lite.Interaction;

/// <summary>Constraint validation (HTML5) for form controls.</summary>
public class ValidityState
{
    public bool valueMissing { get; internal set; }
    public bool typeMismatch { get; internal set; }
    public bool patternMismatch { get; internal set; }
    public bool customError { get; internal set; }
    public bool valid => !(valueMissing || typeMismatch || patternMismatch || customError);
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

    /// <summary>The author-set custom validity message (setCustomValidity); empty/absent = none.</summary>
    internal static string CustomMessage(LayoutNode node) =>
        node.Attributes.GetValueOrDefault("_customValidity", string.Empty);

    /// <summary>The control's validation message: the custom message if set, else a built-in
    /// message for the first failing constraint, else empty when valid.</summary>
    internal static string GetValidationMessage(LayoutNode node)
    {
        var custom = CustomMessage(node);
        if (!string.IsNullOrEmpty(custom)) return custom;
        var v = GetValidity(node);
        if (v.valid) return string.Empty;
        if (v.valueMissing) return "Please fill out this field.";
        if (v.typeMismatch) return "Please enter a valid value.";
        if (v.patternMismatch) return "Please match the requested format.";
        return string.Empty;
    }

    internal static ValidityState GetValidity(LayoutNode node)
    {
        var state = new ValidityState { customError = !string.IsNullOrEmpty(CustomMessage(node)) };
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
