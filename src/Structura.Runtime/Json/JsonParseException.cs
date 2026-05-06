namespace Structura.Runtime.Json;

public sealed class JsonParseException : Exception
{
    public JsonParseException(string message) : base(message) { }
    public JsonParseException(string message, Exception inner) : base(message, inner) { }
}
