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

    private static void Render(IStructuraDocument doc, System.IO.StringWriter sw, int totalWidth, DiffReporterOptions? options = null)
    {
        DiffReporterOptions effective = options ?? new DiffReporterOptions();
        SideBySideDiffReporter.RenderTo(doc, sw, effective, totalWidth, useColor: false, useUnicode: true);
    }

    [Fact]
    public void Print_NoChanges_WritesNoChangesMessage()
    {
        var doc = new FakeStructuraDocument(Source, Array.Empty<DocumentChange>(), documentName: "test.json");
        var sw = new System.IO.StringWriter();

        Render(doc, sw, 80);

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
        Render(doc, sbsSw, 120);

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

        Render(doc, sw, 120);

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

        Render(doc, sw, 120);

        sw.ToString().Should().NotContain("\x1b");
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
        // Source has 6 lines: {, "name":, "age":, "city":, "role":, }. Full-file
        // mode shows them all even far from the change.
        output.Should().Contain("\"name\": \"Alice\",");
        output.Should().Contain("\"city\": \"Paris\"");
        output.Should().Contain("\"role\": \"admin\"");
        // The change pair is still highlighted with - and + sigils.
        output.Should().Contain("\"age\": 30,");
        output.Should().Contain("\"age\": 42,");
        // No hunk separator should appear (everything is one continuous block).
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
        // No ellipsis separator anywhere in full-file mode.
        output.Should().NotContain("…");
        // The unchanged middle keys k1..k19 should be visible.
        output.Should().Contain("\"k10\": 10,");
        output.Should().Contain("\"k15\": 15,");
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
        var doc = new FakeStructuraDocument(Source, Array.Empty<DocumentChange>());

        var act = () => SideBySideDiffReporter.Print(doc, writer: null!);

        act.Should().Throw<System.ArgumentNullException>();
    }

    [Fact]
    public void Print_ContextLineNumberingDivergesAfterPriorHunk()
    {
        // First hunk: replace single-line "  \"first\": 1," with two lines, shifting
        // subsequent line numbers by +1. Second hunk far enough away to be a
        // separate hunk, so its context rows show divergent old/new line numbers.
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

        // First change: 1 line → 2 lines (cumulativeLineDelta becomes +1).
        var firstChange = new DocumentChange(
            "/first",
            new TextSpan(firstOffset, firstLen),
            "\"first\": 1",
            "\"first_a\": 1,\n  \"first_b\": 1");
        // Second change far below: keeps 1 line replacement, but its context's old
        // and new line numbers now differ by +1.
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

        // Find a context row in the second hunk (one of the "k15"/"k16"/"k17" lines).
        // Such a row will have left-side OLD number X and right-side NEW number X+1
        // because of the +1 cumulative line delta.
        bool foundDivergent = outputLines.Any(line =>
        {
            int sepIdx = line.IndexOf(" │ ", System.StringComparison.Ordinal);
            if (sepIdx < 0) { return false; }
            // Body row template: "{leftGutter} {sigil} {leftContent}  │  {rightGutter} {sigil} {rightContent}"
            // Both halves of context rows have the same content but different gutters.
            // We're looking for any row where the gutter numbers differ between sides.
            string left = line[..sepIdx];
            string right = line[(sepIdx + 3)..];
            // Strip and read the leading integer from each half (if present).
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
            // Context-line indicator: same sigil ' ' on both sides, content matches.
            return leftNum != rightNum;
        });

        foundDivergent.Should().BeTrue("after a 1→2 line replacement, context-row gutters in later hunks should diverge");
    }
}
