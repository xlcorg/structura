namespace Structura.Runtime.Json;

/// <summary>
/// A JSON number literal. The original textual form is preserved in
/// <see cref="Literal"/>; numeric parsing is left to the consumer so that
/// precision/format choices stay in user code.
/// </summary>
public sealed class JsonSourceNumber : JsonSourceNode
{
    public JsonSourceNumber(TextSpan span, string literal) : base(span)
    {
        Literal = literal;
    }

    public string Literal { get; }
}
