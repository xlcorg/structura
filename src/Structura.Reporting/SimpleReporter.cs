using Structura.Runtime;

namespace Structura.Reporting;

/// <summary>
/// Flat one-line-per-change printer. For every entry in
/// <see cref="IStructuraDocument.Changes"/> it writes a single line
/// in the form <c>  /path: oldLiteral → newLiteral</c>, prefixed by a
/// <c>{n} change(s):</c> header. Empty change sets render as
/// <c>(no changes)</c>.
/// </summary>
public static class SimpleReporter
{
    public static void Print(IStructuraDocument document)
    {
        Print(document, Console.Out);
    }

    public static void Print(IStructuraDocument document, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(writer);

        IReadOnlyList<DocumentChange> changes = document.Changes;
        if (changes.Count == 0)
        {
            writer.WriteLine("(no changes)");
            return;
        }

        writer.WriteLine($"{changes.Count} change(s):");
        foreach (DocumentChange c in changes)
        {
            writer.WriteLine($"  {c.Path}: {c.OldText} → {c.NewText}");
        }
    }
}
