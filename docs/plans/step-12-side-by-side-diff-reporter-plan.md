# Step 12 — SideBySideDiffReporter Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a fourth reporter `SideBySideDiffReporter` rendering a two-column diff (left = OLD, right = NEW) reusing the color scheme from `UnifiedDiffReporter`. Refactor `DiffLine` to carry both old- and new-line numbers (needed for SBS context rows), and extract the shared banner into an internal helper used by both reporters.

**Architecture:** `SideBySideDiffReporter` calls existing `DiffHunkBuilder` to get a flat `IReadOnlyList<DiffLine>`, then a new `SideBySideRowBuilder` pairs runs of `Removed`/`Added` lines into two-column rows (top-aligned, padded with `null` for unequal lengths). `SideBySideRowRenderer` emits one output line per row using `AnsiPalette` (no new constants). The banner is shared via a new `Internal/DiffBanner.cs`. Width is `Console.WindowWidth` with try/catch fallback to 160; long content is truncated with `…` (or `>` ASCII).

**Tech Stack:** .NET 10 (`net10.0`), C# 12+, xunit 2.9, FluentAssertions 7. Source-of-truth spec: `docs/plans/step-12-side-by-side-diff-reporter.md`.

---

## File Structure

**Phase 1 — DiffLine refactor (foundation):**

| Change | File | Purpose |
|---|---|---|
| Modify | `src/Structura.Reporting/Internal/DiffLine.cs` | Replace `int LineNumber` with `int OldLineNumber, int NewLineNumber` |
| Modify | `src/Structura.Reporting/Internal/DiffHunkBuilder.cs` | Populate both fields per kind |
| Modify | `src/Structura.Reporting/Internal/DiffLineRenderer.cs` | Pick number by `Kind` |
| Modify | `src/Structura.Reporting/UnifiedDiffReporter.cs` | `maxLineNumber` from `Math.Max(Old, New)` |
| Modify | `tests/Structura.UnitTests/Reporting/DiffHunkBuilderTests.cs` | Update assertions to new fields |
| Modify | `tests/Structura.UnitTests/Reporting/DiffLineRendererTests.cs` | Update `DiffLine` ctor calls |

**Phase 2 — Banner extraction:**

| Change | File | Purpose |
|---|---|---|
| Create | `src/Structura.Reporting/Internal/DiffBanner.cs` | Shared `Write(...)` method |
| Modify | `src/Structura.Reporting/UnifiedDiffReporter.cs` | Replace local `WriteBanner` with `DiffBanner.Write` |

**Phase 3 — SBS implementation:**

| Change | File | Purpose |
|---|---|---|
| Create | `src/Structura.Reporting/SideBySideDiffOptions.cs` | Public sealed record |
| Create | `src/Structura.Reporting/Internal/SideBySideRow.cs` | `(DiffLine? Left, DiffLine? Right)` record struct |
| Create | `src/Structura.Reporting/Internal/SideBySideRowBuilder.cs` | Pair `DiffLine` list into rows |
| Create | `src/Structura.Reporting/Internal/SideBySideRowRenderer.cs` | Render one row to string |
| Create | `src/Structura.Reporting/SideBySideDiffReporter.cs` | Public static, three `Print` overloads |

**Phase 4 — Tests:**

| Change | File | Purpose |
|---|---|---|
| Create | `tests/Structura.UnitTests/Reporting/SideBySideRowBuilderTests.cs` | Pairing logic tests |
| Create | `tests/Structura.UnitTests/Reporting/SideBySideDiffReporterTests.cs` | Plain-output tests via `StringWriter` |
| Create | `tests/Structura.UnitTests/Reporting/SideBySideDiffReporterColorTests.cs` | ANSI escape verification |
| Create | `tests/Structura.IntegrationTests/Reporting/OrderSampleJsonSideBySideDiffTests.cs` | Real-sample integration |

**Phase 5 — Demo:**

| Change | File | Purpose |
|---|---|---|
| Modify | `src/Structura/Program.cs` | Add SBS block after each Unified block |

---

## Phase 1: DiffLine refactor (foundation)

This phase is one atomic commit — `DiffLine` is internal, but its signature changes ripple through builder + renderer + tests. Build green-bar after every step.

### Task 1: Refactor `DiffLine` record struct

**Files:**
- Modify: `src/Structura.Reporting/Internal/DiffLine.cs`

- [ ] **Step 1: Replace `DiffLine` definition**

In `src/Structura.Reporting/Internal/DiffLine.cs`, replace the `DiffLine` record struct (currently the bottom block of the file, lines ~29-39) with:

```csharp
/// <summary>
/// One rendered line of the unified diff.
/// <list type="bullet">
/// <item><see cref="Context"/> — both <see cref="OldLineNumber"/> and <see cref="NewLineNumber"/> populated (1-based).</item>
/// <item><see cref="Removed"/> — only <see cref="OldLineNumber"/> populated; <see cref="NewLineNumber"/> = 0.</item>
/// <item><see cref="Added"/> — only <see cref="NewLineNumber"/> populated; <see cref="OldLineNumber"/> = 0.</item>
/// <item><see cref="HunkSeparator"/> — both 0; <see cref="Content"/> / <see cref="InlineHighlights"/> unused.</item>
/// </list>
/// </summary>
internal readonly record struct DiffLine(
    DiffLineKind Kind,
    int OldLineNumber,
    int NewLineNumber,
    string Content,
    IReadOnlyList<ColumnRange> InlineHighlights);
```

The `DiffLineKind` enum and `ColumnRange` record struct above stay unchanged.

- [ ] **Step 2: Verify file compiles in isolation by building (will fail downstream)**

Run: `dotnet build src/Structura.Reporting/Structura.Reporting.csproj`
Expected: FAIL with errors in `DiffHunkBuilder.cs` and `DiffLineRenderer.cs` (positional ctor arity mismatch). This is expected — fixed in Tasks 2-3.

### Task 2: Update `DiffHunkBuilder` to populate both line numbers

**Files:**
- Modify: `src/Structura.Reporting/Internal/DiffHunkBuilder.cs`

- [ ] **Step 1: Update `HunkSeparator` emission (line ~35)**

Replace:

```csharp
output.Add(new DiffLine(DiffLineKind.HunkSeparator, 0, string.Empty, Array.Empty<ColumnRange>()));
```

With:

```csharp
output.Add(new DiffLine(DiffLineKind.HunkSeparator, 0, 0, string.Empty, Array.Empty<ColumnRange>()));
```

- [ ] **Step 2: Update pre-context emission (loop near line ~176-184)**

Replace the existing pre-context loop:

```csharp
for (int i = preStart; i < hunk.OldStartLine; i++)
{
    string strippedLine = StripCarriageReturn(oldLines[i]);
    output.Add(new DiffLine(
        DiffLineKind.Context,
        i + 1 + preDelta,
        strippedLine,
        Array.Empty<ColumnRange>()));
}
```

With (Context now carries OLD = `i + 1` and NEW = `i + 1 + preDelta`):

```csharp
for (int i = preStart; i < hunk.OldStartLine; i++)
{
    string strippedLine = StripCarriageReturn(oldLines[i]);
    int oldLineNumber = i + 1;
    int newLineNumber = i + 1 + preDelta;
    output.Add(new DiffLine(
        DiffLineKind.Context,
        oldLineNumber,
        newLineNumber,
        strippedLine,
        Array.Empty<ColumnRange>()));
}
```

- [ ] **Step 3: Update inter-change Context emission (inner `while` near line ~198-208)**

Replace:

```csharp
while (oldCursor < c.OldStartLine)
{
    string contextContent = StripCarriageReturn(oldLines[oldCursor]);
    output.Add(new DiffLine(
        DiffLineKind.Context,
        newCursor + 1,
        contextContent,
        Array.Empty<ColumnRange>()));
    oldCursor++;
    newCursor++;
}
```

With:

```csharp
while (oldCursor < c.OldStartLine)
{
    string contextContent = StripCarriageReturn(oldLines[oldCursor]);
    int oldLineNumber = oldCursor + 1;
    int newLineNumber = newCursor + 1;
    output.Add(new DiffLine(
        DiffLineKind.Context,
        oldLineNumber,
        newLineNumber,
        contextContent,
        Array.Empty<ColumnRange>()));
    oldCursor++;
    newCursor++;
}
```

- [ ] **Step 4: Update Removed emission (loop near line ~214-221)**

Replace:

```csharp
for (int i = removedStart; i <= c.OldEndLine; i++)
{
    string content = StripCarriageReturn(oldLines[i]);
    IReadOnlyList<ColumnRange> highlights = inlineHighlight
        ? CollectRemovedHighlightsForLine(i, hunk.Changes, originalText, content.Length)
        : Array.Empty<ColumnRange>();
    output.Add(new DiffLine(DiffLineKind.Removed, i + 1, content, highlights));
}
```

With:

```csharp
for (int i = removedStart; i <= c.OldEndLine; i++)
{
    string content = StripCarriageReturn(oldLines[i]);
    IReadOnlyList<ColumnRange> highlights = inlineHighlight
        ? CollectRemovedHighlightsForLine(i, hunk.Changes, originalText, content.Length)
        : Array.Empty<ColumnRange>();
    int oldLineNumber = i + 1;
    output.Add(new DiffLine(DiffLineKind.Removed, oldLineNumber, 0, content, highlights));
}
```

- [ ] **Step 5: Update Added emission (loop near line ~231-238)**

Replace:

```csharp
for (int i = addedStart; i <= c.NewEndLine; i++)
{
    string content = StripCarriageReturn(newLines[i]);
    IReadOnlyList<ColumnRange> highlights = inlineHighlight
        ? CollectAddedHighlightsForLine(i, hunk.Changes, content.Length, originalText)
        : Array.Empty<ColumnRange>();
    output.Add(new DiffLine(DiffLineKind.Added, i + 1, content, highlights));
}
```

With:

```csharp
for (int i = addedStart; i <= c.NewEndLine; i++)
{
    string content = StripCarriageReturn(newLines[i]);
    IReadOnlyList<ColumnRange> highlights = inlineHighlight
        ? CollectAddedHighlightsForLine(i, hunk.Changes, content.Length, originalText)
        : Array.Empty<ColumnRange>();
    int newLineNumber = i + 1;
    output.Add(new DiffLine(DiffLineKind.Added, 0, newLineNumber, content, highlights));
}
```

- [ ] **Step 6: Update tail Context loop (near line ~247-257)**

Replace:

```csharp
while (oldCursor <= hunk.OldEndLine)
{
    string contextContent = StripCarriageReturn(oldLines[oldCursor]);
    output.Add(new DiffLine(
        DiffLineKind.Context,
        newCursor + 1,
        contextContent,
        Array.Empty<ColumnRange>()));
    oldCursor++;
    newCursor++;
}
```

With:

```csharp
while (oldCursor <= hunk.OldEndLine)
{
    string contextContent = StripCarriageReturn(oldLines[oldCursor]);
    int oldLineNumber = oldCursor + 1;
    int newLineNumber = newCursor + 1;
    output.Add(new DiffLine(
        DiffLineKind.Context,
        oldLineNumber,
        newLineNumber,
        contextContent,
        Array.Empty<ColumnRange>()));
    oldCursor++;
    newCursor++;
}
```

- [ ] **Step 7: Update post-context loop (near line ~260-268)**

Replace:

```csharp
for (int i = hunk.OldEndLine + 1; i <= postEnd; i++)
{
    string strippedLine = StripCarriageReturn(oldLines[i]);
    output.Add(new DiffLine(
        DiffLineKind.Context,
        i + 1 + postDelta,
        strippedLine,
        Array.Empty<ColumnRange>()));
}
```

With:

```csharp
for (int i = hunk.OldEndLine + 1; i <= postEnd; i++)
{
    string strippedLine = StripCarriageReturn(oldLines[i]);
    int oldLineNumber = i + 1;
    int newLineNumber = i + 1 + postDelta;
    output.Add(new DiffLine(
        DiffLineKind.Context,
        oldLineNumber,
        newLineNumber,
        strippedLine,
        Array.Empty<ColumnRange>()));
}
```

### Task 3: Update `DiffLineRenderer` to pick number by `Kind`

**Files:**
- Modify: `src/Structura.Reporting/Internal/DiffLineRenderer.cs`

- [ ] **Step 1: Update gutter computation in `Render`**

In `src/Structura.Reporting/Internal/DiffLineRenderer.cs`, after the `HunkSeparator` early return and the `sigil` switch (around line 28), replace:

```csharp
string gutter = line.LineNumber.ToString().PadLeft(gutterWidth);
```

With:

```csharp
int gutterValue = line.Kind == DiffLineKind.Removed ? line.OldLineNumber : line.NewLineNumber;
string gutter = gutterValue.ToString().PadLeft(gutterWidth);
```

(Justification: `Context` and `Added` show NEW number — the existing behavior; `Removed` shows OLD number — also the existing behavior, since previously `LineNumber` for Removed was set to `i + 1` of the OLD index.)

### Task 4: Update `UnifiedDiffReporter.maxLineNumber` computation

**Files:**
- Modify: `src/Structura.Reporting/UnifiedDiffReporter.cs`

- [ ] **Step 1: Update gutter-width detection loop (near line 57-73)**

Replace:

```csharp
int additions = 0;
int removals = 0;
int maxLineNumber = 1;
foreach (DiffLine line in lines)
{
    if (line.Kind == DiffLineKind.Added)
    {
        additions++;
    }
    else if (line.Kind == DiffLineKind.Removed)
    {
        removals++;
    }

    if (line.LineNumber > maxLineNumber)
    {
        maxLineNumber = line.LineNumber;
    }
}
```

With:

```csharp
int additions = 0;
int removals = 0;
int maxLineNumber = 1;
foreach (DiffLine line in lines)
{
    if (line.Kind == DiffLineKind.Added)
    {
        additions++;
    }
    else if (line.Kind == DiffLineKind.Removed)
    {
        removals++;
    }

    int candidate = Math.Max(line.OldLineNumber, line.NewLineNumber);
    if (candidate > maxLineNumber)
    {
        maxLineNumber = candidate;
    }
}
```

### Task 5: Update existing reporting tests for new `DiffLine` shape

**Files:**
- Modify: `tests/Structura.UnitTests/Reporting/DiffHunkBuilderTests.cs`
- Modify: `tests/Structura.UnitTests/Reporting/DiffLineRendererTests.cs`

- [ ] **Step 1: Update `DiffHunkBuilderTests` assertions**

In `tests/Structura.UnitTests/Reporting/DiffHunkBuilderTests.cs`, every assertion of the form `lines[N].LineNumber.Should().Be(X);` must be replaced based on the line's kind:

| Existing line kind in test | Replacement assertion |
|---|---|
| `Context` (was `LineNumber == X`) | `lines[N].OldLineNumber.Should().Be(X); lines[N].NewLineNumber.Should().Be(X);` (when no prior delta — same number on both sides) |
| `Removed` (was `LineNumber == X`) | `lines[N].OldLineNumber.Should().Be(X);` |
| `Added` (was `LineNumber == X`) | `lines[N].NewLineNumber.Should().Be(X);` |

Apply to every `LineNumber` assertion in the file. For tests that exercise multi-line replacement with cumulative delta, check the test setup carefully — Context on the second hunk should have `OldLineNumber != NewLineNumber`. If a test currently asserts `LineNumber == new-side-number`, it should now assert `NewLineNumber` for Context.

For the `Build_SingleChange_EmitsContextMinusPlusContext` test specifically (lines 22-59), because there is no cumulative delta (single 1-line replacement), all OLD == NEW for Context. Replace each `lines[N].LineNumber.Should().Be(X);` line by line:

- `lines[0]` Context line 1: `lines[0].OldLineNumber.Should().Be(1); lines[0].NewLineNumber.Should().Be(1);`
- `lines[1]` Context line 2: similarly.
- `lines[2]` Removed line 3: `lines[2].OldLineNumber.Should().Be(3);`
- `lines[3]` Added line 3: `lines[3].NewLineNumber.Should().Be(3);`
- `lines[4..6]` Context lines 4-6: both Old and New equal.

- [ ] **Step 2: Update `DiffLineRendererTests` `DiffLine` ctor calls**

In `tests/Structura.UnitTests/Reporting/DiffLineRendererTests.cs`, every `new DiffLine(kind, N, content, ranges)` becomes `new DiffLine(kind, oldLineNumber, newLineNumber, content, ranges)` per kind:

| Kind | Replacement |
|---|---|
| `DiffLineKind.Context` with N | `new DiffLine(DiffLineKind.Context, N, N, content, ranges)` |
| `DiffLineKind.Removed` with N | `new DiffLine(DiffLineKind.Removed, N, 0, content, ranges)` |
| `DiffLineKind.Added` with N | `new DiffLine(DiffLineKind.Added, 0, N, content, ranges)` |
| `DiffLineKind.HunkSeparator` with 0 | `new DiffLine(DiffLineKind.HunkSeparator, 0, 0, content, ranges)` |

For example (line ~14): replace
```csharp
var line = new DiffLine(DiffLineKind.Context, 7, "  \"x\": 1,", System.Array.Empty<ColumnRange>());
```
with:
```csharp
var line = new DiffLine(DiffLineKind.Context, 7, 7, "  \"x\": 1,", System.Array.Empty<ColumnRange>());
```

Apply this transformation to **every** `new DiffLine(...)` call in the file (Render_ContextLine_NoColor_*, Render_RemovedLine_*, Render_AddedLine_*, Render_HunkSeparator_*, Render_AddedLine_InlineHighlight_*, etc.).

### Task 6: Verify Phase 1 build green and commit

- [ ] **Step 1: Build and run all tests**

Run: `dotnet build Structura.slnx && dotnet test`
Expected: build succeeds with no new warnings; all existing tests pass (Phase 1 is byte-identical for Unified output).

- [ ] **Step 2: Commit**

```bash
git add src/Structura.Reporting/Internal/DiffLine.cs \
        src/Structura.Reporting/Internal/DiffHunkBuilder.cs \
        src/Structura.Reporting/Internal/DiffLineRenderer.cs \
        src/Structura.Reporting/UnifiedDiffReporter.cs \
        tests/Structura.UnitTests/Reporting/DiffHunkBuilderTests.cs \
        tests/Structura.UnitTests/Reporting/DiffLineRendererTests.cs
git commit -m "refactor: split DiffLine.LineNumber into Old/NewLineNumber"
```

---

## Phase 2: Banner extraction

### Task 7: Create `Internal/DiffBanner.cs`

**Files:**
- Create: `src/Structura.Reporting/Internal/DiffBanner.cs`

- [ ] **Step 1: Create the file**

Create `src/Structura.Reporting/Internal/DiffBanner.cs` with the full `WriteBanner` body lifted verbatim from `UnifiedDiffReporter` and renamed to `Write`:

```csharp
namespace Structura.Reporting.Internal;

/// <summary>
/// Two-line banner emitted at the top of unified and side-by-side diff output:
/// <c>● Patched(name)</c> followed by <c>  └ Patched name with N additions and M removals</c>.
/// Wording matches the existing reporter contract — do not paraphrase.
/// </summary>
internal static class DiffBanner
{
    public static void Write(
        TextWriter writer,
        string documentName,
        int additions,
        int removals,
        bool useColor,
        bool useUnicode)
    {
        string dot = useUnicode ? "●" : "*";
        string corner = useUnicode ? "└" : "\\";
        string additionNoun = additions == 1 ? "addition" : "additions";
        string removalNoun = removals == 1 ? "removal" : "removals";

        if (useColor)
        {
            writer.Write(AnsiPalette.FgGreen);
            writer.Write(dot);
            writer.Write(AnsiPalette.FgDefault);
            writer.Write(' ');
            writer.Write(AnsiPalette.Bold);
            writer.Write("Patched");
            writer.Write(AnsiPalette.BoldOff);
            writer.Write('(');
            writer.Write(documentName);
            writer.WriteLine(')');

            writer.Write("  ");
            writer.Write(AnsiPalette.Dim);
            writer.Write(corner);
            writer.Write(' ');
            writer.Write("Patched ");
            writer.Write(AnsiPalette.DimOff);
            writer.Write(AnsiPalette.Bold);
            writer.Write(documentName);
            writer.Write(AnsiPalette.BoldOff);
            writer.Write(AnsiPalette.Dim);
            writer.Write(" with ");
            writer.Write(AnsiPalette.DimOff);
            writer.Write(AnsiPalette.Bold);
            writer.Write(additions);
            writer.Write(AnsiPalette.BoldOff);
            writer.Write(AnsiPalette.Dim);
            writer.Write($" {additionNoun} and ");
            writer.Write(AnsiPalette.DimOff);
            writer.Write(AnsiPalette.Bold);
            writer.Write(removals);
            writer.Write(AnsiPalette.BoldOff);
            writer.Write(AnsiPalette.Dim);
            writer.WriteLine($" {removalNoun}");
            writer.Write(AnsiPalette.DimOff);
        }
        else
        {
            writer.Write(dot);
            writer.Write(' ');
            writer.Write("Patched(");
            writer.Write(documentName);
            writer.WriteLine(')');

            writer.Write("  ");
            writer.Write(corner);
            writer.Write(' ');
            writer.Write("Patched ");
            writer.Write(documentName);
            writer.Write(" with ");
            writer.Write(additions);
            writer.Write(' ');
            writer.Write(additionNoun);
            writer.Write(" and ");
            writer.Write(removals);
            writer.Write(' ');
            writer.WriteLine(removalNoun);
        }
    }
}
```

### Task 8: Refactor `UnifiedDiffReporter` to call `DiffBanner.Write`

**Files:**
- Modify: `src/Structura.Reporting/UnifiedDiffReporter.cs`

- [ ] **Step 1: Replace the call site**

In `src/Structura.Reporting/UnifiedDiffReporter.cs`, replace:

```csharp
WriteBanner(writer, document.DocumentName, additions, removals, useColor, useUnicode);
```

With:

```csharp
DiffBanner.Write(writer, document.DocumentName, additions, removals, useColor, useUnicode);
```

- [ ] **Step 2: Delete the old `WriteBanner` method**

Delete the entire `private static void WriteBanner(...)` method (currently lines ~87-160) including its body.

### Task 9: Verify Phase 2 build green and commit

- [ ] **Step 1: Build and run all tests**

Run: `dotnet build Structura.slnx && dotnet test`
Expected: build succeeds; all existing tests pass — banner output is byte-identical (only its location moved).

- [ ] **Step 2: Commit**

```bash
git add src/Structura.Reporting/Internal/DiffBanner.cs \
        src/Structura.Reporting/UnifiedDiffReporter.cs
git commit -m "refactor: extract DiffBanner from UnifiedDiffReporter"
```

---

## Phase 3: SideBySideDiffReporter implementation

### Task 10: Create `SideBySideDiffOptions`

**Files:**
- Create: `src/Structura.Reporting/SideBySideDiffOptions.cs`

- [ ] **Step 1: Create the file**

```csharp
namespace Structura.Reporting;

/// <summary>
/// Options for <see cref="SideBySideDiffReporter"/>.
/// </summary>
public sealed record SideBySideDiffOptions
{
    public int ContextLines { get; init; } = 3;

    public bool InlineHighlight { get; init; } = true;

    /// <summary>
    /// Total output width (gutters + columns + separator). When <c>null</c>,
    /// the reporter uses <see cref="System.Console.WindowWidth"/> if available
    /// and otherwise falls back to <c>160</c>.
    /// </summary>
    public int? TotalWidth { get; init; } = null;
}
```

### Task 11: Create `SideBySideRow` record struct

**Files:**
- Create: `src/Structura.Reporting/Internal/SideBySideRow.cs`

- [ ] **Step 1: Create the file**

```csharp
namespace Structura.Reporting.Internal;

/// <summary>
/// One rendered row of side-by-side output. Either side may be <c>null</c>,
/// meaning that side renders as blank padding (e.g. multi-line replacement
/// where one side has more lines than the other). For
/// <see cref="DiffLineKind.HunkSeparator"/>, both sides hold the same
/// separator <see cref="DiffLine"/>.
/// </summary>
internal readonly record struct SideBySideRow(DiffLine? Left, DiffLine? Right);
```

### Task 12: Create `SideBySideRowBuilder` (test-first)

**Files:**
- Create: `tests/Structura.UnitTests/Reporting/SideBySideRowBuilderTests.cs`
- Create: `src/Structura.Reporting/Internal/SideBySideRowBuilder.cs`

- [ ] **Step 1: Write failing tests**

Create `tests/Structura.UnitTests/Reporting/SideBySideRowBuilderTests.cs`:

```csharp
using FluentAssertions;

using Structura.Reporting.Internal;

using Xunit;

namespace Structura.UnitTests.Reporting;

public sealed class SideBySideRowBuilderTests
{
    private static readonly System.Collections.Generic.IReadOnlyList<ColumnRange> NoRanges =
        System.Array.Empty<ColumnRange>();

    [Fact]
    public void Build_EmptyInput_ReturnsEmpty()
    {
        var rows = SideBySideRowBuilder.Build(System.Array.Empty<DiffLine>());

        rows.Should().BeEmpty();
    }

    [Fact]
    public void Build_ContextOnly_BothSidesEqual()
    {
        var line = new DiffLine(DiffLineKind.Context, 1, 1, "x", NoRanges);

        var rows = SideBySideRowBuilder.Build(new[] { line });

        rows.Should().HaveCount(1);
        rows[0].Left.Should().Be(line);
        rows[0].Right.Should().Be(line);
    }

    [Fact]
    public void Build_HunkSeparator_BothSidesAreSeparator()
    {
        var sep = new DiffLine(DiffLineKind.HunkSeparator, 0, 0, string.Empty, NoRanges);

        var rows = SideBySideRowBuilder.Build(new[] { sep });

        rows.Should().HaveCount(1);
        rows[0].Left.Should().Be(sep);
        rows[0].Right.Should().Be(sep);
    }

    [Fact]
    public void Build_RemovedAddedPair_OneRowWithBothSides()
    {
        var rem = new DiffLine(DiffLineKind.Removed, 5, 0, "old", NoRanges);
        var add = new DiffLine(DiffLineKind.Added, 0, 5, "new", NoRanges);

        var rows = SideBySideRowBuilder.Build(new[] { rem, add });

        rows.Should().HaveCount(1);
        rows[0].Left.Should().Be(rem);
        rows[0].Right.Should().Be(add);
    }

    [Fact]
    public void Build_RemovedTwoAddedFour_TopAlignedFourRowsTwoLeftNull()
    {
        var rem1 = new DiffLine(DiffLineKind.Removed, 5, 0, "rem1", NoRanges);
        var rem2 = new DiffLine(DiffLineKind.Removed, 6, 0, "rem2", NoRanges);
        var add1 = new DiffLine(DiffLineKind.Added, 0, 5, "add1", NoRanges);
        var add2 = new DiffLine(DiffLineKind.Added, 0, 6, "add2", NoRanges);
        var add3 = new DiffLine(DiffLineKind.Added, 0, 7, "add3", NoRanges);
        var add4 = new DiffLine(DiffLineKind.Added, 0, 8, "add4", NoRanges);

        var rows = SideBySideRowBuilder.Build(new[] { rem1, rem2, add1, add2, add3, add4 });

        rows.Should().HaveCount(4);
        rows[0].Left.Should().Be(rem1);
        rows[0].Right.Should().Be(add1);
        rows[1].Left.Should().Be(rem2);
        rows[1].Right.Should().Be(add2);
        rows[2].Left.Should().BeNull();
        rows[2].Right.Should().Be(add3);
        rows[3].Left.Should().BeNull();
        rows[3].Right.Should().Be(add4);
    }

    [Fact]
    public void Build_RemovedOnlyNoAdded_RightSideNull()
    {
        var rem = new DiffLine(DiffLineKind.Removed, 5, 0, "old", NoRanges);

        var rows = SideBySideRowBuilder.Build(new[] { rem });

        rows.Should().HaveCount(1);
        rows[0].Left.Should().Be(rem);
        rows[0].Right.Should().BeNull();
    }

    [Fact]
    public void Build_AddedOnlyNoRemoved_LeftSideNull()
    {
        var add = new DiffLine(DiffLineKind.Added, 0, 5, "new", NoRanges);

        var rows = SideBySideRowBuilder.Build(new[] { add });

        rows.Should().HaveCount(1);
        rows[0].Left.Should().BeNull();
        rows[0].Right.Should().Be(add);
    }

    [Fact]
    public void Build_ContextRemovedAddedContext_ThreeRowsInOrder()
    {
        var ctxBefore = new DiffLine(DiffLineKind.Context, 4, 4, "before", NoRanges);
        var rem = new DiffLine(DiffLineKind.Removed, 5, 0, "old", NoRanges);
        var add = new DiffLine(DiffLineKind.Added, 0, 5, "new", NoRanges);
        var ctxAfter = new DiffLine(DiffLineKind.Context, 6, 6, "after", NoRanges);

        var rows = SideBySideRowBuilder.Build(new[] { ctxBefore, rem, add, ctxAfter });

        rows.Should().HaveCount(3);
        rows[0].Left.Should().Be(ctxBefore);
        rows[0].Right.Should().Be(ctxBefore);
        rows[1].Left.Should().Be(rem);
        rows[1].Right.Should().Be(add);
        rows[2].Left.Should().Be(ctxAfter);
        rows[2].Right.Should().Be(ctxAfter);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~SideBySideRowBuilderTests"`
Expected: build FAIL — `SideBySideRowBuilder` does not exist.

- [ ] **Step 3: Create `SideBySideRowBuilder`**

Create `src/Structura.Reporting/Internal/SideBySideRowBuilder.cs`:

```csharp
namespace Structura.Reporting.Internal;

/// <summary>
/// Pairs a flat <see cref="DiffLine"/> sequence (as produced by
/// <see cref="DiffHunkBuilder"/>) into <see cref="SideBySideRow"/> entries:
/// <list type="bullet">
/// <item><see cref="DiffLineKind.Context"/> → row with the same line on both sides.</item>
/// <item><see cref="DiffLineKind.HunkSeparator"/> → row with the separator on both sides.</item>
/// <item>Run of <see cref="DiffLineKind.Removed"/> immediately followed by run of
/// <see cref="DiffLineKind.Added"/> (either may be empty) → top-aligned pairs;
/// the shorter side is padded with <c>null</c> at the bottom.</item>
/// </list>
/// </summary>
internal static class SideBySideRowBuilder
{
    public static IReadOnlyList<SideBySideRow> Build(IReadOnlyList<DiffLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var rows = new List<SideBySideRow>(lines.Count);
        var i = 0;
        while (i < lines.Count)
        {
            DiffLine line = lines[i];
            switch (line.Kind)
            {
                case DiffLineKind.Context:
                case DiffLineKind.HunkSeparator:
                    rows.Add(new SideBySideRow(line, line));
                    i++;
                    break;

                case DiffLineKind.Removed:
                case DiffLineKind.Added:
                    i = AppendChangeRun(lines, i, rows);
                    break;

                default:
                    throw new InvalidOperationException($"Unexpected DiffLineKind: {line.Kind}");
            }
        }
        return rows;
    }

    private static int AppendChangeRun(IReadOnlyList<DiffLine> lines, int start, List<SideBySideRow> rows)
    {
        var removed = new List<DiffLine>();
        var added = new List<DiffLine>();
        int cursor = start;

        while (cursor < lines.Count && lines[cursor].Kind == DiffLineKind.Removed)
        {
            removed.Add(lines[cursor]);
            cursor++;
        }
        while (cursor < lines.Count && lines[cursor].Kind == DiffLineKind.Added)
        {
            added.Add(lines[cursor]);
            cursor++;
        }

        int max = Math.Max(removed.Count, added.Count);
        for (var j = 0; j < max; j++)
        {
            DiffLine? left = j < removed.Count ? removed[j] : null;
            DiffLine? right = j < added.Count ? added[j] : null;
            rows.Add(new SideBySideRow(left, right));
        }
        return cursor;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~SideBySideRowBuilderTests"`
Expected: PASS, 8 tests.

### Task 13: Create `SideBySideRowRenderer`

**Files:**
- Create: `src/Structura.Reporting/Internal/SideBySideRowRenderer.cs`

This component is exercised end-to-end by the reporter tests in Task 16. We do not write a separate test class — the reporter tests cover its observable behavior.

- [ ] **Step 1: Create the file**

```csharp
using System.Text;

namespace Structura.Reporting.Internal;

/// <summary>
/// Renders a single <see cref="SideBySideRow"/> as a string. Layout per row:
/// <c>{leftCell}{separator}{rightCell}</c>, where each cell is
/// <c>{lineNumber:>W} {sigil} {content:Wcol}</c>, the separator is
/// <c> │ </c> (or <c> | </c> with <c>useUnicode == false</c>) and content is
/// truncated to <paramref name="contentWidth"/> with <c>…</c> / <c>&gt;</c> as needed.
/// </summary>
internal static class SideBySideRowRenderer
{
    public static string Render(
        SideBySideRow row,
        int gutterWidth,
        int contentWidth,
        bool useColor,
        bool useUnicode)
    {
        string leftCell = RenderCell(row.Left, gutterWidth, contentWidth, isLeftSide: true, useColor, useUnicode);
        string rightCell = RenderCell(row.Right, gutterWidth, contentWidth, isLeftSide: false, useColor, useUnicode);
        string separator = RenderSeparator(useColor, useUnicode);
        return leftCell + separator + rightCell;
    }

    private static string RenderSeparator(bool useColor, bool useUnicode)
    {
        string glyph = useUnicode ? "│" : "|";
        if (!useColor)
        {
            return $" {glyph} ";
        }
        return $" {AnsiPalette.Dim}{glyph}{AnsiPalette.DimOff} ";
    }

    private static string RenderCell(
        DiffLine? maybeLine,
        int gutterWidth,
        int contentWidth,
        bool isLeftSide,
        bool useColor,
        bool useUnicode)
    {
        int cellWidth = gutterWidth + 3 + contentWidth;
        if (maybeLine is null)
        {
            return new string(' ', cellWidth);
        }

        DiffLine line = maybeLine.Value;
        if (line.Kind == DiffLineKind.HunkSeparator)
        {
            return RenderHunkSeparatorCell(cellWidth, useColor, useUnicode);
        }

        int gutterValue = isLeftSide ? line.OldLineNumber : line.NewLineNumber;
        string gutter = gutterValue.ToString().PadLeft(gutterWidth);
        char sigil = line.Kind switch
        {
            DiffLineKind.Removed => '-',
            DiffLineKind.Added => '+',
            _ => ' ',
        };

        var truncation = TruncateContent(line.Content, contentWidth, useUnicode);

        if (line.Kind == DiffLineKind.Context)
        {
            return RenderContextCell(gutter, sigil, truncation, useColor);
        }

        return RenderChangedCell(line, gutter, sigil, truncation, isLeftSide, useColor);
    }

    private readonly record struct TruncatedContent(string Visible, int VisibleContentLength, string Padding);

    private static TruncatedContent TruncateContent(string content, int contentWidth, bool useUnicode)
    {
        if (contentWidth <= 0)
        {
            return new TruncatedContent(string.Empty, 0, string.Empty);
        }
        if (content.Length <= contentWidth)
        {
            string padding = new string(' ', contentWidth - content.Length);
            return new TruncatedContent(content, content.Length, padding);
        }
        string indicator = useUnicode ? "…" : ">";
        int visibleLen = contentWidth - 1;
        string visible = content[..visibleLen] + indicator;
        return new TruncatedContent(visible, visibleLen, string.Empty);
    }

    private static string RenderContextCell(string gutter, char sigil, TruncatedContent t, bool useColor)
    {
        if (!useColor)
        {
            return $"{gutter} {sigil} {t.Visible}{t.Padding}";
        }
        var sb = new StringBuilder();
        sb.Append(AnsiPalette.Dim).Append(gutter).Append(' ').Append(sigil).Append(AnsiPalette.DimOff);
        sb.Append(' ').Append(t.Visible).Append(t.Padding);
        return sb.ToString();
    }

    private static string RenderChangedCell(
        DiffLine line,
        string gutter,
        char sigil,
        TruncatedContent t,
        bool isLeftSide,
        bool useColor)
    {
        if (!useColor)
        {
            return $"{gutter} {sigil} {t.Visible}{t.Padding}";
        }

        string rowBg = line.Kind == DiffLineKind.Removed ? AnsiPalette.BgRemovedRow : AnsiPalette.BgAddedRow;
        string highlightBg = line.Kind == DiffLineKind.Removed ? AnsiPalette.BgRemovedHi : AnsiPalette.BgAddedHi;
        string sigilFg = line.Kind == DiffLineKind.Removed ? AnsiPalette.FgRemovedSigil : AnsiPalette.FgAddedSigil;

        var sb = new StringBuilder();
        sb.Append(rowBg);
        sb.Append(sigilFg).Append(gutter).Append(' ').Append(sigil).Append(AnsiPalette.FgDefault).Append(' ');
        AppendVisibleContentWithHighlights(sb, t, line.InlineHighlights, rowBg, highlightBg);
        sb.Append(t.Padding);
        sb.Append(AnsiPalette.BgDefault);
        return sb.ToString();
    }

    private static void AppendVisibleContentWithHighlights(
        StringBuilder sb,
        TruncatedContent t,
        IReadOnlyList<ColumnRange> highlights,
        string rowBg,
        string highlightBg)
    {
        // The truncation indicator (… / >) is part of t.Visible but lives at
        // index t.VisibleContentLength (when truncated). Highlights apply only
        // to indices [0, t.VisibleContentLength); ranges past that are dropped,
        // ranges crossing it are clipped.
        int visibleEnd = t.VisibleContentLength;
        var clippedRanges = new List<ColumnRange>(highlights.Count);
        foreach (ColumnRange r in highlights)
        {
            int clippedStart = Math.Min(r.Start, visibleEnd);
            int clippedEndExclusive = Math.Min(r.End, visibleEnd);
            int clippedLen = clippedEndExclusive - clippedStart;
            if (clippedLen > 0)
            {
                clippedRanges.Add(new ColumnRange(clippedStart, clippedLen));
            }
        }

        if (clippedRanges.Count == 0)
        {
            sb.Append(t.Visible);
            return;
        }

        int cursor = 0;
        foreach (ColumnRange r in clippedRanges)
        {
            if (r.Start > cursor)
            {
                int leadLen = r.Start - cursor;
                sb.Append(t.Visible, cursor, leadLen);
            }
            sb.Append(highlightBg).Append(AnsiPalette.Bold);
            sb.Append(t.Visible, r.Start, r.Length);
            sb.Append(AnsiPalette.BoldOff);
            sb.Append(rowBg);
            cursor = r.End;
        }
        if (cursor < t.Visible.Length)
        {
            int tailLen = t.Visible.Length - cursor;
            sb.Append(t.Visible, cursor, tailLen);
        }
    }

    private static string RenderHunkSeparatorCell(int cellWidth, bool useColor, bool useUnicode)
    {
        string glyph = useUnicode ? "…" : "...";
        int glyphLen = glyph.Length;
        if (cellWidth <= glyphLen)
        {
            return useColor
                ? AnsiPalette.Dim + glyph + AnsiPalette.DimOff
                : glyph;
        }
        int leftPad = (cellWidth - glyphLen) / 2;
        int rightPad = cellWidth - glyphLen - leftPad;
        string left = new string(' ', leftPad);
        string right = new string(' ', rightPad);
        if (!useColor)
        {
            return left + glyph + right;
        }
        return left + AnsiPalette.Dim + glyph + AnsiPalette.DimOff + right;
    }
}
```

### Task 14: Create `SideBySideDiffReporter` (entry-point)

**Files:**
- Create: `src/Structura.Reporting/SideBySideDiffReporter.cs`

- [ ] **Step 1: Create the file**

```csharp
using Structura.Reporting.Internal;
using Structura.Runtime;

namespace Structura.Reporting;

/// <summary>
/// Renders <see cref="IStructuraDocument.Changes"/> as a two-column side-by-side
/// diff (left = OLD, right = NEW). Reuses <see cref="DiffHunkBuilder"/> and
/// <see cref="AnsiPalette"/> from <see cref="UnifiedDiffReporter"/>.
/// </summary>
public static class SideBySideDiffReporter
{
    private static readonly SideBySideDiffOptions DefaultOptions = new();

    public static void Print(IStructuraDocument document)
    {
        bool useColor = !Console.IsOutputRedirected;
        bool useUnicode = Console.OutputEncoding.WebName == "utf-8";
        RenderTo(document, Console.Out, DefaultOptions, useColor, useUnicode);
    }

    public static void Print(IStructuraDocument document, TextWriter writer)
    {
        RenderTo(document, writer, DefaultOptions, useColor: false, useUnicode: true);
    }

    public static void Print(IStructuraDocument document, TextWriter writer, SideBySideDiffOptions options)
    {
        RenderTo(document, writer, options, useColor: false, useUnicode: true);
    }

    internal static void RenderTo(
        IStructuraDocument document,
        TextWriter writer,
        SideBySideDiffOptions options,
        bool useColor,
        bool useUnicode)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(options);

        IReadOnlyList<DocumentChange> changes = document.Changes;
        if (changes.Count == 0)
        {
            writer.WriteLine("(no changes)");
            return;
        }

        var hunkBuilder = new DiffHunkBuilder();
        UnifiedDiffOptions hunkOptions = new()
        {
            ContextLines = options.ContextLines,
            InlineHighlight = options.InlineHighlight,
        };
        IReadOnlyList<DiffLine> lines = hunkBuilder.Build(document, hunkOptions);

        int additions = 0;
        int removals = 0;
        int maxLineNumber = 1;
        foreach (DiffLine line in lines)
        {
            if (line.Kind == DiffLineKind.Added)
            {
                additions++;
            }
            else if (line.Kind == DiffLineKind.Removed)
            {
                removals++;
            }

            int candidate = Math.Max(line.OldLineNumber, line.NewLineNumber);
            if (candidate > maxLineNumber)
            {
                maxLineNumber = candidate;
            }
        }

        int gutterWidth = maxLineNumber.ToString().Length;
        int totalWidth = options.TotalWidth ?? GetConsoleWindowWidthSafe();
        int minTotal = 2 * (gutterWidth + 3) + 3 + 2;
        if (totalWidth < minTotal)
        {
            totalWidth = minTotal;
        }
        int contentWidth = (totalWidth - 2 * (gutterWidth + 3) - 3) / 2;

        DiffBanner.Write(writer, document.DocumentName, additions, removals, useColor, useUnicode);
        writer.WriteLine();

        IReadOnlyList<SideBySideRow> rows = SideBySideRowBuilder.Build(lines);
        foreach (SideBySideRow row in rows)
        {
            string rendered = SideBySideRowRenderer.Render(row, gutterWidth, contentWidth, useColor, useUnicode);
            writer.WriteLine(rendered);
        }
    }

    private static int GetConsoleWindowWidthSafe()
    {
        try
        {
            int width = Console.WindowWidth;
            return width > 0 ? width : 160;
        }
        catch (IOException)
        {
            return 160;
        }
        catch (PlatformNotSupportedException)
        {
            return 160;
        }
    }
}
```

### Task 15: Verify Phase 3 build green and commit

- [ ] **Step 1: Build solution**

Run: `dotnet build Structura.slnx`
Expected: build succeeds, no new warnings.

- [ ] **Step 2: Run all tests (existing tests should still pass; SBS not yet tested directly)**

Run: `dotnet test`
Expected: all existing + Phase 1 + Phase 2 tests pass.

- [ ] **Step 3: Commit**

```bash
git add src/Structura.Reporting/SideBySideDiffOptions.cs \
        src/Structura.Reporting/SideBySideDiffReporter.cs \
        src/Structura.Reporting/Internal/SideBySideRow.cs \
        src/Structura.Reporting/Internal/SideBySideRowBuilder.cs \
        src/Structura.Reporting/Internal/SideBySideRowRenderer.cs \
        tests/Structura.UnitTests/Reporting/SideBySideRowBuilderTests.cs
git commit -m "feat: SideBySideDiffReporter with shared color scheme"
```

---

## Phase 4: Reporter tests

All tests use explicit `TotalWidth` for determinism.

### Task 16: Plain-output unit tests

**Files:**
- Create: `tests/Structura.UnitTests/Reporting/SideBySideDiffReporterTests.cs`

- [ ] **Step 1: Create the test file**

```csharp
using FluentAssertions;

using Structura.Reporting;
using Structura.Runtime;

using Xunit;

namespace Structura.UnitTests.Reporting;

public sealed class SideBySideDiffReporterTests
{
    private const string Source =
        "{\n" +
        "  \"name\": \"Alice\",\n" +
        "  \"age\": 30,\n" +
        "  \"city\": \"Paris\",\n" +
        "  \"role\": \"admin\"\n" +
        "}";

    private static SideBySideDiffOptions OptionsWithWidth(int width) =>
        new() { TotalWidth = width };

    [Fact]
    public void Print_NoChanges_WritesNoChangesMessage()
    {
        var doc = new FakeStructuraDocument(Source, System.Array.Empty<DocumentChange>(), documentName: "test.json");
        var sw = new System.IO.StringWriter();

        SideBySideDiffReporter.Print(doc, sw, OptionsWithWidth(80));

        sw.ToString().Trim().Should().Be("(no changes)");
    }

    [Fact]
    public void Print_SingleChange_BannerMatchesUnified()
    {
        int ageOffset = Source.IndexOf("30", System.StringComparison.Ordinal);
        var c = new DocumentChange("/age", new TextSpan(ageOffset, 2), "30", "42");
        var doc = new FakeStructuraDocument(Source, new[] { c }, documentName: "test.json")
        {
            CurrentTextOverride = Source[..ageOffset] + "42" + Source[(ageOffset + 2)..],
        };

        var unifiedSw = new System.IO.StringWriter();
        UnifiedDiffReporter.Print(doc, unifiedSw);
        var sbsSw = new System.IO.StringWriter();
        SideBySideDiffReporter.Print(doc, sbsSw, OptionsWithWidth(120));

        // Banner is the first 2 lines (then a blank line).
        string[] unifiedLines = unifiedSw.ToString().Split('\n');
        string[] sbsLines = sbsSw.ToString().Split('\n');
        sbsLines[0].Should().Be(unifiedLines[0]);
        sbsLines[1].Should().Be(unifiedLines[1]);
    }

    [Fact]
    public void Print_SingleChange_BothColumnsShowAgeLine()
    {
        int ageOffset = Source.IndexOf("30", System.StringComparison.Ordinal);
        var c = new DocumentChange("/age", new TextSpan(ageOffset, 2), "30", "42");
        var doc = new FakeStructuraDocument(Source, new[] { c }, documentName: "test.json")
        {
            CurrentTextOverride = Source[..ageOffset] + "42" + Source[(ageOffset + 2)..],
        };
        var sw = new System.IO.StringWriter();

        SideBySideDiffReporter.Print(doc, sw, OptionsWithWidth(120));

        string output = sw.ToString();
        // Separator " │ " must appear on every body row.
        output.Should().Contain(" │ ");
        // Removed line on left, added on right.
        output.Should().Contain(" - ");
        output.Should().Contain(" + ");
        output.Should().Contain("\"age\": 30,");
        output.Should().Contain("\"age\": 42,");
    }

    [Fact]
    public void Print_StringWriter_NoAnsiEscapes()
    {
        int ageOffset = Source.IndexOf("30", System.StringComparison.Ordinal);
        var c = new DocumentChange("/age", new TextSpan(ageOffset, 2), "30", "42");
        var doc = new FakeStructuraDocument(Source, new[] { c }, documentName: "test.json")
        {
            CurrentTextOverride = Source[..ageOffset] + "42" + Source[(ageOffset + 2)..],
        };
        var sw = new System.IO.StringWriter();

        SideBySideDiffReporter.Print(doc, sw, OptionsWithWidth(120));

        sw.ToString().Should().NotContain("\x1b");
    }

    [Fact]
    public void Print_TwoFarChanges_HunkSeparatorRowOnBothSides()
    {
        // 30 lines, change on line 1 and line 25 → separated by > 2*ContextLines.
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("{");
        for (var i = 0; i < 28; i++)
        {
            sb.AppendLine($"  \"k{i}\": {i},");
        }
        sb.Append("}");
        string src = sb.ToString();

        int firstOffset = src.IndexOf("\"k0\": 0", System.StringComparison.Ordinal);
        int firstLen = "\"k0\": 0".Length;
        int secondOffset = src.IndexOf("\"k20\": 20", System.StringComparison.Ordinal);
        int secondLen = "\"k20\": 20".Length;

        var changes = new[]
        {
            new DocumentChange("/k0", new TextSpan(firstOffset, firstLen), "\"k0\": 0", "\"k0\": 999"),
            new DocumentChange("/k20", new TextSpan(secondOffset, secondLen), "\"k20\": 20", "\"k20\": 888"),
        };
        string current =
            src[..firstOffset] + "\"k0\": 999" +
            src[(firstOffset + firstLen)..secondOffset] + "\"k20\": 888" +
            src[(secondOffset + secondLen)..];
        var doc = new FakeStructuraDocument(src, changes, documentName: "test.json")
        {
            CurrentTextOverride = current,
        };
        var sw = new System.IO.StringWriter();

        SideBySideDiffReporter.Print(doc, sw, OptionsWithWidth(120));

        string output = sw.ToString();
        // Hunk separator row contains "…" on both halves.
        string[] outputLines = output.Split('\n');
        bool hasSeparatorRow = outputLines.Any(l => l.Contains("…") && l.Contains(" │ "));
        hasSeparatorRow.Should().BeTrue();
    }

    [Fact]
    public void Print_AddedOnly_LeftSideEmpty()
    {
        // Replace "30," with "30,\n  \"new_key\": 1,\n" — adds a brand-new line.
        int ageEnd = Source.IndexOf("30,", System.StringComparison.Ordinal) + "30,".Length;
        var c = new DocumentChange("/new_key", new TextSpan(ageEnd, 0), string.Empty, "\n  \"new_key\": 1,");
        string current = Source[..ageEnd] + "\n  \"new_key\": 1," + Source[ageEnd..];
        var doc = new FakeStructuraDocument(Source, new[] { c }, documentName: "test.json")
        {
            CurrentTextOverride = current,
        };
        var sw = new System.IO.StringWriter();

        SideBySideDiffReporter.Print(doc, sw, OptionsWithWidth(120));

        string output = sw.ToString();
        output.Should().Contain("\"new_key\": 1,");
        // There must be at least one row whose left side is whitespace before " │ ".
        string[] outputLines = output.Split('\n');
        bool hasEmptyLeft = outputLines.Any(l =>
        {
            int sepIdx = l.IndexOf(" │ ", System.StringComparison.Ordinal);
            if (sepIdx < 0) { return false; }
            string leftPart = l[..sepIdx];
            return leftPart.Trim().Length == 0;
        });
        hasEmptyLeft.Should().BeTrue();
    }

    [Fact]
    public void Print_ContentLongerThanColumn_TruncatedWithEllipsis()
    {
        const string longSource = "{\n  \"x\": \"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"\n}";
        int xOffset = longSource.IndexOf("\"aaa", System.StringComparison.Ordinal);
        int xLen = longSource.LastIndexOf('"') - xOffset + 1;
        string oldLiteral = longSource.Substring(xOffset, xLen);
        var c = new DocumentChange("/x", new TextSpan(xOffset, xLen), oldLiteral, "\"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\"");
        string current = longSource[..xOffset] + "\"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\"" + longSource[(xOffset + xLen)..];
        var doc = new FakeStructuraDocument(longSource, new[] { c }, documentName: "test.json")
        {
            CurrentTextOverride = current,
        };
        var sw = new System.IO.StringWriter();

        // Narrow width forces truncation.
        SideBySideDiffReporter.Print(doc, sw, OptionsWithWidth(40));

        sw.ToString().Should().Contain("…");
    }

    [Fact]
    public void Print_ContextLinesZero_NoContextRows()
    {
        int ageOffset = Source.IndexOf("30", System.StringComparison.Ordinal);
        var c = new DocumentChange("/age", new TextSpan(ageOffset, 2), "30", "42");
        var doc = new FakeStructuraDocument(Source, new[] { c }, documentName: "test.json")
        {
            CurrentTextOverride = Source[..ageOffset] + "42" + Source[(ageOffset + 2)..],
        };
        var sw = new System.IO.StringWriter();

        var options = new SideBySideDiffOptions { TotalWidth = 120, ContextLines = 0 };
        SideBySideDiffReporter.Print(doc, sw, options);

        string output = sw.ToString();
        // Body must contain - and + rows but no plain-context lines containing other keys.
        output.Should().NotContain("\"name\":");
        output.Should().NotContain("\"city\":");
        output.Should().Contain("\"age\": 30,");
        output.Should().Contain("\"age\": 42,");
    }

    [Fact]
    public void Print_NullDocument_Throws()
    {
        var sw = new System.IO.StringWriter();

        var act = () => SideBySideDiffReporter.Print(null!, sw);

        act.Should().Throw<System.ArgumentNullException>();
    }

    [Fact]
    public void Print_NullWriter_Throws()
    {
        var doc = new FakeStructuraDocument(Source, System.Array.Empty<DocumentChange>());

        var act = () => SideBySideDiffReporter.Print(doc, null!);

        act.Should().Throw<System.ArgumentNullException>();
    }
}
```

- [ ] **Step 2: Run tests**

Run: `dotnet test --filter "FullyQualifiedName~SideBySideDiffReporterTests"`
Expected: PASS, 10 tests.

### Task 17: Color unit tests

**Files:**
- Create: `tests/Structura.UnitTests/Reporting/SideBySideDiffReporterColorTests.cs`

- [ ] **Step 1: Create the test file**

```csharp
using FluentAssertions;

using Structura.Reporting;
using Structura.Reporting.Internal;
using Structura.Runtime;

using Xunit;

namespace Structura.UnitTests.Reporting;

public sealed class SideBySideDiffReporterColorTests
{
    private const string Source =
        "{\n" +
        "  \"age\": 30\n" +
        "}";

    private static FakeStructuraDocument MakeDoc()
    {
        int ageOffset = Source.IndexOf("30", System.StringComparison.Ordinal);
        var c = new DocumentChange("/age", new TextSpan(ageOffset, 2), "30", "42");
        return new FakeStructuraDocument(Source, new[] { c }, documentName: "test.json")
        {
            CurrentTextOverride = Source[..ageOffset] + "42" + Source[(ageOffset + 2)..],
        };
    }

    private static string RenderColored(SideBySideDiffOptions options)
    {
        var doc = MakeDoc();
        var sw = new System.IO.StringWriter();
        SideBySideDiffReporter.RenderTo(doc, sw, options, useColor: true, useUnicode: true);
        return sw.ToString();
    }

    [Fact]
    public void Print_ColorEnabled_RemovedCellHasRowBg()
    {
        string output = RenderColored(new SideBySideDiffOptions { TotalWidth = 120 });

        output.Should().Contain(AnsiPalette.BgRemovedRow);
        output.Should().Contain(AnsiPalette.BgAddedRow);
    }

    [Fact]
    public void Print_ColorEnabled_InlineHighlightOn_HasHighlightEscape()
    {
        string output = RenderColored(new SideBySideDiffOptions { TotalWidth = 120, InlineHighlight = true });

        output.Should().Contain(AnsiPalette.BgRemovedHi);
        output.Should().Contain(AnsiPalette.BgAddedHi);
    }

    [Fact]
    public void Print_ColorEnabled_InlineHighlightOff_NoHighlightEscape()
    {
        string output = RenderColored(new SideBySideDiffOptions { TotalWidth = 120, InlineHighlight = false });

        output.Should().NotContain(AnsiPalette.BgRemovedHi);
        output.Should().NotContain(AnsiPalette.BgAddedHi);
    }

    [Fact]
    public void Print_ColorEnabled_SeparatorIsDimmed()
    {
        string output = RenderColored(new SideBySideDiffOptions { TotalWidth = 120 });

        // " {Dim}│{DimOff} " must appear at least once.
        string sep = $" {AnsiPalette.Dim}│{AnsiPalette.DimOff} ";
        output.Should().Contain(sep);
    }

    [Fact]
    public void Print_ColorEnabled_HunkSeparatorIsDimmed()
    {
        // Build a doc with two changes far apart so a hunk separator is emitted.
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("{");
        for (var i = 0; i < 28; i++)
        {
            sb.AppendLine($"  \"k{i}\": {i},");
        }
        sb.Append("}");
        string src = sb.ToString();

        int firstOffset = src.IndexOf("\"k0\": 0", System.StringComparison.Ordinal);
        int firstLen = "\"k0\": 0".Length;
        int secondOffset = src.IndexOf("\"k20\": 20", System.StringComparison.Ordinal);
        int secondLen = "\"k20\": 20".Length;
        var changes = new[]
        {
            new DocumentChange("/k0", new TextSpan(firstOffset, firstLen), "\"k0\": 0", "\"k0\": 999"),
            new DocumentChange("/k20", new TextSpan(secondOffset, secondLen), "\"k20\": 20", "\"k20\": 888"),
        };
        string current =
            src[..firstOffset] + "\"k0\": 999" +
            src[(firstOffset + firstLen)..secondOffset] + "\"k20\": 888" +
            src[(secondOffset + secondLen)..];
        var doc = new FakeStructuraDocument(src, changes, documentName: "test.json")
        {
            CurrentTextOverride = current,
        };
        var sw = new System.IO.StringWriter();

        SideBySideDiffReporter.RenderTo(doc, sw, new SideBySideDiffOptions { TotalWidth = 120 }, useColor: true, useUnicode: true);

        string output = sw.ToString();
        // The hunk-separator cell wraps the … in Dim/DimOff.
        string sepWrapped = AnsiPalette.Dim + "…" + AnsiPalette.DimOff;
        output.Should().Contain(sepWrapped);
    }
}
```

- [ ] **Step 2: Run tests**

Run: `dotnet test --filter "FullyQualifiedName~SideBySideDiffReporterColorTests"`
Expected: PASS, 5 tests.

### Task 18: Integration tests on `order.sample.json`

**Files:**
- Create: `tests/Structura.IntegrationTests/Reporting/OrderSampleJsonSideBySideDiffTests.cs`

- [ ] **Step 1: Create the test file**

```csharp
using FluentAssertions;

using Structura.Generated;
using Structura.Reporting;
using Structura.Runtime;

using Xunit;

namespace Structura.IntegrationTests.Reporting;

public sealed class OrderSampleJsonSideBySideDiffTests
{
    private static string LoadSample() =>
        File.ReadAllText("order.sample.json");

    [Fact]
    public void SideBySideDiffReporter_NoMutation_PrintsNoChanges()
    {
        var order = LoadSample().ParseJson<OrderSampleJson>();
        var sw = new StringWriter();

        SideBySideDiffReporter.Print(order, sw);

        sw.ToString().Trim().Should().Be("(no changes)");
    }

    [Fact]
    public void SideBySideDiffReporter_DocumentName_FromSourceFileName()
    {
        var order = LoadSample().ParseJson<OrderSampleJson>();
        order.Currency = "USD";
        var sw = new StringWriter();

        SideBySideDiffReporter.Print(order, sw, new SideBySideDiffOptions { TotalWidth = 200 });

        string output = sw.ToString();
        output.Should().Contain("Patched(order.sample.json)");
        output.Should().Contain("Patched order.sample.json with");
    }

    [Fact]
    public void SideBySideDiffReporter_RealMutations_BannerAndExpectedRows()
    {
        var order = LoadSample().ParseJson<OrderSampleJson>();
        order.Currency = "USD";
        order.Version = 42;
        order.IsPriority = false;
        order.Customer.FirstName = "Ivan";
        order.Customer.Preferences.MarketingConsent = false;
        order.BillingAddress.City = "Rotterdam";
        order.Items[0].Quantity = 2;
        order.Items[1].Manufacturer.CountryCode = "DE";
        var sw = new StringWriter();

        SideBySideDiffReporter.Print(order, sw, new SideBySideDiffOptions { TotalWidth = 200 });

        string output = sw.ToString();
        output.Should().Contain("Patched order.sample.json with 8 additions and 8 removals");
        // Both pairs of literal values appear in the output.
        output.Should().Contain("\"version\": 7,");
        output.Should().Contain("\"version\": 42,");
        output.Should().Contain("\"currency\": \"RUB\"");
        output.Should().Contain("\"currency\": \"USD\"");
        // Separator appears on body rows.
        output.Should().Contain(" │ ");
    }
}
```

- [ ] **Step 2: Run integration tests**

Run: `dotnet test tests/Structura.IntegrationTests/Structura.IntegrationTests.csproj --filter "FullyQualifiedName~OrderSampleJsonSideBySideDiff"`
Expected: PASS, 3 tests.

### Task 19: Run full test suite and commit Phase 4

- [ ] **Step 1: Run full suite**

Run: `dotnet test`
Expected: all unit + integration tests pass, including the previously existing ones.

- [ ] **Step 2: Commit**

```bash
git add tests/Structura.UnitTests/Reporting/SideBySideDiffReporterTests.cs \
        tests/Structura.UnitTests/Reporting/SideBySideDiffReporterColorTests.cs \
        tests/Structura.IntegrationTests/Reporting/OrderSampleJsonSideBySideDiffTests.cs
git commit -m "test: SideBySideDiffReporter unit + color + integration tests"
```

---

## Phase 5: Demo

### Task 20: Add SBS demo blocks to `Program.cs`

**Files:**
- Modify: `src/Structura/Program.cs`

- [ ] **Step 1: Add SBS block after each Unified block**

In `src/Structura/Program.cs`, after the existing JSON unified block:

```csharp
Console.WriteLine("=== Diff (UnifiedDiffReporter) ===");
UnifiedDiffReporter.Print(order);
Console.WriteLine();
```

Append:

```csharp
Console.WriteLine("=== Diff (SideBySideDiffReporter) ===");
SideBySideDiffReporter.Print(order);
Console.WriteLine();
```

Repeat the same pattern for the BLRWBL XML block (after `UnifiedDiffReporter.Print(waybill);`):

```csharp
Console.WriteLine("=== BLRWBL Diff (SideBySideDiffReporter) ===");
SideBySideDiffReporter.Print(waybill);
Console.WriteLine();
```

For the Library XML block (after `UnifiedDiffReporter.Print(library);`):

```csharp
Console.WriteLine("=== Library Diff (SideBySideDiffReporter) ===");
SideBySideDiffReporter.Print(library);
Console.WriteLine();
```

For the Library JSON block (after `UnifiedDiffReporter.Print(libraryDoc);`):

```csharp
Console.WriteLine("=== Library JSON Diff (SideBySideDiffReporter) ===");
SideBySideDiffReporter.Print(libraryDoc);
Console.WriteLine();
```

- [ ] **Step 2: Build and run demo**

Run: `dotnet run --project src/Structura/Structura.csproj`
Expected: demo prints all four samples, each with both a Unified and a SideBySide block. Spot-check:
- Banner identical between Unified and SBS.
- Side-by-side body has ` │ ` separator on every body row.
- On a TTY: left column with `-` rows on dark red, right column with `+` rows on dark green; both fill the full column width.

- [ ] **Step 3: Verify redirected output is clean**

Run: `dotnet run --project src/Structura/Structura.csproj > /tmp/structura-demo.txt`
Then: `grep -c $'\x1b' /tmp/structura-demo.txt`
Expected: `0` — no ANSI escape codes in redirected output.

- [ ] **Step 4: Commit**

```bash
git add src/Structura/Program.cs
git commit -m "demo: add SideBySideDiffReporter blocks alongside Unified"
```

---

## Verification (post-merge sanity)

After all 5 phases land, do a final pass:

- [ ] `dotnet build Structura.slnx` — clean build.
- [ ] `dotnet test` — all green (existing + new).
- [ ] `dotnet run --project src/Structura/Structura.csproj` in an interactive terminal — visual confirmation:
    - Both Unified and SideBySide blocks render.
    - Banners identical between them.
    - SideBySide: dim separator ` │ `, full-column row backgrounds, inline-highlight on changed segments per side, dim ellipsis hunk-separators when present, truncation with `…` on narrow terminals.
- [ ] `dotnet run --project src/Structura/Structura.csproj > out.txt` — file contains zero ANSI escapes.
