# Step 15 — Unified `DiffReporter` with auto layout — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Collapse `UnifiedDiffReporter` and `SideBySideDiffReporter` into one
public `DiffReporter` that auto-picks layout from console width and content
width, with a user-overridable `DiffReporterLayout` setting; delete the legacy
`SimpleReporter` and `ConsoleDiffReporter`.

**Architecture:** `DiffReporter` is a thin public facade in
`src/Structura.Reporting/`. It builds the hunk once via `DiffHunkBuilder`,
resolves `Auto` against `Console.WindowWidth` (or a 120-column fallback) using
`maxContentLen` and `gutterWidth`, then delegates to an internal renderer.
The existing `UnifiedDiffReporter.RenderTo` and `SideBySideDiffReporter.RenderTo`
bodies move under `Internal/` as `UnifiedRenderer` / `SideBySideRenderer` and
keep their current signatures. Existing reporter tests are re-pointed to
`DiffReporter` with an explicit `Layout`; a small new `DiffReporterLayoutTests`
covers the heuristic. The spec is
`docs/plans/step-15-diff-reporter-with-layout.md`.

**Tech Stack:** C# 13, .NET 10, xUnit 2.9, FluentAssertions 7.

---

## File map

Created:
- `src/Structura.Reporting/DiffReporterLayout.cs`
- `src/Structura.Reporting/DiffReporter.cs`
- `src/Structura.Reporting/Internal/UnifiedRenderer.cs`
- `src/Structura.Reporting/Internal/SideBySideRenderer.cs`
- `tests/Structura.UnitTests/Reporting/DiffReporterTests.cs` (re-pointed unified tests)
- `tests/Structura.UnitTests/Reporting/DiffReporterSideBySideTests.cs` (re-pointed SBS tests)
- `tests/Structura.UnitTests/Reporting/DiffReporterSideBySideColorTests.cs` (re-pointed SBS color tests)
- `tests/Structura.UnitTests/Reporting/DiffReporterLayoutTests.cs` (new heuristic tests)

Modified:
- `src/Structura.Reporting/DiffReporterOptions.cs` — add `Layout` property.
- `src/Structura/Program.cs` — collapse 8 paired reporter calls into one per pipeline.
- `tests/Structura.IntegrationTests/Reporting/OrderSampleJsonReportingTests.cs` — drop `SimpleReporter` and `ConsoleDiffReporter` blocks; re-point Unified block to `DiffReporter`.
- `tests/Structura.IntegrationTests/Reporting/OrderSampleJsonSideBySideDiffTests.cs` — re-point to `DiffReporter` with `Layout = SideBySide`.

Deleted:
- `src/Structura.Reporting/SimpleReporter.cs`
- `src/Structura.Reporting/ConsoleDiffReporter.cs`
- `src/Structura.Reporting/UnifiedDiffReporter.cs`
- `src/Structura.Reporting/SideBySideDiffReporter.cs`
- `tests/Structura.UnitTests/Reporting/SimpleReporterTests.cs`
- `tests/Structura.UnitTests/Reporting/ConsoleDiffReporterTests.cs`
- `tests/Structura.UnitTests/Reporting/UnifiedDiffReporterTests.cs`
- `tests/Structura.UnitTests/Reporting/SideBySideDiffReporterTests.cs`
- `tests/Structura.UnitTests/Reporting/SideBySideDiffReporterColorTests.cs`

Unchanged (used as-is):
- `src/Structura.Reporting/Internal/DiffBanner.cs`
- `src/Structura.Reporting/Internal/DiffHunkBuilder.cs`
- `src/Structura.Reporting/Internal/DiffLine.cs`
- `src/Structura.Reporting/Internal/DiffLineRenderer.cs`
- `src/Structura.Reporting/Internal/DiffStats.cs`
- `src/Structura.Reporting/Internal/SideBySideRow.cs`
- `src/Structura.Reporting/Internal/SideBySideRowBuilder.cs`
- `src/Structura.Reporting/Internal/SideBySideRowRenderer.cs`
- `src/Structura.Reporting/Internal/Highlighting/*`
- `tests/Structura.UnitTests/Reporting/FakeStructuraDocument.cs`
- `tests/Structura.UnitTests/Reporting/DiffHunkBuilderTests.cs`
- `tests/Structura.UnitTests/Reporting/DiffLineRendererTests.cs`
- `tests/Structura.UnitTests/Reporting/SideBySideRowBuilderTests.cs`
- `tests/Structura.UnitTests/Reporting/Highlighting/*`

---

### Task 1: Add `DiffReporterLayout` enum and `Layout` property on options

**Files:**
- Create: `src/Structura.Reporting/DiffReporterLayout.cs`
- Modify: `src/Structura.Reporting/DiffReporterOptions.cs`
- Test: `tests/Structura.UnitTests/Reporting/DiffReporterTests.cs` (created here, will grow over the next tasks)

- [ ] **Step 1: Write the failing test**

Create `tests/Structura.UnitTests/Reporting/DiffReporterTests.cs`:

```csharp
using FluentAssertions;

using Structura.Reporting;

using Xunit;

namespace Structura.UnitTests.Reporting;

public sealed class DiffReporterTests
{
    [Fact]
    public void DiffReporterOptions_Defaults_LayoutIsAuto()
    {
        var options = new DiffReporterOptions();

        options.Layout.Should().Be(DiffReporterLayout.Auto);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Structura.slnx --filter "FullyQualifiedName~DiffReporterOptions_Defaults_LayoutIsAuto"`
Expected: build error — `DiffReporterLayout` and `DiffReporterOptions.Layout` do not exist.

- [ ] **Step 3: Add the enum**

Create `src/Structura.Reporting/DiffReporterLayout.cs`:

```csharp
namespace Structura.Reporting;

/// <summary>
/// Selects which layout <see cref="DiffReporter"/> emits.
/// <see cref="Auto"/> picks side-by-side when the terminal is wide enough for
/// the longest content line (or for an acceptable per-side minimum), otherwise
/// falls back to unified. <see cref="Unified"/> and <see cref="SideBySide"/>
/// override the heuristic and force that layout regardless of width.
/// </summary>
public enum DiffReporterLayout
{
    Auto,
    Unified,
    SideBySide,
}
```

- [ ] **Step 4: Add the `Layout` property**

In `src/Structura.Reporting/DiffReporterOptions.cs`, inside the existing
`DiffReporterOptions` record, add:

```csharp
    /// <summary>
    /// Selects the layout. <see cref="DiffReporterLayout.Auto"/> picks
    /// side-by-side when the terminal is wide enough for the longest content
    /// line (or for an acceptable per-side minimum), otherwise falls back to
    /// unified. <see cref="DiffReporterLayout.Unified"/> and
    /// <see cref="DiffReporterLayout.SideBySide"/> force that layout
    /// regardless of width. Default <see cref="DiffReporterLayout.Auto"/>.
    /// </summary>
    public DiffReporterLayout Layout { get; init; } = DiffReporterLayout.Auto;
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test Structura.slnx --filter "FullyQualifiedName~DiffReporterOptions_Defaults_LayoutIsAuto"`
Expected: PASS.

- [ ] **Step 6: Run full test suite**

Run: `dotnet test Structura.slnx`
Expected: all existing tests still pass — adding the property is additive.

- [ ] **Step 7: Commit**

```bash
git add src/Structura.Reporting/DiffReporterLayout.cs \
        src/Structura.Reporting/DiffReporterOptions.cs \
        tests/Structura.UnitTests/Reporting/DiffReporterTests.cs
git commit -m "feat(reporting): add DiffReporterLayout enum and Options.Layout property"
```

---

### Task 2: Move `UnifiedDiffReporter.RenderTo` body into `Internal/UnifiedRenderer`

**Files:**
- Create: `src/Structura.Reporting/Internal/UnifiedRenderer.cs`
- Modify: `src/Structura.Reporting/UnifiedDiffReporter.cs`

This is a pure move with no behaviour change. `UnifiedDiffReporter` keeps the
public `Print` overloads for now so existing tests still pass; the `RenderTo`
body lives in `Internal/UnifiedRenderer.RenderTo`.

- [ ] **Step 1: Create the internal renderer**

Create `src/Structura.Reporting/Internal/UnifiedRenderer.cs`:

```csharp
using Structura.Reporting.Internal.Highlighting;
using Structura.Runtime;

namespace Structura.Reporting.Internal;

/// <summary>
/// Unified (single-column) renderer. Writes the banner, then one line per
/// <see cref="DiffLine"/> using <see cref="DiffLineRenderer"/>. Called by the
/// public <see cref="Structura.Reporting.DiffReporter"/> after it has built
/// the line list and chosen the layout.
/// </summary>
internal static class UnifiedRenderer
{
    public static void RenderTo(
        IStructuraDocument document,
        TextWriter writer,
        DiffReporterOptions options,
        IReadOnlyList<DiffLine> lines,
        DiffStats stats,
        int gutterWidth,
        bool useColor,
        bool useUnicode)
    {
        IDiffSyntaxPainter painter = useColor
            ? PainterFactory.For(document, options.SyntaxHighlight)
            : NullPainter.Instance;

        DiffBanner.Write(writer, document.DocumentName, stats.Additions, stats.Removals, useColor, useUnicode);
        writer.WriteLine();

        foreach (DiffLine line in lines)
        {
            string rendered = DiffLineRenderer.Render(line, gutterWidth, useColor, useUnicode, painter);
            writer.WriteLine(rendered);
        }
    }
}
```

- [ ] **Step 2: Re-route `UnifiedDiffReporter.RenderTo` through the helper**

Replace `src/Structura.Reporting/UnifiedDiffReporter.cs` with:

```csharp
using Structura.Reporting.Internal;
using Structura.Runtime;

namespace Structura.Reporting;

/// <summary>
/// Public wrapper kept temporarily during the Step 15 migration. The render
/// loop now lives in <see cref="Internal.UnifiedRenderer"/>. This class is
/// removed once <see cref="DiffReporter"/> is wired up and all tests have
/// been re-pointed.
/// </summary>
public static class UnifiedDiffReporter
{
    private static readonly DiffReporterOptions DefaultOptions = new DiffReporterOptions();

    public static void Print(IStructuraDocument document)
    {
        Print(document, DefaultOptions);
    }

    public static void Print(IStructuraDocument document, DiffReporterOptions options)
    {
        bool useColor = !Console.IsOutputRedirected;
        bool useUnicode = Console.OutputEncoding.WebName == "utf-8";
        RenderTo(document, Console.Out, options, useColor, useUnicode);
    }

    public static void Print(IStructuraDocument document, TextWriter writer)
    {
        RenderTo(document, writer, DefaultOptions, useColor: false, useUnicode: true);
    }

    public static void Print(IStructuraDocument document, TextWriter writer, DiffReporterOptions options)
    {
        RenderTo(document, writer, options, useColor: false, useUnicode: true);
    }

    internal static void RenderTo(
        IStructuraDocument document,
        TextWriter writer,
        DiffReporterOptions options,
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

        IReadOnlyList<DiffLine> lines = DiffHunkBuilder.Build(document, options);
        DiffStats stats = DiffStats.Compute(lines);
        int gutterWidth = stats.MaxLineNumber.ToString().Length;

        UnifiedRenderer.RenderTo(document, writer, options, lines, stats, gutterWidth, useColor, useUnicode);
    }
}
```

- [ ] **Step 3: Run full test suite**

Run: `dotnet test Structura.slnx`
Expected: all reporter tests still pass (this is a pure move).

- [ ] **Step 4: Commit**

```bash
git add src/Structura.Reporting/Internal/UnifiedRenderer.cs \
        src/Structura.Reporting/UnifiedDiffReporter.cs
git commit -m "refactor(reporting): move UnifiedDiffReporter render loop into Internal/UnifiedRenderer"
```

---

### Task 3: Move `SideBySideDiffReporter.RenderTo` body into `Internal/SideBySideRenderer`

**Files:**
- Create: `src/Structura.Reporting/Internal/SideBySideRenderer.cs`
- Modify: `src/Structura.Reporting/SideBySideDiffReporter.cs`

Same shape as Task 2 — pure move, public wrapper kept temporarily so existing
SBS tests still build.

- [ ] **Step 1: Create the internal renderer**

Create `src/Structura.Reporting/Internal/SideBySideRenderer.cs`:

```csharp
using Structura.Reporting.Internal.Highlighting;
using Structura.Runtime;

namespace Structura.Reporting.Internal;

/// <summary>
/// Side-by-side (two-column) renderer. Writes the banner, then one
/// <see cref="SideBySideRow"/> per row via <see cref="SideBySideRowRenderer"/>.
/// Layout constants ("min content per side" etc.) live in
/// <see cref="Structura.Reporting.DiffReporter"/> so the same numbers feed both
/// the Auto heuristic and the render path.
/// </summary>
internal static class SideBySideRenderer
{
    public static void RenderTo(
        IStructuraDocument document,
        TextWriter writer,
        DiffReporterOptions options,
        IReadOnlyList<DiffLine> lines,
        DiffStats stats,
        int gutterWidth,
        int contentWidth,
        bool useColor,
        bool useUnicode)
    {
        IDiffSyntaxPainter painter = useColor
            ? PainterFactory.For(document, options.SyntaxHighlight)
            : NullPainter.Instance;

        DiffBanner.Write(writer, document.DocumentName, stats.Additions, stats.Removals, useColor, useUnicode);
        writer.WriteLine();

        IReadOnlyList<SideBySideRow> rows = SideBySideRowBuilder.Build(lines);
        foreach (SideBySideRow row in rows)
        {
            string rendered = SideBySideRowRenderer.Render(row, gutterWidth, contentWidth, useColor, useUnicode, painter);
            writer.WriteLine(rendered);
        }
    }
}
```

- [ ] **Step 2: Re-route `SideBySideDiffReporter.RenderTo` through the helper**

Replace `src/Structura.Reporting/SideBySideDiffReporter.cs` with:

```csharp
using Structura.Reporting.Internal;
using Structura.Runtime;

namespace Structura.Reporting;

/// <summary>
/// Public wrapper kept temporarily during the Step 15 migration. The render
/// loop now lives in <see cref="Internal.SideBySideRenderer"/>. This class is
/// removed once <see cref="DiffReporter"/> is wired up and all tests have
/// been re-pointed.
/// </summary>
public static class SideBySideDiffReporter
{
    private const int FallbackTotalWidth = 160;
    private const int CellPaddingChars = 3;
    private const int SeparatorChars = 3;
    private const int MinContentPerSide = 1;

    private static readonly DiffReporterOptions DefaultOptions = new DiffReporterOptions();

    public static void Print(IStructuraDocument document)
    {
        Print(document, DefaultOptions);
    }

    public static void Print(IStructuraDocument document, DiffReporterOptions options)
    {
        bool useColor = !Console.IsOutputRedirected;
        bool useUnicode = Console.OutputEncoding.WebName == "utf-8";
        int totalWidth = ComputeTotalWidth();
        RenderTo(document, Console.Out, options, totalWidth, useColor, useUnicode);
    }

    public static void Print(IStructuraDocument document, TextWriter writer)
    {
        RenderTo(document, writer, DefaultOptions, FallbackTotalWidth, useColor: false, useUnicode: true);
    }

    public static void Print(IStructuraDocument document, TextWriter writer, DiffReporterOptions options)
    {
        RenderTo(document, writer, options, FallbackTotalWidth, useColor: false, useUnicode: true);
    }

    internal static void RenderTo(
        IStructuraDocument document,
        TextWriter writer,
        DiffReporterOptions options,
        int totalWidth,
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

        IReadOnlyList<DiffLine> lines = DiffHunkBuilder.Build(document, options);
        DiffStats stats = DiffStats.Compute(lines);

        int gutterWidth = stats.MaxLineNumber.ToString().Length;
        int minTotal = 2 * (gutterWidth + CellPaddingChars) + SeparatorChars + 2 * MinContentPerSide;
        int width = totalWidth;
        if (width < minTotal)
        {
            width = minTotal;
        }
        int contentWidth = (width - 2 * (gutterWidth + CellPaddingChars) - SeparatorChars) / 2;

        SideBySideRenderer.RenderTo(document, writer, options, lines, stats, gutterWidth, contentWidth, useColor, useUnicode);
    }

    private static int ComputeTotalWidth()
    {
        int width;
        try
        {
            width = Console.WindowWidth;
        }
        catch (IOException)
        {
            return FallbackTotalWidth;
        }
        catch (PlatformNotSupportedException)
        {
            return FallbackTotalWidth;
        }
        return Math.Max(width, FallbackTotalWidth);
    }
}
```

- [ ] **Step 3: Run full test suite**

Run: `dotnet test Structura.slnx`
Expected: all SBS tests still pass.

- [ ] **Step 4: Commit**

```bash
git add src/Structura.Reporting/Internal/SideBySideRenderer.cs \
        src/Structura.Reporting/SideBySideDiffReporter.cs
git commit -m "refactor(reporting): move SideBySideDiffReporter render loop into Internal/SideBySideRenderer"
```

---

### Task 4: Implement public `DiffReporter` with explicit-layout delegation

**Files:**
- Create: `src/Structura.Reporting/DiffReporter.cs`
- Test: `tests/Structura.UnitTests/Reporting/DiffReporterTests.cs` (extend Task 1's file)

`DiffReporter` is added as the new public surface. In this task it only
honours `Layout = Unified` and `Layout = SideBySide`; the `Auto` branch is
implemented in Task 5. Tests cover the explicit layouts plus null-guards.

- [ ] **Step 1: Extend the test file**

In `tests/Structura.UnitTests/Reporting/DiffReporterTests.cs`, replace the
file contents with:

```csharp
using FluentAssertions;

using Structura.Reporting;
using Structura.Reporting.Internal.Highlighting;
using Structura.Runtime;

using Xunit;

namespace Structura.UnitTests.Reporting;

public sealed class DiffReporterTests
{
    private const string Source =
        "{\n" +
        "  \"name\": \"Alice\",\n" +
        "  \"age\": 30,\n" +
        "  \"city\": \"Paris\"\n" +
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

    [Fact]
    public void DiffReporterOptions_Defaults_LayoutIsAuto()
    {
        var options = new DiffReporterOptions();

        options.Layout.Should().Be(DiffReporterLayout.Auto);
    }

    [Fact]
    public void Print_NoChanges_WritesNoChangesMessage()
    {
        var doc = new FakeStructuraDocument(Source, System.Array.Empty<DocumentChange>(), documentName: "test.json");
        var sw = new System.IO.StringWriter();

        DiffReporter.Print(doc, sw);

        sw.ToString().Trim().Should().Be("(no changes)");
    }

    [Fact]
    public void Print_LayoutUnified_BannerAndUnifiedHunk()
    {
        var doc = MakeDoc();
        var sw = new System.IO.StringWriter();

        var options = new DiffReporterOptions { Layout = DiffReporterLayout.Unified };
        DiffReporter.Print(doc, sw, options);

        string output = sw.ToString();
        output.Should().Contain("● Patched");
        output.Should().Contain("└ Patched test.json with 1 addition and 1 removal");
        output.Should().Contain(" - ");
        output.Should().Contain(" + ");
        output.Should().Contain("\"age\": 30,");
        output.Should().Contain("\"age\": 42,");
        // Unified layout does not emit the SBS separator.
        output.Should().NotContain(" │ ");
    }

    [Fact]
    public void Print_LayoutSideBySide_BothColumnsShowAgeLine()
    {
        var doc = MakeDoc();
        var sw = new System.IO.StringWriter();

        var options = new DiffReporterOptions { Layout = DiffReporterLayout.SideBySide };
        DiffReporter.Print(doc, sw, options);

        string output = sw.ToString();
        output.Should().Contain(" │ ");
        output.Should().Contain("\"age\": 30,");
        output.Should().Contain("\"age\": 42,");
    }

    [Fact]
    public void Print_StringWriter_NoAnsiEscapes()
    {
        var doc = MakeDoc();
        var sw = new System.IO.StringWriter();

        DiffReporter.Print(doc, sw, new DiffReporterOptions { Layout = DiffReporterLayout.Unified });

        sw.ToString().Should().NotContain("\x1b");
    }

    [Fact]
    public void Print_NullDocument_Throws()
    {
        System.Action act = () => DiffReporter.Print(null!, new System.IO.StringWriter());
        act.Should().Throw<System.ArgumentNullException>();
    }

    [Fact]
    public void Print_NullWriter_Throws()
    {
        var doc = new FakeStructuraDocument("x", System.Array.Empty<DocumentChange>());
        System.Action act = () => DiffReporter.Print(doc, writer: null!);
        act.Should().Throw<System.ArgumentNullException>();
    }

    [Fact]
    public void RenderTo_ColorEnabled_SyntaxHighlightOn_AppliesKeyFg()
    {
        var doc = MakeDoc();
        var sw = new System.IO.StringWriter();

        DiffReporter.RenderTo(
            doc,
            sw,
            new DiffReporterOptions { Layout = DiffReporterLayout.Unified },
            terminalWidth: 120,
            useColor: true,
            useUnicode: true);

        string output = sw.ToString();
        output.Should().Contain(SyntaxPalette.Bright(TokenKind.Key));
    }

    [Fact]
    public void RenderTo_ColorEnabled_SyntaxHighlightOff_NoTokenFg()
    {
        var doc = MakeDoc();
        var sw = new System.IO.StringWriter();

        var options = new DiffReporterOptions
        {
            Layout = DiffReporterLayout.Unified,
            SyntaxHighlight = false,
        };
        DiffReporter.RenderTo(doc, sw, options, terminalWidth: 120, useColor: true, useUnicode: true);

        string output = sw.ToString();
        output.Should().NotContain(SyntaxPalette.Bright(TokenKind.Key));
        output.Should().NotContain(SyntaxPalette.Bright(TokenKind.Number));
    }
}
```

- [ ] **Step 2: Run tests — expect FAIL**

Run: `dotnet test Structura.slnx --filter "FullyQualifiedName~DiffReporterTests"`
Expected: build error — `DiffReporter` does not exist yet.

- [ ] **Step 3: Implement `DiffReporter`**

Create `src/Structura.Reporting/DiffReporter.cs`:

```csharp
using Structura.Reporting.Internal;
using Structura.Runtime;

namespace Structura.Reporting;

/// <summary>
/// Renders <see cref="IStructuraDocument.Changes"/> as a unified or
/// side-by-side diff. <see cref="DiffReporterOptions.Layout"/> controls the
/// choice: <see cref="DiffReporterLayout.Unified"/> and
/// <see cref="DiffReporterLayout.SideBySide"/> force a layout;
/// <see cref="DiffReporterLayout.Auto"/> (the default) picks side-by-side when
/// the terminal is wide enough for the longest content line, otherwise falls
/// back to unified.
/// </summary>
public static class DiffReporter
{
    private const int FallbackTotalWidth = 120;
    private const int CellPaddingChars = 3;
    private const int SeparatorChars = 3;
    private const int MinContentPerSide = 40;

    private static readonly DiffReporterOptions DefaultOptions = new DiffReporterOptions();

    public static void Print(IStructuraDocument document)
    {
        Print(document, DefaultOptions);
    }

    public static void Print(IStructuraDocument document, DiffReporterOptions options)
    {
        bool useColor = !Console.IsOutputRedirected;
        bool useUnicode = Console.OutputEncoding.WebName == "utf-8";
        int terminalWidth = ComputeTerminalWidth();
        RenderTo(document, Console.Out, options, terminalWidth, useColor, useUnicode);
    }

    public static void Print(IStructuraDocument document, TextWriter writer)
    {
        RenderTo(document, writer, DefaultOptions, FallbackTotalWidth, useColor: false, useUnicode: true);
    }

    public static void Print(IStructuraDocument document, TextWriter writer, DiffReporterOptions options)
    {
        RenderTo(document, writer, options, FallbackTotalWidth, useColor: false, useUnicode: true);
    }

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

        IReadOnlyList<DocumentChange> changes = document.Changes;
        if (changes.Count == 0)
        {
            writer.WriteLine("(no changes)");
            return;
        }

        IReadOnlyList<DiffLine> lines = DiffHunkBuilder.Build(document, options);
        DiffStats stats = DiffStats.Compute(lines);
        int gutterWidth = stats.MaxLineNumber.ToString().Length;

        // Auto resolution lands in Task 5; for now Auto behaves like Unified.
        DiffReporterLayout resolved = options.Layout switch
        {
            DiffReporterLayout.SideBySide => DiffReporterLayout.SideBySide,
            _ => DiffReporterLayout.Unified,
        };

        if (resolved == DiffReporterLayout.Unified)
        {
            UnifiedRenderer.RenderTo(document, writer, options, lines, stats, gutterWidth, useColor, useUnicode);
            return;
        }

        int contentWidth = ComputeSideBySideContentWidth(terminalWidth, gutterWidth);
        SideBySideRenderer.RenderTo(document, writer, options, lines, stats, gutterWidth, contentWidth, useColor, useUnicode);
    }

    private static int ComputeSideBySideContentWidth(int terminalWidth, int gutterWidth)
    {
        int minTotal = 2 * (gutterWidth + CellPaddingChars) + SeparatorChars + 2 * MinContentPerSide;
        int width = terminalWidth;
        if (width < minTotal)
        {
            width = minTotal;
        }
        return (width - 2 * (gutterWidth + CellPaddingChars) - SeparatorChars) / 2;
    }

    private static int ComputeTerminalWidth()
    {
        int width;
        try
        {
            width = Console.WindowWidth;
        }
        catch (IOException)
        {
            return FallbackTotalWidth;
        }
        catch (PlatformNotSupportedException)
        {
            return FallbackTotalWidth;
        }
        return Math.Max(width, FallbackTotalWidth);
    }
}
```

- [ ] **Step 4: Run `DiffReporterTests` — expect PASS**

Run: `dotnet test Structura.slnx --filter "FullyQualifiedName~DiffReporterTests"`
Expected: PASS.

- [ ] **Step 5: Run full test suite**

Run: `dotnet test Structura.slnx`
Expected: all existing tests still pass.

- [ ] **Step 6: Commit**

```bash
git add src/Structura.Reporting/DiffReporter.cs \
        tests/Structura.UnitTests/Reporting/DiffReporterTests.cs
git commit -m "feat(reporting): add DiffReporter facade with explicit Unified/SideBySide layouts"
```

---

### Task 5: Implement `Auto` layout heuristic in `DiffReporter`

**Files:**
- Modify: `src/Structura.Reporting/DiffReporter.cs`
- Test: `tests/Structura.UnitTests/Reporting/DiffReporterLayoutTests.cs` (new)

The heuristic: side-by-side iff `terminalWidth >= 2*(gutter+3) + 3 + 2*MinContentPerSide`
(uses `MinContentPerSide = 40` so the SBS rows stay readable). When the
natural side-by-side width also fits, no truncation occurs.

- [ ] **Step 1: Write the failing layout tests**

Create `tests/Structura.UnitTests/Reporting/DiffReporterLayoutTests.cs`:

```csharp
using FluentAssertions;

using Structura.Reporting;
using Structura.Runtime;

using Xunit;

namespace Structura.UnitTests.Reporting;

public sealed class DiffReporterLayoutTests
{
    private const string Source =
        "{\n" +
        "  \"name\": \"Alice\",\n" +
        "  \"age\": 30,\n" +
        "  \"city\": \"Paris\"\n" +
        "}";

    private static FakeStructuraDocument MakeDoc(string source = Source, string newAgeLiteral = "42")
    {
        int ageOffset = source.IndexOf("30", System.StringComparison.Ordinal);
        var c = new DocumentChange("/age", new TextSpan(ageOffset, 2), "30", newAgeLiteral);
        return new FakeStructuraDocument(source, new[] { c }, documentName: "test.json")
        {
            CurrentTextOverride = source[..ageOffset] + newAgeLiteral + source[(ageOffset + 2)..],
        };
    }

    private static string Render(IStructuraDocument doc, int terminalWidth, DiffReporterLayout? forced = null)
    {
        var sw = new System.IO.StringWriter();
        var options = forced is DiffReporterLayout f
            ? new DiffReporterOptions { Layout = f }
            : new DiffReporterOptions();
        DiffReporter.RenderTo(doc, sw, options, terminalWidth, useColor: false, useUnicode: true);
        return sw.ToString();
    }

    [Fact]
    public void Auto_PicksSideBySide_When_NaturalWidthFits()
    {
        var doc = MakeDoc();
        string output = Render(doc, terminalWidth: 200);

        output.Should().Contain(" │ ");
        // Natural fit — no truncation indicator on the content lines.
        output.Should().NotContain("…");
    }

    [Fact]
    public void Auto_PicksSideBySide_When_AboveMinButBelowNatural()
    {
        // Long content forces truncation; 100 cols is above MinSbs but below
        // the natural side-by-side width, so the layout stays SBS with `…`.
        const string longSource =
            "{\n" +
            "  \"x\": \"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\n" +
            "  \"age\": 30\n" +
            "}";

        var doc = MakeDoc(longSource);
        string output = Render(doc, terminalWidth: 100);

        output.Should().Contain(" │ ");
        output.Should().Contain("…");
    }

    [Fact]
    public void Auto_PicksUnified_When_BelowMinSideBySide()
    {
        // 60 cols < 2*(1+3) + 3 + 2*40 = 91, so SBS is too cramped → Unified.
        var doc = MakeDoc();
        string output = Render(doc, terminalWidth: 60);

        output.Should().NotContain(" │ ");
        output.Should().Contain(" - ");
        output.Should().Contain(" + ");
    }

    [Fact]
    public void Explicit_Unified_Forces_UnifiedEvenAtWideTerminal()
    {
        var doc = MakeDoc();
        string output = Render(doc, terminalWidth: 200, forced: DiffReporterLayout.Unified);

        output.Should().NotContain(" │ ");
    }

    [Fact]
    public void Explicit_SideBySide_Forces_SbsEvenAtNarrowTerminal()
    {
        var doc = MakeDoc();
        string output = Render(doc, terminalWidth: 40, forced: DiffReporterLayout.SideBySide);

        output.Should().Contain(" │ ");
    }
}
```

- [ ] **Step 2: Run tests — expect FAIL on Auto branches**

Run: `dotnet test Structura.slnx --filter "FullyQualifiedName~DiffReporterLayoutTests"`
Expected: `Auto_PicksSideBySide_*` fail (current Auto falls through to
Unified, so no `│` separator appears); the explicit-layout tests pass.

- [ ] **Step 3: Implement the heuristic**

In `src/Structura.Reporting/DiffReporter.cs`, replace the body of `RenderTo`
starting from the `// Auto resolution lands in Task 5` comment through the
end of the method with:

```csharp
        int maxContentLen = ComputeMaxContentLen(lines);
        DiffReporterLayout resolved = ResolveLayout(options.Layout, terminalWidth, gutterWidth, maxContentLen);

        if (resolved == DiffReporterLayout.Unified)
        {
            UnifiedRenderer.RenderTo(document, writer, options, lines, stats, gutterWidth, useColor, useUnicode);
            return;
        }

        int contentWidth = ComputeSideBySideContentWidth(terminalWidth, gutterWidth);
        SideBySideRenderer.RenderTo(document, writer, options, lines, stats, gutterWidth, contentWidth, useColor, useUnicode);
    }

    private static DiffReporterLayout ResolveLayout(DiffReporterLayout requested, int terminalWidth, int gutterWidth, int maxContentLen)
    {
        if (requested != DiffReporterLayout.Auto)
        {
            return requested;
        }
        int minSbsWidth = 2 * (gutterWidth + CellPaddingChars) + SeparatorChars + 2 * MinContentPerSide;
        return terminalWidth >= minSbsWidth ? DiffReporterLayout.SideBySide : DiffReporterLayout.Unified;
    }

    private static int ComputeMaxContentLen(IReadOnlyList<DiffLine> lines)
    {
        var max = 0;
        foreach (DiffLine line in lines)
        {
            if (line.Content.Length > max)
            {
                max = line.Content.Length;
            }
        }
        return max;
    }
```

Note: `maxContentLen` is computed and passed in for future heuristic tuning
(e.g. preferring Unified when SBS would severely truncate); the current
decision only depends on `gutterWidth` and `terminalWidth` against
`MinContentPerSide`. Keep the parameter; the spec calls out the inputs.

- [ ] **Step 4: Run layout tests — expect PASS**

Run: `dotnet test Structura.slnx --filter "FullyQualifiedName~DiffReporterLayoutTests"`
Expected: PASS.

- [ ] **Step 5: Run full test suite**

Run: `dotnet test Structura.slnx`
Expected: PASS. The existing unified/SBS tests are unaffected because they
pass explicit `Layout` (or use the old wrapper classes that still exist).

- [ ] **Step 6: Commit**

```bash
git add src/Structura.Reporting/DiffReporter.cs \
        tests/Structura.UnitTests/Reporting/DiffReporterLayoutTests.cs
git commit -m "feat(reporting): implement DiffReporter Auto layout heuristic"
```

---

### Task 6: Re-point Unified tests onto `DiffReporter`

**Files:**
- Modify: `tests/Structura.UnitTests/Reporting/UnifiedDiffReporterTests.cs` (rewritten as `DiffReporterUnifiedTests` — see Step 1)
- Delete: `tests/Structura.UnitTests/Reporting/UnifiedDiffReporterTests.cs` (replaced by the new file in Step 4)

The existing `UnifiedDiffReporterTests` already mirrors the
`DiffReporterTests` coverage written in Task 4. Its cases that aren't
duplicated (`Print_MultipleChanges_BannerUsesPlural`,
`Print_DocumentNameFromFake_AppearsInBanner`, `Print_ShowFullFile_RendersAllLines`)
are folded into the unified-only fixture file.

- [ ] **Step 1: Create the replacement fixture**

Create `tests/Structura.UnitTests/Reporting/DiffReporterUnifiedTests.cs`:

```csharp
using FluentAssertions;

using Structura.Reporting;
using Structura.Runtime;

using Xunit;

namespace Structura.UnitTests.Reporting;

public sealed class DiffReporterUnifiedTests
{
    private const string Source =
        "{\n" +
        "  \"name\": \"Alice\",\n" +
        "  \"age\": 30,\n" +
        "  \"city\": \"Paris\"\n" +
        "}";

    private static DiffReporterOptions Unified()
    {
        return new DiffReporterOptions { Layout = DiffReporterLayout.Unified };
    }

    [Fact]
    public void Print_MultipleChanges_BannerUsesPlural()
    {
        int nameOffset = Source.IndexOf("\"Alice\"", System.StringComparison.Ordinal);
        int ageOffset = Source.IndexOf("30", System.StringComparison.Ordinal);
        var changes = new[]
        {
            new DocumentChange("/name", new TextSpan(nameOffset, 7), "\"Alice\"", "\"Bob\""),
            new DocumentChange("/age", new TextSpan(ageOffset, 2), "30", "42"),
        };
        string current = Source[..nameOffset] + "\"Bob\"" + Source[(nameOffset + 7)..ageOffset] + "42" + Source[(ageOffset + 2)..];
        var doc = new FakeStructuraDocument(Source, changes, documentName: "test.json")
        {
            CurrentTextOverride = current,
        };
        var sw = new System.IO.StringWriter();

        DiffReporter.Print(doc, sw, Unified());

        sw.ToString().Should().Contain("with 2 additions and 2 removals");
    }

    [Fact]
    public void Print_DocumentNameFromFake_AppearsInBanner()
    {
        int ageOffset = Source.IndexOf("30", System.StringComparison.Ordinal);
        var c = new DocumentChange("/age", new TextSpan(ageOffset, 2), "30", "42");
        var doc = new FakeStructuraDocument(Source, new[] { c }, documentName: "alpha/beta.json")
        {
            CurrentTextOverride = Source[..ageOffset] + "42" + Source[(ageOffset + 2)..],
        };
        var sw = new System.IO.StringWriter();

        DiffReporter.Print(doc, sw, Unified());

        sw.ToString().Should().Contain("Patched alpha/beta.json with");
    }

    [Fact]
    public void Print_ShowFullFile_RendersAllLines()
    {
        int ageOffset = Source.IndexOf("30", System.StringComparison.Ordinal);
        var c = new DocumentChange("/age", new TextSpan(ageOffset, 2), "30", "42");
        var doc = new FakeStructuraDocument(Source, new[] { c }, documentName: "test.json")
        {
            CurrentTextOverride = Source[..ageOffset] + "42" + Source[(ageOffset + 2)..],
        };
        var sw = new System.IO.StringWriter();

        var options = new DiffReporterOptions
        {
            Layout = DiffReporterLayout.Unified,
            ShowFullFile = true,
        };
        DiffReporter.Print(doc, sw, options);

        string output = sw.ToString();
        output.Should().Contain("\"name\": \"Alice\",");
        output.Should().Contain("\"city\": \"Paris\"");
        output.Should().Contain("\"age\": 30,");
        output.Should().Contain("\"age\": 42,");
        output.Should().NotContain("…");
    }
}
```

- [ ] **Step 2: Run the new fixture — expect PASS**

Run: `dotnet test Structura.slnx --filter "FullyQualifiedName~DiffReporterUnifiedTests"`
Expected: PASS.

- [ ] **Step 3: Delete the obsolete fixture**

```bash
git rm tests/Structura.UnitTests/Reporting/UnifiedDiffReporterTests.cs
```

- [ ] **Step 4: Run full suite — expect PASS**

Run: `dotnet test Structura.slnx`
Expected: PASS. Coverage of the unified path is now spread across
`DiffReporterTests` (Task 4) and `DiffReporterUnifiedTests`.

- [ ] **Step 5: Commit**

```bash
git add tests/Structura.UnitTests/Reporting/DiffReporterUnifiedTests.cs
git commit -m "test(reporting): re-point unified reporter tests onto DiffReporter"
```

---

### Task 7: Re-point Side-by-Side tests onto `DiffReporter`

**Files:**
- Create: `tests/Structura.UnitTests/Reporting/DiffReporterSideBySideTests.cs`
- Create: `tests/Structura.UnitTests/Reporting/DiffReporterSideBySideColorTests.cs`
- Delete: `tests/Structura.UnitTests/Reporting/SideBySideDiffReporterTests.cs`
- Delete: `tests/Structura.UnitTests/Reporting/SideBySideDiffReporterColorTests.cs`

The two SBS fixtures (plain + color) are re-pointed onto `DiffReporter`. Their
helper `Render(doc, sw, totalWidth[, options])` calls `DiffReporter.RenderTo`
with `Layout = SideBySide`. The truncation case at `totalWidth: 40` keeps the
explicit SBS request — that test asserts truncation behaviour, not the Auto
heuristic.

- [ ] **Step 1: Create the plain SBS fixture**

Create `tests/Structura.UnitTests/Reporting/DiffReporterSideBySideTests.cs`:

```csharp
using FluentAssertions;

using Structura.Reporting;
using Structura.Runtime;

using Xunit;

namespace Structura.UnitTests.Reporting;

public sealed class DiffReporterSideBySideTests
{
    private const string Source =
        "{\n" +
        "  \"name\": \"Alice\",\n" +
        "  \"age\": 30,\n" +
        "  \"city\": \"Paris\",\n" +
        "  \"role\": \"admin\"\n" +
        "}";

    private static void Render(IStructuraDocument doc, System.IO.StringWriter sw, int totalWidth, DiffReporterOptions? options = null)
    {
        DiffReporterOptions effective = options ?? new DiffReporterOptions();
        var withLayout = effective with { Layout = DiffReporterLayout.SideBySide };
        DiffReporter.RenderTo(doc, sw, withLayout, totalWidth, useColor: false, useUnicode: true);
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
        DiffReporter.Print(doc, unifiedSw, new DiffReporterOptions { Layout = DiffReporterLayout.Unified });
        var sbsSw = new System.IO.StringWriter();
        Render(doc, sbsSw, 120);

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

        Render(doc, sw, 120);

        string output = sw.ToString();
        output.Should().Contain(" │ ");
        output.Should().Contain(" - ");
        output.Should().Contain(" + ");
        output.Should().Contain("\"age\": 30,");
        output.Should().Contain("\"age\": 42,");
    }

    [Fact]
    public void Print_TwoFarChanges_HunkSeparatorRowOnBothSides()
    {
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

        Render(doc, sw, 120);

        string output = sw.ToString();
        string[] outputLines = output.Split('\n');
        bool hasSeparatorRow = outputLines.Any(l => l.Contains("…") && l.Contains(" │ "));
        hasSeparatorRow.Should().BeTrue();
    }

    [Fact]
    public void Print_AddedOnly_LeftSideEmpty()
    {
        int ageEnd = Source.IndexOf("30,", System.StringComparison.Ordinal) + "30,".Length;
        var c = new DocumentChange("/new_key", new TextSpan(ageEnd, 0), string.Empty, "\n  \"new_key\": 1,");
        string current = Source[..ageEnd] + "\n  \"new_key\": 1," + Source[ageEnd..];
        var doc = new FakeStructuraDocument(Source, new[] { c }, documentName: "test.json")
        {
            CurrentTextOverride = current,
        };
        var sw = new System.IO.StringWriter();

        Render(doc, sw, 120);

        string output = sw.ToString();
        output.Should().Contain("\"new_key\": 1,");
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

        Render(doc, sw, 40);

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

        var options = new DiffReporterOptions { ContextLines = 0 };
        Render(doc, sw, 120, options);

        string output = sw.ToString();
        output.Should().NotContain("\"name\":");
        output.Should().NotContain("\"city\":");
        output.Should().Contain("\"age\": 30,");
        output.Should().Contain("\"age\": 42,");
    }

    [Fact]
    public void Print_ShowFullFile_RendersAllLines()
    {
        int ageOffset = Source.IndexOf("30", System.StringComparison.Ordinal);
        var c = new DocumentChange("/age", new TextSpan(ageOffset, 2), "30", "42");
        var doc = new FakeStructuraDocument(Source, new[] { c }, documentName: "test.json")
        {
            CurrentTextOverride = Source[..ageOffset] + "42" + Source[(ageOffset + 2)..],
        };
        var sw = new System.IO.StringWriter();

        var options = new DiffReporterOptions { ShowFullFile = true };
        Render(doc, sw, 120, options);

        string output = sw.ToString();
        output.Should().Contain("\"name\": \"Alice\",");
        output.Should().Contain("\"city\": \"Paris\"");
        output.Should().Contain("\"role\": \"admin\"");
        output.Should().Contain("\"age\": 30,");
        output.Should().Contain("\"age\": 42,");
        output.Should().NotContain("…");
    }

    [Fact]
    public void Print_ShowFullFile_TwoFarChanges_NoEllipsisSeparator()
    {
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

        var options = new DiffReporterOptions { ShowFullFile = true };
        Render(doc, sw, 120, options);

        string output = sw.ToString();
        output.Should().NotContain("…");
        output.Should().Contain("\"k10\": 10,");
        output.Should().Contain("\"k15\": 15,");
    }

    [Fact]
    public void Print_ContextLineNumberingDivergesAfterPriorHunk()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine("  \"first\": 1,");
        for (var i = 0; i < 18; i++)
        {
            sb.AppendLine($"  \"k{i}\": {i},");
        }
        sb.AppendLine("  \"last\": 99");
        sb.Append("}");
        string src = sb.ToString();

        int firstOffset = src.IndexOf("\"first\": 1", System.StringComparison.Ordinal);
        int firstLen = "\"first\": 1".Length;
        int lastOffset = src.IndexOf("\"last\": 99", System.StringComparison.Ordinal);
        int lastLen = "\"last\": 99".Length;

        var firstChange = new DocumentChange(
            "/first",
            new TextSpan(firstOffset, firstLen),
            "\"first\": 1",
            "\"first_a\": 1,\n  \"first_b\": 1");
        var lastChange = new DocumentChange(
            "/last",
            new TextSpan(lastOffset, lastLen),
            "\"last\": 99",
            "\"last\": 100");

        string current =
            src[..firstOffset] + "\"first_a\": 1,\n  \"first_b\": 1" +
            src[(firstOffset + firstLen)..lastOffset] + "\"last\": 100" +
            src[(lastOffset + lastLen)..];

        var doc = new FakeStructuraDocument(src, new[] { firstChange, lastChange }, documentName: "test.json")
        {
            CurrentTextOverride = current,
        };
        var sw = new System.IO.StringWriter();

        Render(doc, sw, 120);

        string output = sw.ToString();
        string[] outputLines = output.Split('\n');

        bool foundDivergent = outputLines.Any(line =>
        {
            int sepIdx = line.IndexOf(" │ ", System.StringComparison.Ordinal);
            if (sepIdx < 0) { return false; }
            string left = line[..sepIdx];
            string right = line[(sepIdx + 3)..];
            var leftTrim = left.TrimStart();
            var rightTrim = right.TrimStart();
            if (leftTrim.Length == 0 || rightTrim.Length == 0) { return false; }
            int leftEnd = 0;
            while (leftEnd < leftTrim.Length && char.IsDigit(leftTrim[leftEnd])) { leftEnd++; }
            int rightEnd = 0;
            while (rightEnd < rightTrim.Length && char.IsDigit(rightTrim[rightEnd])) { rightEnd++; }
            if (leftEnd == 0 || rightEnd == 0) { return false; }
            int leftNum = int.Parse(leftTrim[..leftEnd]);
            int rightNum = int.Parse(rightTrim[..rightEnd]);
            return leftNum != rightNum;
        });

        foundDivergent.Should().BeTrue("after a 1→2 line replacement, context-row gutters in later hunks should diverge");
    }
}
```

- [ ] **Step 2: Create the color SBS fixture**

Create `tests/Structura.UnitTests/Reporting/DiffReporterSideBySideColorTests.cs`:

```csharp
using FluentAssertions;

using Structura.Reporting;
using Structura.Reporting.Internal;
using Structura.Reporting.Internal.Highlighting;
using Structura.Runtime;

using Xunit;

namespace Structura.UnitTests.Reporting;

public sealed class DiffReporterSideBySideColorTests
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

    private static string RenderColored(DiffReporterOptions options, int totalWidth = 120)
    {
        var doc = MakeDoc();
        var sw = new System.IO.StringWriter();
        var withLayout = options with { Layout = DiffReporterLayout.SideBySide };
        DiffReporter.RenderTo(doc, sw, withLayout, totalWidth, useColor: true, useUnicode: true);
        return sw.ToString();
    }

    [Fact]
    public void Print_ColorEnabled_RemovedAndAddedCellsHaveRowBg()
    {
        string output = RenderColored(new DiffReporterOptions());

        output.Should().Contain(AnsiPalette.BgRemovedRow);
        output.Should().Contain(AnsiPalette.BgAddedRow);
    }

    [Fact]
    public void Print_ColorEnabled_InlineHighlightOn_HasHighlightEscape()
    {
        string output = RenderColored(new DiffReporterOptions { InlineHighlight = true });

        output.Should().Contain(AnsiPalette.BgRemovedHi);
        output.Should().Contain(AnsiPalette.BgAddedHi);
    }

    [Fact]
    public void Print_ColorEnabled_InlineHighlightOff_NoHighlightEscape()
    {
        string output = RenderColored(new DiffReporterOptions { InlineHighlight = false });

        output.Should().NotContain(AnsiPalette.BgRemovedHi);
        output.Should().NotContain(AnsiPalette.BgAddedHi);
    }
}
```

If the previous `SideBySideDiffReporterColorTests` had more cases beyond
these three, port them into the new fixture as well by reading the original
file and mapping each helper call to `DiffReporter.RenderTo(..., Layout =
SideBySide)`.

- [ ] **Step 3: Run the new fixtures — expect PASS**

Run: `dotnet test Structura.slnx --filter "FullyQualifiedName~DiffReporterSideBySide"`
Expected: PASS.

- [ ] **Step 4: Delete the obsolete fixtures**

```bash
git rm tests/Structura.UnitTests/Reporting/SideBySideDiffReporterTests.cs
git rm tests/Structura.UnitTests/Reporting/SideBySideDiffReporterColorTests.cs
```

- [ ] **Step 5: Run full suite — expect PASS**

Run: `dotnet test Structura.slnx`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add tests/Structura.UnitTests/Reporting/DiffReporterSideBySideTests.cs \
        tests/Structura.UnitTests/Reporting/DiffReporterSideBySideColorTests.cs
git commit -m "test(reporting): re-point SBS reporter tests onto DiffReporter"
```

---

### Task 8: Re-point integration tests and drop legacy reporter blocks

**Files:**
- Modify: `tests/Structura.IntegrationTests/Reporting/OrderSampleJsonReportingTests.cs`
- Modify: `tests/Structura.IntegrationTests/Reporting/OrderSampleJsonSideBySideDiffTests.cs`

Drop the `SimpleReporter` and `ConsoleDiffReporter` test blocks (those
reporters are deleted in Task 10). Re-point the unified and SBS blocks onto
`DiffReporter`.

- [ ] **Step 1: Rewrite `OrderSampleJsonReportingTests.cs`**

Replace the file contents with:

```csharp
using FluentAssertions;

using Structura.Generated;
using Structura.Reporting;
using Structura.Runtime;

using Xunit;

namespace Structura.IntegrationTests.Reporting;

/// <summary>
/// End-to-end tests through the generator-produced <see cref="OrderSampleJson"/>:
/// real <c>order.sample.json</c> → mutate → render via <see cref="DiffReporter"/>
/// in unified layout → assert plain-text output via <see cref="StringWriter"/>.
/// </summary>
public sealed class OrderSampleJsonReportingTests
{
    private static string LoadSample()
    {
        return File.ReadAllText("order.sample.json");
    }

    private static DiffReporterOptions Unified()
    {
        return new DiffReporterOptions { Layout = DiffReporterLayout.Unified };
    }

    [Fact]
    public void DiffReporter_NoMutation_PrintsNoChanges()
    {
        var order = LoadSample().ParseJson<OrderSampleJson>();
        var sw = new StringWriter();

        DiffReporter.Print(order, sw, Unified());

        sw.ToString().Trim().Should().Be("(no changes)");
    }

    [Fact]
    public void DiffReporter_DocumentName_FromSourceFileName()
    {
        var order = LoadSample().ParseJson<OrderSampleJson>();
        order.Currency = "USD";
        var sw = new StringWriter();

        DiffReporter.Print(order, sw, Unified());

        sw.ToString().Should().Contain("Patched order.sample.json with");
    }

    [Fact]
    public void DiffReporter_RealMutations_BannerAndHunks()
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

        DiffReporter.Print(order, sw, Unified());

        string output = sw.ToString();
        output.Should().Contain("Patched order.sample.json with 8 additions and 8 removals");
        output.Should().Contain(" - ").And.Contain(" + ");
        output.Should().Contain("\"RUB\"");
        output.Should().Contain("\"USD\"");
        output.Should().Contain("Rotterdam");
        output.Should().Contain("Ivan");
    }
}
```

- [ ] **Step 2: Rewrite `OrderSampleJsonSideBySideDiffTests.cs`**

Replace the file contents with:

```csharp
using FluentAssertions;

using Structura.Generated;
using Structura.Reporting;
using Structura.Runtime;

using Xunit;

namespace Structura.IntegrationTests.Reporting;

public sealed class OrderSampleJsonSideBySideDiffTests
{
    private static string LoadSample() => File.ReadAllText("order.sample.json");

    private static void RenderSideBySide(IStructuraDocument doc, StringWriter sw, int totalWidth)
    {
        var options = new DiffReporterOptions { Layout = DiffReporterLayout.SideBySide };
        DiffReporter.RenderTo(doc, sw, options, totalWidth, useColor: false, useUnicode: true);
    }

    [Fact]
    public void DiffReporter_SideBySide_NoMutation_PrintsNoChanges()
    {
        var order = LoadSample().ParseJson<OrderSampleJson>();
        var sw = new StringWriter();

        DiffReporter.Print(order, sw, new DiffReporterOptions { Layout = DiffReporterLayout.SideBySide });

        sw.ToString().Trim().Should().Be("(no changes)");
    }

    [Fact]
    public void DiffReporter_SideBySide_DocumentName_FromSourceFileName()
    {
        var order = LoadSample().ParseJson<OrderSampleJson>();
        order.Currency = "USD";
        var sw = new StringWriter();

        RenderSideBySide(order, sw, totalWidth: 200);

        sw.ToString().Should().Contain("Patched order.sample.json with");
    }

    [Fact]
    public void DiffReporter_SideBySide_RealMutations_BannerAndExpectedRows()
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

        RenderSideBySide(order, sw, totalWidth: 200);

        string output = sw.ToString();
        output.Should().Contain("Patched order.sample.json with 8 additions and 8 removals");
        output.Should().Contain("\"version\": 7,");
        output.Should().Contain("\"version\": 42,");
        output.Should().Contain("\"currency\": \"RUB\"");
        output.Should().Contain("\"currency\": \"USD\"");
        output.Should().Contain(" │ ");
    }
}
```

- [ ] **Step 3: Run integration tests — expect PASS**

Run: `dotnet test Structura.slnx --filter "FullyQualifiedName~Structura.IntegrationTests.Reporting"`
Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add tests/Structura.IntegrationTests/Reporting/OrderSampleJsonReportingTests.cs \
        tests/Structura.IntegrationTests/Reporting/OrderSampleJsonSideBySideDiffTests.cs
git commit -m "test(reporting): re-point integration reporter tests onto DiffReporter"
```

---

### Task 9: Collapse `Program.cs` reporter calls onto `DiffReporter`

**Files:**
- Modify: `src/Structura/Program.cs`

All eight `UnifiedDiffReporter.Print(...)` / `SideBySideDiffReporter.Print(...)`
pairs collapse into one `DiffReporter.Print(...)` per pipeline (Auto layout).
This must happen before Tasks 10 and 11, which delete the old reporter
classes.

- [ ] **Step 1: Apply the edits**

In `src/Structura/Program.cs`, replace each of the four reporter-print
sections:

For the JSON pipeline (the original lines 38–44):

```csharp
Console.WriteLine("=== Diff (UnifiedDiffReporter) ===");
UnifiedDiffReporter.Print(order);
Console.WriteLine();

Console.WriteLine("=== Diff (SideBySideDiffReporter) ===");
SideBySideDiffReporter.Print(order);
Console.WriteLine();
```

becomes:

```csharp
Console.WriteLine("=== Diff ===");
DiffReporter.Print(order);
Console.WriteLine();
```

Apply the same substitution to the three remaining reporter sections (BLRWBL
XML, Library XML, Library JSON).

- [ ] **Step 2: Build**

Run: `dotnet build Structura.slnx`
Expected: success.

- [ ] **Step 3: Run the demo and eyeball the output**

Run: `dotnet run --project src/Structura/Structura.csproj`
Expected: each pipeline prints `=== Diff ===` followed by exactly one diff
(unified or side-by-side depending on terminal width), with the banner
wording `Patched <name> with N additions and M removals`. No more paired
sections.

- [ ] **Step 4: Commit**

```bash
git add src/Structura/Program.cs
git commit -m "chore(demo): use DiffReporter for all pipelines"
```

---

### Task 10: Drop public `UnifiedDiffReporter` and `SideBySideDiffReporter`

**Files:**
- Delete: `src/Structura.Reporting/UnifiedDiffReporter.cs`
- Delete: `src/Structura.Reporting/SideBySideDiffReporter.cs`

After Tasks 6–9 no caller references either class. Removing them shrinks the
public surface to `DiffReporter` only.

- [ ] **Step 1: Confirm no remaining references**

Run: `grep -rn "UnifiedDiffReporter\|SideBySideDiffReporter" src tests`
Expected: only matches inside the two files about to be deleted.

- [ ] **Step 2: Delete the wrapper files**

```bash
git rm src/Structura.Reporting/UnifiedDiffReporter.cs
git rm src/Structura.Reporting/SideBySideDiffReporter.cs
```

- [ ] **Step 3: Run full build + tests**

Run: `dotnet test Structura.slnx`
Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git commit -m "refactor(reporting): drop public UnifiedDiffReporter and SideBySideDiffReporter"
```

---

### Task 11: Delete `SimpleReporter`, `ConsoleDiffReporter`, and their unit tests

**Files:**
- Delete: `src/Structura.Reporting/SimpleReporter.cs`
- Delete: `src/Structura.Reporting/ConsoleDiffReporter.cs`
- Delete: `tests/Structura.UnitTests/Reporting/SimpleReporterTests.cs`
- Delete: `tests/Structura.UnitTests/Reporting/ConsoleDiffReporterTests.cs`

- [ ] **Step 1: Confirm no remaining references**

Run: `grep -rn "SimpleReporter\|ConsoleDiffReporter" src tests`
Expected: matches only inside the four files about to be deleted.

- [ ] **Step 2: Delete the legacy reporters and their tests**

```bash
git rm src/Structura.Reporting/SimpleReporter.cs
git rm src/Structura.Reporting/ConsoleDiffReporter.cs
git rm tests/Structura.UnitTests/Reporting/SimpleReporterTests.cs
git rm tests/Structura.UnitTests/Reporting/ConsoleDiffReporterTests.cs
```

- [ ] **Step 3: Run full build + tests**

Run: `dotnet test Structura.slnx`
Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git commit -m "refactor(reporting): drop SimpleReporter and ConsoleDiffReporter"
```

---

### Task 12: Final verification

- [ ] **Step 1: Run the full test suite**

Run: `dotnet test Structura.slnx`
Expected: PASS, no warnings about missing reporter types.

- [ ] **Step 2: Sanity-check the public surface**

Run: `grep -rn "public " src/Structura.Reporting/*.cs`
Expected: exactly these public symbols in `src/Structura.Reporting/`:
- `DiffReporter`
- `DiffReporterLayout`
- `DiffReporterOptions`
- No `SimpleReporter`, `ConsoleDiffReporter`, `UnifiedDiffReporter`, or
  `SideBySideDiffReporter`.

- [ ] **Step 3: Confirm no dangling references in tests**

Run: `grep -rn "UnifiedDiffReporter\|SideBySideDiffReporter\|SimpleReporter\|ConsoleDiffReporter" src tests`
Expected: no matches.

- [ ] **Step 4: Final commit if anything was patched in the previous steps**

If Steps 1–3 surfaced anything missed (typo in a removed reference, leftover
`using` directive), patch it and commit:

```bash
git add -p
git commit -m "chore(reporting): clean up trailing references after DiffReporter rollout"
```

If nothing surfaced, skip this step.
