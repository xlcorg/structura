using System.Text;

namespace Structura.Generator;

/// <summary>
/// Escapes a JSON object key for use as a JSON Pointer path segment (RFC 6901)
/// and prepends the leading <c>/</c>.
/// </summary>
internal static class JsonPointerEscaper
{
    /// <summary>
    /// Returns the JSON Pointer path for a root-level property key,
    /// e.g. <c>"currency"</c> → <c>"/currency"</c>,
    /// <c>"a/b"</c> → <c>"/a~1b"</c>.
    /// </summary>
    public static string Escape(string jsonKey)
    {
        // Fast path: no characters need escaping.
        if (jsonKey.IndexOf('~') < 0 && jsonKey.IndexOf('/') < 0)
        {
            return "/" + jsonKey;
        }

        var sb = new StringBuilder(jsonKey.Length + 8);
        sb.Append('/');

        foreach (char c in jsonKey)
        {
            if (c == '~')
            {
                sb.Append("~0");
            }
            else if (c == '/')
            {
                sb.Append("~1");
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}
