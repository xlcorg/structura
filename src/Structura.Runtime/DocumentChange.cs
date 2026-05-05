namespace Structura.Runtime;

/// <summary>
/// One observed mutation: at <see cref="Path"/>, the original text in <see cref="Span"/>
/// (<see cref="OldText"/>) is to be replaced with <see cref="NewText"/>.
/// </summary>
public sealed record DocumentChange(string Path, TextSpan Span, string OldText, string NewText);
