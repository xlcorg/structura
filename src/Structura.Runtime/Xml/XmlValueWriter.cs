using System.Text;

namespace Structura.Runtime.Xml;

/// <summary>
/// Per-leaf-value encoder used by generated XML models when applying a
/// scalar mutation. The minimal-patch model rewrites only the leaf value
/// span; this class produces the exact replacement string for that span.
/// </summary>
public static class XmlValueWriter
{
    /// <summary>
    /// Encodes <paramref name="value"/> as the inner text of an element.
    /// Escapes <c>&amp;</c>, <c>&lt;</c>, <c>&gt;</c>. Null is written as
    /// the empty string (XML has no native null).
    /// </summary>
    public static string WriteElementText(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(value.Length);
        foreach (char c in value)
        {
            switch (c)
            {
                case '&':
                    sb.Append("&amp;");
                    break;
                case '<':
                    sb.Append("&lt;");
                    break;
                case '>':
                    sb.Append("&gt;");
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Encodes <paramref name="value"/> as a complete double-quoted
    /// attribute literal (including the surrounding quote characters).
    /// Escapes <c>&amp;</c>, <c>&lt;</c>, <c>"</c>. Null becomes the
    /// empty literal <c>""</c>.
    /// </summary>
    public static string WriteAttributeValue(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "\"\"";
        }

        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        foreach (char c in value)
        {
            switch (c)
            {
                case '&':
                    sb.Append("&amp;");
                    break;
                case '<':
                    sb.Append("&lt;");
                    break;
                case '"':
                    sb.Append("&quot;");
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }
}
