using Structura.Reporting.Internal;
using Structura.Runtime;

namespace Structura.Reporting;

/// <summary>
/// Renders <see cref="IStructuraDocument.Changes"/> as a unified or
/// side-by-side diff. <see cref="DiffReporterOptions.Layout"/> controls the
/// choice: <see cref="DiffReporterLayout.Unified"/> and
/// <see cref="DiffReporterLayout.SideBySide"/> force a layout;
/// <see cref="DiffReporterLayout.Auto"/> (the default) picks side-by-side when
/// the terminal meets the minimum two-column width, otherwise falls back to unified.
/// </summary>
public static class DiffReporter
{
    private const int FallbackTotalWidth = 120;
    private const int CellPaddingChars = 3;
    private const int SeparatorChars = 3;
    private const int MinContentPerSide = 40;

    private static readonly DiffReporterOptions DefaultOptions = new DiffReporterOptions();

    public static void Print(IStructuraDocument document)
    {
        Print(document, DefaultOptions);
    }

    public static void Print(IStructuraDocument document, DiffReporterOptions options)
    {
        bool useColor = !Console.IsOutputRedirected;
        bool useUnicode = Console.OutputEncoding.WebName == "utf-8";
        int terminalWidth = ComputeTerminalWidth();
        RenderTo(document, Console.Out, options, terminalWidth, useColor, useUnicode);
    }

    public static void Print(IStructuraDocument document, TextWriter writer)
    {
        RenderTo(document, writer, DefaultOptions, FallbackTotalWidth, useColor: false, useUnicode: true);
    }

    public static void Print(IStructuraDocument document, TextWriter writer, DiffReporterOptions options)
    {
        RenderTo(document, writer, options, FallbackTotalWidth, useColor: false, useUnicode: true);
    }

    internal static void RenderTo(
        IStructuraDocument document,
        TextWriter writer,
        DiffReporterOptions options,
        int terminalWidth,
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

        DiffReporterLayout resolved = ResolveLayout(options.Layout, terminalWidth, gutterWidth);

        if (resolved == DiffReporterLayout.Unified)
        {
            UnifiedRenderer.RenderTo(document, writer, options, lines, stats, gutterWidth, useColor, useUnicode);
            return;
        }

        int contentWidth = ComputeSideBySideContentWidth(terminalWidth, gutterWidth);
        SideBySideRenderer.RenderTo(document, writer, options, lines, stats, gutterWidth, contentWidth, useColor, useUnicode);
    }

    private static DiffReporterLayout ResolveLayout(DiffReporterLayout requested, int terminalWidth, int gutterWidth)
    {
        if (requested != DiffReporterLayout.Auto)
        {
            return requested;
        }
        int minSbsWidth = 2 * (gutterWidth + CellPaddingChars) + SeparatorChars + 2 * MinContentPerSide;
        return terminalWidth >= minSbsWidth ? DiffReporterLayout.SideBySide : DiffReporterLayout.Unified;
    }

    private static int ComputeSideBySideContentWidth(int terminalWidth, int gutterWidth)
    {
        int minTotal = 2 * (gutterWidth + CellPaddingChars) + SeparatorChars + 2 * MinContentPerSide;
        int width = terminalWidth;
        if (width < minTotal)
        {
            width = minTotal;
        }
        return (width - 2 * (gutterWidth + CellPaddingChars) - SeparatorChars) / 2;
    }

    private static int ComputeTerminalWidth()
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
