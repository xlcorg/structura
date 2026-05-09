using FluentAssertions;

using Structura.Reporting.Internal;

using Xunit;

namespace Structura.UnitTests.Reporting;

public sealed class DiffLineRendererTests
{
    [Fact]
    public void Render_ContextLine_NoColor_IsPlainGutterAndContent()
    {
        var line = new DiffLine(DiffLineKind.Context, 7, "  \"x\": 1,", System.Array.Empty<ColumnRange>());

        string s = DiffLineRenderer.Render(line, gutterWidth: 3, useColor: false, useUnicode: true);

        s.Should().Be("  7     \"x\": 1,");
    }

    [Fact]
    public void Render_RemovedLine_NoColor_HasMinusSigil()
    {
        var line = new DiffLine(DiffLineKind.Removed, 7, "  \"x\": 1,", System.Array.Empty<ColumnRange>());

        string s = DiffLineRenderer.Render(line, gutterWidth: 3, useColor: false, useUnicode: true);

        s.Should().Be("  7 -   \"x\": 1,");
    }

    [Fact]
    public void Render_AddedLine_NoColor_HasPlusSigil()
    {
        var line = new DiffLine(DiffLineKind.Added, 7, "  \"x\": 2,", System.Array.Empty<ColumnRange>());

        string s = DiffLineRenderer.Render(line, gutterWidth: 3, useColor: false, useUnicode: true);

        s.Should().Be("  7 +   \"x\": 2,");
    }

    [Fact]
    public void Render_HunkSeparator_UnicodeOn_EmitsEllipsis()
    {
        var line = new DiffLine(DiffLineKind.HunkSeparator, 0, string.Empty, System.Array.Empty<ColumnRange>());

        string s = DiffLineRenderer.Render(line, gutterWidth: 3, useColor: false, useUnicode: true);

        s.Should().Be("      …");  // 3 (gutter) + 3 (sep+sigil+sep) = 6 chars then …
    }

    [Fact]
    public void Render_HunkSeparator_UnicodeOff_EmitsThreeDots()
    {
        var line = new DiffLine(DiffLineKind.HunkSeparator, 0, string.Empty, System.Array.Empty<ColumnRange>());

        string s = DiffLineRenderer.Render(line, gutterWidth: 3, useColor: false, useUnicode: false);

        s.Should().Be("      ...");
    }

    [Fact]
    public void Render_RemovedLine_Color_WrapsWithBgEscapes()
    {
        var line = new DiffLine(DiffLineKind.Removed, 7, "  \"x\": 1,", System.Array.Empty<ColumnRange>());

        string s = DiffLineRenderer.Render(line, gutterWidth: 3, useColor: true, useUnicode: true);

        s.Should().StartWith(AnsiPalette.BgRemovedRow);
        s.Should().EndWith(AnsiPalette.BgDefault);
        s.Should().Contain("  7 -   \"x\": 1,");
    }

    [Fact]
    public void Render_AddedLine_InlineHighlight_EmbedsHighlightBg()
    {
        // Single range covering "1" (col 7 of "  \"x\": 1,")
        var ranges = new[] { new ColumnRange(7, 1) };
        var line = new DiffLine(DiffLineKind.Added, 7, "  \"x\": 1,", ranges);

        string s = DiffLineRenderer.Render(line, gutterWidth: 3, useColor: true, useUnicode: true);

        s.Should().Contain(AnsiPalette.BgAddedHi);
        s.Should().Contain(AnsiPalette.BgAddedRow);
    }
}
