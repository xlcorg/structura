using FluentAssertions;

using Structura.Reporting.Internal;
using Structura.Reporting.Internal.Highlighting;

using Xunit;

namespace Structura.UnitTests.Reporting;

public sealed class DiffLineRendererTests
{
    [Fact]
    public void Render_ContextLine_NoColor_IsPlainGutterAndContent()
    {
        var line = new DiffLine(DiffLineKind.Context, 7, 7, "  \"x\": 1,", System.Array.Empty<ColumnRange>());

        string s = DiffLineRenderer.Render(line, gutterWidth: 3, useColor: false, useUnicode: true, NullPainter.Instance);

        s.Should().Be("  7     \"x\": 1,");
    }

    [Fact]
    public void Render_RemovedLine_NoColor_HasMinusSigil()
    {
        var line = new DiffLine(DiffLineKind.Removed, 7, 0, "  \"x\": 1,", System.Array.Empty<ColumnRange>());

        string s = DiffLineRenderer.Render(line, gutterWidth: 3, useColor: false, useUnicode: true, NullPainter.Instance);

        s.Should().Be("  7 -   \"x\": 1,");
    }

    [Fact]
    public void Render_AddedLine_NoColor_HasPlusSigil()
    {
        var line = new DiffLine(DiffLineKind.Added, 0, 7, "  \"x\": 2,", System.Array.Empty<ColumnRange>());

        string s = DiffLineRenderer.Render(line, gutterWidth: 3, useColor: false, useUnicode: true, NullPainter.Instance);

        s.Should().Be("  7 +   \"x\": 2,");
    }

    [Fact]
    public void Render_HunkSeparator_UnicodeOn_EmitsEllipsis()
    {
        var line = new DiffLine(DiffLineKind.HunkSeparator, 0, 0, string.Empty, System.Array.Empty<ColumnRange>());

        string s = DiffLineRenderer.Render(line, gutterWidth: 3, useColor: false, useUnicode: true, NullPainter.Instance);

        s.Should().Be("      …");  // 3 (gutter) + 3 (sep+sigil+sep) = 6 chars then …
    }

    [Fact]
    public void Render_HunkSeparator_UnicodeOff_EmitsThreeDots()
    {
        var line = new DiffLine(DiffLineKind.HunkSeparator, 0, 0, string.Empty, System.Array.Empty<ColumnRange>());

        string s = DiffLineRenderer.Render(line, gutterWidth: 3, useColor: false, useUnicode: false, NullPainter.Instance);

        s.Should().Be("      ...");
    }

    [Fact]
    public void Render_RemovedLine_Color_WrapsWithBgEscapes()
    {
        var line = new DiffLine(DiffLineKind.Removed, 7, 0, "  \"x\": 1,", System.Array.Empty<ColumnRange>());

        string s = DiffLineRenderer.Render(line, gutterWidth: 3, useColor: true, useUnicode: true, NullPainter.Instance);

        s.Should().StartWith(AnsiPalette.BgRemovedRow);
        s.Should().EndWith(AnsiPalette.BgDefault);
        s.Should().Contain(AnsiPalette.FgRemovedSigil);
        s.Should().Contain("  7 -");                  // gutter+sigil under colored fg
        s.Should().Contain("  \"x\": 1,");            // content under default fg
    }

    [Fact]
    public void Render_AddedLine_InlineHighlight_EmbedsHighlightBg()
    {
        // Single range covering "1" (col 7 of "  \"x\": 1,")
        var ranges = new[] { new ColumnRange(7, 1) };
        var line = new DiffLine(DiffLineKind.Added, 0, 7, "  \"x\": 1,", ranges);

        string s = DiffLineRenderer.Render(line, gutterWidth: 3, useColor: true, useUnicode: true, NullPainter.Instance);

        s.Should().Contain(AnsiPalette.BgAddedHi);
        s.Should().Contain(AnsiPalette.BgAddedRow);
    }

    [Fact]
    public void Render_ContextLine_Color_DimsGutterButNotContent()
    {
        var line = new DiffLine(DiffLineKind.Context, 7, 7, "  \"x\": 1,", System.Array.Empty<ColumnRange>());

        string s = DiffLineRenderer.Render(line, gutterWidth: 3, useColor: true, useUnicode: true, NullPainter.Instance);

        s.Should().StartWith(AnsiPalette.Dim);
        s.Should().Contain(AnsiPalette.DimOff);
        s.Should().Contain("  \"x\": 1,");
        s.Should().EndWith(AnsiPalette.FgDefault);
    }

    [Fact]
    public void Render_AddedLine_InlineHighlight_BoldsHighlightedSegment()
    {
        var ranges = new[] { new ColumnRange(7, 1) };
        var line = new DiffLine(DiffLineKind.Added, 0, 7, "  \"x\": 1,", ranges);

        string s = DiffLineRenderer.Render(line, gutterWidth: 3, useColor: true, useUnicode: true, NullPainter.Instance);

        int hiIdx = s.IndexOf(AnsiPalette.BgAddedHi, System.StringComparison.Ordinal);
        int boldIdx = s.IndexOf(AnsiPalette.Bold, System.StringComparison.Ordinal);
        boldIdx.Should().BeGreaterThan(hiIdx);
        s.Should().Contain(AnsiPalette.BoldOff);
    }

    [Fact]
    public void Render_AddedLine_TwoNonOverlappingHighlights_BothEmbedded()
    {
        // content "ab cd ef" — highlight "ab" at [0,2) and "ef" at [6,2).
        var ranges = new[]
        {
            new ColumnRange(0, 2),
            new ColumnRange(6, 2),
        };
        var line = new DiffLine(DiffLineKind.Added, 0, 1, "ab cd ef", ranges);

        string s = DiffLineRenderer.Render(line, gutterWidth: 1, useColor: true, useUnicode: true, NullPainter.Instance);

        // Both highlight regions present; row bg toggled around them.
        int firstHi = s.IndexOf(AnsiPalette.BgAddedHi, System.StringComparison.Ordinal);
        int secondHi = s.IndexOf(AnsiPalette.BgAddedHi, firstHi + 1, System.StringComparison.Ordinal);
        firstHi.Should().BeGreaterThan(0);
        secondHi.Should().BeGreaterThan(firstHi);

        // Plain content "cd" appears between the two highlight regions.
        s.Should().Contain("cd");
    }

    private sealed class StubPainter : IDiffSyntaxPainter
    {
        private readonly TokenRange[] _tokens;
        public StubPainter(params TokenRange[] tokens)
        {
            _tokens = tokens;
        }

        public IReadOnlyList<TokenRange> TokenizeLine(string content) => _tokens;
    }

    [Fact]
    public void Render_AddedLine_WithKeyToken_EmbedsBrightCyanFg()
    {
        const string content = "  \"x\": 1,";
        var keyRange = new ColumnRange(0, content.Length);
        var keyToken = new TokenRange(keyRange, TokenKind.Key);
        var painter = new StubPainter(keyToken);
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
        var keyRange = new ColumnRange(0, content.Length);
        var keyToken = new TokenRange(keyRange, TokenKind.Key);
        var painter = new StubPainter(keyToken);
        var line = new DiffLine(DiffLineKind.Added, 0, 7, content, System.Array.Empty<ColumnRange>());

        string s = DiffLineRenderer.Render(line, gutterWidth: 3, useColor: false, useUnicode: true, painter);

        s.Should().Be("  7 +   \"x\": 1,");
        s.Should().NotContain("\x1b");
    }

    [Fact]
    public void Render_ContextLine_WithStringToken_UsesDimYellowFg()
    {
        const string content = "  \"x\": \"abc\",";
        var stringRange = new ColumnRange(7, 5);
        var stringToken = new TokenRange(stringRange, TokenKind.String);
        var painter = new StubPainter(stringToken);
        var line = new DiffLine(DiffLineKind.Context, 7, 7, content, System.Array.Empty<ColumnRange>());

        string s = DiffLineRenderer.Render(line, gutterWidth: 3, useColor: true, useUnicode: true, painter);

        s.Should().Contain(SyntaxPalette.Dim(TokenKind.String));
        s.Should().NotContain(SyntaxPalette.Bright(TokenKind.String));
    }

    [Fact]
    public void Render_AddedLine_TokenFullyInsideHighlight_TokenFgSuppressed()
    {
        // Token covers exactly the highlighted span — yellow/mauve on bright bg is unreadable,
        // so the renderer drops token fg inside highlights; bg + bold is the change indicator.
        const string content = "  \"x\": 1,";
        var hi = new[] { new ColumnRange(7, 1) };
        var numberRange = new ColumnRange(7, 1);
        var numberToken = new TokenRange(numberRange, TokenKind.Number);
        var painter = new StubPainter(numberToken);
        var line = new DiffLine(DiffLineKind.Added, 0, 7, content, hi);

        string s = DiffLineRenderer.Render(line, gutterWidth: 3, useColor: true, useUnicode: true, painter);

        s.Should().Contain(AnsiPalette.BgAddedHi);
        s.Should().Contain(AnsiPalette.Bold);
        s.Should().NotContain(SyntaxPalette.Bright(TokenKind.Number));
    }

    [Fact]
    public void Render_AddedLine_TokenSpansBeyondHighlight_RestoresFgAfterHighlight()
    {
        // Token is wider than the highlight — fg suppressed inside, but the bright color
        // re-emits after the highlight closes so the token's tail keeps its color.
        const string content = "  \"key\": \"abc\",";
        var hi = new[] { new ColumnRange(8, 5) };
        var stringRange = new ColumnRange(7, 6);
        var stringToken = new TokenRange(stringRange, TokenKind.String);
        var painter = new StubPainter(stringToken);
        var line = new DiffLine(DiffLineKind.Added, 0, 7, content, hi);

        string s = DiffLineRenderer.Render(line, gutterWidth: 3, useColor: true, useUnicode: true, painter);

        s.Should().Contain(AnsiPalette.BgAddedHi);
        // The String yellow appears at least once — for the slice of the token that
        // falls outside the highlight (col 7 only, since the highlight starts at col 8).
        s.Should().Contain(SyntaxPalette.Bright(TokenKind.String));
    }
}
