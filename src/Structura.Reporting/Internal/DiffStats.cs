namespace Structura.Reporting.Internal;

/// <summary>
/// Aggregate counts derived from a flat <see cref="DiffLine"/> sequence:
/// number of added/removed lines and the maximum displayed line number
/// (used by reporters to size the gutter).
/// </summary>
internal readonly record struct DiffStats(int Additions, int Removals, int MaxLineNumber)
{
    public static DiffStats Compute(IReadOnlyList<DiffLine> lines)
    {
        int additions = 0;
        int removals = 0;
        int maxLineNumber = 1;
        foreach (DiffLine line in lines)
        {
            if (line.Kind == DiffLineKind.Added)
            {
                additions++;
            }
            else if (line.Kind == DiffLineKind.Removed)
            {
                removals++;
            }

            int candidate = Math.Max(line.OldLineNumber, line.NewLineNumber);
            if (candidate > maxLineNumber)
            {
                maxLineNumber = candidate;
            }
        }
        return new DiffStats(additions, removals, maxLineNumber);
    }
}
