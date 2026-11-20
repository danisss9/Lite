# 6.K Selectors, cascade and syntax

Branch `css21/p6-k`, worktree `C:\Repos\Lite-p6-k`. Case list `Lite.Conformance/artifacts/lanes/K.txt`. The [README](README.md) has the rules every lane follows.

## Owned files

- Lite/Parser.Cascade.cs
- Lite/Layout/StyleResolver.cs
- Lite/Layout/PropertyTable.cs
- Lite.AngleSharp.Css/** (new fork patches take the next number in its README, with a regression test that fails on the pristine source)

## Opens with (parked findings)

- The 4-digit hex `#1000`, which CSS 2.1 must reject (`color-030`, `outline-color-030`).
- `default-attribute-selector`: a styled `<input>` keeps its UA chrome.
- `character-encoding-017/018/028`: the encoding-dependent selector never matches.

## Coordination

- `Parser.cs` (core) is shared with the HTML5 track through `JsDocument` and the DOM projection. Name any edit there in your notes, and say that the HTML5 gate was not rerun.

## Handed in

_(Other lanes add cases here that belong to this lane, one line each: case, cause, from lane.)_

## Edits outside owned files

_(None yet.)_

## Progress

### 1. Escaped newlines in strings and EOF construct closure (AngleSharp.Css patches 008, 009)
- **Spec:** CSS 2.1 §4.3.7 / §4.1.3 (escaped newlines removed from strings) and §4.2 ("Unexpected end of style sheet": close open constructs at EOF).
- **Moved (6 cases: +3 official, +3 WPT):**
  - `css21-official/html4/eof-002.htm`, `css/CSS2/syntax/eof-002.xht`
  - `css21-official/html4/escapes-000.htm`, `css/CSS2/syntax/escapes-000.xht`
  - `css21-official/html4/uri-017.htm`, `css/CSS2/syntax/uri-017.xht`
- **Still failing (related):** `eof-005`, `eof-006`, `uri-018` (`@import` at EOF without trailing `;`), `escaped-newline-001` (hex escape `\00000a` followed by newline).
- **Handed to other lanes:** None.
- **Checks:**
  - Units: 628 passed, 21 failed (all 21 are sandbox `127.0.0.1` loopback socket permission errors; all non-socket units including new `EscapedNewlineInString_IsRemoved` and `EndOfSheet_ClosesOpenConstructs` passed).
  - Curated gates: `css21` 51/0/1 xfail, `wpt` 86/0/2 xfail.
  - Lane K: 3/62 official, 3/89 WPT (from 0/62, 0/89).
  - Canary: 300/300 (154/154 official, 146/146 WPT).
