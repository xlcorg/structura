# Step 16 — `DiffReporter` separator options (`LeadingBlankLine`, `HorizontalRule`)

## Goal

Give callers explicit control over how `DiffReporter`'s output is separated
from preceding console text. Add two opt-in options to `DiffReporterOptions`:
`LeadingBlankLine` (default `true`) and `HorizontalRule` (default `false`).
The banner itself (`● Patched\n  └ Patched <name> with N additions and M
removals`) is **not** modified.

## Why

Today `DiffReporter` writes the banner as the very first character of its
output. When the demo (or any caller) prints something immediately before
`DiffReporter.Print`, the banner hugs the prior line:

```
=== Diff ===
● Patched
  └ Patched order.sample.json with 8 additions and 8 removals
    4    "created_at_utc": "2026-05-02T10:15:30Z",
```

The `● Patched` row visually merges with the caller's previous output, which
makes the diff section harder to spot in a long console transcript. The fix
belongs in the library — a caller who composes multiple sections cannot rely
on every consumer remembering to add `Console.WriteLine()` before each
`DiffReporter.Print`. At the same time, callers who pipe the banner into
their own UI (a TUI panel, a captured-output pipeline) need to be able to
suppress the separator. Two narrow boolean options cover both cases without
touching the banner.

## Public surface (after Step 16)

```csharp
namespace Structura.Reporting;

public sealed record DiffReporterOptions
{
    public int ContextLines { get; init; } = 3;
    public bool InlineHighlight { get; init; } = true;
    public bool SyntaxHighlight { get; init; } = true;
    public bool ShowFullFile { get; init; } = false;
    public DiffReporterLayout Layout { get; init; } = DiffReporterLayout.Auto;

    /// <summary>
    /// When true, emit a single blank line before any other output.
    /// Separates the diff from preceding console text. Default true.
    /// </summary>
    public bool LeadingBlankLine { get; init; } = true;

    /// <summary>
    /// When true, emit a horizontal rule line spanning the resolved terminal
    /// width immediately before the banner. Default false.
    /// </summary>
    public bool HorizontalRule { get; init; } = false;
}
```

`DiffReporter`'s `Print` overloads, `DiffReporterLayout`, `DiffBanner`, the
unified/side-by-side renderers, and the highlighting pipeline are unchanged.

## Behavior

`DiffReporter.RenderTo` emits, in this fixed order, before any other output:

1. If `options.LeadingBlankLine` → one empty `WriteLine()`.
2. If `options.HorizontalRule` → one rule line followed by `WriteLine()`.
3. Existing output: either `(no changes)` (when the document has no changes)
   or banner + diff body.

When both options are enabled the order is **blank line, then rule, then
banner**:

```
prior text
                ← LeadingBlankLine
────────────    ← HorizontalRule
● Patched       ← banner
  └ Patched ...
```

The blank line is the first thing written so the rule itself gets breathing
room from prior output. Each option keeps an independent meaning regardless
of the other — `LeadingBlankLine` is always a blank line at the very top,
`HorizontalRule` is always a rule immediately above the banner — so callers
can predict the result without reasoning about combinations.

The separator is emitted on **both** the `(no changes)` early-return path
and the rendered path, so behavior is uniform across documents that did or
did not produce any edits. The current `(no changes)` wording stays.

### Horizontal-rule details

- **Character.** `─` (U+2500) when `useUnicode` is true, otherwise `-` —
  matches `DiffBanner`'s existing utf-8 / ascii branching for `●` and `└`.
- **Width.** The rule fills `terminalWidth` characters, where
  `terminalWidth` is the value already computed inside
  `DiffReporter.RenderTo` (real `Console.WindowWidth` for the
  `Console.Out`-on-terminal case, `FallbackTotalWidth` otherwise). No new
  width-computation path is introduced; SBS callers get the same width they
  use for the two-column layout.
- **Color.** No color, no dim, no bold. The banner already carries the
  colored accent (`●` in green, "Patched" in bold); a colored rule would
  compete with it.

## Internal shape

The change is local to `DiffReporter.RenderTo`. Pseudocode:

```csharp
internal static void RenderTo(
    IStructuraDocument document,
    TextWriter writer,
    DiffReporterOptions options,
    int terminalWidth,
    bool useColor,
    bool useUnicode)
{
    ArgumentNullException.ThrowIfNull(document);
    ArgumentNullException.ThrowIfNull(writer);
    ArgumentNullException.ThrowIfNull(options);

    if (options.LeadingBlankLine)
    {
        writer.WriteLine();
    }
    if (options.HorizontalRule)
    {
        var ruleChar = useUnicode ? '─' : '-';
        var rule = new string(ruleChar, terminalWidth);
        writer.WriteLine(rule);
    }

    // …existing (no changes) early return + layout dispatch unchanged
}
```

`DiffBanner.Write` is not called and not modified. The two renderers
(`UnifiedRenderer`, `SideBySideRenderer`) are not modified. No new internal
helpers are introduced; the rule string is built inline because it is two
lines of code used in one place.

## Demo (`src/Structura/Program.cs`)

No changes required. The default `LeadingBlankLine = true` produces the
desired breathing room between `=== Diff ===` and `● Patched` for all four
existing pipelines (order JSON, BLRWBL XML, library XML, library JSON).
The `=== Diff ===` headers stay — the banner already self-identifies, but
the headers also call out the section in the surrounding `=== Modified … ===`
rhythm; removing them is a separate concern.

## Test plan

New unit tests in `tests/Structura.UnitTests/Reporting/DiffReporterTests.cs`
(or a sibling `DiffReporterSeparatorTests.cs` if the existing file grows
unwieldy — author's call at write time). All tests use the existing
internal `RenderTo` overload with `useColor: false` so assertions can be
made on plain text.

1. **Default options write a leading blank line.** Render a small mutated
   document with `new DiffReporterOptions()`; assert the captured output
   starts with `"\n"` (or `Environment.NewLine` — the renderer uses
   `writer.WriteLine`, so the test asserts whatever the writer's newline
   sequence is) and the next non-empty line begins with the banner dot.
2. **`LeadingBlankLine = false, HorizontalRule = false` writes no
   separator.** First character of output is the banner dot (`●` or `*`).
3. **`HorizontalRule = true` writes a rule of the resolved width.** With
   `useUnicode: true` and `terminalWidth: 80`, output contains a line
   consisting of exactly 80 `─` characters immediately before the banner.
   With `useUnicode: false`, the same line consists of `-` characters.
4. **Both enabled — order is blank, rule, banner.** Split the output into
   lines; assert `lines[0] == ""`, `lines[1]` is the rule (all `─`), and
   `lines[2]` starts with the banner dot.
5. **`(no changes)` path also honors both options.** Render a document
   with no `Changes`; assert `LeadingBlankLine = true` produces a leading
   blank line before `(no changes)`, and `HorizontalRule = true` produces
   a rule before `(no changes)`. Combination matches the same blank-then-
   rule order.

Existing reporter tests remain green: they all use `.Should().Contain(...)`
substring assertions on the banner, body, or counts, none of which depend
on the absence of leading whitespace.

## Risks and out-of-scope

- **Default-on `LeadingBlankLine` is observable.** Any consumer parsing
  `DiffReporter` output as a stream and depending on the first character
  being `●` would now see a leading newline. Acceptable — the library has
  no external consumers, and the option exists precisely for callers who
  need the old behavior (`LeadingBlankLine = false`).
- **No `TrailingBlankLine` option.** `DiffReporter` already does not write
  a trailing newline beyond what its renderers produce; adding a trailing
  separator is a separate decision and would need its own justification.
- **No richer rule configuration.** No caller-supplied character, no
  caller-supplied width, no color knob. If a user needs a custom rule they
  set `HorizontalRule = false` and write their own line before calling
  `Print`. Two boolean options is the smallest surface that covers the
  reported pain.
- **Banner is untouched.** Per direct user instruction. The banner's two
  lines, glyphs, wording ("Patched <name> with N additions and M
  removals"), and color treatment are unchanged. A future "boxed banner"
  redesign is explicitly out of scope.
- **Demo unchanged.** No removal of `=== Diff ===` headers, no
  reordering. The default option values produce the desired visual
  improvement without touching `Program.cs`.
