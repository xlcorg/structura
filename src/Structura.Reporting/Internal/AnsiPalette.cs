namespace Structura.Reporting.Internal;

/// <summary>
/// 256-color ANSI escape constants used by <see cref="DiffLineRenderer"/>.
/// Background colors are chosen to match the Claude-Code unified-diff
/// rendering: dark muted reds/greens for row backgrounds, brighter shades
/// for inline-highlight overlays inside a row.
/// </summary>
internal static class AnsiPalette
{
    // Foreground / formatting
    public const string Esc = "\x1b";
    public const string Reset = "\x1b[0m";
    public const string Bold = "\x1b[1m";
    public const string BoldOff = "\x1b[22m";
    public const string Dim = "\x1b[2m";
    public const string DimOff = "\x1b[22m"; // SGR 22 resets both bold and dim
    public const string FgGreen = "\x1b[32m";
    public const string FgDefault = "\x1b[39m";

    // Foregrounds for the gutter line number and sigil on changed rows.
    // Chosen to "pop" against the dim row backgrounds while staying readable.
    public const string FgRemovedSigil = "\x1b[38;5;203m"; // bright red (#ff5f5f) for '-' and old line number
    public const string FgAddedSigil = "\x1b[38;5;41m";    // bright green (#00d75f) for '+' and new line number

    // Backgrounds
    public const string BgRemovedRow = "\x1b[48;5;52m";   // dark red row bg
    public const string BgAddedRow = "\x1b[48;5;22m";     // dark green row bg
    public const string BgRemovedHi = "\x1b[48;5;124m";   // bright red highlight (clearly stands out vs row 52)
    public const string BgAddedHi = "\x1b[48;5;34m";      // bright green highlight (clearly stands out vs row 22)
    public const string BgDefault = "\x1b[49m";
}
