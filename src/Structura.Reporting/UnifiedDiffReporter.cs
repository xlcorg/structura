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
    private static readonly UnifiedDiffOptions DefaultOptions = new();

    public static void Print(IStructuraDocument document)
    {
        bool useColor = !Console.IsOutputRedirected;
        bool useUnicode = Console.OutputEncoding.WebName == "utf-8";
        RenderTo(document, Console.Out, DefaultOptions, useColor, useUnicode);
    }

    public static void Print(IStructuraDocument document, TextWriter writer)
    {
        RenderTo(document, writer, DefaultOptions, useColor: false, useUnicode: true);
    }

    public static void Print(IStructuraDocument document, TextWriter writer, UnifiedDiffOptions options)
    {
        RenderTo(document, writer, options, useColor: false, useUnicode: true);
    }

    private static void RenderTo(
        IStructuraDocument document,
        TextWriter writer,
        UnifiedDiffOptions options,
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

        var builder = new DiffHunkBuilder();
        IReadOnlyList<DiffLine> lines = builder.Build(document, options);

        int additions = 0;
        int removals = 0;
        int maxLineNumber = 1;
        foreach (DiffLine line in lines)
        {
            if (line.Kind == DiffLineKind.Added)
            {
                additions++;
            }
            else if (line.Kind == DiffLineKind.Removed)
            {
                removals++;
            }

            int candidate = Math.Max(line.OldLineNumber, line.NewLineNumber);
            if (candidate > maxLineNumber)
            {
                maxLineNumber = candidate;
            }
        }

        int gutterWidth = maxLineNumber.ToString().Length;

        DiffBanner.Write(writer, document.DocumentName, additions, removals, useColor, useUnicode);
        writer.WriteLine();

        foreach (DiffLine line in lines)
        {
            string rendered = DiffLineRenderer.Render(line, gutterWidth, useColor, useUnicode);
            writer.WriteLine(rendered);
        }
    }

}
