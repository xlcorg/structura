using Structura.Reporting.Internal;
using Structura.Runtime;

namespace Structura.Reporting;

/// <summary>
/// Renders <see cref="IStructuraDocument.Changes"/> as a two-column side-by-side
/// diff (left = OLD, right = NEW). Reuses <see cref="DiffHunkBuilder"/> and
/// <see cref="AnsiPalette"/> from <see cref="UnifiedDiffReporter"/>.
/// </summary>
public static class SideBySideDiffReporter
{
    private static readonly SideBySideDiffOptions DefaultOptions = new();

    public static void Print(IStructuraDocument document)
    {
        Print(document, DefaultOptions);
    }

    public static void Print(IStructuraDocument document, SideBySideDiffOptions options)
    {
        bool useColor = !Console.IsOutputRedirected;
        bool useUnicode = Console.OutputEncoding.WebName == "utf-8";
        int totalWidth = ComputeTotalWidth();
        RenderTo(document, Console.Out, options, totalWidth, useColor, useUnicode);
    }

    public static void Print(IStructuraDocument document, TextWriter writer)
    {
        int totalWidth = ComputeTotalWidth();
        RenderTo(document, writer, DefaultOptions, totalWidth, useColor: false, useUnicode: true);
    }

    public static void Print(IStructuraDocument document, TextWriter writer, SideBySideDiffOptions options)
    {
        int totalWidth = ComputeTotalWidth();
        RenderTo(document, writer, options, totalWidth, useColor: false, useUnicode: true);
    }

    internal static void RenderTo(
        IStructuraDocument document,
        TextWriter writer,
        SideBySideDiffOptions options,
        int totalWidth,
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

        var hunkBuilder = new DiffHunkBuilder();
        UnifiedDiffOptions hunkOptions = new()
        {
            ContextLines = options.ContextLines,
            InlineHighlight = options.InlineHighlight,
            ShowFullFile = options.ShowFullFile,
        };
        IReadOnlyList<DiffLine> lines = hunkBuilder.Build(document, hunkOptions);

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
        int minTotal = 2 * (gutterWidth + 3) + 3 + 2; // 2 cell prefixes + separator + min 1 char per side
        int width = totalWidth;
        if (width < minTotal)
        {
            width = minTotal;
        }
        int contentWidth = (width - 2 * (gutterWidth + 3) - 3) / 2;

        DiffBanner.Write(writer, document.DocumentName, additions, removals, useColor, useUnicode);
        writer.WriteLine();

        IReadOnlyList<SideBySideRow> rows = SideBySideRowBuilder.Build(lines);
        foreach (SideBySideRow row in rows)
        {
            string rendered = SideBySideRowRenderer.Render(row, gutterWidth, contentWidth, useColor, useUnicode);
            writer.WriteLine(rendered);
        }
    }

    private static int ComputeTotalWidth()
    {
        int width;
        try
        {
            width = Console.WindowWidth;
        }
        catch (IOException)
        {
            return 160;
        }
        catch (PlatformNotSupportedException)
        {
            return 160;
        }
        return Math.Max(width, 160);
    }
}
