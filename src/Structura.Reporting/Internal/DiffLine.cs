namespace Structura.Reporting.Internal;

/// <summary>
/// Kind of a single line in the rendered unified diff.
/// </summary>
internal enum DiffLineKind
{
    /// <summary>Context line — no sigil, no row background. Gutter shows new-file line number.</summary>
    Context,

    /// <summary>Removed line — sigil <c>-</c>, dark red row bg. Gutter shows old-file line number.</summary>
    Removed,

    /// <summary>Added line — sigil <c>+</c>, dark green row bg. Gutter shows new-file line number.</summary>
    Added,

    /// <summary>Hunk separator — single ellipsis line, no gutter.</summary>
    HunkSeparator,
}

/// <summary>
/// Half-open <c>[Start, Start+Length)</c> column range inside a line content.
/// </summary>
internal readonly record struct ColumnRange(int Start, int Length)
{
    public int End => Start + Length;
}

/// <summary>
/// One rendered line of the unified diff. <see cref="LineNumber"/> is the
/// gutter number (1-based). For <see cref="DiffLineKind.HunkSeparator"/> it
/// is unused (set to <c>0</c>) and <see cref="Content"/> / <see cref="InlineHighlights"/>
/// are also unused.
/// </summary>
internal readonly record struct DiffLine(
    DiffLineKind Kind,
    int LineNumber,
    string Content,
    IReadOnlyList<ColumnRange> InlineHighlights);
