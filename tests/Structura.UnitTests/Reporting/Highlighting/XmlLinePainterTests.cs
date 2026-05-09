using FluentAssertions;

using Structura.Reporting.Internal.Highlighting;

using Xunit;

namespace Structura.UnitTests.Reporting.Highlighting;

public sealed class XmlLinePainterTests
{
    private static readonly XmlLinePainter Painter = XmlLinePainter.Instance;

    private static void AssertCover(string content, IReadOnlyList<TokenRange> tokens)
    {
        if (content.Length == 0)
        {
            tokens.Should().BeEmpty();
            return;
        }

        tokens.Should().NotBeEmpty();
        tokens[0].Range.Start.Should().Be(0);
        for (var i = 1; i < tokens.Count; i++)
        {
            tokens[i].Range.Start.Should().Be(tokens[i - 1].Range.End);
        }
        tokens[^1].Range.End.Should().Be(content.Length);
    }

    private static string SliceOf(string content, TokenRange t) =>
        content.Substring(t.Range.Start, t.Range.Length);

    [Fact]
    public void TokenizeLine_OpenTagWithAttributes_ClassifiesNameAndAttrs()
    {
        const string content = "<book id=\"b001\" available=\"true\">";

        IReadOnlyList<TokenRange> tokens = Painter.TokenizeLine(content);

        AssertCover(content, tokens);
        TokenRange[] meaningful = tokens.Where(t => t.Kind != TokenKind.Punctuation).ToArray();
        meaningful.Should().HaveCount(5);
        meaningful[0].Kind.Should().Be(TokenKind.ElementName);
        SliceOf(content, meaningful[0]).Should().Be("book");
        meaningful[1].Kind.Should().Be(TokenKind.AttrName);
        SliceOf(content, meaningful[1]).Should().Be("id");
        meaningful[2].Kind.Should().Be(TokenKind.AttrValue);
        SliceOf(content, meaningful[2]).Should().Be("\"b001\"");
        meaningful[3].Kind.Should().Be(TokenKind.AttrName);
        SliceOf(content, meaningful[3]).Should().Be("available");
        meaningful[4].Kind.Should().Be(TokenKind.AttrValue);
        SliceOf(content, meaningful[4]).Should().Be("\"true\"");
    }

    [Fact]
    public void TokenizeLine_NamespacedElement_ElementNameIncludesPrefix()
    {
        const string content = "<meta:info>";

        IReadOnlyList<TokenRange> tokens = Painter.TokenizeLine(content);

        AssertCover(content, tokens);
        TokenRange element = tokens.Single(t => t.Kind == TokenKind.ElementName);
        SliceOf(content, element).Should().Be("meta:info");
    }

    [Fact]
    public void TokenizeLine_SelfClosing_RecognizesSlashGt()
    {
        const string content = "<out-of-stock/>";

        IReadOnlyList<TokenRange> tokens = Painter.TokenizeLine(content);

        AssertCover(content, tokens);
        TokenRange element = tokens.Single(t => t.Kind == TokenKind.ElementName);
        SliceOf(content, element).Should().Be("out-of-stock");
    }

    [Fact]
    public void TokenizeLine_ClosingTag_ClassifiesElementName()
    {
        const string content = "</author>";

        IReadOnlyList<TokenRange> tokens = Painter.TokenizeLine(content);

        AssertCover(content, tokens);
        TokenRange element = tokens.Single(t => t.Kind == TokenKind.ElementName);
        SliceOf(content, element).Should().Be("author");
    }

    [Fact]
    public void TokenizeLine_Comment_WholeLineIsComment()
    {
        const string content = "<!-- hello -->";

        IReadOnlyList<TokenRange> tokens = Painter.TokenizeLine(content);

        AssertCover(content, tokens);
        tokens.Should().HaveCount(1);
        tokens[0].Kind.Should().Be(TokenKind.Comment);
    }

    [Fact]
    public void TokenizeLine_OpenCommentOnly_TailIsComment()
    {
        const string content = "<!-- hi";

        IReadOnlyList<TokenRange> tokens = Painter.TokenizeLine(content);

        AssertCover(content, tokens);
        tokens.Last().Kind.Should().Be(TokenKind.Comment);
    }

    [Fact]
    public void TokenizeLine_EntityRefInText_IsEntityRef()
    {
        const string content = "x &amp; y";

        IReadOnlyList<TokenRange> tokens = Painter.TokenizeLine(content);

        AssertCover(content, tokens);
        TokenRange entity = tokens.Single(t => t.Kind == TokenKind.EntityRef);
        SliceOf(content, entity).Should().Be("&amp;");
    }

    [Fact]
    public void TokenizeLine_SingleLineCData_BodyIsText()
    {
        const string content = "<![CDATA[ payload ]]>";

        IReadOnlyList<TokenRange> tokens = Painter.TokenizeLine(content);

        AssertCover(content, tokens);
        TokenRange text = tokens.Single(t => t.Kind == TokenKind.Text);
        SliceOf(content, text).Should().Be(" payload ");
    }

    [Fact]
    public void TokenizeLine_OpenCDataOnly_RestIsText()
    {
        const string content = "<![CDATA[ start";

        IReadOnlyList<TokenRange> tokens = Painter.TokenizeLine(content);

        AssertCover(content, tokens);
        tokens.Last().Kind.Should().Be(TokenKind.Text);
    }

    [Fact]
    public void TokenizeLine_AttributeWithSingleQuotes_IsAttrValue()
    {
        const string content = "<a href='url'/>";

        IReadOnlyList<TokenRange> tokens = Painter.TokenizeLine(content);

        AssertCover(content, tokens);
        TokenRange attr = tokens.Single(t => t.Kind == TokenKind.AttrValue);
        SliceOf(content, attr).Should().Be("'url'");
    }

    [Fact]
    public void TokenizeLine_NonAsciiElementName_IsAccepted()
    {
        const string content = "<книга/>";

        IReadOnlyList<TokenRange> tokens = Painter.TokenizeLine(content);

        AssertCover(content, tokens);
        TokenRange element = tokens.Single(t => t.Kind == TokenKind.ElementName);
        SliceOf(content, element).Should().Be("книга");
    }

    [Fact]
    public void TokenizeLine_EmptyContent_ReturnsEmpty()
    {
        IReadOnlyList<TokenRange> tokens = Painter.TokenizeLine(string.Empty);

        tokens.Should().BeEmpty();
    }
}
