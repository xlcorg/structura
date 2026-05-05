namespace Structura.Runtime;

/// <summary>
/// Replace <see cref="Span"/> in the original document text with <see cref="Replacement"/>.
/// </summary>
public readonly record struct TextEdit(TextSpan Span, string Replacement);
