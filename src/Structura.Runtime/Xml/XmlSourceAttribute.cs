namespace Structura.Runtime.Xml;

/// <summary>
/// One attribute on an XML element.
/// <see cref="NameSpan"/> covers the attribute name only.
/// <see cref="ValueSpan"/> covers the entire literal — including the
/// surrounding quote characters — so a setter can splice with the
/// original quote style preserved.
/// <see cref="Value"/> is the decoded value (entity references resolved).
/// </summary>
public sealed record XmlSourceAttribute(
    string Name,
    TextSpan NameSpan,
    TextSpan ValueSpan,
    string Value);
