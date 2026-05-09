using System.Text;

using Structura.Reporting.Internal.Highlighting;

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
        bool useUnicode,
        IDiffSyntaxPainter painter)
    {
        string leftCell = RenderCell(row.Left, gutterWidth, contentWidth, isLeftSide: true, useColor, useUnicode, painter);
        string rightCell = RenderCell(row.Right, gutterWidth, contentWidth, isLeftSide: false, useColor, useUnicode, painter);
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
            var clippedStart = Math.Min(t.Range.Start, visibleEnd);
            var clippedEnd = Math.Min(t.Range.End, visibleEnd);
            var len = clippedEnd - clippedStart;
            if (len > 0)
            {
                var clippedRange = new ColumnRange(clippedStart, len);
                var clippedToken = new TokenRange(clippedRange, t.Kind);
                clipped.Add(clippedToken);
            }
        }
        return clipped;
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

    private static string RenderContextCell(string gutter, char sigil, TruncatedContent t, bool useColor, IReadOnlyList<TokenRange> clippedTokens)
    {
        if (!useColor)
        {
            return $"{gutter} {sigil} {t.Visible}{t.Padding}";
        }
        var sb = new StringBuilder();
        sb.Append(AnsiPalette.Dim).Append(gutter).Append(' ').Append(sigil).Append(AnsiPalette.DimOff);
        sb.Append(' ');
        var noHighlights = Array.Empty<ColumnRange>();
        AppendCellContent(sb, t, clippedTokens, noHighlights, rowBg: string.Empty, highlightBg: string.Empty, useDimPalette: true);
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
        var activeTokenKind = TokenKind.Punctuation;
        var activeFg = string.Empty;
        var inHighlight = false;
        var tokenIndex = 0;
        var highlightIndex = 0;

        for (var col = 0; col < t.Visible.Length; col++)
        {
            var insideContent = col < t.VisibleContentLength;

            while (insideContent && tokenIndex < tokens.Count && tokens[tokenIndex].Range.End <= col)
            {
                tokenIndex++;
            }
            var kindAtCol = insideContent && tokenIndex < tokens.Count && tokens[tokenIndex].Range.Start <= col
                ? tokens[tokenIndex].Kind
                : TokenKind.Punctuation;
            var fgAtCol = !insideContent
                ? string.Empty
                : (useDimPalette ? SyntaxPalette.Dim(kindAtCol) : SyntaxPalette.Bright(kindAtCol));

            while (insideContent && highlightIndex < highlights.Count && highlights[highlightIndex].End <= col)
            {
                highlightIndex++;
            }
            var inHighlightAtCol = insideContent && highlightIndex < highlights.Count && highlights[highlightIndex].Start <= col;

            if (inHighlightAtCol)
            {
                fgAtCol = string.Empty;
            }

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
