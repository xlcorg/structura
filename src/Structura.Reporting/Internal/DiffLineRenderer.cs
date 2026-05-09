using System.Text;

namespace Structura.Reporting.Internal;

/// <summary>
/// Formats a single <see cref="DiffLine"/> into a renderable string. Plain
/// when <c>useColor: false</c>; emits 256-color ANSI escapes for row
/// backgrounds and inline highlights when <c>useColor: true</c>.
/// </summary>
internal static class DiffLineRenderer
{
    public static string Render(DiffLine line, int gutterWidth, bool useColor, bool useUnicode)
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

        string gutter = line.LineNumber.ToString().PadLeft(gutterWidth);
        string body = $"{gutter} {sigil} {line.Content}";

        if (!useColor || line.Kind == DiffLineKind.Context)
        {
            return body;
        }

        string rowBg = line.Kind == DiffLineKind.Removed
            ? AnsiPalette.BgRemovedRow
            : AnsiPalette.BgAddedRow;
        string highlightBg = line.Kind == DiffLineKind.Removed
            ? AnsiPalette.BgRemovedHi
            : AnsiPalette.BgAddedHi;
        string sigilFg = line.Kind == DiffLineKind.Removed
            ? AnsiPalette.FgRemovedSigil
            : AnsiPalette.FgAddedSigil;

        if (line.InlineHighlights.Count == 0)
        {
            return rowBg + sigilFg + gutter + " " + sigil + AnsiPalette.FgDefault + " " + line.Content + " " + AnsiPalette.BgDefault;
        }

        var sb = new StringBuilder();
        sb.Append(rowBg);
        sb.Append(sigilFg).Append(gutter).Append(' ').Append(sigil).Append(AnsiPalette.FgDefault).Append(' ');

        // Inline-highlight ranges are over content (0..content.Length).
        int cursor = 0;
        foreach (ColumnRange r in line.InlineHighlights)
        {
            if (r.Start > cursor)
            {
                int leadLen = r.Start - cursor;
                sb.Append(line.Content, cursor, leadLen);
            }
            sb.Append(highlightBg);
            int contentRemainder = line.Content.Length - r.Start;
            int rawLen = Math.Min(r.Length, contentRemainder);
            int len = Math.Max(0, rawLen);
            sb.Append(line.Content, r.Start, len);
            sb.Append(rowBg);
            cursor = r.End;
        }
        if (cursor < line.Content.Length)
        {
            int tailLen = line.Content.Length - cursor;
            sb.Append(line.Content, cursor, tailLen);
        }
        sb.Append(' ');
        sb.Append(AnsiPalette.BgDefault);
        return sb.ToString();
    }
}
