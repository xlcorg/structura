namespace Structura.Reporting;

/// <summary>
/// Selects which layout <see cref="DiffReporter"/> emits.
/// <see cref="Auto"/> picks side-by-side when the terminal is wide enough for
/// the longest content line (or for an acceptable per-side minimum), otherwise
/// falls back to unified. <see cref="Unified"/> and <see cref="SideBySide"/>
/// override the heuristic and force that layout regardless of width.
/// </summary>
public enum DiffReporterLayout
{
    Auto,
    Unified,
    SideBySide,
}
