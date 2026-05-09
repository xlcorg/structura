namespace Structura.Reporting;

/// <summary>
/// Options shared by <see cref="UnifiedDiffReporter"/> and
/// <see cref="SideBySideDiffReporter"/>. Defaults match the spec: 3 lines of
/// surrounding context, inline highlight on, full-file rendering off.
/// </summary>
public sealed record DiffReporterOptions
{
    public int ContextLines { get; init; } = 3;

    public bool InlineHighlight { get; init; } = true;

    /// <summary>
    /// When <c>true</c>, JSON and XML content is rendered with token-aware
    /// foreground colors (keys, strings, numbers, keywords, element/attribute
    /// names, comments, entity refs) layered over the existing row-bg /
    /// inline-highlight machinery. No effect when <c>useColor</c> is false —
    /// plain-text output bytes are unchanged. Default <c>true</c>.
    /// </summary>
    public bool SyntaxHighlight { get; init; } = true;

    /// <summary>
    /// When <c>true</c>, render every line of the document (no hunk grouping, no
    /// <see cref="ContextLines"/> truncation, no <c>…</c> separator). Default <c>false</c>.
    /// </summary>
    public bool ShowFullFile { get; init; } = false;
}
