using Structura.Runtime;

namespace Structura.UnitTests.Reporting;

/// <summary>
/// Minimal in-test stand-in for <see cref="IStructuraDocument"/>. Lets the
/// reporter unit tests construct exact change sets without going through
/// the parser/generator pipeline.
/// </summary>
internal sealed class FakeStructuraDocument : IStructuraDocument
{
    public FakeStructuraDocument(string originalText, IReadOnlyList<DocumentChange> changes)
    {
        OriginalText = originalText;
        Changes = changes;
    }

    public string OriginalText { get; }

    public string CurrentText => OriginalText; // reporters do not read this

    public IReadOnlyList<DocumentChange> Changes { get; }
}
