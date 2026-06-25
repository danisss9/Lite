using System.Text;
using Lite.Models;

namespace Lite.Interaction;

/// <summary>A fully-resolved form submission: where to send it, the HTTP method, and the encoded
/// request body (null for GET, which carries its data in the URL query).</summary>
internal readonly record struct FormSubmission(string Url, string Method, string? Body, string? ContentType);

/// <summary>
/// Serializes a &lt;form&gt;'s successful controls (HTML5 §form submission) and builds the
/// target URL/body. GET data is appended to the URL; POST is encoded as
/// application/x-www-form-urlencoded or multipart/form-data per the form's enctype.
/// </summary>
internal static class FormSubmitter
{
    /// <summary>Resolves a form submission end-to-end: URL, method, and (for POST) an encoded body.</summary>
    internal static FormSubmission BuildSubmission(LayoutNode form, string? baseUrl)
    {
        var method = form.Attributes.GetValueOrDefault("method", "get").ToLowerInvariant();
        var resolved = Resolve(form.Attributes.GetValueOrDefault("action", ""), baseUrl);

        if (method != "post")
        {
            var query = BuildQuery(form);
            var url = query.Length > 0 ? resolved + (resolved.Contains('?') ? "&" : "?") + query : resolved;
            return new FormSubmission(url, "GET", null, null);
        }

        var enctype = form.Attributes.GetValueOrDefault("enctype", "application/x-www-form-urlencoded").ToLowerInvariant();
        if (enctype.StartsWith("multipart/form-data"))
        {
            var boundary = "----LiteFormBoundary" + Guid.NewGuid().ToString("N");
            return new FormSubmission(resolved, "POST", BuildMultipartBody(form, boundary),
                "multipart/form-data; boundary=" + boundary);
        }

        return new FormSubmission(resolved, "POST", BuildQuery(form), "application/x-www-form-urlencoded");
    }

    /// <summary>Encodes a form's successful controls as a multipart/form-data body (RFC 7578),
    /// including file parts (filename + content-type) for &lt;input type=file&gt;.</summary>
    internal static string BuildMultipartBody(LayoutNode form, string boundary)
    {
        var sb = new StringBuilder();
        foreach (var ctrl in Descendants(form))
        {
            if (ctrl.TagName is not ("INPUT" or "SELECT" or "TEXTAREA")) continue;
            if (ctrl.Attributes.ContainsKey("disabled")) continue;
            if (!ctrl.Attributes.TryGetValue("name", out var name) || string.IsNullOrEmpty(name)) continue;

            var type = ctrl.Attributes.GetValueOrDefault("type", "text").ToLowerInvariant();

            if (ctrl.TagName == "INPUT" && type is "checkbox" or "radio")
            {
                if (!FormState.IsChecked(ctrl.NodeKey, ctrl.Attributes.ContainsKey("checked"))) continue;
                AppendTextPart(sb, boundary, name, ctrl.Attributes.GetValueOrDefault("value", "on"));
            }
            else if (type == "file")
            {
                var files = FormState.GetFiles(ctrl.NodeKey);
                if (files.Count == 0)
                {
                    // An empty file control still contributes a part with an empty filename.
                    sb.Append("--").Append(boundary).Append("\r\n")
                      .Append("Content-Disposition: form-data; name=\"").Append(name).Append("\"; filename=\"\"\r\n")
                      .Append("Content-Type: application/octet-stream\r\n\r\n\r\n");
                    continue;
                }
                foreach (var file in files)
                {
                    sb.Append("--").Append(boundary).Append("\r\n")
                      .Append("Content-Disposition: form-data; name=\"").Append(name)
                      .Append("\"; filename=\"").Append(file.Name).Append("\"\r\n")
                      .Append("Content-Type: ").Append(string.IsNullOrEmpty(file.Type) ? "application/octet-stream" : file.Type).Append("\r\n\r\n")
                      .Append(file.TextContent).Append("\r\n");
                }
            }
            else if (type is "submit" or "reset" or "button" or "image")
            {
                continue;
            }
            else
            {
                var value = FormState.GetTextValue(ctrl.NodeKey, ctrl.Attributes.GetValueOrDefault("value"));
                AppendTextPart(sb, boundary, name, value);
            }
        }
        sb.Append("--").Append(boundary).Append("--\r\n");
        return sb.ToString();
    }

    private static void AppendTextPart(StringBuilder sb, string boundary, string name, string value) =>
        sb.Append("--").Append(boundary).Append("\r\n")
          .Append("Content-Disposition: form-data; name=\"").Append(name).Append("\"\r\n\r\n")
          .Append(value).Append("\r\n");

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
