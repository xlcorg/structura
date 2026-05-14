namespace Structura.Reporting;

/// <summary>
/// Selects which layout <see cref="DiffReporter"/> emits.
/// <see cref="Auto"/> picks side-by-side when the terminal meets the minimum
/// two-column width (gutter + padding + separator + 2 × 40 content columns),
/// otherwise falls back to unified. <see cref="Unified"/> and
/// <see cref="SideBySide"/> override the heuristic and force that layout
/// regardless of width.
/// </summary>
public enum DiffReporterLayout
{
    Auto,
    Unified,
    SideBySide,
}
