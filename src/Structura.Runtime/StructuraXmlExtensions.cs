using Structura.Runtime.Xml;

namespace Structura.Runtime;

/// <summary>
/// User-facing entry points for parsing XML into a generated Structura model.
/// </summary>
public static class StructuraXmlExtensions
{
    /// <summary>
    /// Parses <paramref name="xml"/> into <typeparamref name="T"/>. The model
    /// retains a reference to the original text and emits minimal text edits
    /// against it when its properties are mutated.
    /// </summary>
    public static T ParseXml<T>(this string xml) where T : IStructuraXmlDocument<T>
    {
        return T.ParseFromXml(xml);
    }
}
