using System;
using System.Collections.Generic;
using System.Globalization;

namespace Structura.Generator;

// ── Property kind discriminated union ────────────────────────────────────────

internal enum XmlGenScalarKind
{
    String,
    Long,
    Decimal,
    Boolean,
}

// ── Property ─────────────────────────────────────────────────────────────────

internal sealed class XmlGenProperty
{
    public XmlGenProperty(string name, XmlGenScalarKind kind, bool isAttribute)
    {
        Name = name;
        Kind = kind;
        IsAttribute = isAttribute;
    }

    public string Name { get; }
    public XmlGenScalarKind Kind { get; }
    public bool IsAttribute { get; }
}

// ── Result ───────────────────────────────────────────────────────────────────

internal sealed class XmlRootInfo
{
    public XmlRootInfo(string rootName, List<XmlGenProperty> scalars)
    {
        RootName = rootName;
        Scalars = scalars;
    }

    public string RootName { get; }
    public List<XmlGenProperty> Scalars { get; }
}

// ── Parser ───────────────────────────────────────────────────────────────────

/// <summary>
/// Minimal structural XML scanner that runs inside the source generator
/// (netstandard2.0 — cannot depend on <c>Structura.Runtime.Xml</c>). Just
/// enough to enumerate root attributes and root child elements that are
/// pure-text scalars. On any error returns <see langword="null"/> so the
/// generator skips the file rather than crashing the build.
/// </summary>
internal static class GeneratorXmlParser
{
    public static XmlRootInfo? ParseRootInfo(string xml)
    {
        try
        {
            int p = 0;
            SkipProlog(xml, ref p);

            if (p >= xml.Length || xml[p] != '<')
            {
                return null;
            }
            p++; // consume '<'

            string rootName = ReadName(xml, ref p);
            if (rootName.Length == 0)
            {
                return null;
            }

            var scalars = new List<XmlGenProperty>();

            // Attributes on the root element
            while (true)
            {
                SkipWhitespace(xml, ref p);
                if (p >= xml.Length)
                {
                    return null;
                }
                char c = xml[p];
                if (c == '/' || c == '>')
                {
                    break;
                }
                XmlGenProperty? attr = ReadAttribute(xml, ref p);
                if (attr == null)
                {
                    return null;
                }
                scalars.Add(attr);
            }

            // Self-closing root → no element children
            if (xml[p] == '/')
            {
                return new XmlRootInfo(rootName, scalars);
            }

            p++; // consume '>'

            // Walk children of root. The close tag </NAME> MUST appear before
            // EOF; otherwise we consider the root unterminated and reject.
            bool sawCloseTag = false;
            while (p < xml.Length)
            {
                SkipWhitespace(xml, ref p);
                if (p >= xml.Length)
                {
                    return null;
                }

                if (StartsWith(xml, p, "</"))
                {
                    sawCloseTag = true;
                    break;
                }

                if (StartsWith(xml, p, "<!--"))
                {
                    int end = xml.IndexOf("-->", p + 4, StringComparison.Ordinal);
                    if (end < 0)
                    {
                        return null;
                    }
                    p = end + 3;
                    continue;
                }

                if (StartsWith(xml, p, "<![CDATA["))
                {
                    int end = xml.IndexOf("]]>", p + 9, StringComparison.Ordinal);
                    if (end < 0)
                    {
                        return null;
                    }
                    p = end + 3;
                    continue;
                }

                if (xml[p] != '<')
                {
                    // Stray text between tags — skip until next '<'
                    while (p < xml.Length && xml[p] != '<')
                    {
                        p++;
                    }
                    continue;
                }

                XmlGenProperty? child = TryReadChildScalarOrSkip(xml, ref p);
                if (child != null)
                {
                    scalars.Add(child);
                }
            }

            if (!sawCloseTag)
            {
                return null;
            }

            return new XmlRootInfo(rootName, scalars);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Tries to read a child element starting at <paramref name="p"/>. If the
    /// element is a pure-text scalar (only one text run between open and close
    /// tags), returns an <see cref="XmlGenProperty"/> for it. Otherwise advances
    /// past the entire element (potentially recursive) and returns null.
    /// </summary>
    private static XmlGenProperty? TryReadChildScalarOrSkip(string xml, ref int p)
    {
        int elementStart = p;
        p++; // consume '<'
        string name = ReadName(xml, ref p);
        if (name.Length == 0)
        {
            return null;
        }

        // Skip attributes on the child element (we do not expose them in V1).
        while (p < xml.Length)
        {
            SkipWhitespace(xml, ref p);
            if (p >= xml.Length)
            {
                return null;
            }
            char c = xml[p];
            if (c == '/' || c == '>')
            {
                break;
            }
            // Skip the attribute literal name="value" or name='value'.
            ReadName(xml, ref p);
            SkipWhitespace(xml, ref p);
            if (p >= xml.Length || xml[p] != '=')
            {
                return null;
            }
            p++;
            SkipWhitespace(xml, ref p);
            if (p >= xml.Length)
            {
                return null;
            }
            char quote = xml[p];
            if (quote != '"' && quote != '\'')
            {
                return null;
            }
            p++;
            int valEnd = xml.IndexOf(quote, p);
            if (valEnd < 0)
            {
                return null;
            }
            p = valEnd + 1;
        }

        if (p >= xml.Length)
        {
            return null;
        }

        // Self-closing → empty string content → emit string scalar.
        if (xml[p] == '/')
        {
            p++;
            if (p >= xml.Length || xml[p] != '>')
            {
                return null;
            }
            p++;
            return new XmlGenProperty(name, XmlGenScalarKind.String, isAttribute: false);
        }

        // Open tag terminator
        p++; // consume '>'

        // Read raw inner content up to matching close tag, tracking whether
        // we encounter any nested element.
        int contentStart = p;
        bool sawNested = false;
        while (p < xml.Length)
        {
            if (StartsWith(xml, p, "</"))
            {
                break;
            }
            if (StartsWith(xml, p, "<!--"))
            {
                int end = xml.IndexOf("-->", p + 4, StringComparison.Ordinal);
                if (end < 0)
                {
                    return null;
                }
                p = end + 3;
                continue;
            }
            if (StartsWith(xml, p, "<![CDATA["))
            {
                sawNested = true;
                int end = xml.IndexOf("]]>", p + 9, StringComparison.Ordinal);
                if (end < 0)
                {
                    return null;
                }
                p = end + 3;
                continue;
            }
            if (xml[p] == '<')
            {
                sawNested = true;
                // Skip the nested element fully (recursive walk via TryReadChildScalarOrSkip
                // discards its return value here — we are no longer a scalar).
                XmlGenProperty? _ = TryReadChildScalarOrSkip(xml, ref p);
                continue;
            }
            p++;
        }

        if (p >= xml.Length)
        {
            return null;
        }

        int contentEnd = p;
        // Consume close tag </name>
        p += 2; // </
        string closeName = ReadName(xml, ref p);
        if (!string.Equals(closeName, name, StringComparison.Ordinal))
        {
            return null;
        }
        SkipWhitespace(xml, ref p);
        if (p >= xml.Length || xml[p] != '>')
        {
            return null;
        }
        p++;

        if (sawNested)
        {
            return null;
        }

        string rawText = xml.Substring(contentStart, contentEnd - contentStart);
        string decoded = DecodeEntities(rawText);
        return new XmlGenProperty(name, InferKind(decoded), isAttribute: false);
    }

    private static XmlGenProperty? ReadAttribute(string xml, ref int p)
    {
        string name = ReadName(xml, ref p);
        if (name.Length == 0)
        {
            return null;
        }
        SkipWhitespace(xml, ref p);
        if (p >= xml.Length || xml[p] != '=')
        {
            return null;
        }
        p++; // consume '='
        SkipWhitespace(xml, ref p);
        if (p >= xml.Length)
        {
            return null;
        }
        char quote = xml[p];
        if (quote != '"' && quote != '\'')
        {
            return null;
        }
        p++;
        int valStart = p;
        int valEnd = xml.IndexOf(quote, p);
        if (valEnd < 0)
        {
            return null;
        }
        string raw = xml.Substring(valStart, valEnd - valStart);
        p = valEnd + 1;
        string decoded = DecodeEntities(raw);
        return new XmlGenProperty(name, InferKind(decoded), isAttribute: true);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static void SkipProlog(string xml, ref int p)
    {
        SkipWhitespace(xml, ref p);
        if (StartsWith(xml, p, "<?xml"))
        {
            int end = xml.IndexOf("?>", p + 5, StringComparison.Ordinal);
            if (end >= 0)
            {
                p = end + 2;
            }
        }
        // Skip any leading whitespace + comments
        while (true)
        {
            SkipWhitespace(xml, ref p);
            if (StartsWith(xml, p, "<!--"))
            {
                int end = xml.IndexOf("-->", p + 4, StringComparison.Ordinal);
                if (end < 0)
                {
                    return;
                }
                p = end + 3;
                continue;
            }
            return;
        }
    }

    private static void SkipWhitespace(string xml, ref int p)
    {
        while (p < xml.Length)
        {
            char c = xml[p];
            if (c == ' ' || c == '\t' || c == '\n' || c == '\r')
            {
                p++;
            }
            else
            {
                return;
            }
        }
    }

    private static bool StartsWith(string xml, int p, string s)
    {
        if (p + s.Length > xml.Length)
        {
            return false;
        }
        for (int i = 0; i < s.Length; i++)
        {
            if (xml[p + i] != s[i])
            {
                return false;
            }
        }
        return true;
    }

    private static string ReadName(string xml, ref int p)
    {
        if (p >= xml.Length || !IsNameStartChar(xml[p]))
        {
            return string.Empty;
        }
        int start = p;
        p++;
        while (p < xml.Length && IsNameChar(xml[p]))
        {
            p++;
        }
        return xml.Substring(start, p - start);
    }

    private static bool IsNameStartChar(char c)
    {
        return c == '_' || c == ':' || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');
    }

    private static bool IsNameChar(char c)
    {
        return IsNameStartChar(c)
            || c == '-'
            || c == '.'
            || (c >= '0' && c <= '9');
    }

    private static string DecodeEntities(string raw)
    {
        if (raw.IndexOf('&') < 0)
        {
            return raw;
        }
        var sb = new System.Text.StringBuilder(raw.Length);
        int i = 0;
        while (i < raw.Length)
        {
            char c = raw[i];
            if (c != '&')
            {
                sb.Append(c);
                i++;
                continue;
            }
            int semi = raw.IndexOf(';', i + 1);
            if (semi < 0)
            {
                sb.Append(c);
                i++;
                continue;
            }
            string body = raw.Substring(i + 1, semi - i - 1);
            switch (body)
            {
                case "amp": sb.Append('&'); break;
                case "lt": sb.Append('<'); break;
                case "gt": sb.Append('>'); break;
                case "quot": sb.Append('"'); break;
                case "apos": sb.Append('\''); break;
                default:
                    if (body.Length > 1 && body[0] == '#')
                    {
                        int code;
                        bool ok;
                        if (body[1] == 'x' || body[1] == 'X')
                        {
                            ok = int.TryParse(
                                body.Substring(2),
                                NumberStyles.HexNumber,
                                CultureInfo.InvariantCulture,
                                out code);
                        }
                        else
                        {
                            ok = int.TryParse(
                                body.Substring(1),
                                NumberStyles.Integer,
                                CultureInfo.InvariantCulture,
                                out code);
                        }
                        if (ok)
                        {
                            sb.Append(char.ConvertFromUtf32(code));
                        }
                    }
                    break;
            }
            i = semi + 1;
        }
        return sb.ToString();
    }

    private static XmlGenScalarKind InferKind(string text)
    {
        string trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return XmlGenScalarKind.String;
        }
        if (string.Equals(trimmed, "true", StringComparison.Ordinal)
            || string.Equals(trimmed, "false", StringComparison.Ordinal))
        {
            return XmlGenScalarKind.Boolean;
        }
        if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            return XmlGenScalarKind.Long;
        }
        if (trimmed.IndexOf('.') >= 0
            && decimal.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
        {
            return XmlGenScalarKind.Decimal;
        }
        return XmlGenScalarKind.String;
    }
}
