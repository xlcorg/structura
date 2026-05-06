namespace Structura.Runtime.Json;

/// <summary>
/// A node in the parsed JSON source tree. <see cref="Span"/> is the
/// half-open range in the original document text that produced this node
/// (including surrounding quotes for strings, the entire literal for numbers,
/// braces for objects, brackets for arrays).
/// </summary>
public abstract class JsonSourceNode
{
    protected JsonSourceNode(TextSpan span)
    {
        Span = span;
    }

    public TextSpan Span { get; }
}
