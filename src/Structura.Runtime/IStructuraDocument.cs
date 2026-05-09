namespace Structura.Runtime;

/// <summary>
/// Reporter-facing view of a parsed document: the original text, the current
/// (patched) text, the document name (derived from the sample file at codegen
/// time), and the set of mutations applied so far. Generated models implement
/// this implicitly through their embedded <see cref="StructuraDocumentContext"/>.
/// </summary>
public interface IStructuraDocument
{
    string OriginalText { get; }

    string CurrentText { get; }

    string DocumentName { get; }

    IReadOnlyList<DocumentChange> Changes { get; }
}
