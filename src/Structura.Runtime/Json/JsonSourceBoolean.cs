namespace Structura.Runtime.Json;

public sealed class JsonSourceBoolean : JsonSourceNode
{
    public JsonSourceBoolean(TextSpan span, bool value) : base(span)
    {
        Value = value;
    }

    public bool Value { get; }
}
