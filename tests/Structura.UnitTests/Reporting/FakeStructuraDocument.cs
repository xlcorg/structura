using Structura.Runtime;

namespace Structura.UnitTests.Reporting;

/// <summary>
/// Minimal in-test stand-in for <see cref="IStructuraDocument"/>. Lets the
/// reporter unit tests construct exact change sets without going through
/// the parser/generator pipeline.
/// </summary>
internal sealed class FakeStructuraDocument : IStructuraDocument
{
    public FakeStructuraDocument(
        string originalText,
        IReadOnlyList<DocumentChange> changes,
        string documentName = "fake.json")
    {
        OriginalText = originalText;
        Changes = changes;
        DocumentName = documentName;
    }

    public string OriginalText { get; }

    public string? CurrentTextOverride { get; init; }

    public string CurrentText => CurrentTextOverride ?? OriginalText;

    public string DocumentName { get; }

    public IReadOnlyList<DocumentChange> Changes { get; }
}
