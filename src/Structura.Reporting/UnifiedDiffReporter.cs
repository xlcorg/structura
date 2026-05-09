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
        RenderTo(document, Console.Out, DefaultOptions, useColor);
    }

    public static void Print(IStructuraDocument document, TextWriter writer)
    {
        RenderTo(document, writer, DefaultOptions, useColor: false);
    }

    public static void Print(IStructuraDocument document, TextWriter writer, UnifiedDiffOptions options)
    {
        RenderTo(document, writer, options, useColor: false);
    }

    private static void RenderTo(
        IStructuraDocument document,
        TextWriter writer,
        UnifiedDiffOptions options,
        bool useColor)
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

            if (line.LineNumber > maxLineNumber)
            {
                maxLineNumber = line.LineNumber;
            }
        }

        int gutterWidth = maxLineNumber.ToString().Length;
        bool useUnicode = Console.OutputEncoding.WebName == "utf-8";

        WriteBanner(writer, document.DocumentName, additions, removals, useColor, useUnicode);
        writer.WriteLine();

        foreach (DiffLine line in lines)
        {
            string rendered = DiffLineRenderer.Render(line, gutterWidth, useColor, useUnicode);
            writer.WriteLine(rendered);
        }
    }

    private static void WriteBanner(
        TextWriter writer,
        string documentName,
        int additions,
        int removals,
        bool useColor,
        bool useUnicode)
    {
        string dot = useUnicode ? "●" : "*";
        string corner = useUnicode ? "└" : "\\";
        string additionNoun = additions == 1 ? "addition" : "additions";
        string removalNoun = removals == 1 ? "removal" : "removals";

        if (useColor)
        {
            writer.Write(AnsiPalette.FgGreen);
            writer.Write(dot);
            writer.Write(AnsiPalette.FgDefault);
            writer.Write(' ');
            writer.Write(AnsiPalette.Bold);
            writer.Write("Patched");
            writer.Write(AnsiPalette.BoldOff);
            writer.Write('(');
            writer.Write(documentName);
            writer.WriteLine(')');

            writer.Write("  ");
            writer.Write(AnsiPalette.Dim);
            writer.Write(corner);
            writer.Write(' ');
            writer.Write("Patched ");
            writer.Write(AnsiPalette.DimOff);
            writer.Write(AnsiPalette.Bold);
            writer.Write(documentName);
            writer.Write(AnsiPalette.BoldOff);
            writer.Write(AnsiPalette.Dim);
            writer.Write(" with ");
            writer.Write(AnsiPalette.DimOff);
            writer.Write(AnsiPalette.Bold);
            writer.Write(additions);
            writer.Write(AnsiPalette.BoldOff);
            writer.Write(AnsiPalette.Dim);
            writer.Write($" {additionNoun} and ");
            writer.Write(AnsiPalette.DimOff);
            writer.Write(AnsiPalette.Bold);
            writer.Write(removals);
            writer.Write(AnsiPalette.BoldOff);
            writer.Write(AnsiPalette.Dim);
            writer.WriteLine($" {removalNoun}");
            writer.Write(AnsiPalette.DimOff);
        }
        else
        {
            writer.Write(dot);
            writer.Write(' ');
            writer.Write("Patched(");
            writer.Write(documentName);
            writer.WriteLine(')');

            writer.Write("  ");
            writer.Write(corner);
            writer.Write(' ');
            writer.Write("Patched ");
            writer.Write(documentName);
            writer.Write(" with ");
            writer.Write(additions);
            writer.Write(' ');
            writer.Write(additionNoun);
            writer.Write(" and ");
            writer.Write(removals);
            writer.Write(' ');
            writer.WriteLine(removalNoun);
        }
    }
}
