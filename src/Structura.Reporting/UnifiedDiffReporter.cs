using Structura.Reporting.Internal;
using Structura.Runtime;

namespace Structura.Reporting;

/// <summary>
/// Public wrapper kept temporarily during the Step 15 migration. The render
/// loop now lives in <see cref="Internal.UnifiedRenderer"/>. This class is
/// removed once <see cref="DiffReporter"/> is wired up and all tests have
/// been re-pointed.
/// </summary>
public static class UnifiedDiffReporter
{
    private static readonly DiffReporterOptions DefaultOptions = new DiffReporterOptions();

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

    internal static void RenderTo(
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

        UnifiedRenderer.RenderTo(document, writer, options, lines, stats, gutterWidth, useColor, useUnicode);
    }
}
