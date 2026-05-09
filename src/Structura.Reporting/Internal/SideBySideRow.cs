namespace Structura.Reporting.Internal;

/// <summary>
/// One rendered row of side-by-side output. Either side may be <c>null</c>,
/// meaning that side renders as blank padding (e.g. multi-line replacement
/// where one side has more lines than the other). For
/// <see cref="DiffLineKind.HunkSeparator"/>, both sides hold the same
/// separator <see cref="DiffLine"/>.
/// </summary>
internal readonly record struct SideBySideRow(DiffLine? Left, DiffLine? Right);
