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
/// One rendered line of the unified diff.
/// <list type="bullet">
/// <item><see cref="Context"/> — both <see cref="OldLineNumber"/> and <see cref="NewLineNumber"/> populated (1-based).</item>
/// <item><see cref="Removed"/> — only <see cref="OldLineNumber"/> populated; <see cref="NewLineNumber"/> = 0.</item>
/// <item><see cref="Added"/> — only <see cref="NewLineNumber"/> populated; <see cref="OldLineNumber"/> = 0.</item>
/// <item><see cref="HunkSeparator"/> — both 0; <see cref="Content"/> / <see cref="InlineHighlights"/> unused.</item>
/// </list>
/// </summary>
internal readonly record struct DiffLine(
    DiffLineKind Kind,
    int OldLineNumber,
    int NewLineNumber,
    string Content,
    IReadOnlyList<ColumnRange> InlineHighlights);
