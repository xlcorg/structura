namespace Structura.Reporting.Internal.Highlighting;

/// <summary>
/// Tokenizes a single rendered diff line into <see cref="TokenRange"/>s for
/// foreground coloring. Implementations MUST be stateless across calls — a
/// per-line CDATA opener that has no closer on the same line tokenizes to
/// the best-effort point and stops; the next line is tokenized fresh.
/// </summary>
internal interface IDiffSyntaxPainter
{
    /// <summary>
    /// Returns non-overlapping token ranges sorted by <see cref="ColumnRange.Start"/>,
    /// covering <c>[0, content.Length)</c>. Empty content returns an empty list.
    /// </summary>
    IReadOnlyList<TokenRange> TokenizeLine(string content);
}
