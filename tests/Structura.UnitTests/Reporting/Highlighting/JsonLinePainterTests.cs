using FluentAssertions;

using Structura.Reporting.Internal.Highlighting;

using Xunit;

namespace Structura.UnitTests.Reporting.Highlighting;

public sealed class JsonLinePainterTests
{
    private static readonly JsonLinePainter Painter = JsonLinePainter.Instance;

    private static void AssertCover(string content, IReadOnlyList<TokenRange> tokens)
    {
        if (content.Length == 0)
        {
            tokens.Should().BeEmpty();
            return;
        }

        tokens.Should().NotBeEmpty();
        tokens[0].Range.Start.Should().Be(0, "cover must start at column 0");

        for (var i = 1; i < tokens.Count; i++)
        {
            tokens[i].Range.Start.Should().Be(
                tokens[i - 1].Range.End,
                "cover must be contiguous (token {0} starts where {1} ended)", i, i - 1);
        }

        tokens[^1].Range.End.Should().Be(
            content.Length,
            "cover must end at content.Length");
    }

    [Fact]
    public void TokenizeLine_KeyValuePair_ClassifiesQuotedNameAsKey()
    {
        const string content = "  \"order_id\": \"ORD-1\",";

        IReadOnlyList<TokenRange> tokens = Painter.TokenizeLine(content);

        AssertCover(content, tokens);
        TokenRange[] kindOrder = tokens.Where(t => t.Kind != TokenKind.Punctuation).ToArray();
        kindOrder.Should().HaveCount(2);
        kindOrder[0].Kind.Should().Be(TokenKind.Key);
        kindOrder[1].Kind.Should().Be(TokenKind.String);

        string keySlice = content.Substring(kindOrder[0].Range.Start, kindOrder[0].Range.Length);
        keySlice.Should().Be("\"order_id\"");
        string valSlice = content.Substring(kindOrder[1].Range.Start, kindOrder[1].Range.Length);
        valSlice.Should().Be("\"ORD-1\"");
    }

    [Fact]
    public void TokenizeLine_NumericValue_IsNumber()
    {
        const string content = "  \"version\": 7,";

        IReadOnlyList<TokenRange> tokens = Painter.TokenizeLine(content);

        AssertCover(content, tokens);
        TokenRange number = tokens.Single(t => t.Kind == TokenKind.Number);
        string slice = content.Substring(number.Range.Start, number.Range.Length);
        slice.Should().Be("7");
    }

    [Fact]
    public void TokenizeLine_BoolKeyword_IsKeyword()
    {
        const string content = "  \"is_priority\": true,";

        IReadOnlyList<TokenRange> tokens = Painter.TokenizeLine(content);

        AssertCover(content, tokens);
        TokenRange keyword = tokens.Single(t => t.Kind == TokenKind.Keyword);
        string slice = content.Substring(keyword.Range.Start, keyword.Range.Length);
        slice.Should().Be("true");
    }

    [Fact]
    public void TokenizeLine_NullKeyword_IsKeyword()
    {
        const string content = "  \"middle_name\": null,";

        IReadOnlyList<TokenRange> tokens = Painter.TokenizeLine(content);

        AssertCover(content, tokens);
        TokenRange keyword = tokens.Single(t => t.Kind == TokenKind.Keyword);
        string slice = content.Substring(keyword.Range.Start, keyword.Range.Length);
        slice.Should().Be("null");
    }

    [Fact]
    public void TokenizeLine_NegativeFloat_IsNumber()
    {
        const string content = "  \"risk_score\": -1.25e+3,";

        IReadOnlyList<TokenRange> tokens = Painter.TokenizeLine(content);

        AssertCover(content, tokens);
        TokenRange number = tokens.Single(t => t.Kind == TokenKind.Number);
        string slice = content.Substring(number.Range.Start, number.Range.Length);
        slice.Should().Be("-1.25e+3");
    }

    [Fact]
    public void TokenizeLine_StringValueWithoutFollowingColon_IsString()
    {
        const string content = "  \"electronics\",";

        IReadOnlyList<TokenRange> tokens = Painter.TokenizeLine(content);

        AssertCover(content, tokens);
        tokens.Should().NotContain(t => t.Kind == TokenKind.Key);
        TokenRange str = tokens.Single(t => t.Kind == TokenKind.String);
        string slice = content.Substring(str.Range.Start, str.Range.Length);
        slice.Should().Be("\"electronics\"");
    }

    [Fact]
    public void TokenizeLine_UnterminatedString_TokenCoversTail()
    {
        const string content = "  \"unterminated";

        IReadOnlyList<TokenRange> tokens = Painter.TokenizeLine(content);

        AssertCover(content, tokens);
        TokenRange tail = tokens.Last();
        tail.Kind.Should().Be(TokenKind.String);
        tail.Range.End.Should().Be(content.Length);
    }

    [Fact]
    public void TokenizeLine_TrueWithSuffix_IsNotKeyword()
    {
        const string content = "trueish";

        IReadOnlyList<TokenRange> tokens = Painter.TokenizeLine(content);

        AssertCover(content, tokens);
        tokens.Should().NotContain(t => t.Kind == TokenKind.Keyword);
    }

    [Fact]
    public void TokenizeLine_EmptyContent_ReturnsEmpty()
    {
        IReadOnlyList<TokenRange> tokens = Painter.TokenizeLine(string.Empty);

        tokens.Should().BeEmpty();
    }

    [Fact]
    public void TokenizeLine_PunctuationOnly_IsCovered()
    {
        const string content = "{},[]:";

        IReadOnlyList<TokenRange> tokens = Painter.TokenizeLine(content);

        AssertCover(content, tokens);
        tokens.Should().AllSatisfy(t => t.Kind.Should().Be(TokenKind.Punctuation));
    }
}
