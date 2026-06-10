using Lite.Models;

namespace Lite.Interaction;

/// <summary>
/// Serializes a &lt;form&gt;'s successful controls (HTML5 §form submission) and builds the
/// target URL. POST bodies are not navigated (no body channel in the loader) — the action
/// URL is returned and GET query strings are appended.
/// </summary>
internal static class FormSubmitter
{
    /// <summary>Builds an application/x-www-form-urlencoded string from a form's controls.</summary>
    internal static string BuildQuery(LayoutNode form)
    {
        var pairs = new List<string>();
        foreach (var ctrl in Descendants(form))
        {
            if (ctrl.TagName is not ("INPUT" or "SELECT" or "TEXTAREA")) continue;
            if (ctrl.Attributes.ContainsKey("disabled")) continue;
            if (!ctrl.Attributes.TryGetValue("name", out var name) || string.IsNullOrEmpty(name)) continue;

            var type = ctrl.Attributes.GetValueOrDefault("type", "text").ToLowerInvariant();

            if (ctrl.TagName == "INPUT" && type is "checkbox" or "radio")
            {
                if (!FormState.IsChecked(ctrl.NodeKey, ctrl.Attributes.ContainsKey("checked"))) continue;
                pairs.Add(Encode(name) + "=" + Encode(ctrl.Attributes.GetValueOrDefault("value", "on")));
            }
            else if (type is "submit" or "reset" or "button" or "image" or "file")
            {
                continue; // not successful controls here (submit handled by the activating button)
            }
            else
            {
                var value = FormState.GetTextValue(ctrl.NodeKey, ctrl.Attributes.GetValueOrDefault("value"));
                pairs.Add(Encode(name) + "=" + Encode(value));
            }
        }
        return string.Join("&", pairs);
    }

    /// <summary>Builds the URL to navigate to when the form is submitted.</summary>
    internal static string BuildActionUrl(LayoutNode form, string? baseUrl)
    {
        var action = form.Attributes.GetValueOrDefault("action", "");
        var method = form.Attributes.GetValueOrDefault("method", "get").ToLowerInvariant();
        var resolved = Resolve(action, baseUrl);
        var query = BuildQuery(form);

        if (method == "get" && query.Length > 0)
        {
            var sep = resolved.Contains('?') ? "&" : "?";
            return resolved + sep + query;
        }
        return resolved;
    }

    /// <summary>Resets a form's controls to their default values/checked state.</summary>
    internal static void Reset(LayoutNode form)
    {
        foreach (var ctrl in Descendants(form))
        {
            switch (ctrl.TagName)
            {
                case "INPUT":
                    var type = ctrl.Attributes.GetValueOrDefault("type", "text").ToLowerInvariant();
                    if (type is "checkbox" or "radio")
                    {
                        if (ctrl.Attributes.ContainsKey("checked")) FormState.CheckedBoxes.Add(ctrl.NodeKey);
                        else FormState.CheckedBoxes.Remove(ctrl.NodeKey);
                    }
                    else
                    {
                        FormState.TextInputValues[ctrl.NodeKey] = ctrl.Attributes.GetValueOrDefault("value", "");
                    }
                    break;
                case "TEXTAREA":
                    FormState.TextInputValues[ctrl.NodeKey] = ctrl.Attributes.GetValueOrDefault("value", "");
                    break;
                case "SELECT":
                    FormState.TextInputValues[ctrl.NodeKey] = ctrl.Attributes.GetValueOrDefault("value", "");
                    break;
            }
        }
    }

    private static string Encode(string s) => Uri.EscapeDataString(s ?? "");

    private static string Resolve(string action, string? baseUrl)
    {
        if (string.IsNullOrEmpty(action)) return baseUrl ?? "";
        if (Uri.TryCreate(action, UriKind.Absolute, out _)) return action;
        if (baseUrl is not null && Uri.TryCreate(new Uri(baseUrl), action, out var resolved))
            return resolved.ToString();
        return action;
    }

    private static IEnumerable<LayoutNode> Descendants(LayoutNode root)
    {
        // Push children in reverse so they pop in document order (form submission order matters).
        var stack = new Stack<LayoutNode>();
        for (int i = root.Children.Count - 1; i >= 0; i--) stack.Push(root.Children[i]);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            yield return n;
            for (int i = n.Children.Count - 1; i >= 0; i--) stack.Push(n.Children[i]);
        }
    }
}
