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
