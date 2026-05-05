namespace Structura.Runtime.Json;

public sealed class JsonSourceObject : JsonSourceNode
{
    public JsonSourceObject(TextSpan span, IReadOnlyList<JsonSourceProperty> properties) : base(span)
    {
        Properties = properties;
    }

    public IReadOnlyList<JsonSourceProperty> Properties { get; }

    public JsonSourceProperty? FindProperty(string name)
    {
        foreach (var property in Properties)
        {
            if (string.Equals(property.Name, name, StringComparison.Ordinal))
            {
                return property;
            }
        }
        return null;
    }

    public JsonSourceProperty RequireProperty(string name)
        => FindProperty(name)
            ?? throw new InvalidOperationException($"Required JSON property '{name}' is missing.");
}
