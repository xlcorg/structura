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

    [Fact]
    public void Print_ColorEnabled_SeparatorIsDimmed()
    {
        string output = RenderColored(new DiffReporterOptions());

        string sep = $" {AnsiPalette.Dim}│{AnsiPalette.DimOff} ";
        output.Should().Contain(sep);
    }

    [Fact]
    public void Print_ColorEnabled_RowBgFillsToColumnEdge()
    {
        string output = RenderColored(new DiffReporterOptions());

        // Find a Removed cell. It begins with BgRemovedRow and ends with BgDefault.
        // Between the visible content (which contains "30") and BgDefault, there
        // must be padding spaces filling to the column edge — otherwise SBS would
        // look like Unified and the column-fill design would be lost.
        int rowStart = output.IndexOf(AnsiPalette.BgRemovedRow, System.StringComparison.Ordinal);
        rowStart.Should().BeGreaterThanOrEqualTo(0);
        int rowEnd = output.IndexOf(AnsiPalette.BgDefault, rowStart, System.StringComparison.Ordinal);
        rowEnd.Should().BeGreaterThan(rowStart);

        string cell = output.Substring(rowStart, rowEnd - rowStart);
        // The cell must contain trailing whitespace before the BgDefault marker.
        cell.Should().EndWith(" ", "row bg must extend past content with padding spaces to fill the column");
    }

    [Fact]
    public void Print_ColorEnabled_HunkSeparatorIsDimmed()
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

        var withLayout = new DiffReporterOptions() with { Layout = DiffReporterLayout.SideBySide };
        DiffReporter.RenderTo(doc, sw, withLayout, terminalWidth: 120, useColor: true, useUnicode: true);

        string output = sw.ToString();
        string sepWrapped = AnsiPalette.Dim + "…" + AnsiPalette.DimOff;
        output.Should().Contain(sepWrapped);
    }

    [Fact]
    public void Print_ColorEnabled_SyntaxOn_AddedCellEmbedsKeyFg()
    {
        string output = RenderColored(new DiffReporterOptions { SyntaxHighlight = true });

        output.Should().Contain(SyntaxPalette.Bright(TokenKind.Key));
        // Number fg is suppressed inside the inline-highlight (the changed "30"/"42" span);
        // bright bg + bold is the change indicator there.
        output.Should().NotContain(SyntaxPalette.Bright(TokenKind.Number));
    }

    [Fact]
    public void Print_ColorEnabled_SyntaxOff_NoTokenFg()
    {
        string output = RenderColored(new DiffReporterOptions { SyntaxHighlight = false });

        output.Should().NotContain(SyntaxPalette.Bright(TokenKind.Key));
        output.Should().NotContain(SyntaxPalette.Bright(TokenKind.Number));
    }
}
