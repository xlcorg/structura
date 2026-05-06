namespace Structura.Runtime.Json;

/// <summary>
/// A JSON string literal. <see cref="Span"/> covers the literal including
/// surrounding double quotes. <see cref="Value"/> is the decoded string
/// (escape sequences resolved).
/// </summary>
public sealed class JsonSourceString : JsonSourceNode
{
    public JsonSourceString(TextSpan span, string value) : base(span)
    {
        Value = value;
    }

    public string Value { get; }
}
