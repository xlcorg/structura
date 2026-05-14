namespace Structura.Reporting;

/// <summary>
/// Options consumed by <see cref="DiffReporter"/>. Defaults match the spec:
/// 3 lines of surrounding context, inline highlight on, syntax highlight on,
/// full-file rendering off, Auto layout.
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

    /// <summary>
    /// Selects the layout. <see cref="DiffReporterLayout.Auto"/> picks
    /// side-by-side when the terminal meets the minimum two-column width,
    /// otherwise falls back to unified. <see cref="DiffReporterLayout.Unified"/>
    /// and <see cref="DiffReporterLayout.SideBySide"/> force that layout
    /// regardless of width. Default <see cref="DiffReporterLayout.Auto"/>.
    /// </summary>
    public DiffReporterLayout Layout { get; init; } = DiffReporterLayout.Auto;

    /// <summary>
    /// When <c>true</c>, emit a single blank line before any other output.
    /// Separates the diff from preceding console text. Default <c>true</c>.
    /// </summary>
    public bool LeadingBlankLine { get; init; } = true;

    /// <summary>
    /// When <c>true</c>, emit a horizontal rule line spanning the resolved
    /// terminal width immediately before the banner. The rule character is
    /// <c>─</c> (utf-8) or <c>-</c> (ascii), matching the banner's
    /// utf-8 / ascii branching. Default <c>false</c>.
    /// </summary>
    public bool HorizontalRule { get; init; } = false;
}
