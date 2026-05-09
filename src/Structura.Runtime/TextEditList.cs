using System.Text;

namespace Structura.Runtime;

/// <summary>
/// Mutable, span-keyed collection of text edits over a single source document.
/// Setting two edits with the same span keeps only the latest. Edits with
/// different but intersecting spans are rejected by <see cref="Validate"/>
/// (and consequently by <see cref="Apply"/>); <see cref="Snapshot"/> is a
/// pure read-only view that never throws.
/// </summary>
public sealed class TextEditList
{
    private readonly Dictionary<TextSpan, TextEdit> _edits = new Dictionary<TextSpan, TextEdit>();

    public bool HasEdits => _edits.Count > 0;

    public void Set(TextEdit edit)
    {
        _edits[edit.Span] = edit;
    }

    public bool Remove(TextSpan span)
    {
        return _edits.Remove(span);
    }

    public void Clear()
    {
        _edits.Clear();
    }

    /// <summary>
    /// Returns the current edits sorted by <see cref="TextSpan.Start"/>.
    /// Pure: never throws, even if edits overlap. Use <see cref="Validate"/>
    /// to detect overlaps explicitly.
    /// </summary>
    public IReadOnlyList<TextEdit> Snapshot()
    {
        var sorted = new List<TextEdit>(_edits.Values);
        sorted.Sort(static (a, b) => a.Span.Start.CompareTo(b.Span.Start));
        return sorted;
    }

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> if any two edits have
    /// distinct but intersecting spans. Same-span overwrites are not an
    /// overlap (they are deduplicated by <see cref="Set"/>).
    /// </summary>
    public void Validate()
    {
        EnsureNonOverlapping(Snapshot());
    }

    public string Apply(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        IReadOnlyList<TextEdit> sorted = Snapshot();
        EnsureNonOverlapping(sorted);
        if (sorted.Count == 0)
        {
            return source;
        }

        var sb = new StringBuilder(source.Length);
        var cursor = 0;
        foreach (TextEdit edit in sorted)
        {
            if (edit.Span.End > source.Length)
            {
                throw new InvalidOperationException(
                    $"Edit span {edit.Span} extends past source length {source.Length}.");
            }
            sb.Append(source, cursor, edit.Span.Start - cursor);
            sb.Append(edit.Replacement);
            cursor = edit.Span.End;
        }
        sb.Append(source, cursor, source.Length - cursor);
        return sb.ToString();
    }

    private static void EnsureNonOverlapping(IReadOnlyList<TextEdit> sorted)
    {
        for (var i = 1; i < sorted.Count; i++)
        {
            if (sorted[i - 1].Span.End > sorted[i].Span.Start)
            {
                throw new InvalidOperationException(
                    $"Overlapping edits: {sorted[i - 1].Span} and {sorted[i].Span}.");
            }
        }
    }
}
