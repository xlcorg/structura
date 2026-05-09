using Structura.Reporting.Internal;
using Structura.Runtime;

namespace Structura.Reporting;

/// <summary>
/// Renders <see cref="IStructuraDocument.Changes"/> as a Claude-Code-style
/// unified diff: two-line banner, gutter with line numbers, sigil after the
/// number, dark red/green row backgrounds, brighter inline-highlight overlays.
/// Pairs with <see cref="SimpleReporter"/> (path-oriented) and
/// <see cref="ConsoleDiffReporter"/> (per-change mini-hunks).
/// </summary>
public static class UnifiedDiffReporter
{
    private static readonly DiffReporterOptions DefaultOptions = new();

    public static void Print(IStructuraDocument document)
    {
        Print(document, DefaultOptions);
    }

    public static void Print(IStructuraDocument document, DiffReporterOptions options)
    {
        bool useColor = !Console.IsOutputRedirected;
        bool useUnicode = Console.OutputEncoding.WebName == "utf-8";
        RenderTo(document, Console.Out, options, useColor, useUnicode);
    }

    public static void Print(IStructuraDocument document, TextWriter writer)
    {
        RenderTo(document, writer, DefaultOptions, useColor: false, useUnicode: true);
    }

    public static void Print(IStructuraDocument document, TextWriter writer, DiffReporterOptions options)
    {
        RenderTo(document, writer, options, useColor: false, useUnicode: true);
    }

    private static void RenderTo(
        IStructuraDocument document,
        TextWriter writer,
        DiffReporterOptions options,
        bool useColor,
        bool useUnicode)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(options);

        IReadOnlyList<DocumentChange> changes = document.Changes;
        if (changes.Count == 0)
        {
            writer.WriteLine("(no changes)");
            return;
        }

        IReadOnlyList<DiffLine> lines = DiffHunkBuilder.Build(document, options);
        DiffStats stats = DiffStats.Compute(lines);
        int gutterWidth = stats.MaxLineNumber.ToString().Length;

        DiffBanner.Write(writer, document.DocumentName, stats.Additions, stats.Removals, useColor, useUnicode);
        writer.WriteLine();

        foreach (DiffLine line in lines)
        {
            string rendered = DiffLineRenderer.Render(line, gutterWidth, useColor, useUnicode);
            writer.WriteLine(rendered);
        }
    }

}
