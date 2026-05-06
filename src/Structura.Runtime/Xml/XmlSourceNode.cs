namespace Structura.Runtime.Xml;

/// <summary>
/// A node in the parsed XML source tree. <see cref="Span"/> is the half-open
/// range in the original document text that produced this node — for an
/// element this covers the entire <c>&lt;tag…&gt;…&lt;/tag&gt;</c>, for a
/// text run it covers the raw (still-encoded) text between two tags.
/// </summary>
public abstract class XmlSourceNode
{
    protected XmlSourceNode(TextSpan span)
    {
        Span = span;
    }

    public TextSpan Span { get; }
}
