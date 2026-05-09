using System.Text;

namespace Structura.Reporting.Internal;

/// <summary>
/// Renders a single <see cref="SideBySideRow"/> as a string. Layout per row:
/// <c>{leftCell}{separator}{rightCell}</c>, where each cell is
/// <c>{lineNumber:>W} {sigil} {content:Wcol}</c>, the separator is
/// <c> │ </c> (or <c> | </c> with <c>useUnicode == false</c>) and content is
/// truncated to <c>contentWidth</c> with <c>…</c> / <c>&gt;</c> as needed.
/// </summary>
internal static class SideBySideRowRenderer
{
    private readonly record struct TruncatedContent(string Visible, int VisibleContentLength, string Padding);

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

        return RenderChangedCell(line, gutter, sigil, truncation, useColor);
    }

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
