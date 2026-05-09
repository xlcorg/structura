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
    // Default/minimum total width used when there is no real terminal to query
    // (TextWriter overloads, redirected output, IDE run-console stub).
    private const int FallbackTotalWidth = 160;

    // Per-cell padding around the gutter: a space, a sigil, and a space → " s ".
    private const int CellPaddingChars = 3;

    // Inter-cell separator " │ " (or " | " when useUnicode is false): three chars either way.
    private const int SeparatorChars = 3;

    // Minimum visible content characters per side before truncation kicks in.
    private const int MinContentPerSide = 1;

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
        RenderTo(document, writer, DefaultOptions, FallbackTotalWidth, useColor: false, useUnicode: true);
    }

    public static void Print(IStructuraDocument document, TextWriter writer, SideBySideDiffOptions options)
    {
        RenderTo(document, writer, options, FallbackTotalWidth, useColor: false, useUnicode: true);
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

        UnifiedDiffOptions hunkOptions = new()
        {
            ContextLines = options.ContextLines,
            InlineHighlight = options.InlineHighlight,
            ShowFullFile = options.ShowFullFile,
        };
        IReadOnlyList<DiffLine> lines = DiffHunkBuilder.Build(document, hunkOptions);

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
        int minTotal = 2 * (gutterWidth + CellPaddingChars) + SeparatorChars + 2 * MinContentPerSide;
        int width = totalWidth;
        if (width < minTotal)
        {
            width = minTotal;
        }
        int contentWidth = (width - 2 * (gutterWidth + CellPaddingChars) - SeparatorChars) / 2;

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
            return FallbackTotalWidth;
        }
        catch (PlatformNotSupportedException)
        {
            return FallbackTotalWidth;
        }
        return Math.Max(width, FallbackTotalWidth);
    }
}
