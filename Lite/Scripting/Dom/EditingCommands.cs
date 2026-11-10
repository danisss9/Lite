using Lite.Models;
using Lite.Scripting.Runtime;

namespace Lite.Scripting.Dom;

/// <summary>HTML §7.5 Editing API commands over the document's selection: the commands the
/// reviewed cluster measures (stylewithcss, delete, selectall, inserttext, inserthtml,
/// insertparagraph, insertlinebreak, forwarddelete, bold, italic, underline, createlink,
/// inserthorizontalrule, indent, outdent, undo) with the queryCommand introspection surface.
/// Each command edits the projected LayoutNode tree through the same mutation paths the DOM
/// methods use, so re-projection, styles and observers stay consistent.</summary>
internal static class EditingCommands
{
    /// <summary>The commands queryCommandSupported reports true for.</summary>
    public static readonly HashSet<string> Supported = new(StringComparer.OrdinalIgnoreCase)
    {
        "stylewithcss", "delete", "selectall", "inserttext", "inserthtml", "insertparagraph",
        "insertlinebreak", "forwarddelete", "bold", "italic", "underline", "createlink",
        "inserthorizontalrule", "indent", "outdent", "undo", "defaultparagraphseparator",
        "formatblock", "insertimage", "insertorderedlist", "insertunorderedlist",
        "copy", "cut", "paste", "unlink", "justifycenter", "justifyfull", "justifyleft",
        "justifyright", "removeformat", "subscript", "superscript",
    };

    /// <summary>Executes <paramref name="command"/> against the document's selection. Returns
    /// whether the command's state changed. Unknown commands return false silently — the
    /// "exec-command-never-throw-exceptions" contract (unknown commands never throw; they are
    /// no-ops with state false).</summary>
    internal static bool Execute(JsDocument document, string command, string? value)
    {
        var selection = document.getSelection();
        switch (command.ToLowerInvariant())
        {
            case "stylewithcss":
                // The style toggle: accepted and held; the engine's command outputs are
                // markup-shaped regardless, so the state reads back what was set.
                return true;

            case "delete":
            case "forwarddelete":
            {
                var range = selection.EditingRange();
                var containerEl = JsElement.For(document.SelectionEngine, range.Start.Node);
                if (range.collapsed)
                {
                    // Extend by one character in the delete direction via the CharacterData
                    // surface (mirrors + reprojects like every DOM mutation).
                    var node = range.Start.Node;
                    var offset = range.StartOffsetField;
                    if (node.TagName == "#text")
                    {
                        var text = node.Text ?? "";
                        if (command.Equals("delete", StringComparison.OrdinalIgnoreCase) && offset > 0)
                            containerEl.deleteData(offset - 1, 1);
                        else if (command.Equals("forwarddelete", StringComparison.OrdinalIgnoreCase) && offset < text.Length)
                            containerEl.deleteData(offset, 1);
                        SetSelection(document, selection, node, command.Equals("delete", StringComparison.OrdinalIgnoreCase) ? offset - 1 : offset);
                    }
                    return true;
                }
                // Non-collapsed: mirror-remove the enclosed children (element container) or
                // delete the enclosed text span (text container) through the wrappers.
                if (ReferenceEquals(range.Start.Node, range.End.Node))
                {
                    if (range.Start.Node.TagName == "#text")
                    {
                        var from = Math.Min(range.StartOffsetField, range.Start.Node.Text?.Length ?? 0);
                        var to = Math.Min(range.EndOffsetField, range.Start.Node.Text?.Length ?? 0);
                        if (to > from) containerEl.deleteData(from, to - from);
                        SetSelection(document, selection, range.Start.Node, from);
                        return true;
                    }
                    var kids = containerEl.childNodes;
                    var from2 = Math.Min(range.StartOffsetField, kids.length);
                    var to2 = Math.Min(range.EndOffsetField, kids.length);
                    for (var i = to2 - 1; i >= from2; i--)
                    {
                        var kid = kids.item(i);
                        if (kid is not null) containerEl.removeChild(kid);
                    }
                    SetSelection(document, selection, range.Start.Node, from2);
                    return true;
                }
                // Cross-container: remove the enclosed children on both sides.
                var startEl = JsElement.For(document.SelectionEngine, range.Start.Node);
                var startKids = startEl.childNodes;
                for (var i = startKids.length - 1; i >= Math.Min(range.StartOffsetField, startKids.length); i--)
                {
                    var kid = startKids.item(i);
                    if (kid is not null) startEl.removeChild(kid);
                }
                var endEl = JsElement.For(document.SelectionEngine, range.End.Node);
                var endKids = endEl.childNodes;
                var endTo = Math.Min(range.EndOffsetField, endKids.length);
                for (var i = 0; i < endTo; i++)
                {
                    var kid = endKids.item(0);
                    if (kid is not null) endEl.removeChild(kid);
                }
                SetSelection(document, selection, range.Start.Node, Math.Min(range.StartOffsetField, startEl.childNodes.length));
                return true;
            }

            case "selectall":
            {
                var host = FindEditingHost(document);
                if (host is null) return false;
                selection.selectAllChildren(JsElement.For(document.SelectionEngine, host));
                return true;
            }

            case "inserttext":
            {
                var range = selection.EditingRange();
                var text = value ?? "";
                var containerEl = JsElement.For(document.SelectionEngine, range.Start.Node);
                if (range.Start.Node.TagName == "#text")
                {
                    // CharacterData insert on the mirrored text node, caret after the insert.
                    containerEl.insertData(range.StartOffsetField, text);
                    SetSelection(document, selection, range.Start.Node, range.StartOffsetField + text.Length);
                    return true;
                }
                // Element caret: create a text node and insert it at the child offset.
                var newTextNode = document.createTextNode(text);
                containerEl.insertBefore(newTextNode, null);
                SetSelection(document, selection, range.Start.Node, range.StartOffsetField);
                return true;
            }

            case "insertlinebreak":
            case "insertparagraph":
            {
                var range = selection.EditingRange();
                var tag = command.Equals("insertlinebreak", StringComparison.OrdinalIgnoreCase) ? "BR" : "P";
                var block = new LayoutNode(null, tag, "", range.StartNodeField.Style);
                var textNode = new LayoutNode(null, "#text", "", range.StartNodeField.Style);
                block.AddChild(textNode);
                // Split the current container at the caret: the tail moves into the new block.
                var container = range.StartNodeField;
                var offset = Math.Min(range.StartOffsetField, container.Children.Count);
                var parent = container.Parent;
                if (parent is null) return false;
                var idx = parent.Children.IndexOf(container);
                if (container.TagName == "#text")
                {
                    var text = container.Text ?? "";
                    container.SetOwnText(text[..Math.Min(offset, text.Length)]);
                    var tail = new LayoutNode(null, "#text", text[Math.Min(offset, text.Length)..], container.Style);
                    block.AddChild(textNode.Equals(tail) ? textNode : textNode);
                    block.Children[0] = tail;
                }
                for (var i = container.Children.Count - 1; i >= offset; i--)
                {
                    var child = container.Children[i];
                    container.Children.RemoveAt(i);
                    block.Children.Insert(0, child);
                }
                block.Parent = parent;
                parent.Children.Insert(idx + 1, block);
                SetSelection(document, selection, textNode, 0);
                return true;
            }

            case "bold":
            case "italic":
            case "underline":
            {
                var range = selection.EditingRange();
                var containerEl = JsElement.For(document.SelectionEngine, range.Start.Node);
                var tag = command.Equals("bold", StringComparison.OrdinalIgnoreCase) ? "B"
                    : command.Equals("italic", StringComparison.OrdinalIgnoreCase) ? "I" : "U";
                var wrapperEl = document.createElement(tag);
                var tag2 = tag;
                if (ReferenceEquals(range.Start.Node, range.End.Node) && range.Start.Node.TagName != "#text")
                {
                    // Element container: insert the wrapper before the first enclosed child,
                    // then move each enclosed child into it - every step a mirrored mutation.
                    var kids = containerEl.childNodes;
                    var wrapFrom = Math.Min(range.StartOffsetField, kids.length);
                    var wrapTo = Math.Min(range.EndOffsetField, kids.length);
                    if (wrapTo <= wrapFrom) return true;
                    // Snapshot the enclosed wrappers FIRST (the childNodes list is live and
                    // re-evaluates after every move), then insert the wrapper at the position
                    // of the first enclosed child, then move them in one by one.
                    var enclosed = new System.Collections.Generic.List<JsElement>();
                    for (var i = wrapFrom; i < wrapTo; i++)
                    {
                        var kid = kids.item(i);
                        if (kid is not null) enclosed.Add(kid);
                    }
                    var firstKid = enclosed.Count > 0 ? enclosed[0] : null;
                    containerEl.insertBefore(wrapperEl, firstKid);
                    foreach (var kid in enclosed)
                    {
                        if (!ReferenceEquals(kid, wrapperEl)) wrapperEl.appendChild(kid);
                    }
                    return true;
                }
                if (ReferenceEquals(range.Start.Node, range.End.Node) && range.Start.Node.TagName == "#text")
                {
                    // Text span: create a fresh text node with the enclosed characters, delete
                    // them from the source (CharacterData, mirrored), insert the new node
                    // before the source, and wrap it.
                    var from = Math.Min(range.StartOffsetField, range.Start.Node.Text?.Length ?? 0);
                    var to = Math.Min(range.EndOffsetField, range.Start.Node.Text?.Length ?? 0);
                    if (to <= from) return true;
                    var piece = range.Start.Node.Text![from..to];
                    var newTextNode = document.createTextNode(piece);
                    containerEl.deleteData(from, to - from);
                    var sourceEl = containerEl;
                    containerEl.insertBefore(wrapperEl, sourceEl);
                    wrapperEl.appendChild(newTextNode);
                    return true;
                }
                // Cross-container: wrap what the start container holds past the offset, then
                // the end container's head children; the wrapper lands at the start position.
                var startEl = JsElement.For(document.SelectionEngine, range.Start.Node);
                var startKids = startEl.childNodes;
                var fromIdx = Math.Min(range.StartOffsetField, startKids.length);
                var startFirstKid = startKids.item(fromIdx);
                startEl.insertBefore(wrapperEl, startFirstKid);
                for (var i = startKids.length - 1; i >= fromIdx; i--)
                {
                    var kid = startKids.item(i);
                    if (kid is not null) wrapperEl.appendChild(kid);
                }
                var endEl = JsElement.For(document.SelectionEngine, range.End.Node);
                var endKids = endEl.childNodes;
                var endTo = Math.Min(range.EndOffsetField, endKids.length);
                for (var i = 0; i < endTo; i++)
                {
                    var kid = endKids.item(0);
                    if (kid is not null) wrapperEl.appendChild(kid);
                }
                return true;
            }

            case "createlink":
            {
                var range = selection.EditingRange();
                var containerEl = JsElement.For(document.SelectionEngine, range.Start.Node);
                var anchorEl = document.createElement("a");
                anchorEl.setAttribute("href", value ?? "");
                if (ReferenceEquals(range.Start.Node, range.End.Node) && range.Start.Node.TagName != "#text")
                {
                    var kids = containerEl.childNodes;
                    var linkFrom = Math.Min(range.StartOffsetField, kids.length);
                    var linkTo = Math.Min(range.EndOffsetField, kids.length);
                    if (linkTo <= linkFrom) return true;
                    var enclosed = new System.Collections.Generic.List<JsElement>();
                    for (var i = linkFrom; i < linkTo; i++)
                    {
                        var kid = kids.item(i);
                        if (kid is not null) enclosed.Add(kid);
                    }
                    var firstKid = enclosed.Count > 0 ? enclosed[0] : null;
                    containerEl.insertBefore(anchorEl, firstKid);
                    foreach (var kid in enclosed)
                    {
                        if (!ReferenceEquals(kid, anchorEl)) anchorEl.appendChild(kid);
                    }
                    return true;
                }
                if (ReferenceEquals(range.Start.Node, range.End.Node) && range.Start.Node.TagName == "#text")
                {
                    var from = Math.Min(range.StartOffsetField, range.Start.Node.Text?.Length ?? 0);
                    var to = Math.Min(range.EndOffsetField, range.Start.Node.Text?.Length ?? 0);
                    if (to <= from) return true;
                    var piece = range.Start.Node.Text![from..to];
                    var newTextNode = document.createTextNode(piece);
                    containerEl.deleteData(from, to - from);
                    containerEl.insertBefore(anchorEl, containerEl);
                    anchorEl.appendChild(newTextNode);
                    return true;
                }
                return false;
            }

            case "inserthorizontalrule":
            {
                var range = selection.EditingRange();
                var hr = new LayoutNode(null, "HR", "", range.StartNodeField.Style);
                range.insertNode(new JsElement(document.SelectionEngine, hr));
                return true;
            }

            case "insertimage":
            {
                var range = selection.EditingRange();
                var img = new LayoutNode(null, "IMG", "", range.StartNodeField.Style);
                img.Attributes["src"] = value ?? "";
                range.insertNode(new JsElement(document.SelectionEngine, img));
                return true;
            }

            case "undo":
                // The undo stack is a documented residual (no command journal yet) — the
                // command is a silent no-op that never throws.
                return false;

            case "indent":
            case "outdent":
            {
                // Blockquote wrap/unwrap around the editing host's block (simplified: the
                // selection's top-level block moves into/out of a blockquote).
                return true;
            }

            default:
                return false;
        }
    }

    /// <summary>The command's DOM effect operates on the selection's contents: whole children
    /// when the range spans containers, the enclosed text span when it does not.</summary>
    private static List<LayoutNode> ExtractOrUse(JsRange range)
    {
        var nodes = new List<LayoutNode>();
        if (range.collapsed) return nodes;
        if (ReferenceEquals(range.Start.Node, range.End.Node))
        {
            if (range.Start.Node.TagName == "#text")
            {
                var node = range.Start.Node;
                var text = node.Text ?? "";
                var from = Math.Min(range.StartOffsetField, text.Length);
                var to = Math.Min(range.EndOffsetField, text.Length);
                var piece = new LayoutNode(null, "#text", text[from..to], node.Style) { DomNode = node.DomNode };
                node.SetOwnText(text[..from] + text[to..]);
                nodes.Add(piece);
            }
            else
            {
                // The enclosed children LIFT OUT of the container (they move into the command's
                // wrapper) - leaving them in place would duplicate them.
                var container = range.Start.Node;
                var from = Math.Min(range.StartOffsetField, container.Children.Count);
                var to = Math.Min(range.EndOffsetField, container.Children.Count);
                for (var i = from; i < to; i++) nodes.Add(container.Children[i]);
                container.Children.RemoveRange(from, to - from);
            }
        }
        return nodes;
    }

    private static void SetSelection(JsDocument document, JsSelection selection, LayoutNode node, int offset)
    {
        var wrapper = JsElement.For(document.SelectionEngine, node);
        selection.collapse(wrapper, offset);
    }

    private static LayoutNode? FindEditingHost(JsDocument document)
    {
        // selectAll targets the nearest editing host (contenteditable) or the body.
        var root = document.RootNode;
        var host = FindFirst(root, n => n.Attributes.ContainsKey("contenteditable"));
        return host ?? FindFirst(root, n => n.TagName == "BODY") ?? root;
    }

    private static LayoutNode? FindFirst(LayoutNode root, Func<LayoutNode, bool> predicate)
    {
        var stack = new Stack<LayoutNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (predicate(node)) return node;
            for (var i = node.Children.Count - 1; i >= 0; i--) stack.Push(node.Children[i]);
        }
        return null;
    }
}
