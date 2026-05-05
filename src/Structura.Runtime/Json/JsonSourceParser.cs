using System.Globalization;
using System.Text;

namespace Structura.Runtime.Json;

/// <summary>
/// Hand-written recursive-descent parser for strict JSON. Produces a
/// <see cref="JsonSourceNode"/> tree where every node carries the
/// <see cref="TextSpan"/> from the original source. No comments,
/// no trailing commas, no unquoted keys.
/// </summary>
public static class JsonSourceParser
{
    public static JsonSourceNode Parse(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var state = new ParserState(source);
        state.SkipWhitespace();
        var root = state.ParseValue();
        state.SkipWhitespace();
        if (!state.IsAtEnd)
        {
            throw new JsonParseException(
                $"Unexpected content after root value at position {state.Position}.");
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

        public JsonSourceNode ParseValue()
        {
            SkipWhitespace();
            if (IsAtEnd)
            {
                throw new JsonParseException("Unexpected end of input.");
            }
            var c = _source[_pos];
            return c switch
            {
                '{' => ParseObject(),
                '[' => ParseArray(),
                '"' => ParseString(),
                't' or 'f' => ParseBoolean(),
                'n' => ParseNull(),
                '-' => ParseNumber(),
                _ when char.IsDigit(c) => ParseNumber(),
                _ => throw new JsonParseException(
                    $"Unexpected character '{c}' at position {_pos}."),
            };
        }

        private JsonSourceObject ParseObject()
        {
            var start = _pos;
            Expect('{');
            SkipWhitespace();
            var properties = new List<JsonSourceProperty>();
            if (!IsAtEnd && _source[_pos] == '}')
            {
                _pos++;
                return new JsonSourceObject(new TextSpan(start, _pos - start), properties);
            }
            while (true)
            {
                SkipWhitespace();
                if (IsAtEnd || _source[_pos] != '"')
                {
                    throw new JsonParseException(
                        $"Expected string key at position {_pos}.");
                }
                var key = ParseString();
                SkipWhitespace();
                Expect(':');
                SkipWhitespace();
                var value = ParseValue();
                properties.Add(new JsonSourceProperty(key.Value, key.Span, value.Span, value));
                SkipWhitespace();
                if (IsAtEnd)
                {
                    throw new JsonParseException("Unterminated object.");
                }
                var c = _source[_pos];
                if (c == ',')
                {
                    _pos++;
                    continue;
                }
                if (c == '}')
                {
                    _pos++;
                    break;
                }
                throw new JsonParseException(
                    $"Expected ',' or '}}' in object at position {_pos}.");
            }
            return new JsonSourceObject(new TextSpan(start, _pos - start), properties);
        }

        private JsonSourceArray ParseArray()
        {
            var start = _pos;
            Expect('[');
            SkipWhitespace();
            var items = new List<JsonSourceNode>();
            if (!IsAtEnd && _source[_pos] == ']')
            {
                _pos++;
                return new JsonSourceArray(new TextSpan(start, _pos - start), items);
            }
            while (true)
            {
                SkipWhitespace();
                items.Add(ParseValue());
                SkipWhitespace();
                if (IsAtEnd)
                {
                    throw new JsonParseException("Unterminated array.");
                }
                var c = _source[_pos];
                if (c == ',')
                {
                    _pos++;
                    continue;
                }
                if (c == ']')
                {
                    _pos++;
                    break;
                }
                throw new JsonParseException(
                    $"Expected ',' or ']' in array at position {_pos}.");
            }
            return new JsonSourceArray(new TextSpan(start, _pos - start), items);
        }

        private JsonSourceString ParseString()
        {
            var start = _pos;
            Expect('"');
            var sb = new StringBuilder();
            while (true)
            {
                if (IsAtEnd)
                {
                    throw new JsonParseException("Unterminated string literal.");
                }
                var c = _source[_pos];
                if (c == '"')
                {
                    _pos++;
                    return new JsonSourceString(new TextSpan(start, _pos - start), sb.ToString());
                }
                if (c == '\\')
                {
                    _pos++;
                    if (IsAtEnd)
                    {
                        throw new JsonParseException("Unterminated escape sequence.");
                    }
                    var esc = _source[_pos];
                    switch (esc)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (_pos + 4 >= _source.Length)
                            {
                                throw new JsonParseException("Truncated \\u escape sequence.");
                            }
                            var hex = _source.Substring(_pos + 1, 4);
                            if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                            {
                                throw new JsonParseException($"Invalid \\u escape '{hex}'.");
                            }
                            sb.Append((char)code);
                            _pos += 4;
                            break;
                        default:
                            throw new JsonParseException($"Invalid escape '\\{esc}' at position {_pos - 1}.");
                    }
                    _pos++;
                }
                else if (c < 0x20)
                {
                    throw new JsonParseException(
                        $"Unescaped control character U+{(int)c:X4} at position {_pos}.");
                }
                else
                {
                    sb.Append(c);
                    _pos++;
                }
            }
        }

        private JsonSourceNumber ParseNumber()
        {
            var start = _pos;
            if (_source[_pos] == '-')
            {
                _pos++;
            }
            if (IsAtEnd || !char.IsDigit(_source[_pos]))
            {
                throw new JsonParseException($"Expected digit at position {_pos}.");
            }
            if (_source[_pos] == '0')
            {
                _pos++;
                if (!IsAtEnd && char.IsDigit(_source[_pos]))
                {
                    throw new JsonParseException(
                        $"Leading zeros are not allowed in JSON numbers at position {_pos - 1}.");
                }
            }
            else
            {
                while (!IsAtEnd && char.IsDigit(_source[_pos]))
                {
                    _pos++;
                }
            }
            if (!IsAtEnd && _source[_pos] == '.')
            {
                _pos++;
                if (IsAtEnd || !char.IsDigit(_source[_pos]))
                {
                    throw new JsonParseException($"Expected fractional digits at position {_pos}.");
                }
                while (!IsAtEnd && char.IsDigit(_source[_pos]))
                {
                    _pos++;
                }
            }
            if (!IsAtEnd && (_source[_pos] == 'e' || _source[_pos] == 'E'))
            {
                _pos++;
                if (!IsAtEnd && (_source[_pos] == '+' || _source[_pos] == '-'))
                {
                    _pos++;
                }
                if (IsAtEnd || !char.IsDigit(_source[_pos]))
                {
                    throw new JsonParseException($"Expected exponent digits at position {_pos}.");
                }
                while (!IsAtEnd && char.IsDigit(_source[_pos]))
                {
                    _pos++;
                }
            }
            var span = new TextSpan(start, _pos - start);
            return new JsonSourceNumber(span, _source.Substring(start, span.Length));
        }

        private JsonSourceBoolean ParseBoolean()
        {
            var start = _pos;
            if (Match("true"))
            {
                return new JsonSourceBoolean(new TextSpan(start, 4), true);
            }
            if (Match("false"))
            {
                return new JsonSourceBoolean(new TextSpan(start, 5), false);
            }
            throw new JsonParseException($"Expected 'true' or 'false' at position {start}.");
        }

        private JsonSourceNull ParseNull()
        {
            var start = _pos;
            if (Match("null"))
            {
                return new JsonSourceNull(new TextSpan(start, 4));
            }
            throw new JsonParseException($"Expected 'null' at position {start}.");
        }

        private bool Match(string token)
        {
            if (_pos + token.Length > _source.Length)
            {
                return false;
            }
            for (var i = 0; i < token.Length; i++)
            {
                if (_source[_pos + i] != token[i])
                {
                    return false;
                }
            }
            _pos += token.Length;
            return true;
        }

        private void Expect(char c)
        {
            if (IsAtEnd || _source[_pos] != c)
            {
                throw new JsonParseException($"Expected '{c}' at position {_pos}.");
            }
            _pos++;
        }

        public void SkipWhitespace()
        {
            while (!IsAtEnd)
            {
                var c = _source[_pos];
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
    }
}
