namespace Structura.Runtime;

/// <summary>
/// The hidden state object embedded in every generated document model. Holds
/// the original source text and the active edit / change set. The generated
/// model owns one of these as a private field and forwards
/// <see cref="IStructuraDocument"/> calls to it.
/// </summary>
public sealed class StructuraDocumentContext
{
    private readonly Dictionary<TextSpan, RecordedEdit> _edits = new();

    public StructuraDocumentContext(string originalText, string documentName)
    {
        ArgumentNullException.ThrowIfNull(originalText);
        ArgumentNullException.ThrowIfNull(documentName);
        OriginalText = originalText;
        DocumentName = documentName;
    }

    public string OriginalText { get; }

    public string DocumentName { get; }

    public bool HasChanges => _edits.Count > 0;

    public IReadOnlyList<DocumentChange> Changes
    {
        get
        {
            var list = new List<DocumentChange>(_edits.Count);
            foreach ((TextSpan span, RecordedEdit edit) in _edits)
            {
                var oldText = OriginalText.Substring(span.Start, span.Length);
                list.Add(new DocumentChange(edit.Path, span, oldText, edit.Replacement));
            }
            list.Sort(static (a, b) => a.Span.Start.CompareTo(b.Span.Start));
            return list;
        }
    }

    /// <summary>
    /// Records that the value at <paramref name="path"/> spanning
    /// <paramref name="originalSpan"/> in the source should be replaced with
    /// <paramref name="replacement"/>. Setting the value back to the original
    /// text drops the edit. Two records on the same span (regardless of path)
    /// collapse to a single entry — the latest wins.
    /// </summary>
    public void Record(string path, TextSpan originalSpan, string replacement)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(replacement);

        var oldText = OriginalText.Substring(originalSpan.Start, originalSpan.Length);
        if (string.Equals(oldText, replacement, StringComparison.Ordinal))
        {
            _edits.Remove(originalSpan);
            return;
        }
        _edits[originalSpan] = new RecordedEdit(path, replacement);
    }

    public string ApplyEdits()
    {
        if (_edits.Count == 0)
        {
            return OriginalText;
        }
        var list = new TextEditList();
        foreach ((TextSpan span, RecordedEdit edit) in _edits)
        {
            list.Set(new TextEdit(span, edit.Replacement));
        }
        return list.Apply(OriginalText);
    }

    private readonly record struct RecordedEdit(string Path, string Replacement);
}
