namespace Structura.Reporting.Internal.Highlighting;

/// <summary>
/// 256-color ANSI foreground escapes per <see cref="TokenKind"/>, in two
/// tiers: <see cref="Bright"/> for changed rows (sit on dark red/green row
/// backgrounds and on the brighter inline-highlight backgrounds), and
/// <see cref="Dim"/> for context rows (muted to preserve the dim look).
/// Returning <see cref="string.Empty"/> signals "no color — use default fg".
/// </summary>
internal static class SyntaxPalette
{
    private const string BrightCyan   = "\x1b[38;5;81m";
    private const string BrightYellow = "\x1b[38;5;180m";
    private const string BrightMauve  = "\x1b[38;5;176m";
    private const string BrightOrange = "\x1b[38;5;215m";
    private const string BrightSage   = "\x1b[38;5;150m";
    private const string BrightGrey   = "\x1b[38;5;245m";

    private const string DimCyan   = "\x1b[38;5;67m";
    private const string DimYellow = "\x1b[38;5;144m";
    private const string DimMauve  = "\x1b[38;5;103m";
    private const string DimOrange = "\x1b[38;5;137m";
    private const string DimSage   = "\x1b[38;5;108m";
    private const string DimGrey   = "\x1b[38;5;240m";

    public static string Bright(TokenKind kind) =>
        kind switch
        {
            TokenKind.Key         => BrightCyan,
            TokenKind.ElementName => BrightCyan,
            TokenKind.String      => BrightYellow,
            TokenKind.AttrValue   => BrightYellow,
            TokenKind.Number      => BrightMauve,
            TokenKind.Keyword     => BrightOrange,
            TokenKind.EntityRef   => BrightOrange,
            TokenKind.AttrName    => BrightSage,
            TokenKind.Comment     => BrightGrey,
            TokenKind.Punctuation => string.Empty,
            TokenKind.Text        => string.Empty,
            _ => string.Empty,
        };

    public static string Dim(TokenKind kind) =>
        kind switch
        {
            TokenKind.Key         => DimCyan,
            TokenKind.ElementName => DimCyan,
            TokenKind.String      => DimYellow,
            TokenKind.AttrValue   => DimYellow,
            TokenKind.Number      => DimMauve,
            TokenKind.Keyword     => DimOrange,
            TokenKind.EntityRef   => DimOrange,
            TokenKind.AttrName    => DimSage,
            TokenKind.Comment     => DimGrey,
            TokenKind.Punctuation => string.Empty,
            TokenKind.Text        => string.Empty,
            _ => string.Empty,
        };
}
