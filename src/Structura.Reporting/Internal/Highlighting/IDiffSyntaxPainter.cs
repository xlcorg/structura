namespace Structura.Reporting.Internal.Highlighting;

/// <summary>
/// Tokenizes a single rendered diff line into <see cref="TokenRange"/>s for
/// foreground coloring. Implementations MUST be stateless across calls —
/// state must not be carried between lines. Painters tokenize as much of
/// each line as they can recognize; unmatched constructs degrade to
/// default-coloring tokens.
/// </summary>
internal interface IDiffSyntaxPainter
{
    /// <summary>
    /// Returns non-overlapping token ranges sorted by <see cref="ColumnRange.Start"/>,
    /// covering <c>[0, content.Length)</c>. Empty content returns an empty list.
    /// </summary>
    IReadOnlyList<TokenRange> TokenizeLine(string content);
}
