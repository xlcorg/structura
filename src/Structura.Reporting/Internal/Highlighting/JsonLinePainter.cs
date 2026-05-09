namespace Structura.Reporting.Internal.Highlighting;

/// <summary>
/// Stateless per-line JSON tokenizer. Output is a complete cover of the line
/// using <see cref="TokenKind.Key"/>, <see cref="TokenKind.String"/>,
/// <see cref="TokenKind.Number"/>, <see cref="TokenKind.Keyword"/>, and
/// <see cref="TokenKind.Punctuation"/>. Multi-line JSON strings are not
/// supported — an unterminated string at end of line produces a single
/// <see cref="TokenKind.String"/> token covering everything up to the line's end.
/// </summary>
internal sealed class JsonLinePainter : IDiffSyntaxPainter
{
    public static readonly JsonLinePainter Instance = new();

    private JsonLinePainter() { }

    public IReadOnlyList<TokenRange> TokenizeLine(string content)
    {
        if (content.Length == 0)
        {
            return Array.Empty<TokenRange>();
        }

        var tokens = new List<TokenRange>();
        var i = 0;
        while (i < content.Length)
        {
            var tokenStart = i;
            var kind = ScanNext(content, ref i);
            var tokenLength = i - tokenStart;
            AppendToken(tokens, tokenStart, tokenLength, kind);
        }
        return tokens;
    }

    private static TokenKind ScanNext(string content, ref int i)
    {
        char c = content[i];
        if (c == '"')
        {
            return ScanString(content, ref i);
        }
        if (c == '-' || c == '+' || (c >= '0' && c <= '9'))
        {
            return ScanNumber(content, ref i);
        }
        if (IsKeywordStart(c))
        {
            TokenKind? kw = TryScanKeyword(content, ref i);
            if (kw.HasValue)
            {
                return kw.Value;
            }
        }
        i++;
        return TokenKind.Punctuation;
    }

    private static TokenKind ScanString(string content, ref int i)
    {
        i++;
        while (i < content.Length)
        {
            char c = content[i];
            if (c == '\\' && i + 1 < content.Length)
            {
                i += 2;
                continue;
            }
            i++;
            if (c == '"')
            {
                break;
            }
        }
        return ClassifyString(content, i);
    }

    private static TokenKind ClassifyString(string content, int afterStringEnd)
    {
        for (int j = afterStringEnd; j < content.Length; j++)
        {
            char c = content[j];
            if (c == ' ' || c == '\t')
            {
                continue;
            }
            return c == ':' ? TokenKind.Key : TokenKind.String;
        }
        return TokenKind.String;
    }

    private static TokenKind ScanNumber(string content, ref int i)
    {
        if (content[i] == '-' || content[i] == '+')
        {
            i++;
        }
        while (i < content.Length && content[i] >= '0' && content[i] <= '9')
        {
            i++;
        }
        if (i < content.Length && content[i] == '.')
        {
            i++;
            while (i < content.Length && content[i] >= '0' && content[i] <= '9')
            {
                i++;
            }
        }
        if (i < content.Length && (content[i] == 'e' || content[i] == 'E'))
        {
            i++;
            if (i < content.Length && (content[i] == '+' || content[i] == '-'))
            {
                i++;
            }
            while (i < content.Length && content[i] >= '0' && content[i] <= '9')
            {
                i++;
            }
        }
        return TokenKind.Number;
    }

    private static bool IsKeywordStart(char c) =>
        c == 't' || c == 'f' || c == 'n';

    private static TokenKind? TryScanKeyword(string content, ref int i)
    {
        if (HasWord(content, i, "true") && IsBoundaryAfter(content, i + 4))
        {
            i += 4;
            return TokenKind.Keyword;
        }
        if (HasWord(content, i, "false") && IsBoundaryAfter(content, i + 5))
        {
            i += 5;
            return TokenKind.Keyword;
        }
        if (HasWord(content, i, "null") && IsBoundaryAfter(content, i + 4))
        {
            i += 4;
            return TokenKind.Keyword;
        }
        return null;
    }

    private static bool HasWord(string content, int start, string word)
    {
        if (start + word.Length > content.Length)
        {
            return false;
        }
        for (var k = 0; k < word.Length; k++)
        {
            if (content[start + k] != word[k])
            {
                return false;
            }
        }
        return true;
    }

    private static bool IsBoundaryAfter(string content, int index)
    {
        if (index >= content.Length)
        {
            return true;
        }
        char c = content[index];
        return !((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_');
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
