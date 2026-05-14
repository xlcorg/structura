using Structura.Reporting.Internal.Highlighting;
using Structura.Runtime;

namespace Structura.Reporting.Internal;

/// <summary>
/// Side-by-side (two-column) renderer. Writes the banner, then one
/// <see cref="SideBySideRow"/> per row via <see cref="SideBySideRowRenderer"/>.
/// Layout constants ("min content per side" etc.) live in
/// <see cref="Structura.Reporting.DiffReporter"/> so the same numbers feed both
/// the Auto heuristic and the render path.
/// </summary>
internal static class SideBySideRenderer
{
    public static void RenderTo(
        IStructuraDocument document,
        TextWriter writer,
        DiffReporterOptions options,
        IReadOnlyList<DiffLine> lines,
        DiffStats stats,
        int gutterWidth,
        int contentWidth,
        bool useColor,
        bool useUnicode)
    {
        IDiffSyntaxPainter painter = useColor
            ? PainterFactory.For(document, options.SyntaxHighlight)
            : NullPainter.Instance;

        DiffBanner.Write(writer, document.DocumentName, stats.Additions, stats.Removals, useColor, useUnicode);
        writer.WriteLine();

        IReadOnlyList<SideBySideRow> rows = SideBySideRowBuilder.Build(lines);
        foreach (SideBySideRow row in rows)
        {
            string rendered = SideBySideRowRenderer.Render(row, gutterWidth, contentWidth, useColor, useUnicode, painter);
            writer.WriteLine(rendered);
        }
    }
}
