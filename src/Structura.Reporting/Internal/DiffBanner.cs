namespace Structura.Reporting.Internal;

/// <summary>
/// Two-line banner emitted at the top of unified and side-by-side diff output:
/// <c>● Patched</c> followed by <c>  └ Patched name with N additions and M removals</c>.
/// Wording matches the existing reporter contract — do not paraphrase.
/// </summary>
internal static class DiffBanner
{
    public static void Write(
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
            writer.WriteLine();

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
            writer.WriteLine("Patched");

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
