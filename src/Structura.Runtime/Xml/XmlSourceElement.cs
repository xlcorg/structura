namespace Structura.Runtime.Xml;

/// <summary>
/// An XML element. <see cref="Span"/> covers the full element including
/// open and close tags. <see cref="InnerSpan"/> covers only the inner
/// content between <c>&gt;</c> and <c>&lt;/…&gt;</c> (zero-length for a
/// self-closing element) — that is the patchable region for "the text
/// content of this element" in the minimal-patch model.
/// </summary>
public sealed class XmlSourceElement : XmlSourceNode
{
    public XmlSourceElement(
        TextSpan span,
        TextSpan nameSpan,
        TextSpan innerSpan,
        string name,
        IReadOnlyList<XmlSourceAttribute> attributes,
        IReadOnlyList<XmlSourceNode> children)
        : base(span)
    {
        NameSpan = nameSpan;
        InnerSpan = innerSpan;
        Name = name;
        Attributes = attributes;
        Children = children;
    }

    public string Name { get; }

    public TextSpan NameSpan { get; }

    public TextSpan InnerSpan { get; }

    public IReadOnlyList<XmlSourceAttribute> Attributes { get; }

    public IReadOnlyList<XmlSourceNode> Children { get; }

    /// <summary>
    /// True if the element has exactly one child and that child is a text
    /// run — i.e. it is a leaf scalar carrier. Mixed content
    /// (text + nested elements) returns false.
    /// </summary>
    public bool IsPureText => Children.Count == 1 && Children[0] is XmlSourceText;

    public XmlSourceElement? FindElement(string name)
    {
        for (int i = 0; i < Children.Count; i++)
        {
            if (Children[i] is XmlSourceElement element
                && string.Equals(element.Name, name, StringComparison.Ordinal))
            {
                return element;
            }
        }
        return null;
    }

    public XmlSourceElement RequireElement(string name)
    {
        return FindElement(name)
            ?? throw new InvalidOperationException(
                $"Element <{name}> not found inside <{Name}>.");
    }

    public XmlSourceAttribute? FindAttribute(string name)
    {
        for (int i = 0; i < Attributes.Count; i++)
        {
            if (string.Equals(Attributes[i].Name, name, StringComparison.Ordinal))
            {
                return Attributes[i];
            }
        }
        return null;
    }

    public XmlSourceAttribute RequireAttribute(string name)
    {
        return FindAttribute(name)
            ?? throw new InvalidOperationException(
                $"Attribute '{name}' not found on <{Name}>.");
    }
}
