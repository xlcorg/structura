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
    public XmlRootInfo(string rootName, IReadOnlyList<string> wrapperChain, List<XmlGenProperty> scalars)
    {
        RootName = rootName;
        WrapperChain = wrapperChain;
        Scalars = scalars;
    }

    /// <summary>The literal root element name in the source document.</summary>
    public string RootName { get; }

    /// <summary>
    /// Names of intermediate envelope elements that the generator descended
    /// through to reach the effective data root (the element whose direct
    /// children/attributes are exposed as scalars). Empty when the literal
    /// root <em>is</em> the effective root.
    /// </summary>
    public IReadOnlyList<string> WrapperChain { get; }

    public List<XmlGenProperty> Scalars { get; }
}

// ── Parser ───────────────────────────────────────────────────────────────────

/// <summary>
/// Minimal structural XML scanner that runs inside the source generator
/// (netstandard2.0 — cannot depend on <c>Structura.Runtime.Xml</c>). It
/// determines the document's effective data root (descending through
/// single-element envelope wrappers) and enumerates that element's scalar
/// attributes and pure-text child elements. On any error returns
/// <see langword="null"/> so the generator skips the file rather than
/// crashing the build.
/// </summary>
internal static class GeneratorXmlParser
{
    public static XmlRootInfo? ParseRootInfo(string xml)
    {
        try
        {
            int p = 0;
            SkipProlog(xml, ref p);

            ElementInfo? literalRoot = TryParseElementAt(xml, ref p);
            if (literalRoot == null)
            {
                return null;
            }

            // The literal root must be the only top-level element.
            SkipTrailing(xml, ref p);
            if (p < xml.Length)
            {
                return null;
            }

            // Walk the wrapper chain. The rule fires only when:
            //   - 0 attributes, AND
            //   - 0 pure-text scalar children, AND
            //   - exactly 1 structural element child.
            var wrapperChain = new List<string>();
            ElementInfo current = literalRoot;
            while (current.Attributes.Count == 0
                && current.PureTextChildren.Count == 0
                && current.StructuralChildPositions.Count == 1)
            {
                int childStart = current.StructuralChildPositions[0];
                ElementInfo? next = TryParseElementAt(xml, ref childStart);
                if (next == null)
                {
                    break;
                }
                wrapperChain.Add(next.Name);
                current = next;
            }

            // Effective root carries the scalars: attributes first, then
            // pure-text element children, in document order.
            var scalars = new List<XmlGenProperty>();
            scalars.AddRange(current.Attributes);
            scalars.AddRange(current.PureTextChildren);

            return new XmlRootInfo(literalRoot.Name, wrapperChain, scalars);
        }
        catch
        {
            return null;
        }
    }

    // ── Element parsing (fully populates structural info) ────────────────────

    private sealed class ElementInfo
    {
        public string Name = string.Empty;
        public List<XmlGenProperty> Attributes = new List<XmlGenProperty>();
        public List<XmlGenProperty> PureTextChildren = new List<XmlGenProperty>();

        /// <summary>
        /// Position (just past the leading <c>&lt;</c>'s match — i.e. the
        /// index of the open <c>&lt;</c> character itself) of every
        /// non-pure-text element child encountered while scanning this
        /// element's content. Used to descend into the wrapper chain.
        /// </summary>
        public List<int> StructuralChildPositions = new List<int>();
    }

    /// <summary>
    /// Parses a single element starting at <paramref name="p"/> (which must
    /// point at a <c>&lt;</c>). Advances <paramref name="p"/> to just past
    /// the closing <c>&gt;</c>. Returns null on any malformed input.
    /// </summary>
    private static ElementInfo? TryParseElementAt(string xml, ref int p)
    {
        if (p >= xml.Length || xml[p] != '<')
        {
            return null;
        }

        int elementStart = p;
        p++; // consume '<'
        string name = ReadName(xml, ref p);
        if (name.Length == 0)
        {
            return null;
        }

        var info = new ElementInfo { Name = name };

        // Attributes
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
            info.Attributes.Add(attr);
        }

        // Self-closing → no inner content
        if (xml[p] == '/')
        {
            p++;
            if (p >= xml.Length || xml[p] != '>')
            {
                return null;
            }
            p++;
            return info;
        }

        p++; // consume '>'

        // Inner content. We need to classify each child element as either a
        // pure-text scalar (record as PureTextChildren) or structural
        // (record only its start position). We must NOT collect text runs as
        // pseudo-children — bare text between tags counts as "non-pure-text"
        // for this element's classification, but for V1 we don't expose it.
        bool sawCloseTag = false;
        while (p < xml.Length)
        {
            // Non-tag content is whitespace or text. Skip but track that we
            // saw text so the caller can know "this element isn't an empty
            // wrapper" if needed. (Currently we don't surface that — it has
            // no impact on V1 scalar discovery.)
            if (xml[p] != '<')
            {
                while (p < xml.Length && xml[p] != '<')
                {
                    p++;
                }
                continue;
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

            // It's a child element. Classify it.
            int childStart = p;
            ChildClassification? classification = ClassifyChild(xml, ref p);
            if (classification is null)
            {
                return null;
            }
            if (classification.PureTextScalar != null)
            {
                info.PureTextChildren.Add(classification.PureTextScalar);
            }
            else
            {
                info.StructuralChildPositions.Add(childStart);
            }
        }

        if (!sawCloseTag)
        {
            return null;
        }

        // Consume close tag </name>
        p += 2; // consume "</"
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

        return info;
    }

    private sealed class ChildClassification
    {
        public XmlGenProperty? PureTextScalar { get; set; }
    }

    /// <summary>
    /// Reads one child element starting at <paramref name="p"/> (which must
    /// point at <c>&lt;</c>). Returns a classification: either a pure-text
    /// scalar property, or "structural" (no scalar) when the element has
    /// attributes, nested elements, or any non-text content. Advances
    /// <paramref name="p"/> past the element's closing tag in either case.
    /// Returns null on parse error.
    /// </summary>
    private static ChildClassification? ClassifyChild(string xml, ref int p)
    {
        p++; // consume '<'
        string name = ReadName(xml, ref p);
        if (name.Length == 0)
        {
            return null;
        }

        // Attributes — record presence but skip values. Any attribute disqualifies
        // this element from being a "pure-text scalar" (we expose root attrs but
        // not nested attrs in V1).
        bool hasAttribute = false;
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
            hasAttribute = true;
            // Skip `name="value"` or `name='value'` literal.
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

        var result = new ChildClassification();

        // Self-closing → empty content → string scalar (unless it has attrs).
        if (xml[p] == '/')
        {
            p++;
            if (p >= xml.Length || xml[p] != '>')
            {
                return null;
            }
            p++;
            if (!hasAttribute)
            {
                result.PureTextScalar = new XmlGenProperty(name, XmlGenScalarKind.String, isAttribute: false);
            }
            return result;
        }

        p++; // consume '>'
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
                ChildClassification? _ = ClassifyChild(xml, ref p);
                if (_ == null)
                {
                    return null;
                }
                continue;
            }
            p++;
        }

        if (p >= xml.Length)
        {
            return null;
        }

        int contentEnd = p;
        p += 2; // consume "</"
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

        // Pure-text scalar requires: no attributes, no nested elements, no CDATA.
        if (!hasAttribute && !sawNested)
        {
            string rawText = xml.Substring(contentStart, contentEnd - contentStart);
            string decoded = DecodeEntities(rawText);
            result.PureTextScalar = new XmlGenProperty(name, InferKind(decoded), isAttribute: false);
        }
        return result;
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
        // Skip leading whitespace + comments
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

    private static void SkipTrailing(string xml, ref int p)
    {
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
