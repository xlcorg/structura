namespace Structura.Reporting;

/// <summary>
/// Options for <see cref="SideBySideDiffReporter"/>.
/// </summary>
public sealed record SideBySideDiffOptions
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
