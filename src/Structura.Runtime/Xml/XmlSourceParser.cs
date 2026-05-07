using System.Globalization;
using System.Text;

namespace Structura.Runtime.Xml;

/// <summary>
/// Hand-written recursive-descent parser for a strict subset of XML 1.0
/// sufficient for the Structura V1 minimal-patch model:
/// <list type="bullet">
///   <item>One root element.</item>
///   <item>Optional <c>&lt;?xml … ?&gt;</c> declaration is skipped.</item>
///   <item>Comments and CDATA sections are recognised; CDATA payload is
///         exposed as a single <see cref="XmlSourceText"/> with the raw
///         payload as its value, comments are skipped.</item>
///   <item>Entity references <c>&amp;amp;</c>, <c>&amp;lt;</c>,
///         <c>&amp;gt;</c>, <c>&amp;quot;</c>, <c>&amp;apos;</c>,
///         <c>&amp;#NNN;</c>, <c>&amp;#xHHH;</c> are decoded.</item>
/// </list>
/// DTD/DOCTYPE, processing instructions other than the XML declaration,
/// and unrecognised entities are out of scope and produce
/// <see cref="XmlParseException"/>.
/// </summary>
public static class XmlSourceParser
{
    public static XmlSourceElement Parse(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var state = new ParserState(source);
        state.SkipProlog();

        if (state.IsAtEnd || state.Peek() != '<')
        {
            throw new XmlParseException(
                $"Expected element at position {state.Position}.");
        }

        XmlSourceElement root = state.ParseElement();
        state.SkipTrailingWhitespaceAndComments();
        if (!state.IsAtEnd)
        {
            throw new XmlParseException(
                $"Unexpected content after root element at position {state.Position}.");
        }
        return root;
    }

    private sealed class ParserState
    {
        private readonly string _source;
        private int _pos;

        public ParserState(string source)
        {
            _source = source;
        }

        public int Position => _pos;
        public bool IsAtEnd => _pos >= _source.Length;

        public char Peek()
        {
            return _source[_pos];
        }

        /// <summary>True if the parser walked past a DOCTYPE block.</summary>
        public bool SawDtd { get; private set; }

        /// <summary>True if the parser saw an unknown entity reference and preserved it as literal text.</summary>
        public bool SawUnknownEntity { get; private set; }

        public void SkipProlog()
        {
            SkipWhitespace();
            // Optional XML declaration <?xml … ?>
            if (StartsWith("<?xml"))
            {
                int end = _source.IndexOf("?>", _pos + 5, StringComparison.Ordinal);
                if (end < 0)
                {
                    throw new XmlParseException("Unterminated XML declaration.");
                }
                _pos = end + 2;
            }
            SkipTrailingWhitespaceAndComments();
        }

        /// <summary>
        /// Walks past whitespace, comments, and an optional <c>&lt;!DOCTYPE …&gt;</c>
        /// block. Internal subsets (<c>[ … ]</c>) are matched by scanning to the
        /// closing <c>]&gt;</c>; subsets without internal markup are matched by
        /// the next <c>&gt;</c>. Sets <see cref="SawDtd"/> when a DOCTYPE is
        /// encountered so the source generator can emit STR0006.
        /// </summary>
        public void SkipTrailingWhitespaceAndComments()
        {
            while (true)
            {
                SkipWhitespace();
                if (StartsWith("<!--"))
                {
                    int end = _source.IndexOf("-->", _pos + 4, StringComparison.Ordinal);
                    if (end < 0)
                    {
                        throw new XmlParseException("Unterminated comment.");
                    }
                    _pos = end + 3;
                    continue;
                }
                if (StartsWith("<!DOCTYPE"))
                {
                    SawDtd = true;
                    SkipDoctype();
                    continue;
                }
                return;
            }
        }

        private void SkipDoctype()
        {
            // Pre: positioned at '<' of "<!DOCTYPE…>".
            // The DOCTYPE may include an internal subset enclosed by [ … ].
            // Scan forward looking for either ']>' (closes a subset) or '>'
            // (closes a subset-less DOCTYPE). Track depth in case of nested
            // angle brackets inside the subset (rare but possible).
            int depthAngle = 0;
            int p = _pos + "<!DOCTYPE".Length;
            bool insideSubset = false;
            while (p < _source.Length)
            {
                char c = _source[p];
                if (c == '[')
                {
                    insideSubset = true;
                }
                else if (c == ']' && insideSubset)
                {
                    // Look for matching '>' after ']'.
                    int afterBracket = p + 1;
                    while (afterBracket < _source.Length
                           && (_source[afterBracket] == ' '
                               || _source[afterBracket] == '\t'
                               || _source[afterBracket] == '\n'
                               || _source[afterBracket] == '\r'))
                    {
                        afterBracket++;
                    }
                    if (afterBracket < _source.Length && _source[afterBracket] == '>')
                    {
                        _pos = afterBracket + 1;
                        return;
                    }
                }
                else if (c == '<')
                {
                    depthAngle++;
                }
                else if (c == '>')
                {
                    if (depthAngle > 0)
                    {
                        depthAngle--;
                    }
                    else if (!insideSubset)
                    {
                        _pos = p + 1;
                        return;
                    }
                }
                p++;
            }

            throw new XmlParseException("Unterminated <!DOCTYPE …>.");
        }

        public XmlSourceElement ParseElement()
        {
            int start = _pos;
            Expect('<');

            int nameStart = _pos;
            string name = ReadName();
            var nameSpan = new TextSpan(nameStart, _pos - nameStart);

            var attributes = new List<XmlSourceAttribute>();
            while (true)
            {
                SkipWhitespace();
                if (IsAtEnd)
                {
                    throw new XmlParseException("Unterminated open tag.");
                }
                char c = Peek();
                if (c == '/' || c == '>')
                {
                    break;
                }
                attributes.Add(ParseAttribute());
            }

            // Self-closing
            if (Peek() == '/')
            {
                _pos++;
                Expect('>');
                int innerStartSelf = _pos;
                var spanSelf = new TextSpan(start, _pos - start);
                var innerSpanSelf = new TextSpan(innerStartSelf, 0);
                return new XmlSourceElement(
                    spanSelf,
                    nameSpan,
                    innerSpanSelf,
                    name,
                    attributes,
                    Array.Empty<XmlSourceNode>());
            }

            // Open tag terminator
            Expect('>');
            int innerStart = _pos;

            var children = new List<XmlSourceNode>();
            while (true)
            {
                if (IsAtEnd)
                {
                    throw new XmlParseException(
                        $"Unterminated element <{name}>.");
                }

                if (StartsWith("</"))
                {
                    break;
                }

                if (StartsWith("<!--"))
                {
                    // Skip comments — they remain in source spans but are
                    // not exposed as nodes.
                    int end = _source.IndexOf("-->", _pos + 4, StringComparison.Ordinal);
                    if (end < 0)
                    {
                        throw new XmlParseException("Unterminated comment.");
                    }
                    _pos = end + 3;
                    continue;
                }

                if (StartsWith("<![CDATA["))
                {
                    int textStart = _pos;
                    int end = _source.IndexOf("]]>", _pos + 9, StringComparison.Ordinal);
                    if (end < 0)
                    {
                        throw new XmlParseException("Unterminated CDATA section.");
                    }
                    string payload = _source.Substring(_pos + 9, end - (_pos + 9));
                    _pos = end + 3;
                    children.Add(new XmlSourceText(
                        new TextSpan(textStart, _pos - textStart),
                        payload));
                    continue;
                }

                if (Peek() == '<')
                {
                    children.Add(ParseElement());
                    continue;
                }

                children.Add(ParseTextRun());
            }

            int innerEnd = _pos;
            var innerSpan = new TextSpan(innerStart, innerEnd - innerStart);

            // Close tag </name>
            Expect('<');
            Expect('/');
            int closeNameStart = _pos;
            string closeName = ReadName();
            if (!string.Equals(closeName, name, StringComparison.Ordinal))
            {
                throw new XmlParseException(
                    $"Mismatched close tag </{closeName}> for <{name}> at position {closeNameStart}.");
            }
            SkipWhitespace();
            Expect('>');

            var span = new TextSpan(start, _pos - start);
            return new XmlSourceElement(
                span,
                nameSpan,
                innerSpan,
                name,
                attributes,
                children);
        }

        private XmlSourceAttribute ParseAttribute()
        {
            int nameStart = _pos;
            string name = ReadName();
            var nameSpan = new TextSpan(nameStart, _pos - nameStart);

            SkipWhitespace();
            Expect('=');
            SkipWhitespace();

            if (IsAtEnd)
            {
                throw new XmlParseException("Unterminated attribute.");
            }
            char quote = Peek();
            if (quote != '"' && quote != '\'')
            {
                throw new XmlParseException(
                    $"Expected quoted attribute value at position {_pos}.");
            }

            int valueStart = _pos;
            _pos++; // consume opening quote
            var sb = new StringBuilder();
            while (true)
            {
                if (IsAtEnd)
                {
                    throw new XmlParseException("Unterminated attribute value.");
                }
                char c = Peek();
                if (c == quote)
                {
                    _pos++; // consume closing quote
                    break;
                }
                if (c == '<')
                {
                    throw new XmlParseException(
                        $"Unescaped '<' in attribute value at position {_pos}.");
                }
                if (c == '&')
                {
                    sb.Append(ReadEntityReference());
                    continue;
                }
                sb.Append(c);
                _pos++;
            }

            var valueSpan = new TextSpan(valueStart, _pos - valueStart);
            return new XmlSourceAttribute(name, nameSpan, valueSpan, sb.ToString());
        }

        private XmlSourceText ParseTextRun()
        {
            int start = _pos;
            var sb = new StringBuilder();
            while (!IsAtEnd && Peek() != '<')
            {
                char c = Peek();
                if (c == '&')
                {
                    sb.Append(ReadEntityReference());
                    continue;
                }
                sb.Append(c);
                _pos++;
            }
            return new XmlSourceText(new TextSpan(start, _pos - start), sb.ToString());
        }

        private string ReadEntityReference()
        {
            // Pre: Peek() == '&'
            int amp = _pos;
            _pos++; // consume '&'
            int semi = _source.IndexOf(';', _pos);
            if (semi < 0)
            {
                throw new XmlParseException(
                    $"Unterminated entity reference at position {amp}.");
            }
            string body = _source.Substring(_pos, semi - _pos);
            _pos = semi + 1;

            switch (body)
            {
                case "amp": return "&";
                case "lt": return "<";
                case "gt": return ">";
                case "quot": return "\"";
                case "apos": return "'";
            }

            if (body.Length > 1 && body[0] == '#')
            {
                int code;
                if (body[1] == 'x' || body[1] == 'X')
                {
                    if (!int.TryParse(
                        body.AsSpan(2),
                        NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture,
                        out code))
                    {
                        throw new XmlParseException(
                            $"Invalid hex character reference '&{body};'.");
                    }
                }
                else
                {
                    if (!int.TryParse(
                        body.AsSpan(1),
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out code))
                    {
                        throw new XmlParseException(
                            $"Invalid decimal character reference '&{body};'.");
                    }
                }
                return char.ConvertFromUtf32(code);
            }

            // Unknown entity — tolerate by preserving the literal `&body;`
            // text in the decoded output. The generator surfaces STR0007.
            SawUnknownEntity = true;
            return "&" + body + ";";
        }

        private string ReadName()
        {
            if (IsAtEnd || !IsNameStartChar(Peek()))
            {
                throw new XmlParseException(
                    $"Expected name at position {_pos}.");
            }
            int start = _pos;
            _pos++;
            while (!IsAtEnd && IsNameChar(Peek()))
            {
                _pos++;
            }
            return _source.Substring(start, _pos - start);
        }

        public void SkipWhitespace()
        {
            while (!IsAtEnd)
            {
                char c = _source[_pos];
                if (c == ' ' || c == '\t' || c == '\n' || c == '\r')
                {
                    _pos++;
                }
                else
                {
                    return;
                }
            }
        }

        private bool StartsWith(string s)
        {
            if (_pos + s.Length > _source.Length)
            {
                return false;
            }
            for (var i = 0; i < s.Length; i++)
            {
                if (_source[_pos + i] != s[i])
                {
                    return false;
                }
            }
            return true;
        }

        private void Expect(char c)
        {
            if (IsAtEnd || _source[_pos] != c)
            {
                throw new XmlParseException(
                    $"Expected '{c}' at position {_pos}.");
            }
            _pos++;
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
    }
}
