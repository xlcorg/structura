namespace Structura.Runtime;

/// <summary>
/// Reporter-facing view of a parsed document: the original text, the current
/// (patched) text, and the set of mutations applied so far. Generated models
/// implement this implicitly through their embedded <see cref="StructuraDocumentContext"/>.
/// </summary>
public interface IStructuraDocument
{
    string OriginalText { get; }

    string CurrentText { get; }

    IReadOnlyList<DocumentChange> Changes { get; }
}
