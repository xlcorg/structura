namespace Structura.Runtime.Json;

public sealed class JsonSourceArray : JsonSourceNode
{
    public JsonSourceArray(TextSpan span, IReadOnlyList<JsonSourceNode> items) : base(span)
    {
        Items = items;
    }

    public IReadOnlyList<JsonSourceNode> Items { get; }
}
