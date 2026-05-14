using Structura.Reporting.Internal;
using Structura.Runtime;

namespace Structura.Reporting;

/// <summary>
/// Public wrapper kept temporarily during the Step 15 migration. The render
/// loop now lives in <see cref="Internal.SideBySideRenderer"/>. This class is
/// removed once <see cref="DiffReporter"/> is wired up and all tests have
/// been re-pointed.
/// </summary>
public static class SideBySideDiffReporter
{
    private const int FallbackTotalWidth = 160;
    private const int CellPaddingChars = 3;
    private const int SeparatorChars = 3;
    private const int MinContentPerSide = 1;

    private static readonly DiffReporterOptions DefaultOptions = new DiffReporterOptions();

    public static void Print(IStructuraDocument document)
    {
        Print(document, DefaultOptions);
    }

    public static void Print(IStructuraDocument document, DiffReporterOptions options)
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

    public static void Print(IStructuraDocument document, TextWriter writer, DiffReporterOptions options)
    {
        RenderTo(document, writer, options, FallbackTotalWidth, useColor: false, useUnicode: true);
    }

    internal static void RenderTo(
        IStructuraDocument document,
        TextWriter writer,
        DiffReporterOptions options,
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

        IReadOnlyList<DiffLine> lines = DiffHunkBuilder.Build(document, options);
        DiffStats stats = DiffStats.Compute(lines);

        int gutterWidth = stats.MaxLineNumber.ToString().Length;
        int minTotal = 2 * (gutterWidth + CellPaddingChars) + SeparatorChars + 2 * MinContentPerSide;
        int width = totalWidth;
        if (width < minTotal)
        {
            width = minTotal;
        }
        int contentWidth = (width - 2 * (gutterWidth + CellPaddingChars) - SeparatorChars) / 2;

        SideBySideRenderer.RenderTo(document, writer, options, lines, stats, gutterWidth, contentWidth, useColor, useUnicode);
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
