using System.Text;

namespace Structura.Runtime;

/// <summary>
/// Mutable, span-keyed collection of text edits over a single source document.
/// Setting two edits with the same span keeps only the latest. Edits with
/// different but intersecting spans are rejected at <see cref="Apply"/> time.
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

    public IReadOnlyList<TextEdit> Snapshot()
    {
        var sorted = new List<TextEdit>(_edits.Values);
        sorted.Sort(static (a, b) => a.Span.Start.CompareTo(b.Span.Start));
        for (var i = 1; i < sorted.Count; i++)
        {
            if (sorted[i - 1].Span.End > sorted[i].Span.Start)
            {
                throw new InvalidOperationException(
                    $"Overlapping edits: {sorted[i - 1].Span} and {sorted[i].Span}.");
            }
        }
        return sorted;
    }

    public string Apply(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        IReadOnlyList<TextEdit> sorted = Snapshot();
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
}
