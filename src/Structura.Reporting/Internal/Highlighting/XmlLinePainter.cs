namespace Structura.Reporting.Internal.Highlighting;

/// <summary>
/// Stateless per-line XML tokenizer. Output is a complete cover of the line.
/// Multi-line constructs (comments and CDATA bodies that span lines) are not
/// supported — an unclosed comment / CDATA on a line tokenizes that line
/// best-effort and stops; subsequent lines start fresh in outside-tag mode.
/// </summary>
internal sealed class XmlLinePainter : IDiffSyntaxPainter
{
    public static readonly XmlLinePainter Instance = new();

    private XmlLinePainter() { }

    private enum Mode { Outside, InsideTag, InsideComment }

    public IReadOnlyList<TokenRange> TokenizeLine(string content)
    {
        if (content.Length == 0)
        {
            return Array.Empty<TokenRange>();
        }

        var tokens = new List<TokenRange>();
        var mode = Mode.Outside;
        var i = 0;
        while (i < content.Length)
        {
            var tokenStart = i;
            var kind = mode switch
            {
                Mode.Outside       => ScanOutside(content, ref i, ref mode),
                Mode.InsideTag     => ScanInsideTag(content, ref i, ref mode),
                Mode.InsideComment => ScanInsideComment(content, ref i, ref mode),
                _                  => ScanOutside(content, ref i, ref mode),
            };
            var tokenLength = i - tokenStart;
            AppendToken(tokens, tokenStart, tokenLength, kind);
        }
        return tokens;
    }

    private static TokenKind ScanOutside(string content, ref int i, ref Mode mode)
    {
        char c = content[i];
        if (c == '<')
        {
            if (StartsWith(content, i, "<!--"))
            {
                i += 4;
                if (TryConsumeUntil(content, ref i, "-->"))
                {
                    return TokenKind.Comment;
                }
                mode = Mode.InsideComment;
                return TokenKind.Comment;
            }
            if (StartsWith(content, i, "<![CDATA["))
            {
                i += 9;
                return TokenKind.Punctuation;
            }
            if (StartsWith(content, i, "</") || StartsWith(content, i, "<?") || StartsWith(content, i, "<!"))
            {
                i += 2;
            }
            else
            {
                i++;
            }
            mode = Mode.InsideTag;
            return TokenKind.Punctuation;
        }
        if (c == ']' && StartsWith(content, i, "]]>"))
        {
            i += 3;
            return TokenKind.Punctuation;
        }
        if (c == '&')
        {
            int start = i;
            i++;
            while (i < content.Length && content[i] != ';' && !char.IsWhiteSpace(content[i]))
            {
                i++;
            }
            if (i < content.Length && content[i] == ';')
            {
                i++;
            }
            if (i > start + 1)
            {
                return TokenKind.EntityRef;
            }
            return TokenKind.Text;
        }
        i++;
        return TokenKind.Text;
    }

    private static TokenKind ScanInsideTag(string content, ref int i, ref Mode mode)
    {
        char c = content[i];
        if (char.IsWhiteSpace(c))
        {
            i++;
            return TokenKind.Punctuation;
        }
        if (c == '>')
        {
            i++;
            mode = Mode.Outside;
            return TokenKind.Punctuation;
        }
        if ((c == '/' || c == '?') && i + 1 < content.Length && content[i + 1] == '>')
        {
            i += 2;
            mode = Mode.Outside;
            return TokenKind.Punctuation;
        }
        if (c == '=')
        {
            i++;
            return TokenKind.Punctuation;
        }
        if (c == '"' || c == '\'')
        {
            char quote = c;
            i++;
            while (i < content.Length && content[i] != quote)
            {
                i++;
            }
            if (i < content.Length)
            {
                i++;
            }
            return TokenKind.AttrValue;
        }
        if (IsNameChar(c))
        {
            int start = i;
            while (i < content.Length && IsNameChar(content[i]))
            {
                i++;
            }
            return IsAtElementNamePosition(content, start) ? TokenKind.ElementName : TokenKind.AttrName;
        }
        i++;
        return TokenKind.Punctuation;
    }

    private static TokenKind ScanInsideComment(string content, ref int i, ref Mode mode)
    {
        if (TryConsumeUntil(content, ref i, "-->"))
        {
            mode = Mode.Outside;
        }
        else
        {
            i = content.Length;
        }
        return TokenKind.Comment;
    }

    private static bool StartsWith(string content, int i, string s)
    {
        if (i + s.Length > content.Length)
        {
            return false;
        }
        for (var k = 0; k < s.Length; k++)
        {
            if (content[i + k] != s[k])
            {
                return false;
            }
        }
        return true;
    }

    private static bool TryConsumeUntil(string content, ref int i, string terminator)
    {
        int found = content.IndexOf(terminator, i, StringComparison.Ordinal);
        if (found < 0)
        {
            i = content.Length;
            return false;
        }
        i = found + terminator.Length;
        return true;
    }

    private static bool IsNameChar(char c) =>
        !char.IsWhiteSpace(c) && c != '>' && c != '<' && c != '/' && c != '=' && c != '?' && c != '"' && c != '\'';

    private static bool IsAtElementNamePosition(string content, int identifierStart)
    {
        int j = identifierStart - 1;
        while (j >= 0 && char.IsWhiteSpace(content[j]))
        {
            j--;
        }
        if (j < 0)
        {
            return false;
        }
        if (content[j] == '<')
        {
            return true;
        }
        if (j > 0 && (content[j] == '/' || content[j] == '?' || content[j] == '!') && content[j - 1] == '<')
        {
            return true;
        }
        return false;
    }

    private static void AppendToken(List<TokenRange> tokens, int start, int length, TokenKind kind)
    {
        if (length <= 0)
        {
            return;
        }
        if (tokens.Count > 0)
        {
            TokenRange last = tokens[^1];
            if (last.Kind == kind && last.Range.End == start)
            {
                var coalescedRange = new ColumnRange(last.Range.Start, last.Range.Length + length);
                var coalesced = new TokenRange(coalescedRange, kind);
                tokens[^1] = coalesced;
                return;
            }
        }
        var range = new ColumnRange(start, length);
        var token = new TokenRange(range, kind);
        tokens.Add(token);
    }
}
