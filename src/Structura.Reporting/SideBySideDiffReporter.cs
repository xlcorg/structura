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
        RenderTo(document, Console.Out, options, useColor, useUnicode);
    }

    public static void Print(IStructuraDocument document, TextWriter writer)
    {
        RenderTo(document, writer, DefaultOptions, useColor: false, useUnicode: true);
    }

    public static void Print(IStructuraDocument document, TextWriter writer, SideBySideDiffOptions options)
    {
        RenderTo(document, writer, options, useColor: false, useUnicode: true);
    }

    internal static void RenderTo(
        IStructuraDocument document,
        TextWriter writer,
        SideBySideDiffOptions options,
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
        int totalWidth = options.TotalWidth ?? GetConsoleWindowWidthSafe();
        int minTotal = 2 * (gutterWidth + 3) + 3 + 2; // 2 cell prefixes + separator + min 1 char per side
        if (totalWidth < minTotal)
        {
            totalWidth = minTotal;
        }
        int contentWidth = (totalWidth - 2 * (gutterWidth + 3) - 3) / 2;

        DiffBanner.Write(writer, document.DocumentName, additions, removals, useColor, useUnicode);
        writer.WriteLine();

        IReadOnlyList<SideBySideRow> rows = SideBySideRowBuilder.Build(lines);
        foreach (SideBySideRow row in rows)
        {
            string rendered = SideBySideRowRenderer.Render(row, gutterWidth, contentWidth, useColor, useUnicode);
            writer.WriteLine(rendered);
        }
    }

    private static int GetConsoleWindowWidthSafe()
    {
        try
        {
            int width = Console.WindowWidth;
            return width > 0 ? width : 160;
        }
        catch (IOException)
        {
            return 160;
        }
        catch (PlatformNotSupportedException)
        {
            return 160;
        }
    }
}
