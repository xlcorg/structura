namespace Structura.Reporting.Internal;

/// <summary>
/// Pairs a flat <see cref="DiffLine"/> sequence (as produced by
/// <see cref="DiffHunkBuilder"/>) into <see cref="SideBySideRow"/> entries:
/// <list type="bullet">
/// <item><see cref="DiffLineKind.Context"/> → row with the same line on both sides.</item>
/// <item><see cref="DiffLineKind.HunkSeparator"/> → row with the separator on both sides.</item>
/// <item>Run of <see cref="DiffLineKind.Removed"/> immediately followed by run of
/// <see cref="DiffLineKind.Added"/> (either may be empty) → top-aligned pairs;
/// the shorter side is padded with <c>null</c> at the bottom.</item>
/// </list>
/// </summary>
internal static class SideBySideRowBuilder
{
    public static IReadOnlyList<SideBySideRow> Build(IReadOnlyList<DiffLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var rows = new List<SideBySideRow>(lines.Count);
        var i = 0;
        while (i < lines.Count)
        {
            DiffLine line = lines[i];
            switch (line.Kind)
            {
                case DiffLineKind.Context:
                case DiffLineKind.HunkSeparator:
                    rows.Add(new SideBySideRow(line, line));
                    i++;
                    break;

                case DiffLineKind.Removed:
                case DiffLineKind.Added:
                    i = AppendChangeRun(lines, i, rows);
                    break;

                default:
                    throw new InvalidOperationException($"Unexpected DiffLineKind: {line.Kind}");
            }
        }
        return rows;
    }

    private static int AppendChangeRun(IReadOnlyList<DiffLine> lines, int start, List<SideBySideRow> rows)
    {
        var removed = new List<DiffLine>();
        var added = new List<DiffLine>();
        int cursor = start;

        while (cursor < lines.Count && lines[cursor].Kind == DiffLineKind.Removed)
        {
            removed.Add(lines[cursor]);
            cursor++;
        }
        while (cursor < lines.Count && lines[cursor].Kind == DiffLineKind.Added)
        {
            added.Add(lines[cursor]);
            cursor++;
        }

        int max = Math.Max(removed.Count, added.Count);
        for (var j = 0; j < max; j++)
        {
            DiffLine? left = j < removed.Count ? removed[j] : null;
            DiffLine? right = j < added.Count ? added[j] : null;
            rows.Add(new SideBySideRow(left, right));
        }
        return cursor;
    }
}
