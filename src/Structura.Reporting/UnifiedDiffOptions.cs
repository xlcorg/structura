namespace Structura.Reporting;

/// <summary>
/// Options for <see cref="UnifiedDiffReporter"/>. Defaults match the spec:
/// 3 lines of context on each side of every change, inline highlight on.
/// </summary>
public sealed record UnifiedDiffOptions
{
    public int ContextLines { get; init; } = 3;

    public bool InlineHighlight { get; init; } = true;
}
