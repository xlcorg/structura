# Step 13 — Reporter syntax highlighting Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add token-aware syntax highlighting (JSON keys/strings/numbers/keywords; XML elements/attrs/comments/entities) to `UnifiedDiffReporter` and `SideBySideDiffReporter`, on by default, zero-byte change when `useColor=false`.

**Architecture:** Stateless per-line lexers (`JsonLinePainter`, `XmlLinePainter`) hidden behind `IDiffSyntaxPainter`. Painter resolved once per `RenderTo` call by `PainterFactory.For(doc, syntaxOn)` (DocumentName extension → first-char sniff → `NullPainter`). Renderers (`DiffLineRenderer`, `SideBySideRowRenderer`) take the painter and interleave token foreground codes with existing row-bg / inline-highlight escapes. A two-tier `SyntaxPalette` (Bright for changed rows, Dim for context rows) keeps the muted-context look while colorizing tokens.

**Tech Stack:** .NET 10 / C# 13, xUnit 2.9, FluentAssertions 7, central package management. Existing `Structura.Reporting` project, internal types under `Structura.Reporting.Internal.Highlighting`.

**Spec:** `docs/plans/step-13-reporter-syntax-highlighting.md` — read it first, especially the *Token model*, *Per-line lexers*, *Palette*, and *Renderer integration* sections. The spec is binding; this plan is the execution sequence.

---

## File Structure

```
src/Structura.Reporting/
├── DiffReporterOptions.cs                     [CREATE in Task 0; MODIFY in Task 6]
├── UnifiedDiffOptions.cs                      [DELETE in Task 0]
├── SideBySideDiffOptions.cs                   [DELETE in Task 0]
├── UnifiedDiffReporter.cs                     [MODIFY in Tasks 0, 9]
├── SideBySideDiffReporter.cs                  [MODIFY in Tasks 0, 10]
└── Internal/
    ├── DiffHunkBuilder.cs                     [MODIFY in Task 0]
    ├── DiffLineRenderer.cs                    [MODIFY in Task 7]
    ├── SideBySideRowRenderer.cs               [MODIFY in Task 8]
    └── Highlighting/                          [CREATE folder in Task 1]
        ├── TokenKind.cs                       [CREATE in Task 1]
        ├── TokenRange.cs                      [CREATE in Task 1]
        ├── IDiffSyntaxPainter.cs              [CREATE in Task 1]
        ├── NullPainter.cs                     [CREATE in Task 1]
        ├── SyntaxPalette.cs                   [CREATE in Task 2]
        ├── JsonLinePainter.cs                 [CREATE in Task 3]
        ├── XmlLinePainter.cs                  [CREATE in Task 4]
        └── PainterFactory.cs                  [CREATE in Task 5]

tests/Structura.UnitTests/Reporting/
├── DiffLineRendererTests.cs                   [MODIFY in Task 7]
├── SideBySideDiffReporterColorTests.cs        [MODIFY in Task 8]
├── DiffHunkBuilderTests.cs                    [MODIFY in Task 0]
├── SideBySideDiffReporterTests.cs             [MODIFY in Task 0]
├── UnifiedDiffReporterTests.cs                [MODIFY in Task 6]
└── Reporting/Highlighting/                    [CREATE folder in Task 3]
    ├── JsonLinePainterTests.cs                [CREATE in Task 3]
    ├── XmlLinePainterTests.cs                 [CREATE in Task 4]
    └── PainterFactoryTests.cs                 [CREATE in Task 5]

tests/Structura.IntegrationTests/Reporting/
└── OrderSampleJsonSideBySideDiffTests.cs      [MODIFY in Task 0]

src/Structura/Program.cs                       (no changes — visual demo only)
```

---

## Coding conventions (binding for every task)

- File-scoped namespaces; braces always required.
- `var` only when the type is evident; otherwise type-explicit.
- Private constants and `static readonly` fields → UpperCamelCase.
- 4-space indent, LF line endings, max line length 170.
- **Never pass method-call results directly as arguments.** Save to a local variable with a meaningful name first. Example: write `var tokens = painter.TokenizeLine(line.Content); RenderContent(tokens, …);` — not `RenderContent(painter.TokenizeLine(line.Content), …)`.
- Default to **no comments**. Only add a comment when the WHY is non-obvious (a hidden constraint, a workaround). Don't explain WHAT — let names do that. Don't reference task numbers, fixes, or callers in comments.
- xUnit + FluentAssertions for tests. Test method names: `Method_Scenario_ExpectedOutcome`.
- Reporter banner wording must remain "Patched <name> with N additions and M removals" — do not paraphrase.

---

## Task 0: Prerequisite — Merge options into `DiffReporterOptions`

**Why this is first:** The two existing options records are field-identical, and `SideBySideDiffReporter.RenderTo` repackages one into the other before calling `DiffHunkBuilder.Build`. Adding `SyntaxHighlight` to both would deepen the duplication. We collapse to one record before any feature work.

**Files:**
- Create: `src/Structura.Reporting/DiffReporterOptions.cs`
- Delete: `src/Structura.Reporting/UnifiedDiffOptions.cs`
- Delete: `src/Structura.Reporting/SideBySideDiffOptions.cs`
- Modify: `src/Structura.Reporting/UnifiedDiffReporter.cs` (rename type)
- Modify: `src/Structura.Reporting/SideBySideDiffReporter.cs` (rename type, drop re-pack block lines 70–75)
- Modify: `src/Structura.Reporting/Internal/DiffHunkBuilder.cs` (rename param type)
- Modify: `tests/Structura.UnitTests/Reporting/DiffHunkBuilderTests.cs` (rename)
- Modify: `tests/Structura.UnitTests/Reporting/SideBySideDiffReporterTests.cs` (rename)
- Modify: `tests/Structura.UnitTests/Reporting/SideBySideDiffReporterColorTests.cs` (rename)
- Modify: `tests/Structura.UnitTests/Reporting/UnifiedDiffReporterTests.cs` (rename — currently doesn't reference type explicitly except via tests/options usage; verify)
- Modify: `tests/Structura.IntegrationTests/Reporting/OrderSampleJsonSideBySideDiffTests.cs` (rename)

- [ ] **Step 1: Create the new options record**

Create `src/Structura.Reporting/DiffReporterOptions.cs`:

```csharp
namespace Structura.Reporting;

/// <summary>
/// Options shared by <see cref="UnifiedDiffReporter"/> and
/// <see cref="SideBySideDiffReporter"/>. Defaults match the spec: 3 lines of
/// surrounding context, inline highlight on, full-file rendering off.
/// </summary>
public sealed record DiffReporterOptions
{
    public int ContextLines { get; init; } = 3;

    public bool InlineHighlight { get; init; } = true;

    /// <summary>
    /// When <c>true</c>, render every line of the document (no hunk grouping, no
    /// <see cref="ContextLines"/> truncation, no <c>…</c> separator). Default <c>false</c>.
    /// </summary>
    public bool ShowFullFile { get; init; } = false;
}
```

- [ ] **Step 2: Delete the old records**

```bash
rm src/Structura.Reporting/UnifiedDiffOptions.cs
rm src/Structura.Reporting/SideBySideDiffOptions.cs
```

- [ ] **Step 3: Update `UnifiedDiffReporter` — rename type references**

In `src/Structura.Reporting/UnifiedDiffReporter.cs`, replace every occurrence of `UnifiedDiffOptions` with `DiffReporterOptions`. There are five occurrences: line 15 (`DefaultOptions` field), lines 22 and 34 (`Print` overload params), line 42 (`RenderTo` param), and the doc-comment on lines 7–11 (no rename needed in prose, but the references to `UnifiedDiffOptions` in `<see cref>` if any — there are none).

Final code at the top of the class:

```csharp
public static class UnifiedDiffReporter
{
    private static readonly DiffReporterOptions DefaultOptions = new();

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

    private static void RenderTo(
        IStructuraDocument document,
        TextWriter writer,
        DiffReporterOptions options,
        bool useColor,
        bool useUnicode)
    {
        // … body unchanged …
    }
}
```

- [ ] **Step 4: Update `SideBySideDiffReporter` — rename type AND drop the repack block**

In `src/Structura.Reporting/SideBySideDiffReporter.cs`, rename `SideBySideDiffOptions` → `DiffReporterOptions` everywhere (six call sites: line 26, 33, 46, 54, 70 outer, and the `internal RenderTo` signature). Then **delete** the repack block at lines 70–75:

Old (lines 70–76):
```csharp
UnifiedDiffOptions hunkOptions = new()
{
    ContextLines = options.ContextLines,
    InlineHighlight = options.InlineHighlight,
    ShowFullFile = options.ShowFullFile,
};
IReadOnlyList<DiffLine> lines = DiffHunkBuilder.Build(document, hunkOptions);
```

New:
```csharp
IReadOnlyList<DiffLine> lines = DiffHunkBuilder.Build(document, options);
```

The `DefaultOptions` field at line 26 also changes type:

```csharp
private static readonly DiffReporterOptions DefaultOptions = new();
```

- [ ] **Step 5: Update `DiffHunkBuilder` — rename param type**

In `src/Structura.Reporting/Internal/DiffHunkBuilder.cs`, line 13:

```csharp
public static IReadOnlyList<DiffLine> Build(IStructuraDocument document, DiffReporterOptions options)
```

No other change in this file.

- [ ] **Step 6: Migrate all test references**

Run a project-wide rename across the test files. The mechanical substitutions are:
- `UnifiedDiffOptions` → `DiffReporterOptions`
- `SideBySideDiffOptions` → `DiffReporterOptions`

Files to touch (every occurrence):
- `tests/Structura.UnitTests/Reporting/DiffHunkBuilderTests.cs` — multiple `new UnifiedDiffOptions(...)` calls.
- `tests/Structura.UnitTests/Reporting/SideBySideDiffReporterTests.cs` — `SideBySideDiffOptions?` parameter type, three `new SideBySideDiffOptions { … }` literals.
- `tests/Structura.UnitTests/Reporting/SideBySideDiffReporterColorTests.cs` — six `new SideBySideDiffOptions(...)` and one `RenderColored(SideBySideDiffOptions options, …)` parameter.
- `tests/Structura.UnitTests/Reporting/UnifiedDiffReporterTests.cs` — line 120 `new UnifiedDiffOptions { ShowFullFile = true }`.
- `tests/Structura.IntegrationTests/Reporting/OrderSampleJsonSideBySideDiffTests.cs` — two `new SideBySideDiffOptions()` calls (lines 34, 54).

Use Edit's `replace_all` per file. Verify nothing else references the old names:

```bash
grep -rn "UnifiedDiffOptions\|SideBySideDiffOptions" src tests
```

Expected: empty output.

- [ ] **Step 7: Build and run all tests**

```bash
dotnet build Structura.slnx
dotnet test
```

Expected: build clean, all tests green. No new tests yet — this is a pure rename and the existing suite must pass unchanged.

- [ ] **Step 8: Commit**

```bash
git add src/Structura.Reporting tests
git commit -m "$(cat <<'EOF'
refactor(reporting): merge options into DiffReporterOptions

UnifiedDiffOptions and SideBySideDiffOptions were field-identical, and
SideBySideDiffReporter literally re-packaged one into the other before
calling DiffHunkBuilder.Build. Collapse to a single DiffReporterOptions
shared by both reporters; delete the repack block. No behavior change.

Prep for step 13 — adds SyntaxHighlight to a single record instead of two.
EOF
)"
```

---

## Task 1: Core highlighting types — `TokenKind`, `TokenRange`, `IDiffSyntaxPainter`, `NullPainter`

**Files:**
- Create: `src/Structura.Reporting/Internal/Highlighting/TokenKind.cs`
- Create: `src/Structura.Reporting/Internal/Highlighting/TokenRange.cs`
- Create: `src/Structura.Reporting/Internal/Highlighting/IDiffSyntaxPainter.cs`
- Create: `src/Structura.Reporting/Internal/Highlighting/NullPainter.cs`

These are foundational types used by every later task. No tests in this task — the types are trivial; behavior tests live with the painters that use them.

- [ ] **Step 1: Create `TokenKind` enum**

`src/Structura.Reporting/Internal/Highlighting/TokenKind.cs`:

```csharp
namespace Structura.Reporting.Internal.Highlighting;

/// <summary>
/// Format-agnostic kinds of tokens emitted by per-line painters. Punctuation
/// and Text map to "no foreground color" in <see cref="SyntaxPalette"/>; they
/// exist so the painter's output is a complete cover of the line.
/// </summary>
internal enum TokenKind
{
    Punctuation,
    Key,
    String,
    Number,
    Keyword,
    ElementName,
    AttrName,
    AttrValue,
    Comment,
    EntityRef,
    Text,
}
```

- [ ] **Step 2: Create `TokenRange`**

`src/Structura.Reporting/Internal/Highlighting/TokenRange.cs`:

```csharp
namespace Structura.Reporting.Internal.Highlighting;

/// <summary>
/// Half-open <c>[Range.Start, Range.Start + Range.Length)</c> column range
/// inside a line, paired with its <see cref="TokenKind"/>. Painters return
/// non-overlapping, sorted ranges that together cover <c>[0, content.Length)</c>.
/// </summary>
internal readonly record struct TokenRange(ColumnRange Range, TokenKind Kind);
```

`ColumnRange` already exists in `Internal/DiffLine.cs` — reused as-is.

- [ ] **Step 3: Create `IDiffSyntaxPainter`**

`src/Structura.Reporting/Internal/Highlighting/IDiffSyntaxPainter.cs`:

```csharp
namespace Structura.Reporting.Internal.Highlighting;

/// <summary>
/// Tokenizes a single rendered diff line into <see cref="TokenRange"/>s for
/// foreground coloring. Implementations MUST be stateless across calls — a
/// per-line CDATA opener that has no closer on the same line tokenizes to
/// the best-effort point and stops; the next line is tokenized fresh.
/// </summary>
internal interface IDiffSyntaxPainter
{
    /// <summary>
    /// Returns non-overlapping token ranges sorted by <see cref="ColumnRange.Start"/>,
    /// covering <c>[0, content.Length)</c>. Empty content returns an empty list.
    /// </summary>
    IReadOnlyList<TokenRange> TokenizeLine(string content);
}
```

- [ ] **Step 4: Create `NullPainter`**

`src/Structura.Reporting/Internal/Highlighting/NullPainter.cs`:

```csharp
namespace Structura.Reporting.Internal.Highlighting;

/// <summary>
/// No-op painter used when syntax highlighting is disabled or the format is
/// unknown. Returns an empty token list; the renderer's coalesced walker
/// then degenerates to plain content emission.
/// </summary>
internal sealed class NullPainter : IDiffSyntaxPainter
{
    public static readonly NullPainter Instance = new();

    private NullPainter() { }

    public IReadOnlyList<TokenRange> TokenizeLine(string content) =>
        Array.Empty<TokenRange>();
}
```

- [ ] **Step 5: Build and verify**

```bash
dotnet build Structura.slnx
```

Expected: clean build. No tests run (none added).

- [ ] **Step 6: Commit**

```bash
git add src/Structura.Reporting/Internal/Highlighting
git commit -m "feat(reporting): add Highlighting core types

TokenKind, TokenRange, IDiffSyntaxPainter, NullPainter — the foundation
for per-line syntax painters in step 13. No behavior change yet."
```

---

## Task 2: `SyntaxPalette` — Bright + Dim foreground tables

**Files:**
- Create: `src/Structura.Reporting/Internal/Highlighting/SyntaxPalette.cs`

The palette is a static lookup table: `TokenKind → ANSI 256-color foreground escape` for two tiers (Bright on changed rows, Dim on context rows). `Punctuation` and `Text` map to the empty string ("use default foreground"); the renderer treats `""` as a signal to emit `FgDefault` once per token transition rather than a colored escape.

- [ ] **Step 1: Create `SyntaxPalette`**

`src/Structura.Reporting/Internal/Highlighting/SyntaxPalette.cs`:

```csharp
namespace Structura.Reporting.Internal.Highlighting;

/// <summary>
/// 256-color ANSI foreground escapes per <see cref="TokenKind"/>, in two
/// tiers: <see cref="Bright"/> for changed rows (sit on dark red/green row
/// backgrounds and on the brighter inline-highlight backgrounds), and
/// <see cref="Dim"/> for context rows (muted to preserve the dim look).
/// Returning <see cref="string.Empty"/> signals "no color — use default fg".
/// </summary>
internal static class SyntaxPalette
{
    // Bright tier — chosen to remain legible on bg 5;52 (dark red), 5;22 (dark
    // green), 5;124 (bright red highlight), 5;34 (bright green highlight).
    private const string BrightCyan   = "\x1b[38;5;81m";
    private const string BrightYellow = "\x1b[38;5;180m";
    private const string BrightMauve  = "\x1b[38;5;176m";
    private const string BrightOrange = "\x1b[38;5;215m";
    private const string BrightSage   = "\x1b[38;5;150m";
    private const string BrightGrey   = "\x1b[38;5;245m";

    // Dim tier — desaturated counterparts for context rows.
    private const string DimCyan   = "\x1b[38;5;67m";
    private const string DimYellow = "\x1b[38;5;144m";
    private const string DimMauve  = "\x1b[38;5;103m";
    private const string DimOrange = "\x1b[38;5;137m";
    private const string DimSage   = "\x1b[38;5;108m";
    private const string DimGrey   = "\x1b[38;5;240m";

    public static string Bright(TokenKind kind) =>
        kind switch
        {
            TokenKind.Key         => BrightCyan,
            TokenKind.ElementName => BrightCyan,
            TokenKind.String      => BrightYellow,
            TokenKind.AttrValue   => BrightYellow,
            TokenKind.Number      => BrightMauve,
            TokenKind.Keyword     => BrightOrange,
            TokenKind.EntityRef   => BrightOrange,
            TokenKind.AttrName    => BrightSage,
            TokenKind.Comment     => BrightGrey,
            TokenKind.Punctuation => string.Empty,
            TokenKind.Text        => string.Empty,
            _ => string.Empty,
        };

    public static string Dim(TokenKind kind) =>
        kind switch
        {
            TokenKind.Key         => DimCyan,
            TokenKind.ElementName => DimCyan,
            TokenKind.String      => DimYellow,
            TokenKind.AttrValue   => DimYellow,
            TokenKind.Number      => DimMauve,
            TokenKind.Keyword     => DimOrange,
            TokenKind.EntityRef   => DimOrange,
            TokenKind.AttrName    => DimSage,
            TokenKind.Comment     => DimGrey,
            TokenKind.Punctuation => string.Empty,
            TokenKind.Text        => string.Empty,
            _ => string.Empty,
        };
}
```

- [ ] **Step 2: Build**

```bash
dotnet build Structura.slnx
```

Expected: clean build.

- [ ] **Step 3: Commit**

```bash
git add src/Structura.Reporting/Internal/Highlighting/SyntaxPalette.cs
git commit -m "feat(reporting): add SyntaxPalette with Bright + Dim tiers

Two static lookups mapping TokenKind to 256-color ANSI foreground escapes.
Bright tier reads on dark row backgrounds and bright highlight overlays;
Dim tier preserves the muted look on context rows."
```

---

## Task 3: `JsonLinePainter` — TDD

**Files:**
- Create: `src/Structura.Reporting/Internal/Highlighting/JsonLinePainter.cs`
- Create: `tests/Structura.UnitTests/Reporting/Highlighting/JsonLinePainterTests.cs`

Lexer rules (from spec §"Per-line lexers" → JsonLinePainter):
- `"` opens a string. Read until closing `"`, honoring `\"`. Decide `Key` vs `String` by scanning forward over whitespace: next non-whitespace `:` ⇒ `Key`; anything else (including end of line) ⇒ `String`.
- Digit, `-`, `+` opens a `Number` matching `-?\d+(\.\d+)?([eE][+-]?\d+)?`. Greedy.
- Bare lowercase `true`/`false`/`null` ⇒ `Keyword`. Identifier boundary on either side (start of line, end of line, or non-letter character).
- `{`, `}`, `[`, `]`, `,`, `:` ⇒ `Punctuation`.
- Anything else (whitespace, stray bytes) ⇒ `Punctuation` to keep cover complete.

Tokens are merged when adjacent and same-kind (so a 4-space indent is one `Punctuation` of length 4, not four).

- [ ] **Step 1: Create the test file with the cover invariant helper and a first failing test**

`tests/Structura.UnitTests/Reporting/Highlighting/JsonLinePainterTests.cs`:

```csharp
using FluentAssertions;

using Structura.Reporting.Internal.Highlighting;

using Xunit;

namespace Structura.UnitTests.Reporting.Highlighting;

public sealed class JsonLinePainterTests
{
    private static readonly JsonLinePainter Painter = JsonLinePainter.Instance;

    private static void AssertCover(string content, IReadOnlyList<TokenRange> tokens)
    {
        if (content.Length == 0)
        {
            tokens.Should().BeEmpty();
            return;
        }

        tokens.Should().NotBeEmpty();
        tokens[0].Range.Start.Should().Be(0, "cover must start at column 0");

        for (var i = 1; i < tokens.Count; i++)
        {
            tokens[i].Range.Start.Should().Be(
                tokens[i - 1].Range.End,
                "cover must be contiguous (token {0} starts where {1} ended)", i, i - 1);
        }

        tokens[^1].Range.End.Should().Be(
            content.Length,
            "cover must end at content.Length");
    }

    [Fact]
    public void TokenizeLine_KeyValuePair_ClassifiesQuotedNameAsKey()
    {
        const string content = "  \"order_id\": \"ORD-1\",";

        IReadOnlyList<TokenRange> tokens = Painter.TokenizeLine(content);

        AssertCover(content, tokens);
        TokenRange[] kindOrder = tokens.Where(t => t.Kind != TokenKind.Punctuation).ToArray();
        kindOrder.Should().HaveCount(2);
        kindOrder[0].Kind.Should().Be(TokenKind.Key);
        kindOrder[1].Kind.Should().Be(TokenKind.String);

        string keySlice = content.Substring(kindOrder[0].Range.Start, kindOrder[0].Range.Length);
        keySlice.Should().Be("\"order_id\"");
        string valSlice = content.Substring(kindOrder[1].Range.Start, kindOrder[1].Range.Length);
        valSlice.Should().Be("\"ORD-1\"");
    }
}
```

- [ ] **Step 2: Run the test — expect failure (no `JsonLinePainter` type yet)**

```bash
dotnet test --filter "FullyQualifiedName~JsonLinePainterTests"
```

Expected: build error referencing missing `JsonLinePainter`.

- [ ] **Step 3: Create `JsonLinePainter` skeleton (singleton + empty implementation that fails the cover invariant)**

`src/Structura.Reporting/Internal/Highlighting/JsonLinePainter.cs`:

```csharp
using System.Text;

namespace Structura.Reporting.Internal.Highlighting;

/// <summary>
/// Stateless per-line JSON tokenizer. Output is a complete cover of the line
/// using <see cref="TokenKind.Key"/>, <see cref="TokenKind.String"/>,
/// <see cref="TokenKind.Number"/>, <see cref="TokenKind.Keyword"/>, and
/// <see cref="TokenKind.Punctuation"/>. Multi-line JSON strings are not
/// supported — an unterminated string at end of line produces a single
/// <see cref="TokenKind.String"/> token covering everything up to the line's end.
/// </summary>
internal sealed class JsonLinePainter : IDiffSyntaxPainter
{
    public static readonly JsonLinePainter Instance = new();

    private JsonLinePainter() { }

    public IReadOnlyList<TokenRange> TokenizeLine(string content)
    {
        if (content.Length == 0)
        {
            return Array.Empty<TokenRange>();
        }

        var tokens = new List<TokenRange>();
        var i = 0;
        while (i < content.Length)
        {
            int tokenStart = i;
            TokenKind kind = ScanNext(content, ref i);
            int tokenLength = i - tokenStart;
            AppendToken(tokens, tokenStart, tokenLength, kind);
        }
        return tokens;
    }

    private static TokenKind ScanNext(string content, ref int i)
    {
        char c = content[i];
        if (c == '"')
        {
            return ScanString(content, ref i);
        }
        if (c == '-' || c == '+' || (c >= '0' && c <= '9'))
        {
            return ScanNumber(content, ref i);
        }
        if (IsKeywordStart(c))
        {
            TokenKind? kw = TryScanKeyword(content, ref i);
            if (kw.HasValue)
            {
                return kw.Value;
            }
        }
        i++;
        return TokenKind.Punctuation;
    }

    private static TokenKind ScanString(string content, ref int i)
    {
        i++; // consume opening quote
        while (i < content.Length)
        {
            char c = content[i];
            if (c == '\\' && i + 1 < content.Length)
            {
                i += 2;
                continue;
            }
            i++;
            if (c == '"')
            {
                break;
            }
        }
        return ClassifyString(content, i);
    }

    private static TokenKind ClassifyString(string content, int afterStringEnd)
    {
        for (int j = afterStringEnd; j < content.Length; j++)
        {
            char c = content[j];
            if (c == ' ' || c == '\t')
            {
                continue;
            }
            return c == ':' ? TokenKind.Key : TokenKind.String;
        }
        return TokenKind.String;
    }

    private static TokenKind ScanNumber(string content, ref int i)
    {
        if (content[i] == '-' || content[i] == '+')
        {
            i++;
        }
        while (i < content.Length && content[i] >= '0' && content[i] <= '9')
        {
            i++;
        }
        if (i < content.Length && content[i] == '.')
        {
            i++;
            while (i < content.Length && content[i] >= '0' && content[i] <= '9')
            {
                i++;
            }
        }
        if (i < content.Length && (content[i] == 'e' || content[i] == 'E'))
        {
            i++;
            if (i < content.Length && (content[i] == '+' || content[i] == '-'))
            {
                i++;
            }
            while (i < content.Length && content[i] >= '0' && content[i] <= '9')
            {
                i++;
            }
        }
        return TokenKind.Number;
    }

    private static bool IsKeywordStart(char c) =>
        c == 't' || c == 'f' || c == 'n';

    private static TokenKind? TryScanKeyword(string content, ref int i)
    {
        if (HasWord(content, i, "true") && IsBoundaryAfter(content, i + 4))
        {
            i += 4;
            return TokenKind.Keyword;
        }
        if (HasWord(content, i, "false") && IsBoundaryAfter(content, i + 5))
        {
            i += 5;
            return TokenKind.Keyword;
        }
        if (HasWord(content, i, "null") && IsBoundaryAfter(content, i + 4))
        {
            i += 4;
            return TokenKind.Keyword;
        }
        return null;
    }

    private static bool HasWord(string content, int start, string word)
    {
        if (start + word.Length > content.Length)
        {
            return false;
        }
        for (var k = 0; k < word.Length; k++)
        {
            if (content[start + k] != word[k])
            {
                return false;
            }
        }
        return true;
    }

    private static bool IsBoundaryAfter(string content, int index)
    {
        if (index >= content.Length)
        {
            return true;
        }
        char c = content[index];
        return !((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_');
    }

    private static void AppendToken(List<TokenRange> tokens, int start, int length, TokenKind kind)
    {
        if (length <= 0)
        {
            return;
        }
        if (tokens.Count > 0)
        {
            TokenRange last = tokens[^1];
            if (last.Kind == kind && last.Range.End == start)
            {
                tokens[^1] = new TokenRange(new ColumnRange(last.Range.Start, last.Range.Length + length), kind);
                return;
            }
        }
        tokens.Add(new TokenRange(new ColumnRange(start, length), kind));
    }
}
```

- [ ] **Step 4: Run the first test — expect pass**

```bash
dotnet test --filter "FullyQualifiedName~JsonLinePainterTests"
```

Expected: 1 passed.

- [ ] **Step 5: Add the rest of the table-driven cases**

Append to `JsonLinePainterTests.cs` (inside the class):

```csharp
[Fact]
public void TokenizeLine_NumericValue_IsNumber()
{
    const string content = "  \"version\": 7,";

    IReadOnlyList<TokenRange> tokens = Painter.TokenizeLine(content);

    AssertCover(content, tokens);
    TokenRange number = tokens.Single(t => t.Kind == TokenKind.Number);
    string slice = content.Substring(number.Range.Start, number.Range.Length);
    slice.Should().Be("7");
}

[Fact]
public void TokenizeLine_BoolKeyword_IsKeyword()
{
    const string content = "  \"is_priority\": true,";

    IReadOnlyList<TokenRange> tokens = Painter.TokenizeLine(content);

    AssertCover(content, tokens);
    TokenRange keyword = tokens.Single(t => t.Kind == TokenKind.Keyword);
    string slice = content.Substring(keyword.Range.Start, keyword.Range.Length);
    slice.Should().Be("true");
}

[Fact]
public void TokenizeLine_NullKeyword_IsKeyword()
{
    const string content = "  \"middle_name\": null,";

    IReadOnlyList<TokenRange> tokens = Painter.TokenizeLine(content);

    AssertCover(content, tokens);
    TokenRange keyword = tokens.Single(t => t.Kind == TokenKind.Keyword);
    string slice = content.Substring(keyword.Range.Start, keyword.Range.Length);
    slice.Should().Be("null");
}

[Fact]
public void TokenizeLine_NegativeFloat_IsNumber()
{
    const string content = "  \"risk_score\": -1.25e+3,";

    IReadOnlyList<TokenRange> tokens = Painter.TokenizeLine(content);

    AssertCover(content, tokens);
    TokenRange number = tokens.Single(t => t.Kind == TokenKind.Number);
    string slice = content.Substring(number.Range.Start, number.Range.Length);
    slice.Should().Be("-1.25e+3");
}

[Fact]
public void TokenizeLine_StringValueWithoutFollowingColon_IsString()
{
    const string content = "  \"electronics\",";

    IReadOnlyList<TokenRange> tokens = Painter.TokenizeLine(content);

    AssertCover(content, tokens);
    tokens.Should().NotContain(t => t.Kind == TokenKind.Key);
    TokenRange str = tokens.Single(t => t.Kind == TokenKind.String);
    string slice = content.Substring(str.Range.Start, str.Range.Length);
    slice.Should().Be("\"electronics\"");
}

[Fact]
public void TokenizeLine_UnterminatedString_TokenCoversTail()
{
    const string content = "  \"unterminated";

    IReadOnlyList<TokenRange> tokens = Painter.TokenizeLine(content);

    AssertCover(content, tokens);
    TokenRange tail = tokens.Last();
    tail.Kind.Should().Be(TokenKind.String);
    tail.Range.End.Should().Be(content.Length);
}

[Fact]
public void TokenizeLine_TrueWithSuffix_IsNotKeyword()
{
    const string content = "trueish";

    IReadOnlyList<TokenRange> tokens = Painter.TokenizeLine(content);

    AssertCover(content, tokens);
    tokens.Should().NotContain(t => t.Kind == TokenKind.Keyword);
}

[Fact]
public void TokenizeLine_EmptyContent_ReturnsEmpty()
{
    IReadOnlyList<TokenRange> tokens = Painter.TokenizeLine(string.Empty);

    tokens.Should().BeEmpty();
}

[Fact]
public void TokenizeLine_PunctuationOnly_IsCovered()
{
    const string content = "{},[]:";

    IReadOnlyList<TokenRange> tokens = Painter.TokenizeLine(content);

    AssertCover(content, tokens);
    tokens.Should().AllSatisfy(t => t.Kind.Should().Be(TokenKind.Punctuation));
}
```

- [ ] **Step 6: Run all painter tests — expect pass**

```bash
dotnet test --filter "FullyQualifiedName~JsonLinePainterTests"
```

Expected: 9 passed.

If any test fails, **do not paper over it**. Diagnose: typically a missing branch in `ScanNext` or an off-by-one in `ScanNumber`/`ScanString`. Fix the lexer until all 9 pass.

- [ ] **Step 7: Commit**

```bash
git add src/Structura.Reporting/Internal/Highlighting/JsonLinePainter.cs \
        tests/Structura.UnitTests/Reporting/Highlighting/JsonLinePainterTests.cs
git commit -m "feat(reporting): add JsonLinePainter with table-driven tests"
```

---

## Task 4: `XmlLinePainter` — TDD

**Files:**
- Create: `src/Structura.Reporting/Internal/Highlighting/XmlLinePainter.cs`
- Create: `tests/Structura.UnitTests/Reporting/Highlighting/XmlLinePainterTests.cs`

Lexer rules (from spec §"Per-line lexers" → XmlLinePainter):
- Three modes implemented as a small in-function state machine: `OutsideTag`, `InsideTag`, `InsideComment`. State does **not** carry across lines — an unclosed comment opens, gets `Comment` kind to end of line, then the next call starts fresh in `OutsideTag` (multi-line CDATA / multi-line comments are non-goals).
- Outside-tag: `<!--` opens comment mode; `<![CDATA[` and `]]>` are `Punctuation` with body `Text`; `&...;` is `EntityRef` (terminator: `;` or whitespace); `<` (and `</`, `<?`, `<!`) enters tag mode as `Punctuation`; everything else is `Text`.
- Inside-tag: first identifier is `ElementName` (accepts `name:local`); subsequent `name="value"`/`name='value'` are `AttrName` + `=` (`Punctuation`) + `AttrValue` (quotes included); `>`, `/>`, `?>` close as `Punctuation`. Whitespace inside the tag is `Punctuation`. Stray characters are `Punctuation`.
- Identifier characters: any char that is not whitespace, `>`, `<`, `/`, `=`, `?`, `'`, or `"`. This permits Unicode element/attr names.
- DOCTYPE / XML decl prefixes `<?xml` and `<!DOCTYPE` are tokenized as `Punctuation` for the leading `<?`/`<!` and then standard inside-tag rules apply for the rest.

Token coalescing: same as JSON — adjacent tokens of the same kind merge.

- [ ] **Step 1: Create test file with cover-invariant helper and a first failing test**

`tests/Structura.UnitTests/Reporting/Highlighting/XmlLinePainterTests.cs`:

```csharp
using FluentAssertions;

using Structura.Reporting.Internal.Highlighting;

using Xunit;

namespace Structura.UnitTests.Reporting.Highlighting;

public sealed class XmlLinePainterTests
{
    private static readonly XmlLinePainter Painter = XmlLinePainter.Instance;

    private static void AssertCover(string content, IReadOnlyList<TokenRange> tokens)
    {
        if (content.Length == 0)
        {
            tokens.Should().BeEmpty();
            return;
        }

        tokens.Should().NotBeEmpty();
        tokens[0].Range.Start.Should().Be(0);
        for (var i = 1; i < tokens.Count; i++)
        {
            tokens[i].Range.Start.Should().Be(tokens[i - 1].Range.End);
        }
        tokens[^1].Range.End.Should().Be(content.Length);
    }

    private static string SliceOf(string content, TokenRange t) =>
        content.Substring(t.Range.Start, t.Range.Length);

    [Fact]
    public void TokenizeLine_OpenTagWithAttributes_ClassifiesNameAndAttrs()
    {
        const string content = "<book id=\"b001\" available=\"true\">";

        IReadOnlyList<TokenRange> tokens = Painter.TokenizeLine(content);

        AssertCover(content, tokens);
        TokenRange[] meaningful = tokens.Where(t => t.Kind != TokenKind.Punctuation).ToArray();
        meaningful.Should().HaveCount(5);
        meaningful[0].Kind.Should().Be(TokenKind.ElementName);
        SliceOf(content, meaningful[0]).Should().Be("book");
        meaningful[1].Kind.Should().Be(TokenKind.AttrName);
        SliceOf(content, meaningful[1]).Should().Be("id");
        meaningful[2].Kind.Should().Be(TokenKind.AttrValue);
        SliceOf(content, meaningful[2]).Should().Be("\"b001\"");
        meaningful[3].Kind.Should().Be(TokenKind.AttrName);
        SliceOf(content, meaningful[3]).Should().Be("available");
        meaningful[4].Kind.Should().Be(TokenKind.AttrValue);
        SliceOf(content, meaningful[4]).Should().Be("\"true\"");
    }
}
```

- [ ] **Step 2: Run — expect failure (missing type)**

```bash
dotnet test --filter "FullyQualifiedName~XmlLinePainterTests"
```

Expected: build error.

- [ ] **Step 3: Create `XmlLinePainter`**

`src/Structura.Reporting/Internal/Highlighting/XmlLinePainter.cs`:

```csharp
namespace Structura.Reporting.Internal.Highlighting;

/// <summary>
/// Stateless per-line XML tokenizer. Output is a complete cover of the line.
/// Multi-line constructs (comments and CDATA bodies that span lines) are not
/// supported — an unclosed comment / CDATA on a line tokenizes that line
/// best-effort and stops; subsequent lines start fresh in outside-tag mode.
/// </summary>
internal sealed class XmlLinePainter : IDiffSyntaxPainter
{
    public static readonly XmlLinePainter Instance = new();

    private XmlLinePainter() { }

    private enum Mode { Outside, InsideTag, InsideComment }

    public IReadOnlyList<TokenRange> TokenizeLine(string content)
    {
        if (content.Length == 0)
        {
            return Array.Empty<TokenRange>();
        }

        var tokens = new List<TokenRange>();
        Mode mode = Mode.Outside;
        var i = 0;
        while (i < content.Length)
        {
            int tokenStart = i;
            TokenKind kind = mode switch
            {
                Mode.Outside       => ScanOutside(content, ref i, ref mode),
                Mode.InsideTag     => ScanInsideTag(content, ref i, ref mode),
                Mode.InsideComment => ScanInsideComment(content, ref i, ref mode),
                _                  => ScanOutside(content, ref i, ref mode),
            };
            int tokenLength = i - tokenStart;
            AppendToken(tokens, tokenStart, tokenLength, kind);
        }
        return tokens;
    }

    private static TokenKind ScanOutside(string content, ref int i, ref Mode mode)
    {
        char c = content[i];
        if (c == '<')
        {
            if (StartsWith(content, i, "<!--"))
            {
                i += 4;
                if (TryConsumeUntil(content, ref i, "-->"))
                {
                    return TokenKind.Comment;
                }
                mode = Mode.InsideComment;
                return TokenKind.Comment;
            }
            if (StartsWith(content, i, "<![CDATA["))
            {
                i += 9;
                return TokenKind.Punctuation;
            }
            if (StartsWith(content, i, "</") || StartsWith(content, i, "<?") || StartsWith(content, i, "<!"))
            {
                i += 2;
            }
            else
            {
                i++;
            }
            mode = Mode.InsideTag;
            return TokenKind.Punctuation;
        }
        if (c == ']' && StartsWith(content, i, "]]>"))
        {
            i += 3;
            return TokenKind.Punctuation;
        }
        if (c == '&')
        {
            int start = i;
            i++;
            while (i < content.Length && content[i] != ';' && !char.IsWhiteSpace(content[i]))
            {
                i++;
            }
            if (i < content.Length && content[i] == ';')
            {
                i++;
            }
            if (i > start + 1)
            {
                return TokenKind.EntityRef;
            }
            return TokenKind.Text;
        }
        i++;
        return TokenKind.Text;
    }

    private static TokenKind ScanInsideTag(string content, ref int i, ref Mode mode)
    {
        char c = content[i];
        if (char.IsWhiteSpace(c))
        {
            i++;
            return TokenKind.Punctuation;
        }
        if (c == '>')
        {
            i++;
            mode = Mode.Outside;
            return TokenKind.Punctuation;
        }
        if ((c == '/' || c == '?') && i + 1 < content.Length && content[i + 1] == '>')
        {
            i += 2;
            mode = Mode.Outside;
            return TokenKind.Punctuation;
        }
        if (c == '=')
        {
            i++;
            return TokenKind.Punctuation;
        }
        if (c == '"' || c == '\'')
        {
            char quote = c;
            i++;
            while (i < content.Length && content[i] != quote)
            {
                i++;
            }
            if (i < content.Length)
            {
                i++;
            }
            return TokenKind.AttrValue;
        }
        if (IsNameChar(c))
        {
            int start = i;
            while (i < content.Length && IsNameChar(content[i]))
            {
                i++;
            }
            // First identifier in the tag is ElementName; subsequent are AttrName.
            // Distinguish by looking at the previous emitted token: if the most
            // recent meaningful char before `start` is `<` (or `</`, `<?`, `<!`),
            // we're naming the element.
            return IsAtElementNamePosition(content, start) ? TokenKind.ElementName : TokenKind.AttrName;
        }
        i++;
        return TokenKind.Punctuation;
    }

    private static TokenKind ScanInsideComment(string content, ref int i, ref Mode mode)
    {
        if (TryConsumeUntil(content, ref i, "-->"))
        {
            mode = Mode.Outside;
        }
        else
        {
            i = content.Length;
        }
        return TokenKind.Comment;
    }

    private static bool StartsWith(string content, int i, string s)
    {
        if (i + s.Length > content.Length)
        {
            return false;
        }
        for (var k = 0; k < s.Length; k++)
        {
            if (content[i + k] != s[k])
            {
                return false;
            }
        }
        return true;
    }

    private static bool TryConsumeUntil(string content, ref int i, string terminator)
    {
        int found = content.IndexOf(terminator, i, StringComparison.Ordinal);
        if (found < 0)
        {
            i = content.Length;
            return false;
        }
        i = found + terminator.Length;
        return true;
    }

    private static bool IsNameChar(char c) =>
        !char.IsWhiteSpace(c) && c != '>' && c != '<' && c != '/' && c != '=' && c != '?' && c != '"' && c != '\'';

    private static bool IsAtElementNamePosition(string content, int identifierStart)
    {
        // Walk backwards from identifierStart over whitespace; if we land on
        // `<`, `</`, `<?`, or `<!`, this identifier names the element.
        int j = identifierStart - 1;
        while (j >= 0 && char.IsWhiteSpace(content[j]))
        {
            j--;
        }
        if (j < 0)
        {
            return false;
        }
        if (content[j] == '<')
        {
            return true;
        }
        if (j > 0 && (content[j] == '/' || content[j] == '?' || content[j] == '!') && content[j - 1] == '<')
        {
            return true;
        }
        return false;
    }

    private static void AppendToken(List<TokenRange> tokens, int start, int length, TokenKind kind)
    {
        if (length <= 0)
        {
            return;
        }
        if (tokens.Count > 0)
        {
            TokenRange last = tokens[^1];
            if (last.Kind == kind && last.Range.End == start)
            {
                tokens[^1] = new TokenRange(new ColumnRange(last.Range.Start, last.Range.Length + length), kind);
                return;
            }
        }
        tokens.Add(new TokenRange(new ColumnRange(start, length), kind));
    }
}
```

- [ ] **Step 4: Run the first XML test — expect pass**

```bash
dotnet test --filter "FullyQualifiedName~XmlLinePainterTests"
```

Expected: 1 passed.

- [ ] **Step 5: Add the rest of the XML cases**

Append to `XmlLinePainterTests.cs` (inside the class):

```csharp
[Fact]
public void TokenizeLine_NamespacedElement_ElementNameIncludesPrefix()
{
    const string content = "<meta:info>";

    IReadOnlyList<TokenRange> tokens = Painter.TokenizeLine(content);

    AssertCover(content, tokens);
    TokenRange element = tokens.Single(t => t.Kind == TokenKind.ElementName);
    SliceOf(content, element).Should().Be("meta:info");
}

[Fact]
public void TokenizeLine_SelfClosing_RecognizesSlashGt()
{
    const string content = "<out-of-stock/>";

    IReadOnlyList<TokenRange> tokens = Painter.TokenizeLine(content);

    AssertCover(content, tokens);
    TokenRange element = tokens.Single(t => t.Kind == TokenKind.ElementName);
    SliceOf(content, element).Should().Be("out-of-stock");
}

[Fact]
public void TokenizeLine_ClosingTag_ClassifiesElementName()
{
    const string content = "</author>";

    IReadOnlyList<TokenRange> tokens = Painter.TokenizeLine(content);

    AssertCover(content, tokens);
    TokenRange element = tokens.Single(t => t.Kind == TokenKind.ElementName);
    SliceOf(content, element).Should().Be("author");
}

[Fact]
public void TokenizeLine_Comment_WholeLineIsComment()
{
    const string content = "<!-- hello -->";

    IReadOnlyList<TokenRange> tokens = Painter.TokenizeLine(content);

    AssertCover(content, tokens);
    tokens.Should().HaveCount(1);
    tokens[0].Kind.Should().Be(TokenKind.Comment);
}

[Fact]
public void TokenizeLine_OpenCommentOnly_TailIsComment()
{
    const string content = "<!-- hi";

    IReadOnlyList<TokenRange> tokens = Painter.TokenizeLine(content);

    AssertCover(content, tokens);
    tokens.Last().Kind.Should().Be(TokenKind.Comment);
}

[Fact]
public void TokenizeLine_EntityRefInText_IsEntityRef()
{
    const string content = "x &amp; y";

    IReadOnlyList<TokenRange> tokens = Painter.TokenizeLine(content);

    AssertCover(content, tokens);
    TokenRange entity = tokens.Single(t => t.Kind == TokenKind.EntityRef);
    SliceOf(content, entity).Should().Be("&amp;");
}

[Fact]
public void TokenizeLine_SingleLineCData_BodyIsText()
{
    const string content = "<![CDATA[ payload ]]>";

    IReadOnlyList<TokenRange> tokens = Painter.TokenizeLine(content);

    AssertCover(content, tokens);
    TokenRange text = tokens.Single(t => t.Kind == TokenKind.Text);
    SliceOf(content, text).Should().Be(" payload ");
}

[Fact]
public void TokenizeLine_OpenCDataOnly_RestIsText()
{
    const string content = "<![CDATA[ start";

    IReadOnlyList<TokenRange> tokens = Painter.TokenizeLine(content);

    AssertCover(content, tokens);
    tokens.Last().Kind.Should().Be(TokenKind.Text);
}

[Fact]
public void TokenizeLine_AttributeWithSingleQuotes_IsAttrValue()
{
    const string content = "<a href='url'/>";

    IReadOnlyList<TokenRange> tokens = Painter.TokenizeLine(content);

    AssertCover(content, tokens);
    TokenRange attr = tokens.Single(t => t.Kind == TokenKind.AttrValue);
    SliceOf(content, attr).Should().Be("'url'");
}

[Fact]
public void TokenizeLine_NonAsciiElementName_IsAccepted()
{
    const string content = "<книга/>";

    IReadOnlyList<TokenRange> tokens = Painter.TokenizeLine(content);

    AssertCover(content, tokens);
    TokenRange element = tokens.Single(t => t.Kind == TokenKind.ElementName);
    SliceOf(content, element).Should().Be("книга");
}

[Fact]
public void TokenizeLine_EmptyContent_ReturnsEmpty()
{
    IReadOnlyList<TokenRange> tokens = Painter.TokenizeLine(string.Empty);

    tokens.Should().BeEmpty();
}
```

- [ ] **Step 6: Run all XML tests — fix until all pass**

```bash
dotnet test --filter "FullyQualifiedName~XmlLinePainterTests"
```

Expected: 12 passed (1 original + 11 new). If a case fails, diagnose and fix `XmlLinePainter` — typically a missing case in `ScanOutside`/`ScanInsideTag` or an off-by-one in `IsAtElementNamePosition`.

- [ ] **Step 7: Commit**

```bash
git add src/Structura.Reporting/Internal/Highlighting/XmlLinePainter.cs \
        tests/Structura.UnitTests/Reporting/Highlighting/XmlLinePainterTests.cs
git commit -m "feat(reporting): add XmlLinePainter with table-driven tests"
```

---

## Task 5: `PainterFactory` — TDD

**Files:**
- Create: `src/Structura.Reporting/Internal/Highlighting/PainterFactory.cs`
- Create: `tests/Structura.UnitTests/Reporting/Highlighting/PainterFactoryTests.cs`

Selection rule (from spec §"Painter selection"):
1. `syntaxOn == false` ⇒ `NullPainter`.
2. `DocumentName` ends with `.json` (case-insensitive) ⇒ `JsonLinePainter`.
3. `DocumentName` ends with `.xml` (case-insensitive) ⇒ `XmlLinePainter`.
4. Else, scan `OriginalText` skipping Unicode whitespace: first char `{` or `[` ⇒ `JsonLinePainter`; first char `<` ⇒ `XmlLinePainter`; otherwise (or empty/whitespace-only) ⇒ `NullPainter`.

- [ ] **Step 1: Create the test file with the first failing test**

`tests/Structura.UnitTests/Reporting/Highlighting/PainterFactoryTests.cs`:

```csharp
using FluentAssertions;

using Structura.Reporting.Internal.Highlighting;
using Structura.Runtime;

using Xunit;

namespace Structura.UnitTests.Reporting.Highlighting;

public sealed class PainterFactoryTests
{
    private static FakeStructuraDocument MakeDoc(string documentName, string originalText = "{}") =>
        new FakeStructuraDocument(originalText, Array.Empty<DocumentChange>(), documentName);

    [Fact]
    public void For_SyntaxOff_ReturnsNullPainter()
    {
        var doc = MakeDoc("order.sample.json");

        IDiffSyntaxPainter painter = PainterFactory.For(doc, syntaxOn: false);

        painter.Should().BeSameAs(NullPainter.Instance);
    }
}
```

- [ ] **Step 2: Run — expect failure (missing `PainterFactory`)**

```bash
dotnet test --filter "FullyQualifiedName~PainterFactoryTests"
```

Expected: build error.

- [ ] **Step 3: Implement `PainterFactory`**

`src/Structura.Reporting/Internal/Highlighting/PainterFactory.cs`:

```csharp
using Structura.Runtime;

namespace Structura.Reporting.Internal.Highlighting;

/// <summary>
/// Resolves an <see cref="IDiffSyntaxPainter"/> for a given document.
/// Selection: <c>DocumentName</c> file extension first; if neither
/// <c>.json</c> nor <c>.xml</c>, sniff the first non-whitespace character
/// of <c>OriginalText</c>; otherwise <see cref="NullPainter"/>.
/// </summary>
internal static class PainterFactory
{
    public static IDiffSyntaxPainter For(IStructuraDocument doc, bool syntaxOn)
    {
        if (!syntaxOn)
        {
            return NullPainter.Instance;
        }

        IDiffSyntaxPainter? byName = ByDocumentName(doc.DocumentName);
        if (byName is not null)
        {
            return byName;
        }
        return SniffByFirstChar(doc.OriginalText);
    }

    private static IDiffSyntaxPainter? ByDocumentName(string documentName)
    {
        if (documentName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            return JsonLinePainter.Instance;
        }
        if (documentName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
        {
            return XmlLinePainter.Instance;
        }
        return null;
    }

    private static IDiffSyntaxPainter SniffByFirstChar(string originalText)
    {
        for (var i = 0; i < originalText.Length; i++)
        {
            char c = originalText[i];
            if (char.IsWhiteSpace(c))
            {
                continue;
            }
            if (c == '{' || c == '[')
            {
                return JsonLinePainter.Instance;
            }
            if (c == '<')
            {
                return XmlLinePainter.Instance;
            }
            return NullPainter.Instance;
        }
        return NullPainter.Instance;
    }
}
```

- [ ] **Step 4: Run the first test — expect pass**

```bash
dotnet test --filter "FullyQualifiedName~PainterFactoryTests"
```

Expected: 1 passed.

- [ ] **Step 5: Add the rest of the cases**

Append to `PainterFactoryTests.cs`:

```csharp
[Fact]
public void For_JsonByDocumentName_ReturnsJsonPainter()
{
    var doc = MakeDoc("order.sample.json", originalText: "<not-json>");

    IDiffSyntaxPainter painter = PainterFactory.For(doc, syntaxOn: true);

    painter.Should().BeSameAs(JsonLinePainter.Instance);
}

[Fact]
public void For_XmlByDocumentName_ReturnsXmlPainter()
{
    var doc = MakeDoc("library.sample.xml", originalText: "{not-xml}");

    IDiffSyntaxPainter painter = PainterFactory.For(doc, syntaxOn: true);

    painter.Should().BeSameAs(XmlLinePainter.Instance);
}

[Fact]
public void For_DocumentNameCaseInsensitive_RecognizesUppercaseExtension()
{
    var doc = MakeDoc("Order.Sample.JSON");

    IDiffSyntaxPainter painter = PainterFactory.For(doc, syntaxOn: true);

    painter.Should().BeSameAs(JsonLinePainter.Instance);
}

[Fact]
public void For_UnknownExtension_SniffsJsonByOpenBrace()
{
    var doc = MakeDoc("data.txt", originalText: "  \n  { \"a\": 1 }");

    IDiffSyntaxPainter painter = PainterFactory.For(doc, syntaxOn: true);

    painter.Should().BeSameAs(JsonLinePainter.Instance);
}

[Fact]
public void For_UnknownExtension_SniffsXmlByLessThan()
{
    var doc = MakeDoc("data.txt", originalText: "\t<root/>");

    IDiffSyntaxPainter painter = PainterFactory.For(doc, syntaxOn: true);

    painter.Should().BeSameAs(XmlLinePainter.Instance);
}

[Fact]
public void For_UnknownExtensionAndUnknownContent_ReturnsNullPainter()
{
    var doc = MakeDoc("data.txt", originalText: "abcdef");

    IDiffSyntaxPainter painter = PainterFactory.For(doc, syntaxOn: true);

    painter.Should().BeSameAs(NullPainter.Instance);
}

[Fact]
public void For_EmptyOriginalText_ReturnsNullPainter()
{
    var doc = MakeDoc("data.txt", originalText: string.Empty);

    IDiffSyntaxPainter painter = PainterFactory.For(doc, syntaxOn: true);

    painter.Should().BeSameAs(NullPainter.Instance);
}
```

- [ ] **Step 6: Run all factory tests — expect pass**

```bash
dotnet test --filter "FullyQualifiedName~PainterFactoryTests"
```

Expected: 8 passed.

- [ ] **Step 7: Commit**

```bash
git add src/Structura.Reporting/Internal/Highlighting/PainterFactory.cs \
        tests/Structura.UnitTests/Reporting/Highlighting/PainterFactoryTests.cs
git commit -m "feat(reporting): add PainterFactory (DocumentName, sniff, Null)"
```

---

## Task 6: Add `SyntaxHighlight` field to `DiffReporterOptions`

**Files:**
- Modify: `src/Structura.Reporting/DiffReporterOptions.cs`
- Modify: `tests/Structura.UnitTests/Reporting/UnifiedDiffReporterTests.cs` (one new test)

Field is additive; default is `true`. No reporter wires to it yet — that's Tasks 9 & 10. This task only updates the record and locks the default with a test.

- [ ] **Step 1: Add the field**

In `src/Structura.Reporting/DiffReporterOptions.cs`, add `SyntaxHighlight` after `InlineHighlight`:

```csharp
namespace Structura.Reporting;

public sealed record DiffReporterOptions
{
    public int ContextLines { get; init; } = 3;

    public bool InlineHighlight { get; init; } = true;

    /// <summary>
    /// When <c>true</c>, JSON and XML content is rendered with token-aware
    /// foreground colors (keys, strings, numbers, keywords, element/attribute
    /// names, comments, entity refs) layered over the existing row-bg /
    /// inline-highlight machinery. No effect when <c>useColor</c> is false —
    /// plain-text output bytes are unchanged. Default <c>true</c>.
    /// </summary>
    public bool SyntaxHighlight { get; init; } = true;

    public bool ShowFullFile { get; init; } = false;
}
```

- [ ] **Step 2: Add a test that pins the default**

Append to `tests/Structura.UnitTests/Reporting/UnifiedDiffReporterTests.cs`:

```csharp
[Fact]
public void DiffReporterOptions_Defaults_SyntaxHighlightIsTrue()
{
    var options = new DiffReporterOptions();

    options.SyntaxHighlight.Should().BeTrue();
}
```

(`Structura.Reporting` is already imported by this file; FluentAssertions too.)

- [ ] **Step 3: Build and run**

```bash
dotnet build Structura.slnx
dotnet test --filter "FullyQualifiedName~UnifiedDiffReporterTests"
```

Expected: green.

- [ ] **Step 4: Commit**

```bash
git add src/Structura.Reporting/DiffReporterOptions.cs \
        tests/Structura.UnitTests/Reporting/UnifiedDiffReporterTests.cs
git commit -m "feat(reporting): add SyntaxHighlight option (default true)"
```

---

## Task 7: Wire painter into `DiffLineRenderer`

**Files:**
- Modify: `src/Structura.Reporting/Internal/DiffLineRenderer.cs`
- Modify: `tests/Structura.UnitTests/Reporting/DiffLineRendererTests.cs`

The renderer's signature gains an `IDiffSyntaxPainter` parameter. When the painter returns no tokens (e.g. `NullPainter`), behavior is byte-equal to today. When it returns tokens, foreground escapes are interleaved with the existing row-bg / inline-highlight escape pairs.

Key invariant: every existing test that calls `Render(..., useColor: false, ...)` must continue to pass unchanged in output. We achieve this by **always passing `NullPainter` when `useColor=false`** (the renderer's internal call path) and by making the painter parameter additive — old tests get updated to pass `NullPainter.Instance` and produce identical output.

- [ ] **Step 1: Add the new parameter — failing build first**

Update the `Render` signature in `src/Structura.Reporting/Internal/DiffLineRenderer.cs`:

```csharp
public static string Render(DiffLine line, int gutterWidth, bool useColor, bool useUnicode, IDiffSyntaxPainter painter)
```

Add `using Structura.Reporting.Internal.Highlighting;` at the top.

- [ ] **Step 2: Update existing tests to pass `NullPainter.Instance`**

In `tests/Structura.UnitTests/Reporting/DiffLineRendererTests.cs`:

- Add `using Structura.Reporting.Internal.Highlighting;` to the imports.
- Add every existing `Render(line, gutterWidth, useColor, useUnicode)` call → append `, NullPainter.Instance` as the new last arg.

Run the suite — should still be green. **Do not change any expected string** in the existing tests.

```bash
dotnet test --filter "FullyQualifiedName~DiffLineRendererTests"
```

Expected: all existing tests still pass.

- [ ] **Step 3: Add new failing tests for painter integration**

Append to `DiffLineRendererTests.cs`:

```csharp
private sealed class StubPainter : IDiffSyntaxPainter
{
    private readonly TokenRange[] _tokens;
    public StubPainter(params TokenRange[] tokens) => _tokens = tokens;
    public IReadOnlyList<TokenRange> TokenizeLine(string content) => _tokens;
}

[Fact]
public void Render_AddedLine_WithKeyToken_EmbedsBrightCyanFg()
{
    const string content = "  \"x\": 1,";
    // Whole content as one Key token to keep the test focused on color emission.
    var painter = new StubPainter(new TokenRange(new ColumnRange(0, content.Length), TokenKind.Key));
    var line = new DiffLine(DiffLineKind.Added, 0, 7, content, System.Array.Empty<ColumnRange>());

    string s = DiffLineRenderer.Render(line, gutterWidth: 3, useColor: true, useUnicode: true, painter);

    s.Should().StartWith(AnsiPalette.BgAddedRow);
    s.Should().EndWith(AnsiPalette.BgDefault);
    s.Should().Contain(SyntaxPalette.Bright(TokenKind.Key));
    s.Should().Contain(content);
}

[Fact]
public void Render_AddedLine_NoColor_PainterIgnored()
{
    const string content = "  \"x\": 1,";
    var painter = new StubPainter(new TokenRange(new ColumnRange(0, content.Length), TokenKind.Key));
    var line = new DiffLine(DiffLineKind.Added, 0, 7, content, System.Array.Empty<ColumnRange>());

    string s = DiffLineRenderer.Render(line, gutterWidth: 3, useColor: false, useUnicode: true, painter);

    s.Should().Be("  7 +   \"x\": 1,");
    s.Should().NotContain("\x1b");
}

[Fact]
public void Render_ContextLine_WithStringToken_UsesDimYellowFg()
{
    const string content = "  \"x\": \"abc\",";
    var range = new ColumnRange(7, 5); // cover the "abc" string with quotes
    var painter = new StubPainter(new TokenRange(range, TokenKind.String));
    var line = new DiffLine(DiffLineKind.Context, 7, 7, content, System.Array.Empty<ColumnRange>());

    string s = DiffLineRenderer.Render(line, gutterWidth: 3, useColor: true, useUnicode: true, painter);

    s.Should().Contain(SyntaxPalette.Dim(TokenKind.String));
    s.Should().NotContain(SyntaxPalette.Bright(TokenKind.String));
}

[Fact]
public void Render_AddedLine_TokenInsideInlineHighlight_KeepsBothFgAndHighlightBg()
{
    const string content = "  \"x\": 1,";
    var hi = new[] { new ColumnRange(7, 1) };
    var painter = new StubPainter(new TokenRange(new ColumnRange(7, 1), TokenKind.Number));
    var line = new DiffLine(DiffLineKind.Added, 0, 7, content, hi);

    string s = DiffLineRenderer.Render(line, gutterWidth: 3, useColor: true, useUnicode: true, painter);

    s.Should().Contain(AnsiPalette.BgAddedHi);
    s.Should().Contain(SyntaxPalette.Bright(TokenKind.Number));
}
```

- [ ] **Step 4: Run new tests — expect failures**

```bash
dotnet test --filter "FullyQualifiedName~DiffLineRendererTests"
```

Expected: the four new tests fail (painter is accepted but ignored).

- [ ] **Step 5: Implement painter-aware rendering**

Replace the body of `DiffLineRenderer.Render` with a version that walks tokens. The key restructure is the inner content emission. Existing helpers (gutter, sigil, hunk-separator branch, context-line branch) keep their shapes.

Replacement file `src/Structura.Reporting/Internal/DiffLineRenderer.cs`:

```csharp
using System.Text;

using Structura.Reporting.Internal.Highlighting;

namespace Structura.Reporting.Internal;

/// <summary>
/// Formats a single <see cref="DiffLine"/> into a renderable string. Plain
/// when <c>useColor: false</c>; emits 256-color ANSI escapes for row
/// backgrounds, inline highlights, and (when the painter returns tokens)
/// per-token foreground colors when <c>useColor: true</c>.
/// </summary>
internal static class DiffLineRenderer
{
    public static string Render(DiffLine line, int gutterWidth, bool useColor, bool useUnicode, IDiffSyntaxPainter painter)
    {
        if (line.Kind == DiffLineKind.HunkSeparator)
        {
            string ellipsis = useUnicode ? "…" : "...";
            string gutterPad = new string(' ', gutterWidth);
            return gutterPad + "   " + ellipsis;
        }

        char sigil = line.Kind switch
        {
            DiffLineKind.Removed => '-',
            DiffLineKind.Added => '+',
            _ => ' ',
        };
        int gutterValue = line.Kind == DiffLineKind.Removed ? line.OldLineNumber : line.NewLineNumber;
        string gutter = gutterValue.ToString().PadLeft(gutterWidth);
        string body = $"{gutter} {sigil} {line.Content}";

        if (!useColor)
        {
            return body;
        }

        IReadOnlyList<TokenRange> tokens = painter.TokenizeLine(line.Content);

        if (line.Kind == DiffLineKind.Context)
        {
            return RenderContext(line, gutter, sigil, tokens);
        }
        return RenderChanged(line, gutter, sigil, tokens);
    }

    private static string RenderContext(DiffLine line, string gutter, char sigil, IReadOnlyList<TokenRange> tokens)
    {
        var sb = new StringBuilder();
        sb.Append(AnsiPalette.Dim).Append(gutter).Append(' ').Append(sigil).Append(AnsiPalette.DimOff);
        sb.Append(' ');
        AppendContent(sb, line.Content, tokens, Array.Empty<ColumnRange>(), rowBg: string.Empty, highlightBg: string.Empty, useDimPalette: true);
        return sb.ToString();
    }

    private static string RenderChanged(DiffLine line, string gutter, char sigil, IReadOnlyList<TokenRange> tokens)
    {
        string rowBg = line.Kind == DiffLineKind.Removed ? AnsiPalette.BgRemovedRow : AnsiPalette.BgAddedRow;
        string highlightBg = line.Kind == DiffLineKind.Removed ? AnsiPalette.BgRemovedHi : AnsiPalette.BgAddedHi;
        string sigilFg = line.Kind == DiffLineKind.Removed ? AnsiPalette.FgRemovedSigil : AnsiPalette.FgAddedSigil;

        var sb = new StringBuilder();
        sb.Append(rowBg);
        sb.Append(sigilFg).Append(gutter).Append(' ').Append(sigil).Append(AnsiPalette.FgDefault).Append(' ');
        AppendContent(sb, line.Content, tokens, line.InlineHighlights, rowBg, highlightBg, useDimPalette: false);
        sb.Append(' ');
        sb.Append(AnsiPalette.BgDefault);
        return sb.ToString();
    }

    private static void AppendContent(
        StringBuilder sb,
        string content,
        IReadOnlyList<TokenRange> tokens,
        IReadOnlyList<ColumnRange> highlights,
        string rowBg,
        string highlightBg,
        bool useDimPalette)
    {
        // Walk content column-by-column, tracking the active token (for fg) and
        // whether the current column is inside an inline-highlight range. Emit
        // an ANSI escape only when the active fg or the highlight state changes.
        TokenKind activeTokenKind = TokenKind.Punctuation;
        string activeFg = string.Empty;
        bool inHighlight = false;
        var tokenIndex = 0;
        var highlightIndex = 0;

        for (var col = 0; col < content.Length; col++)
        {
            while (tokenIndex < tokens.Count && tokens[tokenIndex].Range.End <= col)
            {
                tokenIndex++;
            }
            TokenKind kindAtCol = tokenIndex < tokens.Count && tokens[tokenIndex].Range.Start <= col
                ? tokens[tokenIndex].Kind
                : TokenKind.Punctuation;
            string fgAtCol = useDimPalette ? SyntaxPalette.Dim(kindAtCol) : SyntaxPalette.Bright(kindAtCol);

            while (highlightIndex < highlights.Count && highlights[highlightIndex].End <= col)
            {
                highlightIndex++;
            }
            bool inHighlightAtCol = highlightIndex < highlights.Count && highlights[highlightIndex].Start <= col;

            if (col == 0 || kindAtCol != activeTokenKind || fgAtCol != activeFg)
            {
                if (fgAtCol.Length > 0)
                {
                    sb.Append(fgAtCol);
                }
                else
                {
                    sb.Append(AnsiPalette.FgDefault);
                }
                activeTokenKind = kindAtCol;
                activeFg = fgAtCol;
            }

            if (inHighlightAtCol != inHighlight)
            {
                if (inHighlightAtCol)
                {
                    sb.Append(highlightBg).Append(AnsiPalette.Bold);
                }
                else
                {
                    sb.Append(AnsiPalette.BoldOff).Append(rowBg.Length > 0 ? rowBg : AnsiPalette.BgDefault);
                }
                inHighlight = inHighlightAtCol;
            }

            sb.Append(content[col]);
        }

        if (inHighlight)
        {
            sb.Append(AnsiPalette.BoldOff);
            if (rowBg.Length > 0)
            {
                sb.Append(rowBg);
            }
        }
        sb.Append(AnsiPalette.FgDefault);
    }
}
```

The reset at the end (`FgDefault`) is safe even if no token was ever emitted. The outer `BgDefault` reset for changed rows is added by `RenderChanged` after the content walk.

- [ ] **Step 6: Update the call site in `UnifiedDiffReporter` to compile**

In `src/Structura.Reporting/UnifiedDiffReporter.cs`, the `RenderTo` method calls `DiffLineRenderer.Render`. Pass `NullPainter.Instance` as the new last arg for now (Task 9 will replace this with a real painter):

```csharp
using Structura.Reporting.Internal.Highlighting;
// … existing usings …

private static void RenderTo(...)
{
    // … existing code up to the foreach …
    foreach (DiffLine line in lines)
    {
        string rendered = DiffLineRenderer.Render(line, gutterWidth, useColor, useUnicode, NullPainter.Instance);
        writer.WriteLine(rendered);
    }
}
```

- [ ] **Step 7: Build and run all tests**

```bash
dotnet build Structura.slnx
dotnet test
```

Expected: every test passes — old DiffLineRenderer tests because painter is `NullPainter` and emits no tokens; new tests because the painter now interleaves fg correctly.

If a new test fails: the most likely cause is the FgDefault/BgDefault reset order at the end of `RenderChanged`. Verify that the trailing `FgDefault` appears before the trailing `BgDefault`.

If an old test fails (e.g. `Render_RemovedLine_Color_WrapsWithBgEscapes`): something in `RenderChanged` changed observable output. Check that you still emit the prologue `BgRemovedRow + FgRemovedSigil + gutter + ' ' + sigil + FgDefault + ' '` and that the final char before `BgDefault` is `' '` (the single trailing space).

- [ ] **Step 8: Commit**

```bash
git add src/Structura.Reporting/Internal/DiffLineRenderer.cs \
        src/Structura.Reporting/UnifiedDiffReporter.cs \
        tests/Structura.UnitTests/Reporting/DiffLineRendererTests.cs
git commit -m "feat(reporting): DiffLineRenderer interleaves token fg with diff bg

Render now accepts an IDiffSyntaxPainter and walks content column-by-column,
emitting an ANSI fg escape on each token-kind transition. NullPainter keeps
the output byte-equal to step 12 by returning an empty token list.
UnifiedDiffReporter still passes NullPainter — wiring lands in a later step."
```

---

## Task 8: Wire painter into `SideBySideRowRenderer`

**Files:**
- Modify: `src/Structura.Reporting/Internal/SideBySideRowRenderer.cs`
- Modify: `tests/Structura.UnitTests/Reporting/SideBySideDiffReporterColorTests.cs`

Same shape as Task 7: signature gains `IDiffSyntaxPainter`, every cell tokenizes its content, the truncation logic clips token ranges to `[0, VisibleContentLength)`. The truncation indicator (`…` / `>`) is emitted at default fg.

- [ ] **Step 1: Add the parameter — break compilation**

Update the `Render` signature in `src/Structura.Reporting/Internal/SideBySideRowRenderer.cs`:

```csharp
public static string Render(
    SideBySideRow row,
    int gutterWidth,
    int contentWidth,
    bool useColor,
    bool useUnicode,
    IDiffSyntaxPainter painter)
```

Add `using Structura.Reporting.Internal.Highlighting;`.

- [ ] **Step 2: Update existing color/non-color tests to pass `NullPainter.Instance`**

In `tests/Structura.UnitTests/Reporting/SideBySideDiffReporterColorTests.cs` and `SideBySideDiffReporterTests.cs`, every call to `SideBySideRowRenderer.Render(...)` (note: most tests go through `SideBySideDiffReporter.RenderTo`, which adds the painter automatically — but the renderer is also called directly in some tests, see file). Run a project-wide grep:

```bash
grep -rn "SideBySideRowRenderer.Render" tests
```

For each direct call, append `, NullPainter.Instance`. The reporter-level tests (`RenderColored`, etc.) don't need changes — those go through `SideBySideDiffReporter.RenderTo`, which Task 10 wires up.

Update the `SideBySideDiffReporter.cs` internal `RenderTo` to pass `NullPainter.Instance` as a placeholder (Task 10 replaces it):

```csharp
foreach (SideBySideRow row in rows)
{
    string rendered = SideBySideRowRenderer.Render(row, gutterWidth, contentWidth, useColor, useUnicode, NullPainter.Instance);
    writer.WriteLine(rendered);
}
```

Add `using Structura.Reporting.Internal.Highlighting;` at the top of `SideBySideDiffReporter.cs`.

Build:

```bash
dotnet build Structura.slnx
```

Expected: clean.

- [ ] **Step 3: Run the existing suite — should still be green**

```bash
dotnet test
```

Expected: green. The painter is `NullPainter`, so every test sees identical output.

- [ ] **Step 4: Add a failing test for SBS token coloring**

Append to `SideBySideDiffReporterColorTests.cs`:

```csharp
[Fact]
public void Print_ColorEnabled_SyntaxOn_AddedCellEmbedsKeyFg()
{
    string output = RenderColored(new DiffReporterOptions { SyntaxHighlight = true });

    output.Should().Contain(SyntaxPalette.Bright(TokenKind.Key));
    output.Should().Contain(SyntaxPalette.Bright(TokenKind.Number));
}

[Fact]
public void Print_ColorEnabled_SyntaxOff_NoTokenFg()
{
    string output = RenderColored(new DiffReporterOptions { SyntaxHighlight = false });

    output.Should().NotContain(SyntaxPalette.Bright(TokenKind.Key));
    output.Should().NotContain(SyntaxPalette.Bright(TokenKind.Number));
}
```

Add `using Structura.Reporting.Internal.Highlighting;` to imports.

- [ ] **Step 5: Run new tests — expect failures**

```bash
dotnet test --filter "FullyQualifiedName~SideBySideDiffReporterColorTests"
```

Expected: the two new tests fail — `RenderColored` still passes `NullPainter` to the renderer.

- [ ] **Step 6: Implement painter-aware cell rendering**

The cell renderer currently has a `RenderChangedCell` and `RenderContextCell`, both calling `AppendVisibleContentWithHighlights`. Replace `AppendVisibleContentWithHighlights` with a two-arg version that accepts the painter-derived clipped tokens AND clipped highlights, and that walks columns the same way `DiffLineRenderer.AppendContent` does.

Replace the relevant block in `src/Structura.Reporting/Internal/SideBySideRowRenderer.cs`. The full new cell-rendering logic:

```csharp
private static string RenderContextCell(string gutter, char sigil, TruncatedContent t, bool useColor, IReadOnlyList<TokenRange> clippedTokens)
{
    if (!useColor)
    {
        return $"{gutter} {sigil} {t.Visible}{t.Padding}";
    }
    var sb = new StringBuilder();
    sb.Append(AnsiPalette.Dim).Append(gutter).Append(' ').Append(sigil).Append(AnsiPalette.DimOff);
    sb.Append(' ');
    AppendCellContent(sb, t, clippedTokens, Array.Empty<ColumnRange>(), rowBg: string.Empty, highlightBg: string.Empty, useDimPalette: true);
    sb.Append(t.Padding);
    return sb.ToString();
}

private static string RenderChangedCell(
    DiffLine line,
    string gutter,
    char sigil,
    TruncatedContent t,
    bool useColor,
    IReadOnlyList<TokenRange> clippedTokens)
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
    AppendCellContent(sb, t, clippedTokens, line.InlineHighlights, rowBg, highlightBg, useDimPalette: false);
    sb.Append(t.Padding);
    sb.Append(AnsiPalette.BgDefault);
    return sb.ToString();
}

private static void AppendCellContent(
    StringBuilder sb,
    TruncatedContent t,
    IReadOnlyList<TokenRange> tokens,
    IReadOnlyList<ColumnRange> highlights,
    string rowBg,
    string highlightBg,
    bool useDimPalette)
{
    // Walk t.Visible (which may include the truncation indicator at index
    // VisibleContentLength when truncation happened). Highlights and tokens
    // apply only to indices [0, VisibleContentLength); columns past that
    // emit at default fg / no highlight so the indicator stays neutral.
    TokenKind activeTokenKind = TokenKind.Punctuation;
    string activeFg = string.Empty;
    bool inHighlight = false;
    var tokenIndex = 0;
    var highlightIndex = 0;

    for (var col = 0; col < t.Visible.Length; col++)
    {
        bool insideContent = col < t.VisibleContentLength;

        while (insideContent && tokenIndex < tokens.Count && tokens[tokenIndex].Range.End <= col)
        {
            tokenIndex++;
        }
        TokenKind kindAtCol = insideContent && tokenIndex < tokens.Count && tokens[tokenIndex].Range.Start <= col
            ? tokens[tokenIndex].Kind
            : TokenKind.Punctuation;
        string fgAtCol = !insideContent
            ? string.Empty
            : (useDimPalette ? SyntaxPalette.Dim(kindAtCol) : SyntaxPalette.Bright(kindAtCol));

        while (insideContent && highlightIndex < highlights.Count && highlights[highlightIndex].End <= col)
        {
            highlightIndex++;
        }
        bool inHighlightAtCol = insideContent && highlightIndex < highlights.Count && highlights[highlightIndex].Start <= col;

        if (col == 0 || kindAtCol != activeTokenKind || fgAtCol != activeFg)
        {
            sb.Append(fgAtCol.Length > 0 ? fgAtCol : AnsiPalette.FgDefault);
            activeTokenKind = kindAtCol;
            activeFg = fgAtCol;
        }

        if (inHighlightAtCol != inHighlight)
        {
            if (inHighlightAtCol)
            {
                sb.Append(highlightBg).Append(AnsiPalette.Bold);
            }
            else
            {
                sb.Append(AnsiPalette.BoldOff).Append(rowBg.Length > 0 ? rowBg : AnsiPalette.BgDefault);
            }
            inHighlight = inHighlightAtCol;
        }

        sb.Append(t.Visible[col]);
    }

    if (inHighlight)
    {
        sb.Append(AnsiPalette.BoldOff);
        if (rowBg.Length > 0)
        {
            sb.Append(rowBg);
        }
    }
    sb.Append(AnsiPalette.FgDefault);
}
```

Update `RenderCell` to compute the clipped tokens once per cell and pass them to the changed/context renderer. Replace the existing `RenderCell`:

```csharp
private static string RenderCell(
    DiffLine? maybeLine,
    int gutterWidth,
    int contentWidth,
    bool isLeftSide,
    bool useColor,
    bool useUnicode,
    IDiffSyntaxPainter painter)
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
    IReadOnlyList<TokenRange> rawTokens = useColor
        ? painter.TokenizeLine(line.Content)
        : Array.Empty<TokenRange>();
    IReadOnlyList<TokenRange> clippedTokens = ClipTokens(rawTokens, truncation.VisibleContentLength);

    if (line.Kind == DiffLineKind.Context)
    {
        return RenderContextCell(gutter, sigil, truncation, useColor, clippedTokens);
    }
    return RenderChangedCell(line, gutter, sigil, truncation, useColor, clippedTokens);
}

private static IReadOnlyList<TokenRange> ClipTokens(IReadOnlyList<TokenRange> tokens, int visibleEnd)
{
    if (tokens.Count == 0 || visibleEnd <= 0)
    {
        return Array.Empty<TokenRange>();
    }
    var clipped = new List<TokenRange>(tokens.Count);
    foreach (TokenRange t in tokens)
    {
        int clippedStart = Math.Min(t.Range.Start, visibleEnd);
        int clippedEnd = Math.Min(t.Range.End, visibleEnd);
        int len = clippedEnd - clippedStart;
        if (len > 0)
        {
            clipped.Add(new TokenRange(new ColumnRange(clippedStart, len), t.Kind));
        }
    }
    return clipped;
}
```

And update the top-level `Render` to pass the painter into `RenderCell`:

```csharp
public static string Render(
    SideBySideRow row,
    int gutterWidth,
    int contentWidth,
    bool useColor,
    bool useUnicode,
    IDiffSyntaxPainter painter)
{
    string leftCell = RenderCell(row.Left, gutterWidth, contentWidth, isLeftSide: true, useColor, useUnicode, painter);
    string rightCell = RenderCell(row.Right, gutterWidth, contentWidth, isLeftSide: false, useColor, useUnicode, painter);
    string separator = RenderSeparator(useColor, useUnicode);
    return leftCell + separator + rightCell;
}
```

Delete the old `AppendVisibleContentWithHighlights` method and its callers — they have been replaced by `AppendCellContent`.

- [ ] **Step 7: Update `SideBySideDiffReporter.RenderTo` to pass `NullPainter` (Task 10 replaces)**

Already done in Step 2 of this task.

- [ ] **Step 8: Run all tests — expect all pass except the new SBS color tests if SBS reporter still passes NullPainter**

```bash
dotnet test
```

Expected: all existing tests green; the two new color tests in Task 8 step 4 still **fail** because `SideBySideDiffReporter.RenderTo` is still passing `NullPainter`. That's fine — they will be fixed in Task 10.

- [ ] **Step 9: Commit**

```bash
git add src/Structura.Reporting/Internal/SideBySideRowRenderer.cs \
        src/Structura.Reporting/SideBySideDiffReporter.cs \
        tests/Structura.UnitTests/Reporting/SideBySideDiffReporterColorTests.cs \
        tests/Structura.UnitTests/Reporting/SideBySideDiffReporterTests.cs
git commit -m "feat(reporting): SideBySideRowRenderer interleaves token fg per cell

Token ranges are clipped to [0, VisibleContentLength) so the truncation
indicator stays at default fg. Cell walker mirrors DiffLineRenderer's
column-by-column emission. Reporter still passes NullPainter — wiring
lands in the next step."
```

---

## Task 9: Wire painter into `UnifiedDiffReporter.RenderTo`

**Files:**
- Modify: `src/Structura.Reporting/UnifiedDiffReporter.cs`
- Modify: `tests/Structura.UnitTests/Reporting/UnifiedDiffReporterTests.cs`

The reporter resolves a real painter via `PainterFactory.For(document, options.SyntaxHighlight)` when `useColor=true`, and `NullPainter.Instance` when `useColor=false`.

- [ ] **Step 1: Add a failing reporter-level test for syntax coloring**

Append to `UnifiedDiffReporterTests.cs`. Note that `UnifiedDiffReporter.Print` overloads with `TextWriter` are hard-wired to `useColor: false`. We need to call `RenderTo` directly, or add a color-mode test via `Console.Out` redirection… but `RenderTo` is private. The cleanest path: change visibility of `UnifiedDiffReporter.RenderTo` to `internal` — same pattern as `SideBySideDiffReporter.RenderTo` (already internal). Do that as part of this task.

Update `src/Structura.Reporting/UnifiedDiffReporter.cs`: change `private static void RenderTo` → `internal static void RenderTo`.

Then append the test:

```csharp
[Fact]
public void RenderTo_ColorEnabled_SyntaxHighlightOn_AppliesKeyFg()
{
    int ageOffset = Source.IndexOf("30", System.StringComparison.Ordinal);
    var c = new DocumentChange("/age", new TextSpan(ageOffset, 2), "30", "42");
    var doc = new FakeStructuraDocument(Source, new[] { c }, documentName: "test.json")
    {
        CurrentTextOverride = Source[..ageOffset] + "42" + Source[(ageOffset + 2)..],
    };
    var sw = new System.IO.StringWriter();

    UnifiedDiffReporter.RenderTo(doc, sw, new DiffReporterOptions(), useColor: true, useUnicode: true);

    string output = sw.ToString();
    output.Should().Contain(Structura.Reporting.Internal.Highlighting.SyntaxPalette.Bright(Structura.Reporting.Internal.Highlighting.TokenKind.Key));
    output.Should().Contain(Structura.Reporting.Internal.Highlighting.SyntaxPalette.Bright(Structura.Reporting.Internal.Highlighting.TokenKind.Number));
}

[Fact]
public void RenderTo_ColorEnabled_SyntaxHighlightOff_NoTokenFg()
{
    int ageOffset = Source.IndexOf("30", System.StringComparison.Ordinal);
    var c = new DocumentChange("/age", new TextSpan(ageOffset, 2), "30", "42");
    var doc = new FakeStructuraDocument(Source, new[] { c }, documentName: "test.json")
    {
        CurrentTextOverride = Source[..ageOffset] + "42" + Source[(ageOffset + 2)..],
    };
    var sw = new System.IO.StringWriter();

    UnifiedDiffReporter.RenderTo(doc, sw, new DiffReporterOptions { SyntaxHighlight = false }, useColor: true, useUnicode: true);

    string output = sw.ToString();
    output.Should().NotContain(Structura.Reporting.Internal.Highlighting.SyntaxPalette.Bright(Structura.Reporting.Internal.Highlighting.TokenKind.Key));
}
```

(Or add `using Structura.Reporting.Internal.Highlighting;` to keep the assertions readable.)

- [ ] **Step 2: Run — expect failure (still passes NullPainter)**

```bash
dotnet test --filter "FullyQualifiedName~UnifiedDiffReporterTests"
```

Expected: the two new tests fail.

- [ ] **Step 3: Wire `PainterFactory.For` into `RenderTo`**

In `src/Structura.Reporting/UnifiedDiffReporter.cs`, replace the foreach block with:

```csharp
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
```

Make sure `using Structura.Reporting.Internal.Highlighting;` is at the top.

- [ ] **Step 4: Run all tests**

```bash
dotnet test
```

Expected: green across the board. Old tests pass (DocumentName="test.json" → JsonLinePainter → token coloring is added on color=true paths but those old tests check `Should().Contain("\"age\": 30,")` etc. — substrings still appear inside the colored output). New tests pass.

If an old test fails on a color-mode assertion (e.g. exact-string equality), inspect: `RenderTo` color-mode tests use `Contain` not `Be`, so substrings should match. Only break if a test compared exact ANSI output — there are none.

- [ ] **Step 5: Commit**

```bash
git add src/Structura.Reporting/UnifiedDiffReporter.cs \
        tests/Structura.UnitTests/Reporting/UnifiedDiffReporterTests.cs
git commit -m "feat(reporting): UnifiedDiffReporter resolves painter via PainterFactory

useColor=false stays on NullPainter; useColor=true picks JsonLinePainter
or XmlLinePainter from DocumentName extension (sniff fallback). Plain-text
TextWriter overloads are unaffected — they pass useColor=false."
```

---

## Task 10: Wire painter into `SideBySideDiffReporter.RenderTo`

**Files:**
- Modify: `src/Structura.Reporting/SideBySideDiffReporter.cs`

Symmetric to Task 9. Replace `NullPainter.Instance` (placeholder from Task 8) with `PainterFactory.For(document, options.SyntaxHighlight)` when `useColor=true`.

- [ ] **Step 1: Confirm the two SBS color tests from Task 8 still fail**

```bash
dotnet test --filter "FullyQualifiedName~SideBySideDiffReporterColorTests"
```

Expected: `Print_ColorEnabled_SyntaxOn_AddedCellEmbedsKeyFg` and `Print_ColorEnabled_SyntaxOff_NoTokenFg` still fail (placeholder painter).

- [ ] **Step 2: Replace the placeholder**

In `src/Structura.Reporting/SideBySideDiffReporter.cs`, the `RenderTo` method's foreach currently looks like:

```csharp
foreach (SideBySideRow row in rows)
{
    string rendered = SideBySideRowRenderer.Render(row, gutterWidth, contentWidth, useColor, useUnicode, NullPainter.Instance);
    writer.WriteLine(rendered);
}
```

Replace the `NullPainter.Instance` argument by computing the painter once before the loop:

```csharp
IDiffSyntaxPainter painter = useColor
    ? PainterFactory.For(document, options.SyntaxHighlight)
    : NullPainter.Instance;

// … existing banner + width math …

foreach (SideBySideRow row in rows)
{
    string rendered = SideBySideRowRenderer.Render(row, gutterWidth, contentWidth, useColor, useUnicode, painter);
    writer.WriteLine(rendered);
}
```

- [ ] **Step 3: Run all tests**

```bash
dotnet test
```

Expected: all green, including the two SBS color tests.

- [ ] **Step 4: Commit**

```bash
git add src/Structura.Reporting/SideBySideDiffReporter.cs
git commit -m "feat(reporting): SideBySideDiffReporter resolves painter via PainterFactory"
```

---

## Task 11: Manual demo verification

**Files:** none modified. This task is the human-eye legibility check called out in the spec's *Risks / open questions* section.

- [ ] **Step 1: Run the demo in an interactive terminal**

```bash
dotnet run --project src/Structura/Structura.csproj
```

- [ ] **Step 2: Visually verify each of the eight diff blocks**

Look for the headings emitted by `Program.cs`. For each, confirm:

- `=== Diff (UnifiedDiffReporter) ===` (order JSON):
  - cyan keys, yellow string values, mauve numbers, orange `true`/`false`/`null`.
  - inline-highlight bg + bold marks the changed sub-span; token fg remains visible inside.
- `=== Diff (SideBySideDiffReporter) ===` (order JSON): same palette per cell, truncation indicator stays neutral.
- `=== BLRWBL Diff (UnifiedDiffReporter) ===` and `… SideBySideDiffReporter ===` (XML): cyan element names, sage attribute names, yellow attribute values.
- `=== Library Diff (UnifiedDiffReporter) ===` and `… SideBySideDiffReporter ===` (XML with comments + entities): comments grey, `&amp;`/`&lt;`/`&gt;` orange.
- `=== Library JSON Diff (UnifiedDiffReporter) ===` and `… SideBySideDiffReporter ===`: as for the order.

- [ ] **Step 3: Worst-case legibility check**

Locate any line where an orange `Keyword` (e.g. `true` → `false` change) sits on the dark-red row background (`Removed` line). If the orange is illegible, tune `BrightOrange` in `SyntaxPalette.cs` from `38;5;215` to `38;5;220` (more yellow) and re-run.

- [ ] **Step 4: CDATA regression check (informational, no fail)**

In the Library XML output, lines 2–3 of the multi-line CDATA body inside `b001` should render at default foreground. This matches the documented limitation in the spec's *Non-goals*. If they accidentally render in `Comment` grey or `Text` color, the lexer state machine has a bug — fix and re-test.

- [ ] **Step 5: Commit only if you adjusted the palette**

If you tuned a color in Step 3, commit:

```bash
git add src/Structura.Reporting/Internal/Highlighting/SyntaxPalette.cs
git commit -m "tweak(reporting): adjust SyntaxPalette.<color> for row-bg legibility"
```

If no palette adjustment was needed, no commit for this task.

---

## Self-Review Checklist (already performed)

- **Spec coverage:** Every spec section maps to a task — Token model (Task 1), Palette (Task 2), JSON lexer (Task 3), XML lexer (Task 4), Painter selection (Task 5), Public API (Tasks 0 + 6), Renderer integration (Tasks 7 + 8), Plain-text fallback (already correct after Tasks 7 + 8 by way of `useColor=false ⇒ NullPainter`), Acceptance demo (Task 11), Tests (covered per task).
- **No placeholders:** Every code block is complete; no TBD, TODO, or "implement later".
- **Type consistency:** `DiffReporterOptions` (introduced in Task 0) used consistently in Tasks 6, 7, 8, 9, 10. `IDiffSyntaxPainter`, `TokenKind`, `TokenRange`, `SyntaxPalette`, `PainterFactory`, `NullPainter`, `JsonLinePainter`, `XmlLinePainter` — same names everywhere they appear.
- **TDD:** Every painter and the renderer integration introduce tests before implementation.
- **Frequent commits:** Eleven commits (one per task plus Task 11 conditional).
