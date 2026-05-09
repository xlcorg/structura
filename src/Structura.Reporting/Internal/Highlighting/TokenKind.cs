namespace Structura.Reporting.Internal.Highlighting;

/// <summary>
/// Format-agnostic kinds of tokens emitted by per-line painters. Punctuation
/// and Text map to "no foreground color" in <see cref="SyntaxPalette"/>; they
/// exist so the painter's output is a complete cover of the line.
/// </summary>
internal enum TokenKind
{
    Punctuation,
    Key,
    String,
    Number,
    Keyword,
    ElementName,
    AttrName,
    AttrValue,
    Comment,
    EntityRef,
    Text,
}
