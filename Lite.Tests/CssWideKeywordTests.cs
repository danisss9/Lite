using Lite;
using Lite.Extensions;
using Lite.Layout;
using Lite.Models;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>Phase 3 (#12) — initial / inherit / unset cascade-wide keywords for non-parsed styles.</summary>
public static class CssWideKeywordTests
{
    private static readonly ICssStyleDeclarationProvider _style = new();
    private sealed class ICssStyleDeclarationProvider
    {
        public AngleSharp.Css.Dom.ICssStyleDeclaration S { get; } = Parser.ParseFragment("<div></div>")[0].Style;
    }

    private static (LayoutNode parent, LayoutNode child) Pair()
    {
        var parent = new LayoutNode(null, "DIV", "", _style.S);
        var child = new LayoutNode(null, "SPAN", "", _style.S);
        parent.AddChild(child);
        return (parent, child);
    }

    [Test]
    public static void Initial_ResolvesToPropertyInitialValue()
    {
        var (_, child) = Pair();
        child.StyleOverrides["color"] = "initial";
        StyleResolver.ResolveCssWideKeyword(child, "color");
        Equal("black", child.StyleOverrides.GetValueOrDefault("color"));
    }

    [Test]
    public static void Inherit_TakesParentValue()
    {
        var (parent, child) = Pair();
        parent.StyleOverrides["color"] = "purple";
        child.StyleOverrides["color"] = "inherit";
        StyleResolver.ResolveCssWideKeyword(child, "color");
        Equal("purple", child.StyleOverrides.GetValueOrDefault("color"));
    }

    [Test]
    public static void Unset_InheritedProperty_BehavesAsInherit()
    {
        var (parent, child) = Pair();
        parent.StyleOverrides["color"] = "green";
        child.StyleOverrides["color"] = "unset";       // color inherits → take parent
        StyleResolver.ResolveCssWideKeyword(child, "color");
        Equal("green", child.StyleOverrides.GetValueOrDefault("color"));
    }

    [Test]
    public static void Unset_NonInheritedProperty_BehavesAsInitial()
    {
        var (parent, child) = Pair();
        parent.StyleOverrides["display"] = "flex";
        child.StyleOverrides["display"] = "unset";     // display does NOT inherit → initial (inline)
        StyleResolver.ResolveCssWideKeyword(child, "display");
        Equal("inline", child.StyleOverrides.GetValueOrDefault("display"));
    }

    [Test]
    public static void ListStyleImage_ParsesUrlFromPropertyAndShorthand()
    {
        var (_, a) = Pair();
        a.StyleOverrides["list-style-image"] = "url('http://x/bullet.png')";
        Equal("http://x/bullet.png", a.GetListStyleImage());

        var (_, b) = Pair();
        b.StyleOverrides["list-style"] = "square url(\"http://x/dot.gif\") inside";
        Equal("http://x/dot.gif", b.GetListStyleImage());
    }

    [Test]
    public static void StylesheetEncoding_FollowsTransportThenBomAndCharset()
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        // CSS 2.1 §4.4. Decoding every sheet as UTF-8 both mangles one in a legacy encoding and
        // leaves the U+FEFF of a BOM at the front of the text, which breaks its first selector.
        var utf8 = System.Text.Encoding.UTF8;

        var bom = utf8.GetPreamble().Concat(utf8.GetBytes("#a { color: green }")).ToArray();
        Equal("#a { color: green }", Parser.DecodeCss(bom, null, null, null, out var used1));
        Equal("utf-8", used1);

        // A legacy @charset applies when transport metadata is absent.
        var declared = "@charset \"shift-JIS\";\n.\u5e73\u548c { color: green }";
        var sjis = System.Text.Encoding.GetEncoding("shift-jis").GetBytes(declared);
        True(Parser.DecodeCss(sjis, null, null, null, out _).Contains("\u5e73\u548c"),
            "an @charset-declared encoding must decode legacy text");
        Equal(utf8.GetString(sjis), Parser.DecodeCss(sjis, "utf-8", null, null, out var transport));
        Equal("utf-8", transport);
        Equal(System.Text.Encoding.GetEncoding("iso-8859-5").GetString(bom),
            Parser.DecodeCss(bom, "iso-8859-5", null, null, out _));
        Equal("#a { color: green }", Parser.DecodeCss(bom, "utf-8", null, null, out _));
        // A debugger stops on first-chance ArgumentException if the resolver probes
        // UTF_8 before trying UTF-8, even though decoding eventually succeeds.
        var unsupportedUtf8Probes = 0;
        EventHandler<System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs> onFirstChance = (_, e) =>
        {
            if (e.Exception is ArgumentException argument &&
                argument.Message.Contains("UTF_8", StringComparison.OrdinalIgnoreCase))
                unsupportedUtf8Probes++;
        };
        AppDomain.CurrentDomain.FirstChanceException += onFirstChance;
        try
        {
            Equal("#a { color: green }", Parser.DecodeCss(bom, "UTF-8", null, null, out _));
            Equal("#a { color: green }", Parser.DecodeCss(bom, "utf-8", null, null, out _));
        }
        finally { AppDomain.CurrentDomain.FirstChanceException -= onFirstChance; }
        Equal(0, unsupportedUtf8Probes);

        // With neither, the linking element's charset attribute is consulted before the fallback.
        var plain = System.Text.Encoding.GetEncoding("shift-jis").GetBytes(".\u5e73\u548c { color: green }");
        True(Parser.DecodeCss(plain, null, "shift-JIS", null, out _).Contains("\u5e73\u548c"),
            "the link element's charset attribute must be honoured");
        True(Parser.DecodeCss(plain, null, null, "shift-JIS", out _).Contains("\u5e73\u548c"),
            "and the referring document's encoding after it");
    }

    [Test]
    public static void StylesheetEncoding_DetectsUtf16CharsetWithoutBom()
    {
        // CSS 2.1 §4.4: a BOM-less sheet whose bytes spell @charset in UTF-16 is decoded as
        // UTF-16 in the detected endianness, but only when the declaration names utf-16.
        var be = System.Text.Encoding.BigEndianUnicode.GetBytes("@charset \"utf-16\"; .p\u5e73 { color: green }");
        True(Parser.DecodeCss(be, null, null, null, out var usedBe).Contains("\u5e73"),
            "UTF-16BE without a BOM must decode through the NUL-interleaved @charset");
        Equal("utf-16be", usedBe);
        var le = System.Text.Encoding.Unicode.GetBytes("@charset \"UTF-16\"; .p\u5e73 { color: green }");
        True(Parser.DecodeCss(le, null, null, null, out var usedLe).Contains("\u5e73"),
            "UTF-16LE without a BOM must decode through the NUL-interleaved @charset");
        Equal("utf-16le", usedLe);
        // A non-utf-16 declaration inside UTF-16 bytes decodes as UTF-8 per the spec, which
        // mangles it — the observed bytes, not a silently recovered text.
        var misdeclared = System.Text.Encoding.BigEndianUnicode.GetBytes("@charset \"shift-JIS\"; .p { color: green }");
        True(!Parser.DecodeCss(misdeclared, null, null, null, out var usedOther).Contains("\u5e73"),
            "a non-utf-16 declaration must not keep the UTF-16 decode");
        Equal("utf-8", usedOther);
    }
}
