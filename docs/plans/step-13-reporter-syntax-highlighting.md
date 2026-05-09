# Step 13 — Reporter syntax highlighting (JSON / XML)

## Goal

Render JSON and XML content in `UnifiedDiffReporter` and `SideBySideDiffReporter`
with token-aware foreground colors (keys, strings, numbers, keywords, element
names, attribute names/values, comments, entity refs). Preserve every existing
visual contract — banner, gutter, row backgrounds for added/removed,
inline-highlight overlay, dim context — and lay token coloring over the top.

Highlighting is on by default and adds zero output bytes when `useColor=false`,
so existing `TextWriter` golden tests stay green untouched.

## Non-goals

- `SimpleReporter` (one-liner `path: old → new`) and the legacy
  `ConsoleDiffReporter` (whole-line FG red/green) are out of scope.
- Multi-line JSON strings (`"…\n…"`) and multi-line XML CDATA bodies are
  rendered best-effort: the lexer is per-line stateless, so continuation
  lines fall back to default foreground. `library.sample.xml` contains a
  CDATA body that spans three lines (book `b001`); after step 13 lines 2–3
  of that body render as default fg, which is acceptable.
- No public, user-configurable palette and no light/dark theme. The single
  Claude Code-style dark palette is `internal`, leaving room for a future
  non-breaking `Palette` option.

## Prerequisite refactor (separate commit, before step 13 work)

`UnifiedDiffOptions` and `SideBySideDiffOptions` are field-for-field
identical today, and `SideBySideDiffReporter.RenderTo` literally
re-packages one into the other before calling `DiffHunkBuilder.Build`
(see lines 70–75). Adding `SyntaxHighlight` to both would only deepen
the duplication.

**Prep step (committed separately, no new feature):**

- Replace both records with one `public sealed record DiffReporterOptions`
  carrying the existing `ContextLines`, `InlineHighlight`, `ShowFullFile`
  fields and the same defaults.
- Both reporters' `Print(...)` overloads accept `DiffReporterOptions`.
- `DiffHunkBuilder.Build` accepts `DiffReporterOptions` directly; the
  re-packaging block in `SideBySideDiffReporter` is deleted.
- All call sites (`Program.cs`, every `tests/...` reference) migrate by
  rename. No external callers exist.

After the prep commit, step 13 adds `SyntaxHighlight` to a single record.

## Public API surface

```csharp
public sealed record DiffReporterOptions
{
    public int  ContextLines    { get; init; } = 3;
    public bool InlineHighlight { get; init; } = true;
    public bool SyntaxHighlight { get; init; } = true;   // NEW (step 13)
    public bool ShowFullFile    { get; init; } = false;
}
```

No new public types beyond `DiffReporterOptions`. Both reporters keep
their existing `Print` overload shapes; only the options-record type
name changes.

## Architecture (Approach A — paint at render time)

```
Structura.Reporting
├── UnifiedDiffReporter.cs           (resolve painter, pass to renderer)
├── SideBySideDiffReporter.cs        (resolve painter, pass to renderer)
├── DiffReporterOptions.cs           (+ SyntaxHighlight; merged in prep)
└── Internal/
    ├── AnsiPalette.cs               (unchanged)
    ├── DiffLineRenderer.cs          (accepts IDiffSyntaxPainter)
    ├── SideBySideRowRenderer.cs     (accepts IDiffSyntaxPainter)
    └── Highlighting/
        ├── TokenKind.cs             (enum)
        ├── TokenRange.cs            (record struct)
        ├── IDiffSyntaxPainter.cs    (interface)
        ├── NullPainter.cs           (returns Array.Empty<TokenRange>())
        ├── JsonLinePainter.cs
        ├── XmlLinePainter.cs
        ├── PainterFactory.cs        (interface check → sniff → Null)
        └── SyntaxPalette.cs         (TokenKind → BrightFg / DimFg)
```

`DiffHunkBuilder` and `DiffLine` do not change. The painter is plumbed only
through the two renderers. This keeps step 13 fully additive: removing the
plumbing would restore step 12 byte-for-byte.

## Token model

```csharp
internal enum TokenKind
{
    Punctuation,    // { } [ ] , : in JSON; < > /> </ ?> = in XML — no fg
    Key,            // JSON object key (string before ':')
    String,         // JSON string value (string not before ':')
    Number,         // JSON numeric literal
    Keyword,        // JSON true/false/null
    ElementName,    // XML <foo> / <ns:foo> (full name, including prefix)
    AttrName,       // XML attribute name (full name, including prefix)
    AttrValue,      // XML attribute value (including its quotes)
    Comment,        // XML <!-- ... --> body and delimiters
    EntityRef,      // XML &amp; &#xNN; etc.
    Text,           // XML text-node content — no fg (default)
}

internal readonly record struct TokenRange(ColumnRange Range, TokenKind Kind);

internal interface IDiffSyntaxPainter
{
    /// <summary>
    /// Tokenize a single line. Returned ranges are non-overlapping, sorted
    /// by Start, and form a complete cover of [0, content.Length) — every
    /// column belongs to exactly one token. Empty content returns an empty
    /// list. Painters MUST be stateless across calls.
    /// </summary>
    IReadOnlyList<TokenRange> TokenizeLine(string content);
}
```

`Punctuation` and `Text` tokens are emitted but mapped to "no fg" in the
palette. They exist so that the renderer's walker can stay branch-free and
operate on a complete cover of the line.

Half-open `[Start, Start+Length)` semantics match the existing `ColumnRange`
used by inline highlights, so the renderer's interleaving logic does not
need a second variant.

## Painter selection

```csharp
internal static class PainterFactory
{
    public static IDiffSyntaxPainter For(IStructuraDocument doc, bool syntaxOn)
    {
        if (!syntaxOn)
        {
            return NullPainter.Instance;
        }
        if (doc is IStructuraJsonDocument)
        {
            return JsonLinePainter.Instance;
        }
        if (doc is IStructuraXmlDocument)
        {
            return XmlLinePainter.Instance;
        }
        return SniffByFirstChar(doc.OriginalText);
    }
}
```

`SniffByFirstChar` skips Unicode whitespace, returns `JsonLinePainter` for
`{` or `[`, `XmlLinePainter` for `<`, `NullPainter` otherwise. The sniff is
the fallback path; in the current codebase every document implements one of
the two interfaces, so the sniff is reached only by future formats or
test doubles.

Both reporter `RenderTo` methods resolve the painter once at the top of the
method, before any line is emitted, and pass it down. `useColor=false`
short-circuits to `NullPainter` regardless of `SyntaxHighlight`.

## Per-line lexers

Both lexers are pure functions over a `string` line, allocate a
`List<TokenRange>` they return as `IReadOnlyList<TokenRange>`, and emit a
complete cover of the line — every column index belongs to exactly one
token. Lexers never throw on malformed input; they best-effort tokenize
what they can and emit `Punctuation`/`Text` for the remainder.

### `JsonLinePainter`

Walk left-to-right. For each character:
- `"` opens a string. Read until the closing `"`, honoring `\"` escape. If
  the line ends with the string still open, emit one `String` token for
  what was scanned and stop. The classification step below decides whether
  it is a `Key` or a `String`.
- After a closing `"`, scan forward over whitespace (and only whitespace)
  to decide: the next non-whitespace `:` makes the just-emitted string a
  `Key`; anything else (including end-of-line without finding a colon)
  makes it a `String`. This rule is correct for well-formed JSON; for
  malformed input it errs on the side of `String`.
- A digit, `-`, or `+` opens a `Number`. Read the longest `-?\d+(\.\d+)?
  ([eE][+-]?\d+)?`. (`+` cannot legally start a JSON number, but if the
  user typed one we still consume it — display only.)
- Bare identifiers `true`, `false`, `null` (lowercase only, full word)
  produce a `Keyword`.
- `{`, `}`, `[`, `]`, `,`, `:` produce `Punctuation`.
- Everything else (whitespace, stray bytes) produces `Punctuation` so the
  cover stays complete.

### `XmlLinePainter`

Three modes — outside-tag, inside-tag, inside-comment — implemented as a
small state machine local to the function (state does NOT carry across
lines).

Outside-tag:
- `&...;` until next `;` or whitespace → `EntityRef`.
- `<!--` opens comment mode. Scan until `-->` or end of line. Emit one
  `Comment` token covering both delimiters and the body.
- `<![CDATA[` and `]]>` are emitted as `Punctuation`; the body between
  them on the same line is `Text`. If the closing `]]>` is on a later
  line, the lexer emits `Punctuation` for `<![CDATA[` and `Text` for the
  rest of the line — multi-line CDATA loses coloring on continuation
  lines (acceptable per non-goals).
- `<` enters inside-tag mode. The character itself, plus any of `</` or
  `<?`, is `Punctuation`.
- Anything else is `Text`.

Inside-tag:
- The first identifier (any run of characters that are not whitespace,
  `>`, `/`, `=`, `?`, or quotes; loosely `[A-Za-z_][A-Za-z_0-9.\-]*`
  for ASCII names but also accepts Unicode letters so non-ASCII element
  names tokenize too), optionally followed by `:` and another such
  identifier, is `ElementName`.
- Subsequent `name="value"` and `name='value'` patterns are
  `AttrName` + `=` (`Punctuation`) + `AttrValue` (quotes included). For
  `xmlns:foo`, the full `xmlns:foo` is one `AttrName` token.
- `>`, `/>`, `?>` close the tag and emit `Punctuation`.
- Stray characters are `Punctuation`.

Inside-comment: emit one `Comment` token, no further analysis.

The lexer recognizes `<!DOCTYPE …>` and `<?xml …?>` as a single
`Punctuation` block followed by inside-tag handling for the remainder
of the line — close enough for the diff renderer.

## Palette

`SyntaxPalette` exposes two static lookups: `Bright(TokenKind)` and
`Dim(TokenKind)`, each returning either a 256-color SGR escape (e.g.
`"\x1b[38;5;81m"`) or `string.Empty` for "use default fg".

| TokenKind | Bright (changed rows) | Dim (context rows) |
|---|---|---|
| `Key`, `ElementName` | `38;5;81`  cyan      | `38;5;67`  desat-cyan |
| `String`, `AttrValue` | `38;5;180` warm yellow | `38;5;144` muted yellow |
| `Number`               | `38;5;176` mauve       | `38;5;103` muted mauve  |
| `Keyword`, `EntityRef` | `38;5;215` orange      | `38;5;137` muted orange |
| `AttrName`             | `38;5;150` sage        | `38;5;108` muted sage   |
| `Comment`              | `38;5;245` grey        | `38;5;240` darker grey  |
| `Punctuation`, `Text`  | default fg (`""`)      | default fg (`""`)       |

Concrete codes are tunable during implementation — the spec requires the
**bright row** code to remain legible on row backgrounds `48;5;52`
(red) and `48;5;22` (green) AND on inline-highlight backgrounds
`48;5;124` and `48;5;34`. Implementer must eyeball the `Program.cs`
output across all four samples (`order`, `library` JSON, `blrwbl`,
`library` XML) before merging.

## Renderer integration

### `DiffLineRenderer.Render` — new signature

```csharp
public static string Render(
    DiffLine line,
    int gutterWidth,
    bool useColor,
    bool useUnicode,
    IDiffSyntaxPainter painter)
```

Behavior changes only when `useColor && line.Kind != HunkSeparator`:

1. Resolve `tokens = painter.TokenizeLine(line.Content)` (cheap when
   painter is `NullPainter` — returns the singleton empty array).
2. Build the existing prologue (row-bg + sigil-fg + gutter + sigil +
   `FgDefault` + space) for changed lines. For context lines, build
   the existing `Dim … DimOff` gutter prologue.
3. Walk content with a single coalesced loop that owns three concerns:
   the inline-highlight ranges (already present), the token ranges
   (new), and the row-bg vs highlight-bg switching.
4. Emit at most one ANSI escape per boundary: when the active token
   kind changes, emit `FgDefault` + the new token's `Bright(kind)`
   (or just `FgDefault` if the new token is `Punctuation`/`Text`);
   when entering an inline-highlight range, emit `highlightBg` + `Bold`;
   when leaving, emit `BoldOff` + `rowBg`.
5. Final reset: `FgDefault` + `BgDefault`.

For context lines (`DiffLineKind.Context`), the renderer:
- keeps the existing dim prologue for gutter/sigil;
- after the prologue, walks content using `Dim(kind)` instead of
  `Bright(kind)`. The outer `Dim` SGR-2 on the prologue is closed before
  content; tokens manage their own fg, so the desaturated 256-color codes
  carry the muted look without relying on SGR-2. Tokens classified as
  default-fg fall back to `FgDefault` so the line content does not get
  the previous dim wrapping.

The walker is symmetric in both renderers, so it lives in a small
private helper `TokenAwareContentWriter` (file-internal to each renderer
for now; promote to `Internal/Highlighting/` if we add a third reporter).

### `SideBySideRowRenderer.Render` — new signature

```csharp
public static string Render(
    SideBySideRow row,
    int gutterWidth,
    int contentWidth,
    bool useColor,
    bool useUnicode,
    IDiffSyntaxPainter painter)
```

The row's two cells share the same painter. `RenderCell` calls
`painter.TokenizeLine(line.Content)` once per cell and clips token
ranges to `[0, t.VisibleContentLength)` exactly the way inline highlights
are clipped today (token ranges that cross the truncation marker are
trimmed; ranges past the marker are dropped). The truncation indicator
(`…` / `>`) is emitted at default fg.

### `UnifiedDiffReporter.RenderTo` — wiring

```csharp
IDiffSyntaxPainter painter = useColor
    ? PainterFactory.For(document, options.SyntaxHighlight)
    : NullPainter.Instance;

DiffBanner.Write(...);
foreach (DiffLine line in lines)
{
    string rendered = DiffLineRenderer.Render(line, gutterWidth, useColor, useUnicode, painter);
    writer.WriteLine(rendered);
}
```

The shortcut `useColor ? … : NullPainter.Instance` ensures plain-text
mode never even calls `PainterFactory`, so a missing/buggy lexer cannot
regress golden tests. `SideBySideDiffReporter.RenderTo` mirrors the same
two-line wiring.

## Plain-text fallback

`useColor=false` ⇒ painter is `NullPainter` ⇒ token list is empty ⇒
renderer's coalesced walker degenerates to "emit content verbatim".
Output bytes are byte-equal to step 12.

The two `Print(IStructuraDocument, TextWriter)` and
`Print(IStructuraDocument, TextWriter, options)` overloads remain
hard-coded to `useColor: false`, so all existing `TextWriter` tests
(including `OrderSampleJsonSideBySideDiffTests` and the `UnifiedDiff`
golden tests) need no updates.

## Tests

New unit tests:

- `tests/Structura.UnitTests/Reporting/Highlighting/JsonLinePainterTests.cs`
  Table-driven cases:
  - `"order_id": "ORD"` → `Punctuation"`, `Key("order_id")`, `Punctuation"`,
    `Punctuation:`, `Punctuation `, `Punctuation"`, `String("ORD")`,
    `Punctuation"`.
  - `"version": 7` → `Number(7)` with correct span.
  - `"is_priority": true` → `Keyword(true)`.
  - Lone `null,` → `Keyword(null)` + `Punctuation,`.
  - Negative / scientific number `-1.5e+3`.
  - Stray characters → `Punctuation` cover.
  - Unterminated string at end of line → `String` covers tail.
  - Cover-completeness invariant: for every test input, `tokens` cover
    `[0, content.Length)` with no gaps and no overlaps.

- `tests/Structura.UnitTests/Reporting/Highlighting/XmlLinePainterTests.cs`
  Cases:
  - `<book id="b001" available="true">` → `<`, `book`, ` `, `id`, `=`,
    `"b001"`, ` `, `available`, `=`, `"true"`, `>` with the right kinds.
  - `<meta:info>` → `meta:info` is one `ElementName`.
  - Self-closing `<out-of-stock/>`.
  - Closing tag `</author>` → `</`, `author`, `>`.
  - Comment `<!-- hi -->` whole-line → single `Comment` token.
  - Open-only comment `<!-- hi` → `Comment` to end of line.
  - Entity ref `&amp;` mid-text → `Text`, `EntityRef`, `Text`.
  - CDATA single-line `<![CDATA[ x ]]>` → `Punctuation`, `Text`,
    `Punctuation`.
  - Multi-line CDATA opener `<![CDATA[ start` → `Punctuation`, `Text`.

- `tests/Structura.UnitTests/Reporting/Highlighting/PainterFactoryTests.cs`
  - `IStructuraJsonDocument` mock + `syntaxOn=true` → `JsonLinePainter`.
  - `IStructuraXmlDocument` mock + `syntaxOn=true` → `XmlLinePainter`.
  - `syntaxOn=false` → `NullPainter` regardless of doc type.
  - Plain `IStructuraDocument` with `OriginalText="  { …"` → `JsonLinePainter`.
  - Plain `IStructuraDocument` with `OriginalText="<?xml …"` → `XmlLinePainter`.
  - Empty `OriginalText` → `NullPainter`.

- Extend `DiffLineRendererTests`:
  - With `useColor=true`, painter producing one `Key` token over the
    whole content of an `Added` line, no inline highlights →
    expected escape sequence is `bgAdded sigilFgAdded gutter ' ' '+'
    FgDefault ' ' brightCyan content FgDefault ' ' bgDefault`.
  - Same setup with one inline-highlight range covering a sub-span of
    the `Key` → highlight bg + bold wraps the sub-span without
    dropping the cyan fg.
  - Context line with painter producing `String` over the whole content →
    expected sequence uses dim-yellow fg, no row bg.
  - With `useColor=false`, every painter is ignored — output equals
    pre-step-13 string.

- Extend `SideBySideDiffReporterColorTests` with one case verifying
  token coloring on both sides of a paired `Removed`/`Added` row, plus
  one case where truncation drops a token entirely.

`tests/Structura.IntegrationTests/Reporting/OrderSampleJsonSideBySideDiffTests.cs`
runs through `Print(doc, TextWriter)` (useColor=false) and stays green.
The existing color-mode unit test for side-by-side already covers
`useColor=true`; we only extend it.

## Acceptance demo

`Program.cs` is unchanged. Running `dotnet run --project src/Structura`
in an interactive terminal must show:

- `=== Diff (UnifiedDiffReporter) ===` for the order JSON: cyan keys,
  yellow string values, mauve numbers, orange `true`/`false`. Inline
  highlights still mark the changed sub-spans with bright bg + bold;
  token fg remains visible inside.
- `=== Library Diff (UnifiedDiffReporter) ===` for the XML library:
  cyan element names (including `meta:info`), sage attribute names,
  yellow attribute values, grey comments, orange entity refs.
- `=== ... (SideBySideDiffReporter) ===` for the same documents: same
  palette applied per cell, token coloring respects truncation.
- The plain-text overloads (`Print(doc, writer)`) print byte-equivalent
  output to step 12, verified by existing golden tests.

## Risks / open questions

- **Color legibility on row backgrounds.** Bright orange (`38;5;215`)
  on dark red (`48;5;52`) is the worst pairing; if it's unreadable in
  practice we swap to `38;5;220` or fall back to bold-only for
  `Keyword` on red rows. The implementer must validate this with
  eyeballs across the four samples before declaring step 13 done; if
  unreadable, a follow-up tweak to `SyntaxPalette` is in scope and
  does not require re-spec.
- **Per-line lexer mis-classification.** The JSON lexer can mis-call a
  string a `Key` if a colon happens to appear later on the line for an
  unrelated reason (e.g. inside a value's string content split across
  lines — already a non-goal). Acceptable as part of the per-line
  best-effort policy.
- **CDATA regression in `library.sample.xml`.** Lines 33–34 inside
  the multi-line CDATA will render at default fg in step 13 (was
  default fg in step 12 too; no regression vs. today). Worth a one-line
  note in the demo's expected output, no test for it.
