namespace Structura.Runtime;

/// <summary>
/// The hidden state object embedded in every generated document model. Holds
/// the original source text and the active edit / change set. The generated
/// model owns one of these as a private field and forwards
/// <see cref="IStructuraDocument"/> calls to it.
/// </summary>
public sealed class StructuraDocumentContext
{
    private readonly TextEditList _edits = new();
    private readonly Dictionary<string, DocumentChange> _changesByPath = new(StringComparer.Ordinal);

    public StructuraDocumentContext(string originalText)
    {
        ArgumentNullException.ThrowIfNull(originalText);
        OriginalText = originalText;
    }

    public string OriginalText { get; }

    public bool HasChanges => _edits.HasEdits;

    public IReadOnlyList<DocumentChange> Changes
    {
        get
        {
            var list = new List<DocumentChange>(_changesByPath.Values);
            list.Sort(static (a, b) => a.Span.Start.CompareTo(b.Span.Start));
            return list;
        }
    }

    /// <summary>
    /// Records that the value at <paramref name="path"/> spanning
    /// <paramref name="originalSpan"/> in the source should be replaced with
    /// <paramref name="replacement"/>. Setting the value back to the original
    /// text drops the edit.
    /// </summary>
    public void Record(string path, TextSpan originalSpan, string replacement)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(replacement);

        var oldText = OriginalText.Substring(originalSpan.Start, originalSpan.Length);
        if (string.Equals(oldText, replacement, StringComparison.Ordinal))
        {
            _edits.Remove(originalSpan);
            _changesByPath.Remove(path);
            return;
        }
        _edits.Set(new TextEdit(originalSpan, replacement));
        _changesByPath[path] = new DocumentChange(path, originalSpan, oldText, replacement);
    }

    public string ApplyEdits() => _edits.Apply(OriginalText);
}
