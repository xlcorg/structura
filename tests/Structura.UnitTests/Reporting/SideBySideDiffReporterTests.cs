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
        var doc = new FakeStructuraDocument(Source, Array.Empty<DocumentChange>(), documentName: "test.json");
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

        SideBySideDiffReporter.Print(doc, sw, OptionsWithWidth(120));

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
        var doc = new FakeStructuraDocument(Source, Array.Empty<DocumentChange>());

        var act = () => SideBySideDiffReporter.Print(doc, null!);

        act.Should().Throw<System.ArgumentNullException>();
    }
}
