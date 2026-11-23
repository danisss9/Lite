using AngleSharp.Io;
using AngleSharp;
using AngleSharp.Css;
using AngleSharp.Dom;
using AngleSharp.Css.Dom;
using AngleSharp.Css.Values;
using AngleSharp.Scripting;
using AngleSharp.Xml;
using Lite.Animation;
using Lite.Extensions;
using Lite.Layout;
using Lite.Models;
using Lite.Network;
using Lite.Scripting;
using Lite.Scripting.Dom;
using System.Text;
using System.Text.RegularExpressions;

namespace Lite;

internal static partial class Parser
{
    private static readonly Regex StyleBlockRegex =
        new("<style\\b[^>]*>.*?</style>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex ImportantBang = new("!\\s+important", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    /// <summary>The dynamic pseudo-classes Lite intercepts (hover/focus/active/link/visited),
    /// matched case-insensitively per §4.1.3.</summary>
    private static readonly Regex DynamicPseudoRegex =
        new(":(hover|focus|active|link|visited|lang)\\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    /// <summary>The pseudo-elements Lite lifts out of the cascade, matched case-insensitively
    /// per §4.1.3.</summary>
    private static readonly Regex PseudoElementRegex =
        new("::?(before|after|first-letter|first-line)\\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    /// <summary>The :lang() pseudo-class (any argument), matched case-insensitively — both the
    /// name and the matched document language are ASCII case-insensitive (§5.11.4).</summary>
    /// <summary>:link, which Lite decides itself before a dynamic rule is kept (every link is
    /// unvisited): stripped from the selector the live state re-matches.</summary>
    private static readonly Regex DynamicStripRegex =
        new(":link\\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    /// <summary>The pseudo-classes whose match follows the live interaction state.</summary>
    private static readonly Regex DynamicStateRegex =
        new(":(hover|focus|active)\\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LangPseudoRegex =
        new(":lang\\s*\\(\\s*[^)]*\\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex XmlDeclarationRegex =
        new(@"^(<\?xml\b[^>]*?)encoding\s*=\s*([""'])([^""']*)\2", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Rewrites the encoding pseudo-attribute of the XML declaration to
    /// <paramref name="appliedCharset"/> — the XML parser's re-decode of the
    /// already-transport-decoded string honors the declaration over its UTF-8 bytes, so an
    /// unaligned label double-decodes every non-ASCII attribute.</summary>
    internal static string AlignXmlDeclarationCharset(string html, string appliedCharset)
    {
        return XmlDeclarationRegex.Replace(html, m =>
            $"{m.Groups[1].Value}encoding={m.Groups[2].Value}{appliedCharset}{m.Groups[2].Value}");
    }

    /// <summary>AngleSharp's declaration parser drops "! important" (bang and keyword separated
    /// by whitespace) outright — a valid CSS 2.1 token sequence the official suite uses heavily —
    /// so the whole declaration silently vanishes and every later rule wins. Normalizing the bang
    /// inside style blocks before the CSSOM is built cannot change any stylesheet's meaning: the
    /// two spellings are the same token sequence in CSS.</summary>
    internal static string NormalizeImportantBangs(string html) =>
        StyleBlockRegex.Replace(html, static match =>
            ImportantBang.Replace(match.Value, "!important", int.MaxValue, match.Value.IndexOf('>') + 1));

    /// <summary>Applies the host's user-agent style sheet controls at parse time: disabling
    /// author influence entirely (a normative conformance point) and selecting the preferred
    /// named set among alternate style sheets. Unselected alternate sheets are skipped from
    /// fetching by the link loop; here every author sheet that is not part of the selected
    /// preference is disabled in the CSSOM, which the cascade and rule collection honor.</summary>
    private static void ApplyUserAgentSheetControls(IDocument document)
    {
        var session = Current.Session;
        if (session is null) return;
        var selectedSet = session.SelectedStyleSheetSet;
        foreach (var sheet in document.StyleSheets)
        {
            if (sheet.OwnerNode is not IElement owner) continue;
            if (owner == Current.UaStyleElement || Current.UserStyleElements.Contains(owner)) continue;
            var title = owner.GetAttribute("title");
            if (selectedSet is { Length: > 0 } && !string.IsNullOrEmpty(title) &&
                !title.Equals(selectedSet, StringComparison.OrdinalIgnoreCase))
            {
                SetStyleSheetSuspended(Current, owner, true);
                continue;
            }
            if (!session.AuthorStylesEnabled) SetStyleSheetSuspended(Current, owner, true);
        }
    }

    /// <summary>Keeps only the !important declarations of a style sheet text (the user sheet's
    /// second tier, which must outrank author !important per CSS 2.1 §6.4.1). Selector blocks
    /// whose declarations all disappear are dropped.</summary>
    internal static string ExtractImportantDeclarations(string css)
    {
        var sb = new StringBuilder();
        var i = 0;
        while (i < css.Length)
        {
            var open = IndexOfSignificant(css, i, '{');
            if (open < 0) break;
            var close = MatchingBrace(css, open);
            if (close < 0) break;
            var closeTrimmed = close;
            while (closeTrimmed > open && char.IsWhiteSpace(css[closeTrimmed - 1])) closeTrimmed--;
            if (css.AsSpan(open + 1, closeTrimmed - open - 1).ToString().TrimEnd().EndsWith("!important", StringComparison.OrdinalIgnoreCase))
            {
                sb.Append(css, i, close - i + 1);
                sb.Append('\n');
            }
            i = close + 1;
        }
        return sb.ToString();
    }

    /// <summary>Writes the UA style sheet and the host's user style sheet into the PARSED
    /// document, before any style resolution, so the cascade has its origins. The injection is
    /// DOM-level (the UA sheet is head's first child, the user sheet's normal half follows it,
    /// the user sheet's !important half is appended after every author sheet) and deliberately
    /// not text-level: splicing the sheets into the fetched source re-anchors leading comments
    /// and whitespace into head on the html5lib tree dumps, and synthesizing &lt;head&gt; when
    /// the input lacks one flips the parser from before-head to after-head — landing every
    /// following element in body. Because the call site is the first step of
    /// ParseOpenedDocument, every sheet is still registered before CollectCssRules, so
    /// document order — UA &lt; user &lt; author, with user !important last (§6.4.1 puts user
    /// !important above author !important) — is exactly the cascade order. Marker attributes
    /// are removed after tracking.</summary>
    private static void InjectOriginSheetsDom(IDocument document)
    {
        var host = document.Head ?? document.DocumentElement;
        if (host is null) return;

        var ua = document.CreateElement("style");
        ua.SetAttribute("data-lite-origin", "ua");
        ua.TextContent = UserAgentStylesheet;
        host.InsertBefore(ua, host.FirstChild);

        if (Current.Session?.UserStyleSheet is { Length: > 0 } userCss)
        {
            var normalized = ImportantBang.Replace(userCss, "!important");
            var user = document.CreateElement("style");
            user.SetAttribute("data-lite-origin", "user");
            user.TextContent = normalized;
            host.InsertBefore(user, ua.NextSibling);

            // The user sheet's !important half must come AFTER every author sheet: §6.4.1 puts
            // user !important above author !important, and document order is what the cascade sees.
            var importantHalf = ExtractImportantDeclarations(normalized);
            if (importantHalf.Length > 0)
            {
                var marked = document.CreateElement("style");
                marked.SetAttribute("data-lite-origin", "user");
                marked.TextContent = importantHalf;
                host.AppendChild(marked);
            }
        }
    }

    /// <summary>The injected UA sheet's element and the user sheets' elements — document.styleSheets
    /// must not list them (the CSSOM exposes author sheets only), or scripts mutating cssRules[0]
    /// would target the wrong origin.</summary>
    internal static IElement? UaStyleSheetOwner => Current.UaStyleElement;
    internal static IReadOnlyList<IElement> UserStyleSheetOwners => Current.UserStyleElements;

    /// <summary>The style sheet owned by the given &lt;style&gt;/&lt;link&gt; element, or null.</summary>
    internal static IStyleSheet? SheetFor(IElement? owner)
    {
        if (owner is null) return null;
        var document = owner.Owner ?? Document;
        if (document is null) return null;
        foreach (var sheet in document.StyleSheets)
            if (sheet.OwnerNode == owner) return sheet;
        return null;
    }

    /// <summary>A &lt;style&gt; element's disabled state changed (HTMLStyleElement.disabled), or
    /// a host control toggled: rebuild the collected rule view and refresh the owning document's
    /// frozen snapshot, so the dynamic resolver sees the new rule set. The AngleSharp CSSOM
    /// honors Disabled in its own cascade.</summary>
    /// <summary>Suspends (or resumes) a style sheet's participation in the cascade: the
    /// element's text is parked and replaced by an empty sheet, which both the AngleSharp
    /// cascade and its per-element computed cache observe, then the rule view is refreshed.</summary>
    internal static void SetStyleSheetSuspended(ParseState state, IElement styleElement, bool suspended)
    {
        if (suspended)
        {
            if (!state.SuspendedSheets.Add(styleElement)) return;
            state.SuspendedSheetTexts[styleElement] = styleElement.TextContent;
            state.SuspendedRuleSnapshots[styleElement] = SnapshotSheetRules(styleElement);
            styleElement.TextContent = "";
        }
        else
        {
            if (!state.SuspendedSheets.Remove(styleElement)) return;
            if (state.SuspendedSheetTexts.TryGetValue(styleElement, out var text))
                styleElement.TextContent = text;
            state.SuspendedSheetTexts.Remove(styleElement);
            state.SuspendedRuleSnapshots.Remove(styleElement);
        }
        OnStyleSheetToggled(styleElement);
    }

    /// <summary>Captures a suspended sheet's style rules (selector, properties, importance)
    /// so the recascade can retract exactly the declarations it had contributed.</summary>
    private static List<CssRule> SnapshotSheetRules(IElement styleElement)
    {
        var snapshot = new List<CssRule>();
        if (SheetFor(styleElement) is not ICssStyleSheet sheet) return snapshot;
        CollectSnapshot(sheet.Rules, snapshot);
        return snapshot;
    }

    private static void CollectSnapshot(ICssRuleList rules, List<CssRule> snapshot)
    {
        foreach (var rule in rules)
        {
            if (rule is ICssMediaRule media) { CollectSnapshot(media.Rules, snapshot); continue; }
            if (rule is not ICssStyleRule style || string.IsNullOrEmpty(style.SelectorText)) continue;
            var (props, important) = ParseDeclarations(style.Style.CssText);
            if (props.Count == 0) continue;
            foreach (var sel in SplitSelectorList(style.SelectorText))
            {
                var selector = sel.Trim();
                if (selector.Length == 0) continue;
                snapshot.Add(new CssRule(selector, ComputeSpecificity(selector), 0, props, important, RuleOrigin.Author));
            }
        }
    }

    internal static bool IsStyleSheetSuspended(ParseState state, IElement styleElement) =>
        state.SuspendedSheets.Contains(styleElement);

    internal static void OnStyleSheetToggled(IElement styleElement)
    {
        var document = styleElement.Owner ?? Document;
        if (document is null) return;
        CollectCssRules(document);
        if (Current.Document == document)
            Current.Session?.AttachedDocumentState?.RefreshStyleRules([.. CssRules]);
    }

    /// <summary>Core parse pipeline shared by the top-level load and child (iframe) loads. The
    /// caller must have already set the parse statics (base URL, viewport, cleared script lists)
    /// and opened <paramref name="document"/>. Inlines stylesheets, traverses to a LayoutNode tree,
    /// creates the JS engine, runs scripts, and returns the resulting <see cref="Page"/>.</summary>
    /// <summary>Tells AngleSharp's cascade which origin a UA or user style element's sheet has
    /// (§6.4.1): without it the UA sheet's rules competed on specificity with author rules, so
    /// 'body { margin: 8px }' beat an author '* { margin: 1em }' (background-root-009).</summary>
    private static void MarkCascadeOrigin(IElement styleElement)
    {
        if (ReferenceEquals(styleElement, Current.UaStyleElement))
            AngleSharp.Css.Dom.CascadeOrigin.Mark(styleElement, AngleSharp.Css.Dom.CascadeOrigin.UserAgent);
        else if (Current.UserStyleElements.Contains(styleElement))
            AngleSharp.Css.Dom.CascadeOrigin.Mark(styleElement, AngleSharp.Css.Dom.CascadeOrigin.User);
    }

    /// <summary>
    /// Fetches a stylesheet and decodes its bytes per CSS 2.1 §4.4, in the spec's priority order:
    /// a byte-order mark, then an <c>@charset</c> rule at the very start of the sheet, then the
    /// HTTP <c>Content-Type</c> charset, then UTF-8. Decoding everything as UTF-8 (what
    /// GetStringAsync does without a charset) both mangles a sheet in another encoding and leaves
    /// a U+FEFF at the front of one that carries a BOM — which silently breaks its first selector.
    /// </summary>
    private static string FetchCssText(string url, string? linkCharset, string? referrerCharset,
        out string usedCharset)
    {
        // RFC 2397 data: URIs (e.g. @import url(data:text/css,...)) are decoded locally.
        if (DataUri.IsDataUri(url))
        {
            if (DataUri.TryDecodeBytes(url, out var dataBytes, out _))
                return DecodeCss(dataBytes, null, linkCharset, referrerCharset, out usedCharset);
            usedCharset = "utf-8";
            return string.Empty;
        }
        using var response = Session.Client.GetAsync(url).Result;
        response.EnsureSuccessStatusCode();
        var bytes = response.Content.ReadAsByteArrayAsync().Result;
        return DecodeCss(bytes, response.Content.Headers.ContentType?.CharSet,
                         linkCharset, referrerCharset, out usedCharset);
    }

    /// <summary>
    /// Recursively inlines leading <c>@import</c> rules per CSS 2.1 §6.3 ("CSS 2.1 user agents
    /// must ignore any @import rule that occurs inside a block or after any non-ignored statement
    /// other than an @charset or an @import rule") and §4.2 (open strings, <c>url(</c> tokens and
    /// statements are closed at EOF). Non-leading <c>@import</c> rules are left untouched so the
    /// CSS parser ignores or absorbs them as part of surrounding malformed syntax (at-import-001,
    /// at-import-011, at-rule-006/007, c11-import-000, import-000, eof-005/006, uri-018,
    /// cascade-import-001).
    /// </summary>
    private static string InlineImports(string css, string baseUrl, string? referrerCharset = null, int depth = 0) =>
        InlineImportsCore(css, baseUrl, (resolved, refCharset) =>
        {
            var imported = FetchCssText(resolved, null, refCharset, out var importedCharset);
            return (imported, importedCharset);
        }, referrerCharset, depth);

    /// <summary>Testable core of <see cref="InlineImports"/> with an injectable sheet fetcher.</summary>
    internal static string InlineImportsCore(
        string css,
        string baseUrl,
        Func<string, string?, (string Css, string Charset)?> fetcher,
        string? referrerCharset = null,
        int depth = 0)
    {
        if (depth > 8 || string.IsNullOrEmpty(css) ||
            !css.Contains("@import", StringComparison.OrdinalIgnoreCase))
            return css;

        var sb = new StringBuilder(css.Length);
        var i = 0;
        var expandedAny = false;

        while (i < css.Length)
        {
            SkipImportTrivial(css, ref i);
            var stmtStart = i;
            if (i >= css.Length) break;

            if (css[i] != '@')
            {
                // A rule set: its prelude runs through any ';' up to the '{' of its block (§4.2,
                // at-import-011's '# :unknownpseudo @import ...; .import {...}' is one statement).
                // CSS 2.1 §6.3 ends the @import prologue only at a NON-IGNORED statement, so a
                // rule set the CSS parser drops (invalid selector: at-import-010, at-rule-009)
                // is kept verbatim and the scan goes on.
                SkipStatementTail(css, ref i, semicolonEndsStatement: false);
                if (!IsIgnoredStatement(css, stmtStart, i))
                {
                    sb.Append(css, stmtStart, css.Length - stmtStart);
                    return expandedAny ? sb.ToString() : css;
                }
                sb.Append(css, stmtStart, i - stmtStart);
                sb.Append('\n');
                continue;
            }

            var kwStart = i + 1;
            var kwEnd = kwStart;
            while (kwEnd < css.Length && (AsciiChar.IsLetterOrDigit(css[kwEnd]) || css[kwEnd] == '-'))
                kwEnd++;
            var kw = css.AsSpan(kwStart, kwEnd - kwStart);

            if (kw.Equals("charset", StringComparison.OrdinalIgnoreCase))
            {
                // Skip leading @charset ...; and keep scanning for @import rules.
                i = kwEnd;
                SkipStatementTail(css, ref i);
                continue;
            }

            if (!kw.Equals("import", StringComparison.OrdinalIgnoreCase))
            {
                // Any other VALID at-rule (@media {...}, @page {...}, @font-face, …) ends the
                // @import prologue. An unknown at-rule or a known one that is invalid ('@media;',
                // '@page;') is ignored per §4.2, so it does not (at-import-009, at-rule-008).
                i = kwEnd;
                SkipStatementTail(css, ref i);
                if (!IsIgnoredStatement(css, stmtStart, i))
                {
                    sb.Append(css, stmtStart, css.Length - stmtStart);
                    return expandedAny ? sb.ToString() : css;
                }
                sb.Append(css, stmtStart, i - stmtStart);
                sb.Append('\n');
                continue;
            }

            i = kwEnd;
            SkipImportWhitespaceAndComments(css, ref i);
            if (!TryReadImportTarget(css, ref i, out var importUrl))
            {
                // Malformed @import target — skip to end of statement/block per §4.2.
                SkipStatementTail(css, ref i);
                expandedAny = true;
                continue;
            }

            // Read optional media query list up to top-level ';', '{' or EOF (§4.2).
            var mediaStart = i;
            var hitBlock = false;
            var depthParen = 0;
            var depthBracket = 0;
            while (i < css.Length)
            {
                var c = css[i];
                if (c == '/' && i + 1 < css.Length && css[i + 1] == '*')
                {
                    SkipCssComment(css, ref i);
                    continue;
                }
                if (c == '"' || c == '\'')
                {
                    TryReadCssString(css, ref i, out _);
                    continue;
                }
                if (c == '(') { depthParen++; i++; continue; }
                if (c == ')' && depthParen > 0) { depthParen--; i++; continue; }
                if (c == '[') { depthBracket++; i++; continue; }
                if (c == ']' && depthBracket > 0) { depthBracket--; i++; continue; }
                if (depthParen == 0 && depthBracket == 0)
                {
                    if (c == ';') break;
                    if (c == '{') { hitBlock = true; break; }
                }
                i++;
            }

            var media = css[mediaStart..i].Trim();
            if (hitBlock)
            {
                // @import with a { ... } block is invalid (§4.2); skip through matching '}'.
                SkipBalancedBlock(css, ref i);
                expandedAny = true;
                continue;
            }
            if (i < css.Length && css[i] == ';') i++;

            expandedAny = true;
            var resolved = Lite.Network.UrlUtils.Resolve(importUrl, baseUrl);
            if (resolved is null) continue;
            try
            {
                var fetched = fetcher(resolved, referrerCharset);
                if (fetched is null) continue;
                var (imported, importedCharset) = fetched.Value;
                imported = InlineImportsCore(imported, resolved, fetcher, importedCharset, depth + 1);
                if (string.IsNullOrEmpty(media))
                {
                    sb.Append(imported);
                    sb.Append('\n');
                }
                else
                {
                    sb.Append("@media ");
                    sb.Append(media);
                    sb.Append(" {\n");
                    sb.Append(imported);
                    sb.Append("\n}\n");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[@import load error] {resolved}: {ex.Message}");
            }
        }

        return expandedAny ? sb.ToString() : css;
    }

    private static void SkipImportTrivial(string css, ref int i)
    {
        while (i < css.Length)
        {
            var c = css[i];
            if (c is ' ' or '\t' or '\r' or '\n' or '\f') { i++; continue; }
            if (c == '/' && i + 1 < css.Length && css[i + 1] == '*') { SkipCssComment(css, ref i); continue; }
            if (c == '<' && i + 3 < css.Length && css[i + 1] == '!' && css[i + 2] == '-' && css[i + 3] == '-') { i += 4; continue; }
            if (c == '-' && i + 2 < css.Length && css[i + 1] == '-' && css[i + 2] == '>') { i += 3; continue; }
            break;
        }
    }

    private static void SkipImportWhitespaceAndComments(string css, ref int i)
    {
        while (i < css.Length)
        {
            var c = css[i];
            if (c is ' ' or '\t' or '\r' or '\n' or '\f') { i++; continue; }
            if (c == '/' && i + 1 < css.Length && css[i + 1] == '*') { SkipCssComment(css, ref i); continue; }
            break;
        }
    }

    private static void SkipCssComment(string css, ref int i)
    {
        i += 2;
        while (i < css.Length)
        {
            if (css[i] == '*' && i + 1 < css.Length && css[i + 1] == '/') { i += 2; return; }
            i++;
        }
    }

    /// <summary>
    /// True when the CSS parser drops the statement <c>css[start..end]</c> entirely: an unknown
    /// or malformed at-rule, or a rule set whose selector is invalid. Such a statement is
    /// "ignored" in the sense of CSS 2.1 §6.3 and does not end the @import prologue. The
    /// decision is the parser's own (the same AngleSharp.Css fork that later reads the sheet),
    /// so the prologue and the cascade agree on what was ignored.
    /// </summary>
    private static bool IsIgnoredStatement(string css, int start, int end)
    {
        var statement = css[start..end];
        try
        {
            // A rule set with an invalid selector survives in the sheet with a null Selector
            // (the builder keeps it, the cascade never matches it), so it counts as ignored.
            foreach (var rule in new AngleSharp.Css.Parser.CssParser().ParseStyleSheet(statement).Rules)
                if (rule is not ICssStyleRule { Selector: null })
                    return false;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void SkipStatementTail(string css, ref int i, bool semicolonEndsStatement = true)
    {
        var depthParen = 0;
        var depthBracket = 0;
        while (i < css.Length)
        {
            var c = css[i];
            if (c == '/' && i + 1 < css.Length && css[i + 1] == '*') { SkipCssComment(css, ref i); continue; }
            if (c == '"' || c == '\'') { TryReadCssString(css, ref i, out _); continue; }
            if (c == '(') { depthParen++; i++; continue; }
            if (c == ')' && depthParen > 0) { depthParen--; i++; continue; }
            if (c == '[') { depthBracket++; i++; continue; }
            if (c == ']' && depthBracket > 0) { depthBracket--; i++; continue; }
            if (depthParen == 0 && depthBracket == 0)
            {
                if (c == ';' && semicolonEndsStatement) { i++; return; }
                if (c == '{') { SkipBalancedBlock(css, ref i); return; }
            }
            i++;
        }
    }

    private static void SkipBalancedBlock(string css, ref int i)
    {
        if (i >= css.Length || css[i] != '{') return;
        var depth = 0;
        while (i < css.Length)
        {
            var c = css[i];
            if (c == '/' && i + 1 < css.Length && css[i + 1] == '*') { SkipCssComment(css, ref i); continue; }
            if (c == '"' || c == '\'') { TryReadCssString(css, ref i, out _); continue; }
            if (c == '{') { depth++; i++; continue; }
            if (c == '}') { depth--; i++; if (depth == 0) return; continue; }
            i++;
        }
    }

    private static bool TryReadImportTarget(string css, ref int i, out string url)
    {
        url = string.Empty;
        if (i >= css.Length) return false;
        if (css[i] == '"' || css[i] == '\'')
            return TryReadCssString(css, ref i, out url);

        if (css.Length - i >= 4 && css.AsSpan(i, 4).Equals("url(", StringComparison.OrdinalIgnoreCase))
        {
            i += 4;
            SkipImportWhitespaceAndComments(css, ref i);
            if (i >= css.Length) return true; // empty url( closed at EOF
            if (css[i] == '"' || css[i] == '\'')
            {
                if (!TryReadCssString(css, ref i, out url)) return false;
                SkipImportWhitespaceAndComments(css, ref i);
                if (i >= css.Length) return true; // closed at EOF per §4.2
                if (css[i] == ')') { i++; return true; }
                return false;
            }

            // Unquoted CSS URI token (§4.3.4), with backslash escapes (e.g. \( \) in data: URLs).
            var sb = new StringBuilder();
            while (i < css.Length)
            {
                var c = css[i];
                if (c == ')') { i++; url = sb.ToString(); return true; }
                if (c is ' ' or '\t' or '\r' or '\n' or '\f') break;
                if (c == '"' || c == '\'' || c == '(' || c <= 0x1F || c == 0x7F) return false;
                if (c == '\\')
                {
                    if (!TryAppendCssEscape(css, ref i, sb, allowEscapedNewline: false)) return false;
                    continue;
                }
                sb.Append(c);
                i++;
            }
            SkipImportWhitespaceAndComments(css, ref i);
            if (i >= css.Length) { url = sb.ToString(); return true; } // closed at EOF per §4.2
            if (css[i] == ')') { i++; url = sb.ToString(); return true; }
            return false;
        }

        return false;
    }

    private static bool TryReadCssString(string css, ref int i, out string value)
    {
        value = string.Empty;
        if (i >= css.Length) return false;
        var quote = css[i++];
        var sb = new StringBuilder();
        while (i < css.Length)
        {
            var c = css[i];
            if (c == quote) { i++; value = sb.ToString(); return true; }
            if (c is '\n' or '\r' or '\f') return false; // BAD_STRING (§4.2)
            if (c == '\\')
            {
                if (!TryAppendCssEscape(css, ref i, sb, allowEscapedNewline: true)) return false;
                continue;
            }
            sb.Append(c);
            i++;
        }
        // Unclosed string at EOF is closed per CSS 2.1 §4.2.
        value = sb.ToString();
        return true;
    }

    private static bool TryAppendCssEscape(string css, ref int i, StringBuilder sb, bool allowEscapedNewline)
    {
        i++; // skip '\'
        if (i >= css.Length) return true; // trailing backslash at EOF drops backslash
        var c = css[i];
        if (c == '\r')
        {
            if (!allowEscapedNewline) return false;
            i++;
            if (i < css.Length && css[i] == '\n') i++;
            return true;
        }
        if (c is '\n' or '\f')
        {
            if (!allowEscapedNewline) return false;
            i++;
            return true;
        }
        if (Uri.IsHexDigit(c))
        {
            var codePoint = 0;
            var digits = 0;
            while (i < css.Length && digits < 6 && Uri.IsHexDigit(css[i]))
            {
                codePoint = (codePoint << 4) | Uri.FromHex(css[i]);
                i++;
                digits++;
            }
            // CSS 2.1 §4.1.3: a single whitespace character (or CRLF) after a hex escape is consumed.
            if (i < css.Length)
            {
                if (css[i] == '\r') { i++; if (i < css.Length && css[i] == '\n') i++; }
                else if (css[i] is ' ' or '\t' or '\n' or '\f') i++;
            }
            if (codePoint == 0 || codePoint > 0x10FFFF || (codePoint >= 0xD800 && codePoint <= 0xDFFF))
                sb.Append('\uFFFD');
            else
                sb.Append(char.ConvertFromUtf32(codePoint));
            return true;
        }
        sb.Append(c);
        i++;
        return true;
    }

    /// <summary>Decodes stylesheet bytes; see <see cref="FetchCssText"/> for the priority order.
    /// <paramref name="usedCharset"/> receives the encoding actually applied, which becomes the
    /// referrer charset for any sheet this one imports. Exposed for tests.</summary>
    internal static string DecodeCss(byte[] bytes, string? httpCharset, string? linkCharset,
        string? referrerCharset, out string usedCharset)
    {
        usedCharset = "utf-8";

        // The pinned CSS 2.1 §4.4 gives transport metadata priority over BOM/@charset.
        // Strip a BOM only when it belongs to the selected encoding.
        if (TryGetEncoding(httpCharset) is { } transportEncoding)
        {
            usedCharset = transportEncoding.WebName;
            var preamble = transportEncoding.GetPreamble();
            var offset = preamble.Length > 0 && bytes.AsSpan().StartsWith(preamble) ? preamble.Length : 0;
            return transportEncoding.GetString(bytes, offset, bytes.Length - offset);
        }
        // 2. BOM and/or @charset, consulted only without a usable transport charset. UTF-32
        // BOMs are NOT in the sniffing table (CSS Syntax 3 §3.2 decodes through WHATWG Decode,
        // whose BOM sniff knows only UTF-8/16) — a UTF-32 sheet falls through to the chain
        // below, whose UTF-8 read drops every non-ASCII rule (what the at-charset UTF-32
        // cases on both suites rely on).
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            // §4.4 tier 2: the BOM decides, and an @charset rule the sheet also carries is
            // simply ignored — the detection is done (character-encoding-017's UTF-8-BOM sheet
            // declaring iso-8859-15 still applies its ASCII rules; at-charset-042's "UTF-16BE"
            // declaration loses to the BOM the same way). The old throw-out contradicted the
            // very cases it cited: their green references need the BOM-decoded sheet applied.
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            usedCharset = "utf-16le";
            // The BOM decides (at-charset-043's sheet declares "UTF-8" and at-charset-045's
            // declares "UTF-16BE" — both payloads are UTF-16LE and both must apply).
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            usedCharset = "utf-16be";
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }

        // A NUL-interleaved @charset pattern (CSS 2.1 §4.4's UTF-16 rows) does NOT match CSS
        // Syntax 3 §3.2's ASCII byte pattern, so no @charset is found: the sheet falls through
        // to the link/referrer/UTF-8 chain below, whose UTF-8 read drops every non-ASCII rule —
        // what the utf-16-named sheets on both suites rely on. (The old branch decoded the
        // pattern's own endianness per the CSS 2.1 table; no case on either suite needs it.)

        // 2. @charset at the very start of the sheet. Its own name has to be readable as ASCII,
        //    which it is in every encoding CSS 2.1 allows here.
        var probe = Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 128));
        if (probe.StartsWith("@charset \"", StringComparison.Ordinal))
        {
            var end = probe.IndexOf("\";", 10, StringComparison.Ordinal);
            if (end > 10)
            {
                if (TryGetEncoding(probe[10..end]) is { } declared)
                {
                    usedCharset = declared.WebName;
                    return declared.GetString(bytes);
                }
                // The @charset names an encoding this UA cannot decode: there is no honoring it
                // and no fallback the declaration permits — the style sheet is ignored
                // (character-encoding-018's unknown-encoding declaration).
                return string.Empty;
            }
        }

        // 3. Linking metadata, 4. referring sheet/document, 5. UTF-8.
        foreach (var candidate in new[] { linkCharset, referrerCharset })
            if (TryGetEncoding(candidate) is { } enc)
            {
                usedCharset = enc.WebName;
                return enc.GetString(bytes);
            }
        return Encoding.UTF8.GetString(bytes);
    }

    /// <summary>Resolves a charset name to an encoding, or null when it is unknown. The legacy
    /// code pages (Shift_JIS, windows-125x, koi8-r, iso-8859-x) are not in .NET's shared framework,
    /// so the CodePages provider is registered once here to make them resolvable.</summary>
    private static Encoding? TryGetEncoding(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        if (!_codePagesRegistered)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            _codePagesRegistered = true;
        }
        // Preserve the supplied label first: .NET recognizes UTF-8 and utf-8, while
        // replacing their hyphen with an underscore causes a first-chance exception on
        // every UTF-8 stylesheet. Some legacy labels need an underscore fallback.
        var label = name.Trim().Trim('"');
        try { return Encoding.GetEncoding(label); }
        catch (ArgumentException) { }
        if (!label.Contains('-')) return null;
        try { return Encoding.GetEncoding(label.Replace('-', '_')); }
        catch (ArgumentException) { return null; }
    }

    private static bool _codePagesRegistered;

    /// <summary>
    /// Rewrites a signed unitless zero (<c>-0</c> / <c>+0</c>) to plain <c>0</c>. Both are valid CSS
    /// numbers and mean exactly zero, but AngleSharp fails to parse them and DROPS the whole
    /// declaration — so <c>border-bottom-width: -0</c> silently becomes "no width specified", which
    /// is indistinguishable from a shorthand that omits the width and would wrongly resolve to the
    /// initial 'medium'. Only a sign directly followed by a lone 0 is touched: the negative
    /// lookahead leaves <c>-0.5</c>, <c>-05</c> and any identifier alone, and <c>-0px</c> is
    /// rewritten to <c>0px</c>, which is the same value.
    /// </summary>
    private static string NormalizeSignedZero(string css) =>
        string.IsNullOrEmpty(css) || (!css.Contains("-0") && !css.Contains("+0"))
            ? css
            : System.Text.RegularExpressions.Regex.Replace(css, @"(?<=[:\s,(])[+-]0(?![\d.])", "0");

    /// <summary>
    /// §4.2 parsing errors: a declaration whose value is illegal for the property is dropped
    /// from the stylesheet text before the CSSOM is built. AngleSharp's declaration map is
    /// last-wins per property with no negative-value checks, so <c>width: 0; width: -1px</c>
    /// would silently shadow the valid declaration with the illegal one (layout then reads
    /// the negative as auto instead of the earlier 0). Only the illegal declaration's own
    /// span is removed; sibling declarations, strings and url() values are left untouched.
    /// </summary>
    private static string DropIllegalDeclarations(string css)
    {
        if (string.IsNullOrEmpty(css) || !css.Contains('-')) return css;
        // Scan a copy with comments and string interiors blanked (same length): comment
        // prose can quote property/value text ('width : 100px ... right : -200px' inside a
        // documentation comment), and a value class that spans it would delete real code.
        var masked = MaskCommentsAndStrings(css);
        var result = css;
        foreach (var prop in NonNegativeProperties)
        {
            var pattern = new System.Text.RegularExpressions.Regex(
                @"(?i)(^|[;{\s])(" + System.Text.RegularExpressions.Regex.Escape(prop) + @")\s*:\s*[^;{}]*");
            for (int search = 0; ;)
            {
                var match = pattern.Match(masked, search);
                if (!match.Success) break;
                var colon = masked.IndexOf(':', match.Groups[2].Index + match.Groups[2].Length);
                if (colon < 0 || colon >= match.Index + match.Length)
                {
                    search = match.Index + match.Groups[1].Length + 1;
                    continue;
                }
                var value = masked.Substring(colon + 1, match.Index + match.Length - (colon + 1));
                if (!ContainsNegativeNumber(value))
                {
                    search = match.Index + match.Length;
                    continue;
                }
                // Remove the whole "prop: value" declaration — including the separator that
                // preceded it so no doubled ';' remains — from both copies (they stay in
                // lockstep), then rescan from the cut point.
                var start = match.Groups[1].Length > 0 ? match.Groups[1].Index : match.Groups[2].Index;
                var end = match.Index + match.Length;
                result = result.Remove(start, end - start);
                masked = masked.Remove(start, end - start);
                search = start;
            }
        }
        return result;
    }

    /// <summary>Blanks comment interiors and quoted-string interiors one-for-one (quotes
    /// themselves kept, backslash escapes consumed), so pattern scans over the result see
    /// only real code while every index still addresses the original string.</summary>
    private static string MaskCommentsAndStrings(string css)
    {
        var chars = css.ToCharArray();
        const int Normal = 0, InComment = 1, InDouble = 2, InSingle = 3;
        var state = Normal;
        for (var i = 0; i < chars.Length; i++)
        {
            var c = chars[i];
            switch (state)
            {
                case Normal:
                    if (c == '/' && i + 1 < chars.Length && chars[i + 1] == '*')
                    {
                        chars[i] = ' '; chars[i + 1] = ' '; state = InComment; i++;
                    }
                    else if (c == '"') state = InDouble;
                    else if (c == '\'') state = InSingle;
                    break;
                case InComment:
                    if (c == '*' && i + 1 < chars.Length && chars[i + 1] == '/')
                    {
                        chars[i] = ' '; chars[i + 1] = ' '; state = Normal; i++;
                    }
                    else chars[i] = ' ';
                    break;
                case InDouble:
                case InSingle:
                    var quote = state == InDouble ? '"' : '\'';
                    if (c == '\\')
                    {
                        chars[i] = ' ';
                        if (i + 1 < chars.Length) { chars[i + 1] = ' '; i++; }
                    }
                    else if (c == quote) state = Normal;
                    else chars[i] = ' ';
                    break;
            }
        }
        return new string(chars);
    }

    /// <summary>
    /// Clamps out-of-range rgb()/rgba() components (§4.2.1/§4.2.2: "Values outside the device
    /// gamut should be clipped": percentage components to [0,100], number components to [0,255]).
    /// AngleSharp fails to parse an over-range value and DROPS the whole declaration, so
    /// <c>rgb(101%, 0%, 0%)</c> — which must render exactly like #ff0000 — silently inherited;
    /// the suite's -078 swatch family asserts precisely this clamping.
    /// </summary>
    private static string NormalizeColorRanges(string css)
    {
        if (string.IsNullOrEmpty(css) ||
            !(css.Contains("rgb(", StringComparison.OrdinalIgnoreCase) || css.Contains("rgba(", StringComparison.OrdinalIgnoreCase)))
            return css;

        return System.Text.RegularExpressions.Regex.Replace(css,
            @"rgba?\(([^)]*)\)",
            match =>
            {
                var parts = match.Groups[1].Value.Split(',');
                if (parts.Length < 3) return match.Value;
                var clamped = new string[parts.Length];
                for (var i = 0; i < parts.Length; i++)
                {
                    var part = parts[i].Trim();
                    // Skip the CSS3 alpha comma-form's optional trailing component and anything
                    // that isn't a bare number/percentage (space-separated modern syntax, var()…).
                    var isPercent = part.EndsWith("%", StringComparison.Ordinal);
                    var numeric = isPercent ? part[..^1] : part;
                    if (!float.TryParse(numeric, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var value))
                    {
                        clamped[i] = parts[i];
                        continue;
                    }
                    var limit = isPercent ? 100f : 255f;
                    var fixedValue = Math.Clamp(value, 0f, limit);
                    clamped[i] = fixedValue == value ? parts[i]
                        : fixedValue.ToString("0.############", System.Globalization.CultureInfo.InvariantCulture)
                          + (isPercent ? "%" : "");
                }
                return $"{match.Value[..match.Value.IndexOf('(')]}({string.Join(", ", clamped)})";
            },
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    // CSS properties that AngleSharp.Css doesn't reliably cascade via ComputeCurrentStyle()
    private static readonly string[] s_extraProps =
    [
        "flex-direction", "flex-wrap", "justify-content", "align-items",
        "align-self", "align-content", "flex-grow", "flex-shrink", "flex-basis", "order",
        "row-gap", "column-gap", "gap",
        "flex", "flex-flow",
        "min-width", "max-width", "min-height", "max-height", "visibility",
        "border-radius", "box-shadow", "text-shadow",
        "float", "clear",
        // CSS2 text properties
        "text-transform", "letter-spacing", "word-spacing", "text-indent",
        // CSS2 border style
        "border-style", "border-top-style", "border-right-style", "border-bottom-style", "border-left-style",
        // CSS2 list properties
        "list-style-type", "list-style-position", "list-style",
        // CSS2 outline
        "outline", "outline-width", "outline-style", "outline-color", "outline-offset",
        // CSS2 vertical-align
        "vertical-align",
        // CSS2 background image
        "background-image", "background-repeat", "background-position", "background-size",
        // CSS2 table properties
        "border-collapse", "border-spacing", "table-layout", "caption-side", "empty-cells",
        // CSS2 counter properties
        "counter-reset", "counter-increment",
        // CSS3 text overflow
        "text-overflow",
        // CSS3 filter
        "filter"
    ];

    /// <summary>
    /// Iterates all stylesheet rules that match <paramref name="element"/> and copies
    /// flex-related property values into the node's StyleOverrides dictionary.
    /// This works around AngleSharp.Css not cascading these properties through ComputeCurrentStyle().
    /// </summary>
    private static void ExtractMatchedCssProperties(IElement element, LayoutNode node)
    {
        if (element.Owner?.StyleSheets is null) return;

        _ruleOrder = 0;
        _staticMatches.Clear();
        _pendingLinkOverrides.Clear();
        foreach (var sheet in element.Owner.StyleSheets.OfType<ICssStyleSheet>())
        {
            ProcessRules(sheet.Rules, element, node, mediaText: null);
        }
        ApplyCascadeToExtractedRules(element, node);

        // Pseudo-element rules were lifted out of the sheets before the cascade ran (see
        // NeutralizePseudoElementRules), so they are applied here in CSS 2.1 §6.4.1/§6.4.3
        // cascade order (origin, !important, specificity, source order).
        ApplyPseudoElementRules(element, node);

        StampOwnInheritOverRelativeLengths(element, node);
    }

    private static readonly Regex s_fontRelativeLength =
        new(@"^[+-]?(\d+(\.\d*)?|\.\d+)(em|ex)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>The element's own cascade - origin, !important, specificity, source order and
    /// the style attribute - WITHOUT inheritance (§6.2.1 distinguishes the specified/inherited
    /// value from the computed one). Used where the layout node's raw value could hide an
    /// 'inherit' keyword that AngleSharp already substituted.</summary>
    internal static ICssStyleDeclaration? ComputeExplicitStyleFor(IElement element)
    {
        try
        {
            var window = element.Owner?.DefaultView;
            if (window is null) return null;
            var device = element.Owner!.Context.GetService<IRenderDevice>() ?? new DefaultRenderDevice();
            return window.GetStyleCollection(device).ComputeExplicitStyle(element);
        }
        catch { return null; }
    }

    /// <summary>
    /// CSS 2.1 §6.2.1: 'inherit' takes the parent's COMPUTED value, and an 'em'/'ex' length
    /// computes against the font of the element that declares it (§4.3.2). AngleSharp resolves
    /// the keyword by copying the parent's SPECIFIED declaration into the element's own cascade,
    /// so <c>#child { margin: inherit }</c> under <c>#parent { font-size: 28px; margin: 2em }</c>
    /// reached layout as a bare "2em" that the child's own 40px font then scaled to 80px instead
    /// of 56px (margin-em-inherit-001, padding-em-inherit-001). Once the keyword is replaced it
    /// is indistinguishable from an authored "2em", so the element's own winning declaration is
    /// read here: where it is 'inherit' and the cascade substituted a font-relative length, the
    /// keyword goes to the override channel, which <see cref="StyleResolver"/> resolves against
    /// the parent's computed value (parents first).
    /// </summary>
    private static void StampOwnInheritOverRelativeLengths(IElement element, LayoutNode node)
    {
        List<string>? candidates = null;
        try
        {
            foreach (var property in node.Style)
            {
                if (property.Name is not { Length: > 0 } name || node.StyleOverrides.ContainsKey(name)) continue;
                // Margins and padding only: those are the properties whose 'inherit' StyleResolver
                // re-resolves from the declared channel on every recascade. A stamped 'inherit' on
                // an offset was resolved once, so a later script change to the parent's 'top' never
                // reached the child (dynamic-top-change-002/003 kept a stale position).
                if (!(name.StartsWith("margin-", StringComparison.Ordinal) ||
                      name.StartsWith("padding-", StringComparison.Ordinal))) continue;
                if (s_fontRelativeLength.IsMatch(property.Value?.Trim() ?? "")) (candidates ??= []).Add(name);
            }
        }
        catch { return; }
        if (candidates is null) return;

        // Cheap gate before a second cascade pass: some matching rule or the style attribute
        // must spell 'inherit' for one of the candidates.
        ICssStyleDeclaration? inline = null;
        try { if (element.HasAttribute("style")) inline = element.GetStyle(); }
        catch { inline = null; }
        bool SaysInherit(ICssStyleDeclaration? style, string prop) =>
            style is not null && string.Equals(style.GetPropertyValueSafe(prop)?.Trim(), "inherit",
                StringComparison.OrdinalIgnoreCase);
        if (!candidates.Any(prop => SaysInherit(inline, prop) || _staticMatches.Any(m => SaysInherit(m.Style, prop))))
            return;

        // The element's own cascade (origin, !important, specificity, order and the style
        // attribute), without inheritance: does 'inherit' win there?
        ICssStyleDeclaration? own = ComputeExplicitStyleFor(element);
        if (own is null) return;

        foreach (var prop in candidates)
            if (SaysInherit(own, prop))
                node.StyleOverrides[prop] = "inherit";
    }

    /// <summary>A rule's place in the §6.4.1 cascade: its declarations, the specificity of
    /// the selector (the most specific matching part of a list), its source order across the
    /// document's sheets, and its origin (<see cref="AngleSharp.Css.Dom.CascadeOrigin"/>).</summary>
    private readonly record struct CascadeEntry(ICssStyleDeclaration Style, AngleSharp.Css.Priority Specificity,
        int Order, int Origin);

    /// <summary>A :link (or :lang-only) rule's declarations, waiting for the cascade
    /// post-pass before they reach the override channel.</summary>
    private sealed record PendingOverride(Dictionary<string, string> Properties, CascadeEntry Entry);

    [ThreadStatic] private static int _ruleOrder;
    [ThreadStatic] private static List<CascadeEntry>? _staticMatchList;
    [ThreadStatic] private static List<PendingOverride>? _pendingLinkOverrideList;
    private static List<CascadeEntry> _staticMatches => _staticMatchList ??= [];
    private static List<PendingOverride> _pendingLinkOverrides => _pendingLinkOverrideList ??= [];

    private static AngleSharp.Css.Priority SpecificityFor(ICssStyleRule rule, IElement element)
    {
        try
        {
            var selector = rule.Selector;
            if (selector is AngleSharp.Css.Dom.IMultiSelector multi)
            {
                // A dynamic part matches only in its state: assume every state holds, so the
                // part the rule would apply through is the one whose specificity counts.
                using (AngleSharp.Dom.DynamicPseudoState.AssumingAll())
                    if (multi.GetMatchingSelector(element) is { } part) return part.Specificity;
            }
            return selector.Specificity;
        }
        catch { return AngleSharp.Css.Priority.Zero; }
    }

    private static int OriginOf(ICssStyleRule rule) => AngleSharp.Css.Dom.CascadeOrigin.Of(rule);

    /// <summary>§6.4.1 precedence of a declaration: normal declarations rank by origin
    /// (default sheet, user agent, user, author); author and then user !important outrank
    /// every normal one.</summary>
    private static int CascadeRank(int origin, bool important) => !important ? origin : origin switch
    {
        AngleSharp.Css.Dom.CascadeOrigin.Author => 4,
        AngleSharp.Css.Dom.CascadeOrigin.User => 5,
        _ => origin,
    };

    /// <summary>Whether declaration <paramref name="a"/> wins over <paramref name="b"/> in the
    /// cascade: rank, then specificity, then source order (§6.4.1 steps 2–4).</summary>
    private static bool Outranks(CascadeEntry a, bool aImportant, CascadeEntry b, bool bImportant)
    {
        var (ra, rb) = (CascadeRank(a.Origin, aImportant), CascadeRank(b.Origin, bImportant));
        if (ra != rb) return ra > rb;
        var bySpecificity = a.Specificity.CompareTo(b.Specificity);
        if (bySpecificity != 0) return bySpecificity > 0;
        return a.Order > b.Order;
    }

    private static bool IsImportant(ICssStyleDeclaration style, string property)
    {
        try { return string.Equals(style.GetPropertyPriority(property), "important", StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    /// <summary>The property names a declaration block sets: its longhands, plus the names its
    /// source text spells (shorthands).</summary>
    private static HashSet<string> DeclaredNames(ICssStyleDeclaration style)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var property in style)
                if (property.Name is { Length: > 0 } name) names.Add(name);
            foreach (var decl in SplitCssDeclarations(style.CssText ?? ""))
                if (decl.IndexOf(':') is var colon and > 0) names.Add(decl[..colon].Trim());
        }
        catch { /* an unreadable block declares nothing */ }
        return names;
    }

    /// <summary>The dynamic (:hover/:active/:focus) and :link/:lang rules leave AngleSharp's
    /// cascade and reach the style through Lite's own channels, which outrank every rule
    /// AngleSharp cascades. So each declaration is checked against the static rules that
    /// match the element, and against its style attribute: one that loses the §6.4.1 cascade
    /// to them is dropped (c21-pseu-cls-000: 'a:hover { color: red }' loses to the later
    /// 'a.test { color: green }' of equal specificity, so hovering leaves the link green).
    /// The surviving dynamic rules are kept in cascade order for the live merge.</summary>
    private static void ApplyCascadeToExtractedRules(IElement element, LayoutNode node)
    {
        if (node.DynamicRules is not { Count: > 0 } && _pendingLinkOverrides.Count == 0) return;
        var statics = _staticMatches.Select(entry => (Entry: entry, Names: DeclaredNames(entry.Style))).ToList();
        ICssStyleDeclaration? inline = null;
        try { if (element.HasAttribute("style")) inline = element.GetStyle(); }
        catch { inline = null; }
        var inlineNames = inline is null ? null : DeclaredNames(inline);

        bool Shadowed(string property, CascadeEntry entry)
        {
            var important = IsImportant(entry.Style, property);
            // A shorthand is shadowed when any of its longhands is ('background' by a later
            // 'background-color'): its unshadowed longhands are carried separately.
            bool Declares(HashSet<string> names) => names.Contains(property) ||
                names.Any(n => n.StartsWith(property + "-", StringComparison.OrdinalIgnoreCase));
            foreach (var (other, names) in statics)
                if (Declares(names) && Outranks(other, IsImportant(other.Style, property), entry, important))
                    return true;
            // The style attribute outranks every normal rule declaration (§6.4.3: specificity
            // 1,0,0,0); an !important rule declaration loses only to an !important attribute one.
            return inline is not null && Declares(inlineNames!) && (!important || IsImportant(inline, property));
        }

        if (node.DynamicRules is { Count: > 0 } rules)
        {
            var kept = new List<DynamicRule>(rules.Count);
            foreach (var rule in rules)
            {
                var entry = new CascadeEntry(rule.Style, rule.Specificity, rule.Order, rule.Origin);
                foreach (var property in rule.Properties.Keys.ToList())
                    if (Shadowed(property, entry)) rule.Properties.Remove(property);
                if (rule.Properties.Count > 0) kept.Add(rule);
            }
            kept.Sort((a, b) => a.Specificity.CompareTo(b.Specificity) is var c and not 0 ? c : a.Order.CompareTo(b.Order));
            node.DynamicRules = kept.Count > 0 ? kept : null;
        }
        foreach (var pending in _pendingLinkOverrides
                     .OrderBy(p => p.Entry.Specificity).ThenBy(p => p.Entry.Order))
            foreach (var (property, value) in pending.Properties)
                if (!Shadowed(property, pending.Entry))
                    node.StyleOverrides[property] = value;
        _pendingLinkOverrides.Clear();
    }

    /// <summary>
    /// Recursively processes a rule list, descending into @media blocks.
    /// <paramref name="mediaText"/> is non-null when inside a media rule.
    /// </summary>
    private static void ProcessRules(ICssRuleList rules, IElement element, LayoutNode node, string? mediaText)
    {
        foreach (var rule in rules)
        {
            if (rule is ICssMediaRule mediaRule)
            {
                var text = mediaRule.Media.MediaText;
                // Descend into nested rules, passing the media condition down
                ProcessRules(mediaRule.Rules, element, node, text);
                continue;
            }

            if (rule is not ICssStyleRule styleRule) continue;
            _ruleOrder++;

            // Handle ::before / ::after pseudo-element selectors
            if (TryExtractPseudoElementRule(element, node, styleRule))
                continue;

            // Handle pseudo-class selectors (:hover, :focus, :active)
            if (TryExtractPseudoClassRule(element, node, styleRule, mediaText))
                continue;

            var selectorText = styleRule.SelectorText;
            if (string.IsNullOrWhiteSpace(selectorText)) continue;
            // In XML documents type selectors are case-sensitive (§5.2, via §4.1.3's
            // document-language rule): 'DIV' does not match <div> (case-sensitive-007).
            if (Current.IsXmlDocument && !XmlTypeSelectorMatches(element, selectorText)) continue;
            try { if (!element.Matches(selectorText)) continue; }
            catch { continue; } // malformed selector

            if (mediaText is null)
            {
                // A matching static rule may outrank a dynamic/:link rule's declarations
                // (later at equal specificity, more specific, or !important) — recorded for
                // the cascade post-pass in ExtractMatchedCssProperties.
                _staticMatches.Add(new(styleRule.Style, SpecificityFor(styleRule, element), _ruleOrder, OriginOf(styleRule)));
                // Regular rule — apply directly to overrides
                var style = styleRule.Style;
                foreach (var prop in s_extraProps)
                {
                    var val = style.GetPropertyValueSafe(prop);
                    if (!string.IsNullOrEmpty(val))
                        StoreProp(node, prop, val);
                }
                ParseCssTextForFlexProps(styleRule.Style.CssText, node);
                // AngleSharp's list-style shorthand getter drops the position longhand when the
                // source puts it first ('list-style: inside square' loses 'inside'), so the
                // shorthand is re-decomposed from the rule's own text.
                ParseCssTextForListStyle(styleRule.Style.CssText, node);
                // Same rescue as the cascade path: re-apply the shorthand from the source when
                // AngleSharp dropped it, after the CssText pass so the source wins.
                if (RescuedBackgroundFor(styleRule) is { } rawBg)
                    ExpandBackgroundShorthand(node, rawBg);
                if (RescuedListStyleFor(styleRule) is { } rawLs)
                    DecomposeListStyle(node, rawLs);
                ExtractTransitionAndAnimation(styleRule.Style.CssText, node);
            }
            else
            {
                // Media-conditional rule — store for deferred evaluation and apply if currently matches
                StoreMediaProps(node, mediaText, styleRule.Style.CssText, styleRule.Style, target: "override");
            }
        }
    }

    /// <summary>
    /// Stores all properties from a media-conditional style rule on the node.
    /// Each property is saved into MediaConditionalStyles so it can be re-evaluated on resize,
    /// and applied immediately if the media query currently matches.
    /// </summary>
    private static void StoreMediaProps(LayoutNode node, string mediaText, string cssText,
        ICssStyleDeclaration style, string target)
    {
        var props = new Dictionary<string, string>();

        // Collect via GetPropertyValue for known extra props
        foreach (var prop in s_extraProps)
        {
            var val = style.GetPropertyValueSafe(prop);
            if (!string.IsNullOrEmpty(val))
                props[prop] = val;
        }

        // Also parse CssText to get all properties (display, color, etc.)
        if (!string.IsNullOrEmpty(cssText))
        {
            foreach (var decl in SplitCssDeclarations(cssText))
            {
                var colon = decl.IndexOf(':');
                if (colon < 0) continue;
                var prop = decl[..colon].Trim().ToLowerInvariant();
                var val = decl[(colon + 1)..].Trim().Replace("!important", "").Trim();
                if (!string.IsNullOrEmpty(val))
                    props.TryAdd(prop, val);
            }
        }

        // The paint reads background LONGHANDS, and MediaOverrides are stored as raw property
        // keys — a shorthand inside a media block must decompose exactly like the plain path
        // does (case-sensitive-001: 'BacKgrOUNd: UrL(...)' inside @MeDIa).
        if (props.TryGetValue("background", out var bgShorthand) && SplitBackgroundShorthand(bgShorthand) is { } bgLonghands)
        {
            props.Remove("background");
            foreach (var (prop, val) in bgLonghands)
                if (val is not null) props[prop] = val;
        }

        if (props.Count == 0) return;

        var matches = MediaQueryEvaluator.Matches(mediaText, ViewportWidth, ViewportHeight);
        var targetDict = target switch
        {
            "hover" => node.MediaHoverStyles,
            "focus" => node.MediaFocusStyles,
            "active" => node.MediaActiveStyles,
            _ => node.MediaOverrides,
        };

        foreach (var (prop, val) in props)
        {
            // Record for future resize re-evaluation
            node.MediaConditionalStyles.Add(new MediaConditionalStyle(mediaText, prop, val, target));

            // Apply immediately if the query matches at current viewport
            if (matches)
                targetDict[prop] = val;
        }
    }

    private static readonly HashSet<string> s_flexDirectionValues =
        ["row", "row-reverse", "column", "column-reverse"];
    private static readonly HashSet<string> s_flexWrapValues =
        ["nowrap", "wrap", "wrap-reverse"];

    /// <summary>
    /// Stores a property whose value contains <c>var()</c> into StyleOverrides.
    /// Expands common shorthands (padding, margin) into their longhand forms.
    /// </summary>
    private static void StoreVarProp(LayoutNode node, string prop, string val)
    {
        if (prop is "padding" or "margin")
        {
            node.StyleOverrides[$"{prop}-top"] = val;
            node.StyleOverrides[$"{prop}-right"] = val;
            node.StyleOverrides[$"{prop}-bottom"] = val;
            node.StyleOverrides[$"{prop}-left"] = val;
        }
        else if (prop == "gap")
        {
            node.StyleOverrides["row-gap"] = val;
            node.StyleOverrides["column-gap"] = val;
        }
        else
        {
            node.StyleOverrides[prop] = val;
        }
    }

    private static void StoreProp(LayoutNode node, string prop, string val)
    {
        // Everything this cascade pass writes must be retractable when the element is
        // re-resolved after a class/attribute change. A shorthand is stored as its LONGHANDS
        // ('background: green' → background-color), so marking only the shorthand left the
        // longhands behind and a restyled body kept its parse-time background
        // (background-root-101 through -103). Inline styles set later (JsStyle) remove their
        // key from CascadeAppliedProps, so they are never retracted by this marking.
        StorePropCore(node, prop, val);
        node.CascadeAppliedProps.Add(prop);
        foreach (var key in node.StyleOverrides.Keys) node.CascadeAppliedProps.Add(key);
    }

    private static void StorePropCore(LayoutNode node, string prop, string val)
    {
        if (prop == "background")
        {
            ExpandBackgroundShorthand(node, val);
        }
        else if (prop == "gap")
        {
            var parts = val.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            node.StyleOverrides["row-gap"] = parts[0];
            node.StyleOverrides["column-gap"] = parts.Length > 1 ? parts[1] : parts[0];
        }
        else if (prop == "flex")
        {
            // Decompose flex shorthand into flex-grow, flex-shrink, flex-basis
            DecomposeFlexShorthand(node, val);
        }
        else if (prop == "flex-flow")
        {
            // Decompose flex-flow shorthand into flex-direction + flex-wrap
            var parts = val.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var p in parts)
            {
                var lower = p.ToLowerInvariant();
                if (s_flexDirectionValues.Contains(lower))
                    node.StyleOverrides["flex-direction"] = lower;
                else if (s_flexWrapValues.Contains(lower))
                    node.StyleOverrides["flex-wrap"] = lower;
            }
        }
        else if (prop == "border-style")
        {
            // Expand shorthand to individual sides
            var parts = val.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1)
            {
                node.StyleOverrides["border-top-style"] = parts[0];
                node.StyleOverrides["border-right-style"] = parts[0];
                node.StyleOverrides["border-bottom-style"] = parts[0];
                node.StyleOverrides["border-left-style"] = parts[0];
            }
            else if (parts.Length == 2)
            {
                node.StyleOverrides["border-top-style"] = parts[0];
                node.StyleOverrides["border-bottom-style"] = parts[0];
                node.StyleOverrides["border-right-style"] = parts[1];
                node.StyleOverrides["border-left-style"] = parts[1];
            }
            else if (parts.Length == 3)
            {
                node.StyleOverrides["border-top-style"] = parts[0];
                node.StyleOverrides["border-right-style"] = parts[1];
                node.StyleOverrides["border-left-style"] = parts[1];
                node.StyleOverrides["border-bottom-style"] = parts[2];
            }
            else if (parts.Length >= 4)
            {
                node.StyleOverrides["border-top-style"] = parts[0];
                node.StyleOverrides["border-right-style"] = parts[1];
                node.StyleOverrides["border-bottom-style"] = parts[2];
                node.StyleOverrides["border-left-style"] = parts[3];
            }
        }
        else if (prop == "outline")
        {
            // outline shorthand: [width] [style] [color]
            var parts = val.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var p in parts)
            {
                var lower = p.ToLowerInvariant();
                if (lower is "solid" or "dotted" or "dashed" or "double" or "groove" or "ridge" or "inset" or "outset" or "none")
                    node.StyleOverrides["outline-style"] = lower;
                else if (lower is "thin" or "medium" or "thick" || lower.EndsWith("px"))
                    node.StyleOverrides["outline-width"] = lower;
                else
                    node.StyleOverrides["outline-color"] = lower;
            }
        }
        else if (prop == "list-style")
        {
            // list-style shorthand: [type] [position] [image]
            DecomposeListStyle(node, val);
        }
        else
        {
            // A css-wide keyword stays RAW here: rules cascade per element in document order,
            // so a child's 'inherit' can be stored before its parent's value lands. BoxEngine
            // resolves the pending keywords in tree order at the start of every layout.
            node.StyleOverrides[prop] = val;
        }
    }

    /// <summary>Decomposes a 'list-style' shorthand value into the longhands the list code
    /// reads (§12.5): [type | position | image], in any order. The position and type tokens
    /// are stored even when AngleSharp's own expansion of the shorthand dropped them.</summary>
    private static void DecomposeListStyle(LayoutNode node, string val)
    {
        foreach (var p in SplitBackgroundTokens(val))
        {
            var lower = p.ToLowerInvariant();
            if (lower is "inside" or "outside")
                node.StyleOverrides["list-style-position"] = lower;
            else if (lower.StartsWith("url(", StringComparison.Ordinal) || lower == "none")
                node.StyleOverrides["list-style-image"] = lower;
            else if (Layout.CssCounterStyles.Known.Contains(lower))
                node.StyleOverrides["list-style-type"] = lower;
        }
    }

    /// <summary>Re-applies 'list-style' declarations from the rule's own text: AngleSharp's
    /// shorthand getter drops the position longhand when the source puts it first
    /// ('list-style: inside square'), which left inside markers painting outside.</summary>
    private static void ParseCssTextForListStyle(string cssText, LayoutNode node)
    {
        if (string.IsNullOrEmpty(cssText)) return;
        foreach (var declaration in SplitCssDeclarations(cssText))
        {
            var colonIdx = declaration.IndexOf(':');
            if (colonIdx < 0) continue;
            if (!declaration[..colonIdx].Trim().Equals("list-style", StringComparison.OrdinalIgnoreCase)) continue;
            var val = declaration[(colonIdx + 1)..].Trim();
            if (val.Length == 0 || val is "inherit" or "initial" or "unset") continue;
            DecomposeListStyle(node, val);
        }
    }

    private static void DecomposeFlexShorthand(LayoutNode node, string val)
    {
        val = val.Trim().ToLowerInvariant();
        switch (val)
        {
            case "none":
                node.StyleOverrides["flex-grow"] = "0";
                node.StyleOverrides["flex-shrink"] = "0";
                node.StyleOverrides["flex-basis"] = "auto";
                return;
            case "auto":
                node.StyleOverrides["flex-grow"] = "1";
                node.StyleOverrides["flex-shrink"] = "1";
                node.StyleOverrides["flex-basis"] = "auto";
                return;
        }

        var parts = val.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1)
        {
            // Single value: if it's a number → flex: <grow> 1 0px
            // If it's a length/percent → flex: 1 1 <basis>
            if (float.TryParse(parts[0], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out _))
            {
                node.StyleOverrides["flex-grow"] = parts[0];
                node.StyleOverrides["flex-shrink"] = "1";
                node.StyleOverrides["flex-basis"] = "0px";
            }
            else
            {
                node.StyleOverrides["flex-grow"] = "1";
                node.StyleOverrides["flex-shrink"] = "1";
                node.StyleOverrides["flex-basis"] = parts[0];
            }
        }
        else if (parts.Length == 2)
        {
            node.StyleOverrides["flex-grow"] = parts[0];
            // Second value: number → shrink, length → basis
            if (float.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out _))
            {
                node.StyleOverrides["flex-shrink"] = parts[1];
                node.StyleOverrides["flex-basis"] = "0px";
            }
            else
            {
                node.StyleOverrides["flex-shrink"] = "1";
                node.StyleOverrides["flex-basis"] = parts[1];
            }
        }
        else if (parts.Length >= 3)
        {
            node.StyleOverrides["flex-grow"] = parts[0];
            node.StyleOverrides["flex-shrink"] = parts[1];
            node.StyleOverrides["flex-basis"] = parts[2];
        }
    }

    /// <summary>
    /// Parses the raw CssText of a rule's style declaration to extract flex properties
    /// that AngleSharp.Css may not expose via GetPropertyValue.
    /// </summary>
    private static void ParseCssTextForFlexProps(string cssText, LayoutNode node)
    {
        if (string.IsNullOrEmpty(cssText)) return;

        foreach (var declaration in SplitCssDeclarations(cssText))
        {
            var colonIdx = declaration.IndexOf(':');
            if (colonIdx < 0) continue;

            var prop = declaration[..colonIdx].Trim().ToLowerInvariant();
            var val = declaration[(colonIdx + 1)..].Trim();

            if (string.IsNullOrEmpty(val)) continue;

            // CSS custom properties (--*): store for var() resolution
            if (prop.StartsWith("--"))
            {
                node.CustomProperties[prop] = val;
                continue;
            }

            // Properties with var() references must be stored in overrides
            // because AngleSharp cannot resolve CSS custom properties.
            if (val.Contains("var(", StringComparison.OrdinalIgnoreCase))
            {
                StoreVarProp(node, prop, val);
                continue;
            }

            // Only extract our target properties. 'background' is handled from the DECLARED text
            // only — never from AngleSharp's synthesized shorthand, which every rule reports a
            // value for and would reset the longhands of rules that never mentioned it.
            if (prop == "background" || Array.IndexOf(s_extraProps, prop) >= 0)
                StoreProp(node, prop, val);
        }
    }

    /// <summary>
    /// Extracts the element-matching base selector from a single pseudo-element selector part
    /// (CSS 2.1 §5.10 / §5.12). Preserves the implicit universal selector when a combinator or
    /// descendant whitespace precedes the pseudo-element (<c>div :after</c> → <c>div *</c>, not
    /// <c>div</c>; specificity-011).
    /// </summary>
    internal static string ExtractPseudoElementBaseSelector(string part)
    {
        var m = PseudoElementRegex.Match(part);
        if (!m.Success) return part.Trim();
        var before = part[..m.Index];
        var trimmedBefore = before.TrimEnd();
        if (trimmedBefore.Length == 0) return string.Empty;
        if (before.Length > trimmedBefore.Length || trimmedBefore[^1] is '>' or '+' or '~')
            return trimmedBefore + " *";
        return trimmedBefore;
    }

    private readonly record struct PseudoWinner(int Rank, int Specificity, int Order)
    {
        public bool OutranksOrTies(PseudoWinner other) =>
            Rank != other.Rank ? Rank > other.Rank :
            Specificity != other.Specificity ? Specificity > other.Specificity :
            Order >= other.Order;
    }

    /// <summary>
    /// Applies all neutralized pseudo-element rules to <paramref name="node"/> in CSS 2.1 §6.4.1 /
    /// §6.4.3 cascade order: origin &amp; !important rank, then selector specificity, then source
    /// order (specificity-005, specificity-008, specificity-009, specificity-011).
    /// </summary>
    private static void ApplyPseudoElementRules(IElement element, LayoutNode node)
    {
        if (_pseudoElementRules.Count == 0) return;

        Dictionary<string, PseudoWinner>? beforeWinners = null;
        Dictionary<string, PseudoWinner>? afterWinners = null;
        Dictionary<string, PseudoWinner>? firstLetterWinners = null;
        Dictionary<string, PseudoWinner>? firstLineWinners = null;

        for (var order = 0; order < _pseudoElementRules.Count; order++)
        {
            var (selector, ruleStyle) = _pseudoElementRules[order];
            if (string.IsNullOrEmpty(selector)) continue;
            var origin = AngleSharp.Css.Dom.CascadeOrigin.Of(ruleStyle.Parent as ICssRule);

            foreach (var rawPart in SplitSelectorList(selector))
            {
                var part = rawPart.Trim();
                if (part.Length == 0) continue;
                var lower = part.ToLowerInvariant();
                bool isBefore = lower.Contains("::before") || lower.Contains(":before");
                bool isAfter = lower.Contains("::after") || lower.Contains(":after");
                bool isFirstLetter = lower.Contains("::first-letter") || lower.Contains(":first-letter");
                bool isFirstLine = lower.Contains("::first-line") || lower.Contains(":first-line");
                if (!isBefore && !isAfter && !isFirstLetter && !isFirstLine) continue;

                var baseSelector = ExtractPseudoElementBaseSelector(part);
                if (baseSelector.Length > 0)
                {
                    try { if (!element.Matches(baseSelector)) continue; }
                    catch { continue; }
                }

                var cssText = ruleStyle.CssText;
                if (string.IsNullOrEmpty(cssText)) continue;

                var (props, important) = ParseDeclarations(cssText);
                foreach (var longhand in ruleStyle)
                {
                    if (string.IsNullOrEmpty(longhand.Value)) continue;
                    if (!props.ContainsKey(longhand.Name))
                    {
                        props[longhand.Name] = longhand.Value;
                        if (IsImportant(ruleStyle, longhand.Name))
                            important.Add(longhand.Name);
                    }
                }

                var specificity = ComputeSpecificity(part);

                if (isBefore)
                {
                    node.BeforeStyles ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    beforeWinners ??= new Dictionary<string, PseudoWinner>(StringComparer.OrdinalIgnoreCase);
                    MergePseudoProps(node.BeforeStyles, beforeWinners, props, important, origin, specificity, order);
                }
                if (isAfter)
                {
                    node.AfterStyles ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    afterWinners ??= new Dictionary<string, PseudoWinner>(StringComparer.OrdinalIgnoreCase);
                    MergePseudoProps(node.AfterStyles, afterWinners, props, important, origin, specificity, order);
                }
                if (isFirstLetter)
                {
                    node.FirstLetterStyles ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    firstLetterWinners ??= new Dictionary<string, PseudoWinner>(StringComparer.OrdinalIgnoreCase);
                    MergePseudoProps(node.FirstLetterStyles, firstLetterWinners, props, important, origin, specificity, order);
                }
                if (isFirstLine)
                {
                    node.FirstLineStyles ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    firstLineWinners ??= new Dictionary<string, PseudoWinner>(StringComparer.OrdinalIgnoreCase);
                    MergePseudoProps(node.FirstLineStyles, firstLineWinners, props, important, origin, specificity, order);
                }
            }
        }
    }

    private static void MergePseudoProps(
        Dictionary<string, string> target,
        Dictionary<string, PseudoWinner> winners,
        Dictionary<string, string> props,
        HashSet<string> important,
        int origin,
        int specificity,
        int order)
    {
        foreach (var (p, v) in props)
        {
            var candidate = new PseudoWinner(CascadeRank(origin, important.Contains(p)), specificity, order);
            if (!winners.TryGetValue(p, out var existing) || candidate.OutranksOrTies(existing))
            {
                winners[p] = candidate;
                target[p] = v;
            }
        }
    }

    /// <summary>
    /// Detects ::before / ::after pseudo-element selectors, strips them,
    /// matches the base selector, and stores all properties for later synthetic child creation.
    /// </summary>
    private static bool TryExtractPseudoElementRule(IElement element, LayoutNode node, ICssStyleRule rule)
        => TryExtractPseudoElementRule(element, node, rule.SelectorText, rule.Style);

    private static bool TryExtractPseudoElementRule(
        IElement element, LayoutNode node, string? selector, ICssStyleDeclaration ruleStyle)
    {
        if (selector is null) return false;

        // Pseudo-element names are ASCII case-insensitive (§4.1.3; case-sensitive-003's
        // '.bef:BeFoRe'). A selector LIST names each part's own pseudo ('.bef:before, .af:after'
        // puts content on ::before of .bef and ::after of .af — handling the list whole gave
        // every matching element BOTH pseudos and doubled the sentences).
        var handled = false;
        foreach (var rawPart in SplitSelectorList(selector))
        {
            var part = rawPart.Trim();
            var lower = part.ToLowerInvariant();
            bool isBefore = lower.Contains("::before") || lower.Contains(":before");
            bool isAfter = lower.Contains("::after") || lower.Contains(":after");
            bool isFirstLetter = lower.Contains("::first-letter") || lower.Contains(":first-letter");
            bool isFirstLine = lower.Contains("::first-line") || lower.Contains(":first-line");
            if (!isBefore && !isAfter && !isFirstLetter && !isFirstLine) continue;

            // Strip the pseudo-element to get the part's base selector.
            var baseSelector = ExtractPseudoElementBaseSelector(part);
            if (baseSelector.Length > 0)
            {
                try { if (!element.Matches(baseSelector)) { handled = true; continue; } }
                catch { handled = true; continue; }
            }

            var cssText = ruleStyle.CssText;
            if (string.IsNullOrEmpty(cssText)) return true;

            var props = ParseCssTextToDict(cssText);
            // The raw text keeps shorthands as written; the getters read longhands, so add the
            // longhands AngleSharp expanded ('background: green' → background-color — the
            // content-171 ::before stripe never painted).
            foreach (var longhand in ruleStyle)
                if (!props.ContainsKey(longhand.Name) && !string.IsNullOrEmpty(longhand.Value))
                    props[longhand.Name] = longhand.Value;

            if (isBefore)
            {
                node.BeforeStyles ??= new Dictionary<string, string>();
                foreach (var (p, v) in props) node.BeforeStyles[p] = v;
            }
            if (isAfter)
            {
                node.AfterStyles ??= new Dictionary<string, string>();
                foreach (var (p, v) in props) node.AfterStyles[p] = v;
            }
            if (isFirstLetter)
            {
                node.FirstLetterStyles ??= new Dictionary<string, string>();
                foreach (var (p, v) in props) node.FirstLetterStyles[p] = v;
            }
            if (isFirstLine)
            {
                node.FirstLineStyles ??= new Dictionary<string, string>();
                foreach (var (p, v) in props) node.FirstLineStyles[p] = v;
            }
            handled = true;
        }

        return handled;
    }

    /// <summary>
    /// Detects pseudo-class selectors (:hover, :focus, :active), strips them,
    /// matches the base selector, and stores properties in the appropriate dict.
    /// Returns true if the rule was a pseudo-class rule (whether matched or not).
    /// When <paramref name="mediaText"/> is non-null the rule is media-conditional.
    /// </summary>
    private static bool TryExtractPseudoClassRule(IElement element, LayoutNode node, ICssStyleRule rule,
        string? mediaText = null)
    {
        var selector = rule.SelectorText;

        if (selector is null)
            return false;

        // Only intercept dynamic pseudo-classes (not structural ones). Pseudo-class names are
        // ASCII case-insensitive (§4.1.3), so ':LinK, :viSiTed' is the same selector — the
        // case-sensitive Contains check let it fall through to element.Matches, whose :link
        // semantics the UA sheet then outranked (case-sensitive-003's anchors stayed blue).
        var dynamicPseudo = DynamicPseudoRegex.Match(selector);
        if (!dynamicPseudo.Success)
            return false;

        // Strip pseudo-classes to get the base selector. The :lang(...) form goes whole and
        // FIRST — stripping the bare name first would leave its argument behind as a base
        // fragment that matches nothing.
        var baseSelector = DynamicPseudoRegex.Replace(LangPseudoRegex.Replace(selector, ""), "").Trim();

        // ':link' is Lite's own matching (all links unvisited): AngleSharp's cascade skips
        // rules carrying dynamic pseudo-classes entirely and its Matches never reports the
        // link state, so a rule like ':LinK, :viSiTed { color: green }' would vanish and the
        // injected UA sheet's `a { color: blue }` would win the cascade (case-sensitive-003).
        // An unvisited link is an a/area/link element with an href attribute; a rule naming
        // only :visited stays unapplied (privacy, §5.11.3's specified-permission).
        var hasLinkPseudo = selector.Contains(":link", StringComparison.OrdinalIgnoreCase);
        if (hasLinkPseudo)
        {
            var isUnvisitedLink = element.LocalName is "a" or "area" or "link"
                && element.GetAttribute("href") is not null; // an empty href is a same-document link
            if (!isUnvisitedLink) return true;
        }
        else if (selector.Contains(":visited", StringComparison.OrdinalIgnoreCase) && baseSelector.Length == 0)
        {
            return true;
        }

        // Every :lang(...) argument in the selector must match the element's inherited
        // language — Lite's own reading (§5.11.4), because AngleSharp's Matches reports no
        // language on XML-parsed documents and its cascade drops the whole rule anyway.
        foreach (Match langArg in LangPseudoRegex.Matches(selector))
            if (!ElementMatchesLang(element, langArg.Value)) return true;

        if (baseSelector.Length > 0 && baseSelector.Any(c => c != ','))
        {
            // A selector list leaves commas behind the strip — match each part.
            var matched = false;
            var anyPart = false;
            foreach (var part in baseSelector.Split(','))
            {
                var piece = part.Trim();
                if (piece.Length == 0) continue;
                anyPart = true;
                try { if (element.Matches(piece)) { matched = true; break; } }
                catch { return true; }
            }
            if (anyPart && !matched) return true;
        }
        else if (!hasLinkPseudo && baseSelector.Length == 0 && !LangPseudoRegex.IsMatch(selector))
        {
            // A bare dynamic pseudo-class rule ('X:link', ':lAnG(en)', ':AcTiVe') that Lite does
            // not evaluate itself: AngleSharp's matching decides — with :hover/:active/:focus
            // assumed to hold, since the live state is re-matched at style resolution (at
            // parse time nothing is hovered, so pseudo-010's ':AcTiVe' never reached it).
            using (DynamicStateRegex.IsMatch(selector) ? AngleSharp.Dom.DynamicPseudoState.AssumingAll() : null)
            {
                try { if (!element.Matches(selector)) return true; }
                catch { /* apply universally when the engine cannot evaluate it */ }
            }
        }

        // Extract all CSS properties from this rule
        var cssText = rule.Style.CssText;
        if (string.IsNullOrEmpty(cssText)) return true;

        var props = new Dictionary<string, string>();

        // Parse from CssText to get all properties
        foreach (var decl in SplitCssDeclarations(cssText))
        {
            var colon = decl.IndexOf(':');
            if (colon < 0) continue;
            var prop = decl[..colon].Trim().ToLowerInvariant();
            var val = decl[(colon + 1)..].Trim().Replace("!important", "").Trim();
            // A css-wide keyword on the 'font' shorthand must not be stored as a literal —
            // the value would resolve as a font-family list naming "inherit" and fall back
            // to the wrong face (case-sensitive-003's :link anchors rendered box glyphs).
            if (prop == "font" && val is "inherit" or "initial" or "unset") continue;
            if (!string.IsNullOrEmpty(val))
                props[prop] = val;
        }

        // Also try GetPropertyValue for known extra properties
        foreach (var prop in s_extraProps)
        {
            var val = rule.Style.GetPropertyValueSafe(prop);
            if (!string.IsNullOrEmpty(val) && !props.ContainsKey(prop))
                props[prop] = val;
        }

        // The painters read longhands ('outline-style', 'outline-width'), but CssText keeps
        // the shorthand a rule declared ('outline: 10px solid black'): add every longhand the
        // parsed declaration holds, so a dynamic rule's shorthand reaches the paint.
        foreach (var property in rule.Style)
        {
            if (property.Name is { Length: > 0 } name && !props.ContainsKey(name) &&
                property.Value is { Length: > 0 } value && !name.StartsWith("--", StringComparison.Ordinal))
                props[name] = value.Replace("!important", "").Trim();
        }

        if (mediaText is null)
        {
            // Regular pseudo-class rule
            var hasHover = selector.Contains(":hover", StringComparison.OrdinalIgnoreCase);
            var hasFocus = selector.Contains(":focus", StringComparison.OrdinalIgnoreCase);
            var hasActive = selector.Contains(":active", StringComparison.OrdinalIgnoreCase);
            var hasLink = selector.Contains(":link", StringComparison.OrdinalIgnoreCase);
            // A :hover/:active/:focus rule keeps its whole selector: it is re-matched against
            // the live interaction state at style resolution, so the state may sit on any
            // compound ('textarea:focus + span', 'div:hover p'). The rule is kept only when
            // it could ever match this element (every dynamic pseudo-class assumed true).
            // :link/:lang are decided above and stripped (AngleSharp reports neither); a list
            // part that also needs :visited never applies, since every link is unvisited.
            var live = string.Join(", ", selector.Split(',')
                .Where(part => !part.Contains(":visited", StringComparison.OrdinalIgnoreCase))
                .Select(part => DynamicStripRegex.Replace(LangPseudoRegex.Replace(part, ""), "").Trim())
                .Where(part => part.Length > 0));
            if ((hasHover || hasFocus || hasActive) && live.Length > 0)
            {
                bool couldMatch;
                using (AngleSharp.Dom.DynamicPseudoState.AssumingAll())
                {
                    try { couldMatch = element.Matches(live); }
                    catch { couldMatch = false; }
                }
                if (couldMatch)
                {
                    Interaction.PseudoClassState.Install();
                    (node.DynamicRules ??= []).Add(new DynamicRule(element, live, props, rule.Style,
                        SpecificityFor(rule, element), _ruleOrder, OriginOf(rule)));
                }
            }
            else if (!hasHover && !hasFocus && !hasActive)
            {
                // :link applies to unvisited links (all links are unvisited here) and a rule
                // whose only dynamic pseudo-class is :lang applies as an ordinary author rule
                // (AngleSharp's cascade drops both). Every link is unvisited, so a selector
                // part that also needs :visited never matches: a rule made only of such parts
                // ('a:visited') applies to nothing — it used to fall through to here and style
                // every link as visited (c21-pseu-cls-000's red 'a:visited').
                // A part that strips to nothing (':lang(en)', ':link') is universal: its
                // pseudo-class was decided above.
                var staticLive = string.Join(", ", selector.Split(',')
                    .Where(part => !part.Contains(":visited", StringComparison.OrdinalIgnoreCase))
                    .Select(part => DynamicStripRegex.Replace(LangPseudoRegex.Replace(part, ""), "").Trim())
                    .Select(part => part.Length > 0 ? part : "*"));
                bool liveMatches;
                try { liveMatches = staticLive.Length > 0 && element.Matches(staticLive); }
                catch { liveMatches = false; }
                // The declarations reach the override channel after the cascade post-pass,
                // which drops any a later or more specific static rule outranks. They still
                // beat the injected UA sheet's plain `a { color: blue }` (§6.4.1 author beats
                // UA: case-sensitive-003's anchors stayed blue under the TryAdd form).
                if (liveMatches)
                    _pendingLinkOverrides.Add(new(props, new CascadeEntry(rule.Style,
                        SpecificityFor(rule, element), _ruleOrder, OriginOf(rule))));
            }
        }
        else
        {
            // Media-conditional pseudo-class rule — store and apply if matching
            if (selector.Contains(":hover", StringComparison.OrdinalIgnoreCase))
                StoreMediaProps(node, mediaText, cssText, rule.Style, target: "hover");
            if (selector.Contains(":focus", StringComparison.OrdinalIgnoreCase))
                StoreMediaProps(node, mediaText, cssText, rule.Style, target: "focus");
            if (selector.Contains(":active", StringComparison.OrdinalIgnoreCase))
                StoreMediaProps(node, mediaText, cssText, rule.Style, target: "active");
        }

        return true;
    }

    /// <summary>Whether every type selector in the selector list matches the element's
    /// local name case-sensitively (XML documents only). Checks the rightmost compound of
    /// each part — the subject the rule would apply to.</summary>
    private static bool XmlTypeSelectorMatches(IElement element, string selector)
    {
        // A SELECTOR LIST matches when ANY part matches (§5.2.1's comma semantics), so the
        // case-sensitivity pre-filter must be per part too: 'body, #test { counter-reset: c }'
        // matched the #test div through its '#test' part even though its 'body' part names a
        // different element — requiring every part to pass skipped the whole rule on XML-served
        // documents and left every counter at zero (the WPT content-counters twins). The full
        // match still runs through element.Matches below, which ORs the parts.
        foreach (var rawPart in selector.Split(','))
        {
            var part = rawPart.Trim();
            if (part.Length == 0) continue;
            var compound = part.Split([' ', '>', '+', '~'], StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
            if (compound is null) continue;
            var i = 0;
            while (i < compound.Length && (AsciiChar.IsLetterOrDigit(compound[i]) || compound[i] is '-' or '_')) i++;
            var type = compound[..i];
            if (type.Length == 0 || type == "*") return true;
            if (element.LocalName.Equals(type, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    /// <summary>Whether <paramref name="element"/>'s language matches a ':lang(...)' selector
    /// fragment (§5.11.4): the nearest lang/xml:lang attribute on the element or an ancestor,
    /// or - when none is in scope - the document's own language. The argument is a language
    /// RANGE: it matches the language itself or a more specific tag that extends it with a
    /// hyphen (':lang(en)' matches en-US; ':lang(en-GB)' does not match 'en'), compared ASCII
    /// case-insensitively. The argument may be a quoted string or a wildcard.</summary>
    private static bool ElementMatchesLang(IElement element, string langFragment)
    {
        var open = langFragment.IndexOf('(');
        var close = langFragment.LastIndexOf(')');
        if (open < 0 || close <= open) return false;
        var wanted = langFragment[(open + 1)..close].Trim().Trim('"', '\'');
        if (wanted.Length == 0 || wanted == "*") return true;

        string? inherited = null;
        for (var node = element; node is not null; node = node.ParentElement)
        {
            // An EMPTY lang attribute is itself the in-scope language: HTML §3.2.6.2 makes
            // 'lang=""' say "the language is unknown", which suppresses the HTTP/meta document
            // language instead of falling back to it (the-lang-attribute-009/010).
            if (node.HasAttribute("lang"))
            {
                inherited = node.GetAttribute("lang")!.Trim();
                break;
            }
            // XML documents carry the namespaced spelling; HTML uses the bare attribute. In an
            // HTML document a literal 'xml:lang' attribute sits in no namespace and sets no
            // language (HTML §3.2.6.2) — only the XML-namespaced one, which the HTML parser
            // gives foreign (SVG/MathML) elements, counts (xml-lang-selectors-002).
            var htmlDocument = node.Owner is AngleSharp.Html.Dom.IHtmlDocument;
            var xmlLang = node.Attributes.FirstOrDefault(a =>
                !htmlDocument && a.Name.Equals("xml:lang", StringComparison.OrdinalIgnoreCase) ||
                a.NamespaceUri == "http://www.w3.org/XML/1998/namespace" && a.LocalName == "lang");
            if (xmlLang is not null) { inherited = xmlLang.Value.Trim(); break; }
        }
        inherited ??= DocumentLanguage(element);
        if (string.IsNullOrEmpty(inherited)) return false;
        // A language range matches when the tags are equal, or when the element's tag is the
        // range plus a hyphen and further subtags: 'en' never matches ':lang(en-GB)'.
        return inherited.Equals(wanted, StringComparison.OrdinalIgnoreCase) ||
            inherited.Length > wanted.Length &&
            inherited.StartsWith(wanted, StringComparison.OrdinalIgnoreCase) &&
            inherited[wanted.Length] == '-';
    }

    /// <summary>The document language for ':lang()' when no lang attribute is in scope
    /// (§5.11.4): the response's Content-Language header, else an HTML
    /// <c>meta http-equiv="content-language"</c>.</summary>
    private static string? DocumentLanguage(IElement element)
    {
        if (Current.ContentLanguage is { Length: > 0 } header) return header;
        if (element.Owner is not { } document) return null;
        // An HTML document, or an XHTML document (the XML parser): 'meta http-equiv' is
        // meaningful in both. A plain XML document has no such element semantics.
        var xhtml = document is AngleSharp.Html.Dom.IHtmlDocument ||
            document.DocumentElement?.NamespaceUri == "http://www.w3.org/1999/xhtml";
        if (!xhtml) return null;
        foreach (var meta in document.QuerySelectorAll("meta[http-equiv]"))
            if (string.Equals(meta.GetAttribute("http-equiv"), "content-language",
                    StringComparison.OrdinalIgnoreCase) &&
                meta.GetAttribute("content")?.Trim() is { Length: > 0 } content)
                return content;
        return null;
    }

    // ---- CSS rule storage for the LayoutNode-based cascade (StyleResolver) ----

    /// <summary>A collected author style rule with the data needed for a correct cascade.</summary>
    internal sealed record CssRule(
        string Selector,
        int Specificity,
        int Order,
        Dictionary<string, string> Properties,
        HashSet<string> ImportantProps,
        RuleOrigin Origin = RuleOrigin.Author);

    /// <summary>CSS 2.1 §6.4.1 cascade origins. Ascending precedence for normal declarations is
    /// UA &lt; user &lt; author; for !important declarations author loses to user (the CSS 2.1
    /// balance-of-power rule), so the tier order is UA-normal &lt; user-normal &lt; author-normal
    /// &lt; author-important &lt; user-important.</summary>
    internal enum RuleOrigin
    {
        Ua = 0,
        User = 1,
        Author = 2,
    }

    internal static List<CssRule> CssRules => Current.CssRules;

    /// <summary>
    /// Collects all CSS style rules from the document's stylesheets for the runtime cascade
    /// (StyleResolver) used by dynamically created elements and className changes.
    /// </summary>
    private static void CollectCssRules(AngleSharp.Dom.IDocument document)
    {
        CssRules.Clear();
        CollectRawBackgrounds(document);
        if (document.StyleSheets is null) return;
        foreach (var sheet in document.StyleSheets.OfType<ICssStyleSheet>())
        {
            // A suspended sheet (style.disabled, host author-disable, unselected alternate set)
            // contributes nothing to the cascade (CSS 2.1 §6.4).
            if (sheet.OwnerNode is IElement ownerNode && Current.SuspendedSheets.Contains(ownerNode)) continue;
            var origin = SheetOrigin(sheet);
            CollectRulesFromSheet(sheet.Rules, origin);
        }
    }

    private static RuleOrigin SheetOrigin(ICssStyleSheet sheet)
    {
        if (sheet.OwnerNode is not IElement owner) return RuleOrigin.Author;
        if (owner == Current.UaStyleElement) return RuleOrigin.Ua;
        if (Current.UserStyleElements.Contains(owner)) return RuleOrigin.User;
        return RuleOrigin.Author;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 'background' shorthands rescued from the raw stylesheet text
    // ─────────────────────────────────────────────────────────────────────────
    //
    // AngleSharp.Css drops a whole `background` declaration when the position is written as a bare
    // keyword — `background: red url(x.png) right repeat-y` and `background: bottom green` both
    // vanish, leaving the element with whatever a lower-specificity rule set. The value is
    // perfectly good CSS 2.1 (§14.2), and the engine's own expander already understands it, so the
    // shorthand is read straight out of the stylesheet source and re-applied over the CSSOM's
    // version. Keyed by the rule's selector list, canonicalised so `body>div` and `body > div`
    // agree; a selector declared twice keeps the last value, which is what the cascade would do
    // for equal specificity anyway.

    private static Dictionary<string, (string Value, int Count)> s_rawBackgrounds => Current.RawBackgrounds;

    private static Dictionary<string, (string Value, int Count)> s_rawListStyles => Current.RawListStyles;

    private static void CollectRawBackgrounds(AngleSharp.Dom.IDocument document)
    {
        s_rawBackgrounds.Clear();
        s_rawListStyles.Clear();
        foreach (var styleEl in document.QuerySelectorAll("style"))
            ScanRawBackgrounds(StripCssComments(styleEl.TextContent ?? string.Empty));
    }

    private static void ScanRawBackgrounds(string css)
    {
        var i = 0;
        while (i < css.Length)
        {
            var open = IndexOfSignificant(css, i, '{');
            if (open < 0) break;
            var close = MatchingBrace(css, open);
            if (close < 0) break;

            var prelude = css[i..open].Trim();
            var body = css[(open + 1)..close];
            if (prelude.StartsWith('@'))
            {
                // Conditional groups contain more style rules; everything else (@font-face,
                // @keyframes, @page) has no selector to key on.
                var name = prelude.Split(' ', '(', '\t', '\n', '\r')[0].ToLowerInvariant();
                if (name is "@media" or "@supports" or "@document" or "@layer")
                    ScanRawBackgrounds(body);
            }
            else if (prelude.Length > 0)
            {
                var key = CanonicalSelectorList(prelude);
                if (LastBackgroundDeclaration(body) is { } bg)
                {
                    var seen = s_rawBackgrounds.TryGetValue(key, out var prev) ? prev.Count : 0;
                    s_rawBackgrounds[key] = (bg, seen + 1);
                }
                // AngleSharp drops a whole 'list-style' declaration whose position token comes
                // first ('list-style: inside square'), so the raw source is the only record.
                if (LastRawDeclaration(body, "list-style") is { } ls)
                {
                    var seenLs = s_rawListStyles.TryGetValue(key, out var prevLs) ? prevLs.Count : 0;
                    s_rawListStyles[key] = (ls, seenLs + 1);
                }
            }
            i = close + 1;
        }
    }

    /// <summary>Index of the next <paramref name="wanted"/> character that the CSS tokenizer would
    /// actually see: one that is neither backslash-escaped nor inside a string. `p\{x` has no
    /// block, and `content: "}"` does not close one — a scanner that misses either resynchronises
    /// on the wrong brace and starts attributing declarations to the wrong selector.</summary>
    private static int IndexOfSignificant(string s, int from, char wanted)
    {
        var quote = '\0';
        for (var i = from; i < s.Length; i++)
        {
            var c = s[i];
            if (c == '\\') { i++; continue; }
            if (quote != '\0') { if (c == quote) quote = '\0'; continue; }
            if (c is '"' or '\'') { quote = c; continue; }
            if (c == wanted) return i;
        }
        return -1;
    }

    private static int MatchingBrace(string s, int open)
    {
        var depth = 0;
        var quote = '\0';
        for (var i = open; i < s.Length; i++)
        {
            var c = s[i];
            if (c == '\\') { i++; continue; }
            if (quote != '\0') { if (c == quote) quote = '\0'; continue; }
            if (c is '"' or '\'') { quote = c; continue; }
            if (c == '{') depth++;
            else if (c == '}' && --depth == 0) return i;
        }
        return -1;
    }

    private static string StripCssComments(string css)
    {
        if (!css.Contains("/*", StringComparison.Ordinal)) return css;
        var sb = new System.Text.StringBuilder(css.Length);
        var i = 0;
        while (i < css.Length)
        {
            var start = css.IndexOf("/*", i, StringComparison.Ordinal);
            if (start < 0) { sb.Append(css, i, css.Length - i); break; }
            sb.Append(css, i, start - i);
            var end = css.IndexOf("*/", start + 2, StringComparison.Ordinal);
            if (end < 0) break;
            i = end + 2;
        }
        return sb.ToString();
    }

    /// <summary>Returns the value of the last `background:` (not `background-*:`) declaration in a
    /// rule body, with any !important flag removed, or null when the rule has none.</summary>
    private static string? LastBackgroundDeclaration(string body)
    {
        string? found = null;
        var depth = 0;
        var braces = 0;
        var sawBrace = false;
        var quote = '\0';
        var start = 0;
        for (var i = 0; i <= body.Length; i++)
        {
            if (i < body.Length)
            {
                var c = body[i];
                if (c == '\\') { i++; continue; }
                if (quote != '\0') { if (c == quote) quote = '\0'; continue; }
                if (c is '"' or '\'') { quote = c; continue; }
                if (c == '{') { braces++; sawBrace = true; continue; }
                if (c == '}') { braces = Math.Max(0, braces - 1); continue; }
                if (c == '(') { depth++; continue; }
                if (c == ')') { depth = Math.Max(0, depth - 1); continue; }
                if (c != ';' || depth != 0 || braces != 0) continue;
            }
            var decl = body[start..Math.Min(i, body.Length)];
            start = i + 1;
            // A declaration containing a block is malformed (CSS 2.1 §4.2) and is dropped whole,
            // so a `background` written inside one must not be read back out of it.
            var hadBrace = sawBrace;
            sawBrace = false;
            if (hadBrace) continue;
            var colon = IndexOfSignificant(decl, 0, ':');
            if (colon < 0) continue;
            var propName = decl[..colon].Trim();
            if (propName.StartsWith("background-", StringComparison.OrdinalIgnoreCase))
            {
                // A later background-* longhand overrides an earlier shorthand (§14.2 ordering),
                // so do not rescue an earlier shorthand over it.
                found = null;
                continue;
            }
            if (!propName.Equals("background", StringComparison.OrdinalIgnoreCase)) continue;
            var value = decl[(colon + 1)..].Trim();
            if (value.EndsWith("important", StringComparison.OrdinalIgnoreCase))
            {
                var head = value[..^"important".Length].TrimEnd();
                if (head.EndsWith('!')) value = head[..^1].TrimEnd();
            }
            // The rescue only restores a value the converters refused; one the CSS 2.1 §4.2
            // scan rejects (a BAD_STRING, a BAD_URI such as an unclosed url(, a stray ';') is
            // malformed and stays dropped.
            if (value.Length > 0 && AngleSharp.Css.Parser.CssComponentScan.IsWellFormedValue(value)) found = value;
        }
        return found;
    }

    /// <summary>Canonicalises a selector list so the stylesheet's own text and AngleSharp's
    /// re-serialisation of it produce the same key: whitespace collapsed, spaces around
    /// combinators removed, and the comma-separated parts sorted (order carries no meaning).</summary>
    private static string CanonicalSelectorList(string selectors)
    {
        var parts = SplitSelectorList(selectors)
            .Select(CanonicalSelector)
            .Where(s => s.Length > 0)
            .ToList();
        parts.Sort(StringComparer.Ordinal);
        return string.Join(',', parts);
    }

    private static string CanonicalSelector(string selector)
    {
        var sb = new System.Text.StringBuilder(selector.Length);
        var pendingSpace = false;
        foreach (var c in selector)
        {
            if (char.IsWhiteSpace(c)) { pendingSpace = sb.Length > 0; continue; }
            if (c is '>' or '+' or '~')
            {
                while (sb.Length > 0 && sb[^1] == ' ') sb.Length--;
                pendingSpace = false;
            }
            else if (pendingSpace && sb.Length > 0 && sb[^1] is not ('>' or '+' or '~'))
            {
                sb.Append(' ');
            }
            pendingSpace = false;
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>The raw `background` shorthand declared for a rule's selector list, but only when
    /// AngleSharp lost the whole background family for that rule. When it kept anything — even a
    /// longhand written after the shorthand, which must win over it (§14.2 ordering) — its parse is
    /// authoritative and re-expanding the source would clobber that ordering. A selector that
    /// declares a background more than once is left alone too: the source order that decides
    /// between them is not recoverable from the CSSOM rule alone, so abstaining keeps the engine
    /// no worse than it was.</summary>
    /// <summary>The last 'list-style' declaration in a rule body — same tokenizer rules as
    /// <see cref="LastBackgroundDeclaration"/> (no strings, no nested braces, !important
    /// stripped).</summary>
    private static string? LastRawDeclaration(string body, string prop)
    {
        string? found = null;
        var depth = 0;
        var braces = 0;
        var sawBrace = false;
        var quote = '\0';
        var start = 0;
        for (var i = 0; i <= body.Length; i++)
        {
            var c = i < body.Length ? body[i] : ';';
            if (quote != '\0')
            {
                if (c == quote && (i == 0 || body[i - 1] != '\\')) quote = '\0';
                continue;
            }
            if (c is '"' or '\'') { quote = c; continue; }
            if (c == '(') depth++;
            else if (c == ')') depth = Math.Max(0, depth - 1);
            else if (c == '{') braces++;
            else if (c == '}') { braces--; sawBrace = true; }
            else if (c == ';' || i == body.Length)
            {
                var decl = body[start..Math.Min(i, body.Length)];
                start = i + 1;
                var hadBrace = sawBrace;
                sawBrace = false;
                if (hadBrace) continue;
                if (depth > 0) continue;
                var colon = IndexOfSignificant(decl, 0, ':');
                if (colon < 0) continue;
                if (!decl[..colon].Trim().Equals(prop, StringComparison.OrdinalIgnoreCase)) continue;
                var value = decl[(colon + 1)..].Trim();
                if (value.EndsWith("important", StringComparison.OrdinalIgnoreCase))
                {
                    var head = value[..^"important".Length].TrimEnd();
                    if (head.EndsWith('!')) value = head[..^1].TrimEnd();
                }
                if (value.Length > 0 && AngleSharp.Css.Parser.CssComponentScan.IsWellFormedValue(value)) found = value;
            }
        }
        return found;
    }

    /// <summary>The rule's raw 'list-style' declaration when AngleSharp's own view of the rule
    /// no longer carries one — the whole declaration was dropped (position token first).</summary>
    private static string? RescuedListStyleFor(ICssStyleRule styleRule)
    {
        if (string.IsNullOrEmpty(styleRule.SelectorText)) return null;
        if (!s_rawListStyles.TryGetValue(CanonicalSelectorList(styleRule.SelectorText), out var raw)) return null;
        if (raw.Count != 1) return null;
        var cssText = styleRule.Style?.CssText;
        if (!string.IsNullOrEmpty(cssText) &&
            cssText.Contains("list-style", StringComparison.OrdinalIgnoreCase)) return null;
        return raw.Value;
    }

    private static string? RescuedBackgroundFor(ICssStyleRule styleRule)
    {
        if (string.IsNullOrEmpty(styleRule.SelectorText)) return null;
        if (!s_rawBackgrounds.TryGetValue(CanonicalSelectorList(styleRule.SelectorText), out var raw))
            return null;
        if (raw.Count != 1) return null;
        var cssText = styleRule.Style?.CssText;
        if (!string.IsNullOrEmpty(cssText) &&
            cssText.Contains("background", StringComparison.OrdinalIgnoreCase))
        {
            // When AngleSharp kept an earlier background-* longhand (e.g. background-color: red)
            // while dropping a later `background:` shorthand (background-reset-001), rescue the
            // shorthand only if at least one of its declared components is missing from CSSOM.
            if (SplitBackgroundShorthand(raw.Value) is not { } expected) return null;
            var dropped = false;
            foreach (var (prop, val) in expected)
            {
                if (val is null || IsBackgroundInitial(prop, val)) continue;
                if (string.IsNullOrEmpty(styleRule.Style?.GetPropertyValueSafe(prop)))
                {
                    dropped = true;
                    break;
                }
            }
            if (!dropped) return null;
        }
        return raw.Value;
    }

    private static bool IsBackgroundInitial(string prop, string val) => prop switch
    {
        "background-image" => val == "none",
        "background-repeat" => val == "repeat",
        "background-attachment" => val == "scroll",
        "background-position" => val == "0% 0%",
        "background-color" => val == "transparent",
        _ => false,
    };

    /// <summary>Splits a CSS declaration block on top-level semicolons, ignoring ';' inside
    /// parentheses (such as <c>url(data:image/png;base64,...)</c>) and quoted strings (CSS 2.1 §4.1.1).</summary>
    private static List<string> SplitCssDeclarations(string cssText)
    {
        var result = new List<string>();
        var depth = 0;
        var quote = '\0';
        var start = 0;
        for (var i = 0; i <= cssText.Length; i++)
        {
            if (i < cssText.Length)
            {
                var c = cssText[i];
                if (c == '\\') { i++; continue; }
                if (quote != '\0') { if (c == quote) quote = '\0'; continue; }
                if (c is '"' or '\'') { quote = c; continue; }
                if (c == '(') { depth++; continue; }
                if (c == ')') { depth = Math.Max(0, depth - 1); continue; }
                if (c != ';' || depth != 0) continue;
            }
            if (i > start)
            {
                var part = cssText[start..i];
                if (!string.IsNullOrWhiteSpace(part)) result.Add(part);
            }
            start = i + 1;
        }
        return result;
    }

    private static void CollectRulesFromSheet(ICssRuleList rules, RuleOrigin origin = RuleOrigin.Author)
    {
        foreach (var rule in rules)
        {
            if (rule is ICssMediaRule mediaRule)
            {
                CollectRulesFromSheet(mediaRule.Rules, origin);
                continue;
            }
            if (rule is not ICssStyleRule styleRule) continue;

            if (string.IsNullOrEmpty(styleRule.SelectorText)) continue;

            var (props, important) = ParseDeclarations(styleRule.Style.CssText);
            // The CSSOM serializes a 'font' shorthand with spaces around the size/line-height
            // slash ("font: 20px / 1 Ahem") and AngleSharp's cascade exposes no line-height
            // longhand for it, so extract the component from the shorthand text here (§15.8) —
            // without it every 'font: x/y' rule laid out at 'normal' leading.
            if (!props.ContainsKey("line-height") &&
                FontDeclarationValue(styleRule.Style.CssText) is { } fontValue &&
                LineHeightComponentOf(fontValue) is { } fontLh)
                props["line-height"] = fontLh;
            // A `background` AngleSharp refused to parse never reaches CssText, so it is merged in
            // from the stylesheet source before the empty-rule check — a rule whose only
            // declaration is such a shorthand would otherwise be dropped outright.
            if (RescuedBackgroundFor(styleRule) is { } rawBg &&
                SplitBackgroundShorthand(rawBg) is { } rawLonghands)
            {
                foreach (var (prop, val) in rawLonghands)
                    if (val is not null) props[prop] = val;
            }
            if (props.Count == 0) continue;

            // A selector list ("a, b") gets one entry per selector so each keeps its own specificity.
            foreach (var sel in SplitSelectorList(styleRule.SelectorText))
            {
                var s = sel.Trim();
                if (s.Length == 0) continue;
                // A pseudo-ELEMENT rule styles a generated/partial box, never the element itself:
                // `div:first-letter { color: green }` must not turn the whole div green. Those rules
                // are captured separately into Before/After/FirstLetter/FirstLineStyles by
                // TryExtractPseudoElementRule. Pseudo-CLASSES (:hover, :first-child) are unaffected.
                if (IsPseudoElementSelector(s)) continue;
                CssRules.Add(new CssRule(s, ComputeSpecificity(s), CssRules.Count, props, important, origin));
            }
        }
    }

    /// <summary>
    /// Pseudo-element rules lifted out of the stylesheets before the cascade runs, kept so
    /// <see cref="TryExtractPseudoElementRule"/> can still see their original selectors.
    /// </summary>
    private static List<(string Selector, ICssStyleDeclaration Style)> _pseudoElementRules => Current.PseudoElementRules;

    /// <summary>
    /// Removes pseudo-element rules from the CSSOM so they cannot style the originating element.
    /// AngleSharp matches <c>div:first-letter</c> against the DIV itself, so a rule meant for the
    /// first letter turned the WHOLE element green — and nothing downstream can undo that, because
    /// by then it is indistinguishable from an authored declaration. The rules are recorded first
    /// and re-applied per element as ::before/::after/::first-letter/::first-line styles.
    /// The selector is rewritten (rather than the rule deleted) to keep every other index stable.
    /// </summary>
    private static void NeutralizePseudoElementRules(AngleSharp.Dom.IDocument document)
    {
        _pseudoElementRules.Clear();
        if (document.StyleSheets is null) return;
        foreach (var sheet in document.StyleSheets.OfType<ICssStyleSheet>())
            NeutralizePseudoElementRules(sheet.Rules);
    }

    private static void NeutralizePseudoElementRules(ICssRuleList rules)
    {
        foreach (var rule in rules)
        {
            if (rule is ICssMediaRule mediaRule) { NeutralizePseudoElementRules(mediaRule.Rules); continue; }
            if (rule is not ICssStyleRule styleRule) continue;
            var selector = styleRule.SelectorText;
            if (string.IsNullOrEmpty(selector) || !IsPseudoElementSelector(selector)) continue;

            _pseudoElementRules.Add((selector, styleRule.Style));
            // A tag name that cannot occur in an HTML document, so the cascade never matches it.
            try { styleRule.SelectorText = "lite-neutralized-pseudo-element"; }
            catch { /* read-only CSSOM: leave it; the element-level leak stays for this rule */ }
        }
    }

    /// <summary>
    /// True when a selector targets a pseudo-ELEMENT (::before, ::after, ::first-letter,
    /// ::first-line, including their legacy single-colon spellings). Deliberately does NOT match
    /// pseudo-classes such as <c>:first-child</c>, which merely share a prefix with
    /// <c>:first-letter</c>.
    /// </summary>
    private static bool IsPseudoElementSelector(string selector)
    {
        if (selector.Contains("::", StringComparison.Ordinal)) return true;
        foreach (var legacy in new[] { ":before", ":after", ":first-letter", ":first-line" })
        {
            var idx = selector.IndexOf(legacy, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) continue;
            // Must be the whole token: ":first-letter" matches, ":first-letters" does not.
            var end = idx + legacy.Length;
            if (end == selector.Length || !char.IsLetterOrDigit(selector[end]) && selector[end] != '-')
                return true;
        }
        return false;
    }

    /// <summary>Parses a declaration block, separating normal and !important declarations.
    /// §4.2 parsing errors: a declaration whose value is illegal for the property is ignored
    /// here, so an earlier valid declaration of the same property survives the cascade
    /// ('width: 0; width: -1px' computes to 0, not auto).</summary>
    internal static (Dictionary<string, string> Props, HashSet<string> Important) ParseDeclarations(string cssText)
    {
        var props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var important = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(cssText)) return (props, important);

        foreach (var decl in SplitCssDeclarations(cssText))
        {
            var colon = decl.IndexOf(':');
            if (colon < 0) continue;
            var prop = decl[..colon].Trim().ToLowerInvariant();
            var raw = decl[(colon + 1)..].Trim();
            var importantCandidate = false;
            // "!important" may be written with whitespace after the bang ("! important"), which
            // is what the CSS 2.1 test suite does — matching the literal token alone missed it.
            if (raw.EndsWith("important", StringComparison.OrdinalIgnoreCase))
            {
                var head = raw[..^"important".Length].TrimEnd();
                if (head.EndsWith('!'))
                {
                    raw = head[..^1].TrimEnd();
                    importantCandidate = true;
                }
            }
            if (string.IsNullOrEmpty(raw) || IsIllegalPropertyValue(prop, raw)) continue;
            // A css-wide keyword on a SHORTHAND sets every longhand to it (§6.2.1: 'inherit'
            // is valid as the whole value of any property, shorthand included). Without the
            // expansion the keyword sits on a shorthand the keyword resolver cannot see
            // (PropertyTable tracks longhands), and the suite's "* { font: inherit }" reset
            // rules then leak the raw keyword into the font reads — font-family resolved to
            // the provisioned 'inherit' test font (white-space-normal-003's instruction
            // paragraph rendered in it), font-size/weight stayed unset.
            if (ShorthandLonghands.TryGetValue(prop, out var longhands) &&
                raw.Trim() is "inherit" or "initial" or "unset")
            {
                foreach (var longhand in longhands)
                {
                    props[longhand] = raw;
                    if (importantCandidate) important.Add(longhand);
                }
                continue;
            }
            props[prop] = raw;
            if (importantCandidate) important.Add(prop);
        }
        return (props, important);
    }

    /// <summary>CSS 2.1's shorthands and the PropertyTable longhands each expands to when its
    /// value is a css-wide keyword. Longhands the engine does not track are omitted — the
    /// keyword resolver would drop them anyway.</summary>
    private static readonly Dictionary<string, string[]> ShorthandLonghands = new(StringComparer.OrdinalIgnoreCase)
    {
        ["font"] = new[] { "font-family", "font-size", "font-variant", "font-weight", "font-style", "line-height" },
        ["background"] = new[] { "background-color", "background-image", "background-attachment", "background-position", "background-repeat" },
        ["list-style"] = new[] { "list-style-type", "list-style-position", "list-style-image" },
        ["margin"] = new[] { "margin-top", "margin-right", "margin-bottom", "margin-left" },
        ["padding"] = new[] { "padding-top", "padding-right", "padding-bottom", "padding-left" },
        ["border"] = new[]
        {
            "border-top-width", "border-right-width", "border-bottom-width", "border-left-width",
            "border-top-style", "border-right-style", "border-bottom-style", "border-left-style",
            "border-top-color", "border-right-color", "border-bottom-color", "border-left-color",
        },
        ["border-width"] = new[] { "border-top-width", "border-right-width", "border-bottom-width", "border-left-width" },
        ["border-style"] = new[] { "border-top-style", "border-right-style", "border-bottom-style", "border-left-style" },
        ["border-color"] = new[] { "border-top-color", "border-right-color", "border-bottom-color", "border-left-color" },
        ["border-top"] = new[] { "border-top-width", "border-top-style", "border-top-color" },
        ["border-right"] = new[] { "border-right-width", "border-right-style", "border-right-color" },
        ["border-bottom"] = new[] { "border-bottom-width", "border-bottom-style", "border-bottom-color" },
        ["border-left"] = new[] { "border-left-width", "border-left-style", "border-left-color" },
        ["outline"] = new[] { "outline-color", "outline-style", "outline-width" },
    };

    // Properties whose value grammar forbids negative lengths in CSS 2.1: width and
    // min/max-width (§10.4), height and min/max-height (§10.7), padding (§8.4),
    // border widths (§8.5.1), font-size (§15.7), line-height (§10.8.1), border-spacing
    // (§17.6.1) and outline-width (§18.4). A negative value is a parse error, not a
    // clamped computation.
    private static readonly HashSet<string> NonNegativeProperties = new(StringComparer.Ordinal)
    {
        "width", "min-width", "max-width",
        "height", "min-height", "max-height",
        "padding", "padding-top", "padding-right", "padding-bottom", "padding-left",
        "border-width", "border-top-width", "border-right-width", "border-bottom-width", "border-left-width",
        "outline-width", "font-size", "line-height", "border-spacing",
    };

    /// <summary>True when a value carries a negative number (<c>-1px</c>, <c>-10%</c>, or a
    /// bare negative <c>line-height</c>). <c>-0</c> equals 0 and stays legal; non-numeric
    /// leading dashes (vendor junk) are not lengths.</summary>
    internal static bool ContainsNegativeNumber(string value)
    {
        foreach (var token in value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Length < 2 || token[0] != '-') continue;
            var end = 1;
            while (end < token.Length && (char.IsDigit(token[end]) || token[end] == '.')) end++;
            if (end > 1 &&
                double.TryParse(token[1..end], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var number) &&
                number > 0)
                return true;
        }
        return false;
    }

    private static bool IsIllegalPropertyValue(string prop, string value) =>
        NonNegativeProperties.Contains(prop) && ContainsNegativeNumber(value);

    /// <summary>
    /// Computes a CSS 2.1 §6.4.3 specificity score ((a &lt;&lt; 16) | (b &lt;&lt; 8) | c) for a
    /// compound/complex selector. Single-colon CSS 2.1 pseudo-elements (:before, :after,
    /// :first-letter, :first-line) count in c (element/pseudo-element), not b (pseudo-class).
    /// </summary>
    internal static int ComputeSpecificity(string selector)
    {
        int a = 0, b = 0, c = 0;
        int i = 0;
        while (i < selector.Length)
        {
            var ch = selector[i];
            if (ch == '#') { a++; i++; SkipIdent(selector, ref i); }
            else if (ch == '.' || ch == '[') { b++; i++; if (ch == '[') SkipTo(selector, ref i, ']'); else SkipIdent(selector, ref i); }
            else if (ch == ':')
            {
                i++;
                if (i < selector.Length && selector[i] == ':')
                {
                    c++;
                    i++;
                    SkipIdent(selector, ref i);
                }
                else
                {
                    var nameStart = i;
                    SkipIdent(selector, ref i);
                    var name = selector.AsSpan(nameStart, i - nameStart);
                    if (name.Equals("before", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("after", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("first-letter", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("first-line", StringComparison.OrdinalIgnoreCase))
                    {
                        c++;
                    }
                    else
                    {
                        b++;
                        if (i < selector.Length && selector[i] == '(') SkipTo(selector, ref i, ')');
                    }
                }
            }
            else if (char.IsLetter(ch)) { c++; SkipIdent(selector, ref i); } // type selector
            else i++; // combinators, '*', whitespace
        }
        return (Math.Min(a, 255) << 16) | (Math.Min(b, 255) << 8) | Math.Min(c, 255);
    }

    private static void SkipIdent(string s, ref int i)
    {
        while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '-' || s[i] == '_')) i++;
    }

    private static void SkipTo(string s, ref int i, char end)
    {
        while (i < s.Length && s[i] != end) i++;
        if (i < s.Length) i++;
    }

    private static IEnumerable<string> SplitSelectorList(string selectorText)
    {
        if (string.IsNullOrEmpty(selectorText)) yield break;
        int depth = 0, start = 0;
        for (int i = 0; i < selectorText.Length; i++)
        {
            if (selectorText[i] == '(') depth++;
            else if (selectorText[i] == ')') depth--;
            else if (selectorText[i] == ',' && depth == 0)
            {
                yield return selectorText[start..i];
                start = i + 1;
            }
        }
        yield return selectorText[start..];
    }
}
