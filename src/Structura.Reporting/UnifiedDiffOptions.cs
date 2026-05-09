namespace Structura.Reporting;

/// <summary>
/// Options for <see cref="UnifiedDiffReporter"/>. Defaults match the spec:
/// 3 lines of context on each side of every change, inline highlight on.
/// </summary>
public sealed record UnifiedDiffOptions
{
    public int ContextLines { get; init; } = 3;

    public bool InlineHighlight { get; init; } = true;

    /// <summary>
    /// When <c>true</c>, render every line of the document (no hunk grouping, no
    /// <see cref="ContextLines"/> truncation, no <c>…</c> separator). Useful when
    /// the reader wants to see the full file with changes inline. Default <c>false</c>.
    /// </summary>
    public bool ShowFullFile { get; init; } = false;
}
