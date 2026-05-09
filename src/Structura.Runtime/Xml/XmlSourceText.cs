namespace Structura.Runtime.Xml;

/// <summary>
/// A text run between two tags. <see cref="Span"/> covers the raw,
/// still-encoded source text; <see cref="Value"/> is the decoded form
/// (entity references resolved). The two are byte-identical when the
/// original text contained no entity references.
/// </summary>
public sealed class XmlSourceText : XmlSourceNode
{
    public XmlSourceText(TextSpan span, string value)
        : base(span)
    {
        Value = value;
    }

    public string Value { get; }
}
