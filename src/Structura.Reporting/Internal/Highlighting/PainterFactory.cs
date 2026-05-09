using Structura.Runtime;

namespace Structura.Reporting.Internal.Highlighting;

/// <summary>
/// Resolves the syntax painter for a document. Centralizes the JSON/XML
/// detection so the renderers stay format-agnostic.
/// </summary>
internal static class PainterFactory
{
    public static IDiffSyntaxPainter For(IStructuraDocument doc, bool syntaxOn)
    {
        if (!syntaxOn)
        {
            return NullPainter.Instance;
        }

        IDiffSyntaxPainter? byName = ByDocumentName(doc.DocumentName);
        if (byName is not null)
        {
            return byName;
        }
        return SniffByFirstChar(doc.OriginalText);
    }

    private static IDiffSyntaxPainter? ByDocumentName(string documentName)
    {
        if (documentName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            return JsonLinePainter.Instance;
        }
        if (documentName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
        {
            return XmlLinePainter.Instance;
        }
        return null;
    }

    private static IDiffSyntaxPainter SniffByFirstChar(string originalText)
    {
        for (var i = 0; i < originalText.Length; i++)
        {
            char c = originalText[i];
            if (char.IsWhiteSpace(c))
            {
                continue;
            }
            if (c == '{' || c == '[')
            {
                return JsonLinePainter.Instance;
            }
            if (c == '<')
            {
                return XmlLinePainter.Instance;
            }
            return NullPainter.Instance;
        }
        return NullPainter.Instance;
    }
}
