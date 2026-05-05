namespace Structura.Runtime.Json;

/// <summary>
/// One property of a JSON object. <see cref="KeySpan"/> covers the key literal
/// including its quotes. <see cref="ValueSpan"/> equals <c>Value.Span</c> but
/// is exposed at the property level for convenience.
/// </summary>
public sealed record JsonSourceProperty(string Name, TextSpan KeySpan, TextSpan ValueSpan, JsonSourceNode Value);
