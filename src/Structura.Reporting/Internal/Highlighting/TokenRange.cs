namespace Structura.Reporting.Internal.Highlighting;

/// <summary>
/// Half-open <c>[Range.Start, Range.Start + Range.Length)</c> column range
/// inside a line, paired with its <see cref="TokenKind"/>. Painters return
/// non-overlapping, sorted ranges that together cover <c>[0, content.Length)</c>.
/// </summary>
internal readonly record struct TokenRange(ColumnRange Range, TokenKind Kind);
