namespace Structura.Reporting;

/// <summary>
/// Options for <see cref="SideBySideDiffReporter"/>.
/// </summary>
public sealed record SideBySideDiffOptions
{
    public int ContextLines { get; init; } = 3;

    public bool InlineHighlight { get; init; } = true;

    /// <summary>
    /// Total output width (gutters + columns + separator). When <c>null</c>,
    /// the reporter uses <see cref="System.Console.WindowWidth"/> if available
    /// and otherwise falls back to <c>160</c>.
    /// </summary>
    public int? TotalWidth { get; init; } = null;
}
