using Structura.Reporting.Internal.Highlighting;
using Structura.Runtime;

namespace Structura.Reporting.Internal;

/// <summary>
/// Unified (single-column) renderer. Writes the banner, then one line per
/// <see cref="DiffLine"/> using <see cref="DiffLineRenderer"/>. Called by the
/// public <see cref="Structura.Reporting.DiffReporter"/> after it has built
/// the line list and chosen the layout.
/// </summary>
internal static class UnifiedRenderer
{
    public static void RenderTo(
        IStructuraDocument document,
        TextWriter writer,
        DiffReporterOptions options,
        IReadOnlyList<DiffLine> lines,
        DiffStats stats,
        int gutterWidth,
        bool useColor,
        bool useUnicode)
    {
        IDiffSyntaxPainter painter = useColor
            ? PainterFactory.For(document, options.SyntaxHighlight)
            : NullPainter.Instance;

        DiffBanner.Write(writer, document.DocumentName, stats.Additions, stats.Removals, useColor, useUnicode);
        writer.WriteLine();

        foreach (DiffLine line in lines)
        {
            string rendered = DiffLineRenderer.Render(line, gutterWidth, useColor, useUnicode, painter);
            writer.WriteLine(rendered);
        }
    }
}
