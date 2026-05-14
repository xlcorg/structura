# Step 16 — `DiffReporter` separator options — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add two opt-in options on `DiffReporterOptions` —
`LeadingBlankLine` (default `true`) and `HorizontalRule` (default `false`) —
and emit them at the very top of `DiffReporter.RenderTo`, before the
`(no changes)` early return and before the banner. The banner itself,
both renderers, and `DiffBanner` are not modified.

**Architecture:** The change is purely additive on the public options and
local to one method (`DiffReporter.RenderTo`). When `LeadingBlankLine` is
on, `RenderTo` writes one empty line first. When `HorizontalRule` is on,
it writes a rule line built from `─` (utf-8) or `-` (ascii) repeated
`terminalWidth` times. Order when both are on: blank line, then rule, then
existing output. The `(no changes)` path receives the same separator
because the two `if` blocks sit above the early return.

**Tech Stack:** C# 13, .NET 10, xUnit 2.9, FluentAssertions 7.

Spec: `docs/plans/step-16-diff-reporter-separator-options.md`.

---

## File map

Modified:
- `src/Structura.Reporting/DiffReporterOptions.cs` — add `LeadingBlankLine` and `HorizontalRule` properties.
- `src/Structura.Reporting/DiffReporter.cs` — emit separators at the top of `RenderTo`.
- `tests/Structura.UnitTests/Reporting/DiffReporterTests.cs` — add new tests for the option defaults and the separator behavior.

Unchanged:
- `src/Structura.Reporting/Internal/DiffBanner.cs` — explicitly out of scope.
- `src/Structura.Reporting/Internal/UnifiedRenderer.cs`, `SideBySideRenderer.cs`, the highlighting pipeline, all other `Internal/*.cs`.
- `src/Structura/Program.cs` — the demo's existing pipelines benefit from the default `LeadingBlankLine = true` without source edits.
- All existing reporter tests (unit + integration) — they use `.Should().Contain(...)` substring assertions on the banner, body, and counts, none of which depend on the absence of leading whitespace.

---

### Task 1: Add `LeadingBlankLine` option and emit a leading blank line by default

**Files:**
- Modify: `src/Structura.Reporting/DiffReporterOptions.cs`
- Modify: `src/Structura.Reporting/DiffReporter.cs`
- Test: `tests/Structura.UnitTests/Reporting/DiffReporterTests.cs`

- [ ] **Step 1: Write the failing tests**

Append three tests to the end of `DiffReporterTests` (just before the closing `}` of the class):

```csharp
    [Fact]
    public void DiffReporterOptions_Defaults_LeadingBlankLineIsTrue()
    {
        var options = new DiffReporterOptions();

        options.LeadingBlankLine.Should().BeTrue();
    }

    [Fact]
    public void Print_DefaultOptions_StartsWithBlankLine()
    {
        var doc = MakeDoc();
        var sw = new System.IO.StringWriter();

        DiffReporter.Print(doc, sw);

        string output = sw.ToString();
        output.Should().StartWith(System.Environment.NewLine);
    }

    [Fact]
    public void Print_LeadingBlankLineFalse_StartsWithBannerDot()
    {
        var doc = MakeDoc();
        var sw = new System.IO.StringWriter();

        var options = new DiffReporterOptions
        {
            Layout = DiffReporterLayout.Unified,
            LeadingBlankLine = false,
        };
        DiffReporter.Print(doc, sw, options);

        string output = sw.ToString();
        // The TextWriter overload uses useUnicode = true, so the dot is `●`.
        output.Should().StartWith("●");
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test Structura.slnx --filter "FullyQualifiedName~DiffReporterOptions_Defaults_LeadingBlankLineIsTrue|FullyQualifiedName~Print_DefaultOptions_StartsWithBlankLine|FullyQualifiedName~Print_LeadingBlankLineFalse_StartsWithBannerDot"`
Expected: build error — `DiffReporterOptions.LeadingBlankLine` does not exist.

- [ ] **Step 3: Add the property to `DiffReporterOptions`**

Edit `src/Structura.Reporting/DiffReporterOptions.cs`. Append this property inside the record body, after the existing `Layout` property:

```csharp
    /// <summary>
    /// When <c>true</c>, emit a single blank line before any other output.
    /// Separates the diff from preceding console text. Default <c>true</c>.
    /// </summary>
    public bool LeadingBlankLine { get; init; } = true;
```

- [ ] **Step 4: Run the tests to verify the build passes but behavior tests fail**

Run: `dotnet test Structura.slnx --filter "FullyQualifiedName~DiffReporterOptions_Defaults_LeadingBlankLineIsTrue|FullyQualifiedName~Print_DefaultOptions_StartsWithBlankLine|FullyQualifiedName~Print_LeadingBlankLineFalse_StartsWithBannerDot"`
Expected: `DiffReporterOptions_Defaults_LeadingBlankLineIsTrue` passes;
`Print_DefaultOptions_StartsWithBlankLine` fails — output starts with `●`,
not with a newline; `Print_LeadingBlankLineFalse_StartsWithBannerDot`
passes (already starts with `●`).

- [ ] **Step 5: Emit the leading blank line in `RenderTo`**

Edit `src/Structura.Reporting/DiffReporter.cs`. In the `RenderTo` method,
immediately after the three `ArgumentNullException.ThrowIfNull` calls
and before the `IReadOnlyList<DocumentChange> changes = document.Changes;`
line, insert:

```csharp
        if (options.LeadingBlankLine)
        {
            writer.WriteLine();
        }
```

The full top of `RenderTo` should now read:

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

        IReadOnlyList<DocumentChange> changes = document.Changes;
        if (changes.Count == 0)
        {
            writer.WriteLine("(no changes)");
            return;
        }
        // …unchanged below
    }
```

- [ ] **Step 6: Run the new tests to verify they pass**

Run: `dotnet test Structura.slnx --filter "FullyQualifiedName~DiffReporterOptions_Defaults_LeadingBlankLineIsTrue|FullyQualifiedName~Print_DefaultOptions_StartsWithBlankLine|FullyQualifiedName~Print_LeadingBlankLineFalse_StartsWithBannerDot"`
Expected: all three pass.

- [ ] **Step 7: Run the full reporter test suite to confirm no regressions**

Run: `dotnet test Structura.slnx --filter "FullyQualifiedName~Reporting"`
Expected: all pass — existing assertions are substring-based and tolerate a leading newline.

- [ ] **Step 8: Commit**

```bash
git add src/Structura.Reporting/DiffReporterOptions.cs \
        src/Structura.Reporting/DiffReporter.cs \
        tests/Structura.UnitTests/Reporting/DiffReporterTests.cs
git commit -m "$(cat <<'EOF'
feat(reporting): emit leading blank line by default in DiffReporter

Adds DiffReporterOptions.LeadingBlankLine (default true). RenderTo writes a
single blank line before any other output so the banner no longer hugs
preceding console text. Callers who need the previous behavior can opt out
with LeadingBlankLine = false.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 2: Add `HorizontalRule` option and emit a width-spanning rule before the banner

**Files:**
- Modify: `src/Structura.Reporting/DiffReporterOptions.cs`
- Modify: `src/Structura.Reporting/DiffReporter.cs`
- Test: `tests/Structura.UnitTests/Reporting/DiffReporterTests.cs`

- [ ] **Step 1: Write the failing tests**

Append three tests to the end of `DiffReporterTests`:

```csharp
    [Fact]
    public void DiffReporterOptions_Defaults_HorizontalRuleIsFalse()
    {
        var options = new DiffReporterOptions();

        options.HorizontalRule.Should().BeFalse();
    }

    [Fact]
    public void RenderTo_HorizontalRuleTrue_Unicode_WritesUnicodeRuleAtTerminalWidth()
    {
        var doc = MakeDoc();
        var sw = new System.IO.StringWriter();

        var options = new DiffReporterOptions
        {
            Layout = DiffReporterLayout.Unified,
            LeadingBlankLine = false,
            HorizontalRule = true,
        };
        DiffReporter.RenderTo(doc, sw, options, terminalWidth: 80, useColor: false, useUnicode: true);

        string output = sw.ToString();
        string expectedRule = new string('─', 80);
        output.Should().StartWith(expectedRule + System.Environment.NewLine);
        // Banner is the next thing after the rule.
        output.Should().Contain(expectedRule + System.Environment.NewLine + "●");
    }

    [Fact]
    public void RenderTo_HorizontalRuleTrue_Ascii_WritesAsciiRuleAtTerminalWidth()
    {
        var doc = MakeDoc();
        var sw = new System.IO.StringWriter();

        var options = new DiffReporterOptions
        {
            Layout = DiffReporterLayout.Unified,
            LeadingBlankLine = false,
            HorizontalRule = true,
        };
        DiffReporter.RenderTo(doc, sw, options, terminalWidth: 80, useColor: false, useUnicode: false);

        string output = sw.ToString();
        string expectedRule = new string('-', 80);
        output.Should().StartWith(expectedRule + System.Environment.NewLine);
        output.Should().Contain(expectedRule + System.Environment.NewLine + "*");
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test Structura.slnx --filter "FullyQualifiedName~DiffReporterOptions_Defaults_HorizontalRuleIsFalse|FullyQualifiedName~RenderTo_HorizontalRuleTrue_Unicode_WritesUnicodeRuleAtTerminalWidth|FullyQualifiedName~RenderTo_HorizontalRuleTrue_Ascii_WritesAsciiRuleAtTerminalWidth"`
Expected: build error — `DiffReporterOptions.HorizontalRule` does not exist.

- [ ] **Step 3: Add the property to `DiffReporterOptions`**

Edit `src/Structura.Reporting/DiffReporterOptions.cs`. Append this property after `LeadingBlankLine`:

```csharp
    /// <summary>
    /// When <c>true</c>, emit a horizontal rule line spanning the resolved
    /// terminal width immediately before the banner. The rule character is
    /// <c>─</c> (utf-8) or <c>-</c> (ascii), matching the banner's
    /// utf-8 / ascii branching. Default <c>false</c>.
    /// </summary>
    public bool HorizontalRule { get; init; } = false;
```

- [ ] **Step 4: Emit the rule in `RenderTo`**

Edit `src/Structura.Reporting/DiffReporter.cs`. In `RenderTo`, immediately
after the `LeadingBlankLine` block added in Task 1 and before
`IReadOnlyList<DocumentChange> changes = document.Changes;`, insert:

```csharp
        if (options.HorizontalRule)
        {
            char ruleChar = useUnicode ? '─' : '-';
            string rule = new string(ruleChar, terminalWidth);
            writer.WriteLine(rule);
        }
```

The top of `RenderTo` should now read:

```csharp
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(options);

        if (options.LeadingBlankLine)
        {
            writer.WriteLine();
        }
        if (options.HorizontalRule)
        {
            char ruleChar = useUnicode ? '─' : '-';
            string rule = new string(ruleChar, terminalWidth);
            writer.WriteLine(rule);
        }

        IReadOnlyList<DocumentChange> changes = document.Changes;
        if (changes.Count == 0)
        {
            writer.WriteLine("(no changes)");
            return;
        }
```

- [ ] **Step 5: Run the new tests to verify they pass**

Run: `dotnet test Structura.slnx --filter "FullyQualifiedName~DiffReporterOptions_Defaults_HorizontalRuleIsFalse|FullyQualifiedName~RenderTo_HorizontalRuleTrue_Unicode_WritesUnicodeRuleAtTerminalWidth|FullyQualifiedName~RenderTo_HorizontalRuleTrue_Ascii_WritesAsciiRuleAtTerminalWidth"`
Expected: all three pass.

- [ ] **Step 6: Run the full reporter test suite to confirm no regressions**

Run: `dotnet test Structura.slnx --filter "FullyQualifiedName~Reporting"`
Expected: all pass.

- [ ] **Step 7: Commit**

```bash
git add src/Structura.Reporting/DiffReporterOptions.cs \
        src/Structura.Reporting/DiffReporter.cs \
        tests/Structura.UnitTests/Reporting/DiffReporterTests.cs
git commit -m "$(cat <<'EOF'
feat(reporting): add opt-in HorizontalRule option to DiffReporter

Adds DiffReporterOptions.HorizontalRule (default false). When enabled,
RenderTo writes a terminal-width rule line (─ for utf-8, - for ascii)
immediately before the banner. Combines with LeadingBlankLine: blank line
first, then rule, then banner.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 3: Lock in the order when both options are enabled

**Files:**
- Test: `tests/Structura.UnitTests/Reporting/DiffReporterTests.cs`

This task adds a single regression test that pins the documented order
(blank line → rule → banner). No production code changes — the order
already falls out of the `if` blocks placed in Tasks 1 and 2. The test
exists so a future refactor cannot reorder the blocks silently.

- [ ] **Step 1: Write the test**

Append to `DiffReporterTests`:

```csharp
    [Fact]
    public void RenderTo_BothSeparatorsEnabled_OrderIsBlankThenRuleThenBanner()
    {
        var doc = MakeDoc();
        var sw = new System.IO.StringWriter();

        var options = new DiffReporterOptions
        {
            Layout = DiffReporterLayout.Unified,
            LeadingBlankLine = true,
            HorizontalRule = true,
        };
        DiffReporter.RenderTo(doc, sw, options, terminalWidth: 80, useColor: false, useUnicode: true);

        string output = sw.ToString();
        string[] lines = output.Split(new[] { System.Environment.NewLine }, System.StringSplitOptions.None);

        lines[0].Should().BeEmpty();
        lines[1].Should().Be(new string('─', 80));
        lines[2].Should().StartWith("●");
    }
```

- [ ] **Step 2: Run the test to verify it passes**

Run: `dotnet test Structura.slnx --filter "FullyQualifiedName~RenderTo_BothSeparatorsEnabled_OrderIsBlankThenRuleThenBanner"`
Expected: PASS — Tasks 1 and 2 already produce this order.

- [ ] **Step 3: Commit**

```bash
git add tests/Structura.UnitTests/Reporting/DiffReporterTests.cs
git commit -m "$(cat <<'EOF'
test(reporting): pin DiffReporter separator order when both options set

Locks in the documented order — blank line, then rule, then banner — so a
future refactor of RenderTo cannot reorder the separator blocks silently.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 4: Verify the `(no changes)` path also receives separators

**Files:**
- Test: `tests/Structura.UnitTests/Reporting/DiffReporterTests.cs`

The spec requires uniform behavior across the rendered path and the
`(no changes)` early-return path. The `if` blocks in Tasks 1 and 2 sit
*above* the early return, so this already works — but the spec calls out
this case explicitly, so we lock it in with a test.

- [ ] **Step 1: Write the test**

Append to `DiffReporterTests`:

```csharp
    [Fact]
    public void RenderTo_NoChanges_BothSeparatorsEnabled_EmitsBlankAndRuleBeforeNoChangesMessage()
    {
        var emptyDoc = new FakeStructuraDocument(Source, System.Array.Empty<DocumentChange>(), documentName: "test.json");
        var sw = new System.IO.StringWriter();

        var options = new DiffReporterOptions
        {
            Layout = DiffReporterLayout.Unified,
            LeadingBlankLine = true,
            HorizontalRule = true,
        };
        DiffReporter.RenderTo(emptyDoc, sw, options, terminalWidth: 80, useColor: false, useUnicode: true);

        string output = sw.ToString();
        string[] lines = output.Split(new[] { System.Environment.NewLine }, System.StringSplitOptions.None);

        lines[0].Should().BeEmpty();
        lines[1].Should().Be(new string('─', 80));
        lines[2].Should().Be("(no changes)");
    }
```

- [ ] **Step 2: Run the test to verify it passes**

Run: `dotnet test Structura.slnx --filter "FullyQualifiedName~RenderTo_NoChanges_BothSeparatorsEnabled_EmitsBlankAndRuleBeforeNoChangesMessage"`
Expected: PASS — separator blocks already sit above the early return.

- [ ] **Step 3: Commit**

```bash
git add tests/Structura.UnitTests/Reporting/DiffReporterTests.cs
git commit -m "$(cat <<'EOF'
test(reporting): cover separator emission on DiffReporter no-changes path

Pins that LeadingBlankLine and HorizontalRule fire on the (no changes)
early-return path too, matching the spec's requirement for uniform
separator behavior.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 5: Final whole-suite + demo verification

**Files:**
- Run-only — no code changes.

- [ ] **Step 1: Run the full solution test suite**

Run: `dotnet test Structura.slnx`
Expected: all unit and integration tests pass. Reporter substring
assertions remain green; no other suites are affected.

- [ ] **Step 2: Run the demo and visually confirm the banner has breathing room**

Run: `dotnet run --project src/Structura/Structura.csproj`
Expected output (excerpt):

```
=== Diff ===

● Patched
  └ Patched order.sample.json with 8 additions and 8 removals
    4    "created_at_utc": "2026-05-02T10:15:30Z",
    …
```

A blank line now sits between `=== Diff ===` and `● Patched` for all four
pipelines (order JSON, BLRWBL XML, library XML, library JSON). No
horizontal rule appears because the demo uses default options.

- [ ] **Step 3: No commit**

Step 5 is verification only. If the demo or test suite reveals a
problem, file the fix as a separate task and re-run.

---

## Self-review notes

- **Spec coverage.** Public-surface additions (Task 1, Task 2). Behavior
  ordering (Task 3). `(no changes)` path (Task 4). Demo unchanged (Task 5
  Step 2). Ascii/utf-8 rule branching (Task 2 tests cover both). Default
  values (Task 1 + Task 2 default-tests). Banner untouched (no task
  modifies `DiffBanner`).
- **No placeholders.** Every code step contains complete code. Every test
  has its `expected` line and exact `dotnet test --filter` command.
- **Type/name consistency.** Property names (`LeadingBlankLine`,
  `HorizontalRule`), the rule character logic (`useUnicode ? '─' : '-'`),
  and the option-passing pattern (`new DiffReporterOptions { … }`) match
  across all five tasks.
- **Assertions are cross-platform.** Tests use
  `System.Environment.NewLine` and `string.Split(new[] { Environment.NewLine }, StringSplitOptions.None)`
  rather than hardcoding `"\n"`, so Windows CRLF builds behave the same as
  Linux LF builds.
