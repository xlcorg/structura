# Step 15 — Unified `DiffReporter` with auto / unified / side-by-side layout

## Goal

Collapse `UnifiedDiffReporter` and `SideBySideDiffReporter` into a single public
`DiffReporter` that chooses the layout based on console width and content width,
with a user-overridable mode. Remove `SimpleReporter` and `ConsoleDiffReporter`
as legacy/test scaffolding that has been superseded by the unified/side-by-side
renderers.

## Why

After Step 11 (unified) and Step 12 (side-by-side) shipped, the public surface
grew to four reporters with overlapping responsibilities. The two diff reporters
share `DiffHunkBuilder`, `DiffLine`, the painter pipeline, and the banner — only
the final line-by-line renderer differs. Forcing callers to pick between
`UnifiedDiffReporter` and `SideBySideDiffReporter` is also wrong: the right
choice depends on the *current* console width and the width of the *current*
change set, neither of which the caller can see at compile time. The library
should make that decision and let the caller override it when they have a
reason to.

`SimpleReporter` and `ConsoleDiffReporter` were the bring-up reporters from
Steps 6–10. They're now strictly weaker than the diff path, carry their own
ad-hoc colouring, and exist mostly in tests. Dropping them tightens the public
surface and the test matrix.

## Public surface (after Step 15)

```csharp
namespace Structura.Reporting;

public enum DiffReporterLayout
{
    Auto,
    Unified,
    SideBySide,
}

public sealed record DiffReporterOptions
{
    public int ContextLines { get; init; } = 3;
    public bool InlineHighlight { get; init; } = true;
    public bool SyntaxHighlight { get; init; } = true;
    public bool ShowFullFile { get; init; } = false;
    public DiffReporterLayout Layout { get; init; } = DiffReporterLayout.Auto;
}

public static class DiffReporter
{
    public static void Print(IStructuraDocument document);
    public static void Print(IStructuraDocument document, DiffReporterOptions options);
    public static void Print(IStructuraDocument document, TextWriter writer);
    public static void Print(IStructuraDocument document, TextWriter writer, DiffReporterOptions options);
}
```

Everything else (`DiffHunkBuilder`, `DiffLine`, `DiffStats`, `DiffBanner`,
`DiffLineRenderer`, `SideBySideRowBuilder`, `SideBySideRowRenderer`, the
highlighting pipeline) stays `internal` and is reused unchanged.

`SimpleReporter` and `ConsoleDiffReporter` are removed.

## Layout selection (`Auto`)

The auto heuristic considers both the terminal width and the content widths of
the rendered diff lines. Side-by-side is preferred when it fits without
unreadable truncation; otherwise unified wins because each line gets the full
width.

Inputs (computed once per call after `DiffHunkBuilder.Build`):

```
gutterWidth      = digits(maxLineNumber)
sideCellOverhead = gutterWidth + 3                   // " gutter sigil "
separator        = 3                                 // " │ "
maxContentLen    = max(line.Content.Length over all lines)

naturalSbsWidth  = 2 * sideCellOverhead + separator + 2 * maxContentLen
minSbsWidth      = 2 * sideCellOverhead + separator + 2 * MinContentPerSide
```

Constants (existing in `SideBySideDiffReporter` today, kept on the merged
class):

- `MinContentPerSide = 40` — below this, side-by-side becomes unreadable.
- `FallbackTotalWidth = 120` — used when `Console.WindowWidth` is unavailable
  (redirected output, tests, IDE run-console stubs). Reduced from the current
  `160` because `160` overestimates the typical non-terminal width and pushes
  SBS into output that won't reasonably fit downstream.

Terminal width source:

- `Print(document[, options])` with `Console.Out` and a real terminal →
  `Console.WindowWidth`.
- Both `TextWriter` overloads, and the `Console.Out` overload when
  `WindowWidth` throws (`IOException`, `PlatformNotSupportedException`) →
  `FallbackTotalWidth`.

Decision:

```
if (terminalWidth >= naturalSbsWidth)      → SideBySide   // no truncation
else if (terminalWidth >= minSbsWidth)     → SideBySide   // truncation, as today
else                                       → Unified       // each line full-width
```

When `options.Layout` is `Unified` or `SideBySide` the heuristic is skipped and
that mode is rendered as-is (including the existing truncation behaviour for
forced side-by-side at narrow widths).

## Internal shape

`UnifiedDiffReporter.RenderTo` and `SideBySideDiffReporter.RenderTo` become
internal helpers — concrete proposal: file them under
`Structura.Reporting/Internal/` as `UnifiedRenderer.cs` and
`SideBySideRenderer.cs`, each exposing one static `RenderTo` with the same
signature it has today. `DiffReporter` is a thin public facade that:

1. Computes `terminalWidth` from the call site.
2. Builds `DiffLine[]` via `DiffHunkBuilder` once.
3. Resolves `Auto` → concrete `DiffReporterLayout` using the heuristic above.
4. Delegates to `UnifiedRenderer.RenderTo` or `SideBySideRenderer.RenderTo`
   with `lines`, `stats`, `gutterWidth`, and the painter selection that both
   current reporters do today.

Computing `DiffLine[]` once (instead of inside each renderer) is the only
behavioural change to the existing rendering path. It also lets the heuristic
read `maxContentLen` without duplicating the hunk build.

`(no changes)` empty-document handling moves from each renderer into
`DiffReporter` (single early return, same wording, written before the layout
is resolved).

## Demo (`src/Structura/Program.cs`)

All eight current calls — `UnifiedDiffReporter.Print(doc)` and
`SideBySideDiffReporter.Print(doc)` in pairs after each pipeline — collapse
into one `DiffReporter.Print(doc)` per pipeline. The demo doesn't need to
showcase the explicit-mode overloads; readers can see them in the API or
tests.

## Test plan

Existing tests are moved/renamed rather than rewritten — they cover painters,
banner, inline highlights, truncation, and SBS row layout, all of which keep
working.

Kept and re-pointed:

- `UnifiedDiffReporterTests` → `DiffReporterUnifiedTests`, calls
  `DiffReporter.Print(doc, writer, new DiffReporterOptions { Layout = Unified })`.
- `SideBySideDiffReporterTests` → `DiffReporterSideBySideTests`, calls with
  `Layout = SideBySide` (so the existing `FallbackTotalWidth` truncation cases
  still apply — adjusted for the new `120` baseline).
- `SideBySideDiffReporterColorTests` → `DiffReporterSideBySideColorTests`, same
  re-point, no other change.
- `SideBySideRowBuilderTests`, `OrderSampleJsonReportingTests`,
  `OrderSampleJsonSideBySideDiffTests` — unchanged after class renames; update
  references mechanically.

Removed:

- `SimpleReporterTests.cs`
- `ConsoleDiffReporterTests.cs`

New (one file, several cases):

- `DiffReporterLayoutTests`:
  - `Auto_PicksSideBySide_When_NaturalFits`: terminal = 160, short content →
    SBS, no `…` truncation indicator.
  - `Auto_PicksSideBySide_When_AboveMinButBelowNatural`: terminal = 100,
    long content → SBS with truncation (assert `…` / `>` present).
  - `Auto_PicksUnified_When_BelowMinSbs`: terminal = 60 → Unified (assert
    absence of `│` / `|` separator + presence of single-column gutter).
  - `Explicit_Unified_Wins_Over_AutoHeuristic`: terminal = 160, force Unified.
  - `Explicit_SideBySide_Wins_Over_AutoHeuristic`: terminal = 40, force SBS —
    output stays side-by-side with aggressive truncation.

Terminal width in tests is injected through a new `internal` overload on
`DiffReporter`:

```csharp
internal static void RenderTo(
    IStructuraDocument document,
    TextWriter writer,
    DiffReporterOptions options,
    int terminalWidth,
    bool useColor,
    bool useUnicode);
```

Public `Print` overloads call this with the resolved width/color/unicode.
`Directory.Build.targets` already exposes internals to both `Structura.UnitTests`
and `Structura.IntegrationTests`, so the new `DiffReporterLayoutTests` lives in
`tests/Structura.UnitTests/Reporting/` alongside the other reporter unit tests.

## Risks and out-of-scope

- **No change to colours or painters.** Anything the painters output today
  must be byte-identical with `Layout = Unified` / `Layout = SideBySide` —
  this is asserted by re-pointing the existing tests rather than rewriting
  them.
- **Banner wording unchanged.** `Patched <name> with N additions and M
  removals` per the standing feedback memory.
- **`FallbackTotalWidth` change is observable.** Any consumer that relied on
  SBS at the old 160-wide fallback (i.e. tests asserting specific column
  widths) needs its expected widths re-baselined. Acceptable: that is exactly
  what we want from the reduction.
- **No new options.** No "max content cap" / "force truncation tolerance"
  knobs — `MinContentPerSide = 40` is a hard-coded internal constant; if a
  user needs tighter control they use `Layout = Unified` or `Layout =
  SideBySide` directly.
- **No deprecation period.** `SimpleReporter` and `ConsoleDiffReporter` are
  removed, not `[Obsolete]`-marked, because the library has no external
  consumers yet (Step 14 just landed and the only caller is `Program.cs`).
