using FluentAssertions;

using Structura.Runtime;
using Structura.Runtime.Xml;

using Xunit;

namespace Structura.UnitTests.Xml;

public sealed class XmlSourceParserTests
{
    [Fact]
    public void Parse_ElementWithTextContent_PreservesInnerSpan()
    {
        const string Src = "<currency>RUB</currency>";

        XmlSourceElement el = XmlSourceParser.Parse(Src);

        el.Name.Should().Be("currency");
        el.IsPureText.Should().BeTrue();
        Src.Substring(el.InnerSpan.Start, el.InnerSpan.Length).Should().Be("RUB");
        Src.Substring(el.Span.Start, el.Span.Length).Should().Be(Src);
    }

    [Fact]
    public void Parse_SelfClosingElement_HasZeroLengthInnerSpan()
    {
        const string Src = "<empty/>";

        XmlSourceElement el = XmlSourceParser.Parse(Src);

        el.Children.Should().BeEmpty();
        el.InnerSpan.Length.Should().Be(0);
        el.Span.Length.Should().Be(Src.Length);
    }

    [Fact]
    public void Parse_NestedElements_ExposeChildrenAndCorrectSpans()
    {
        const string Src = "<order><currency>RUB</currency><version>7</version></order>";

        XmlSourceElement root = XmlSourceParser.Parse(Src);

        root.Name.Should().Be("order");
        root.Children.OfType<XmlSourceElement>().Should().HaveCount(2);

        XmlSourceElement currency = root.RequireElement("currency");
        Src.Substring(currency.InnerSpan.Start, currency.InnerSpan.Length).Should().Be("RUB");

        XmlSourceElement version = root.RequireElement("version");
        Src.Substring(version.InnerSpan.Start, version.InnerSpan.Length).Should().Be("7");
    }

    [Fact]
    public void Parse_AttributesWithBothQuoteStyles()
    {
        const string Src = "<order id=\"42\" status='paid'/>";

        XmlSourceElement el = XmlSourceParser.Parse(Src);

        el.Attributes.Should().HaveCount(2);

        XmlSourceAttribute id = el.RequireAttribute("id");
        id.Value.Should().Be("42");
        Src.Substring(id.ValueSpan.Start, id.ValueSpan.Length).Should().Be("\"42\"");

        XmlSourceAttribute status = el.RequireAttribute("status");
        status.Value.Should().Be("paid");
        Src.Substring(status.ValueSpan.Start, status.ValueSpan.Length).Should().Be("'paid'");
    }

    [Theory]
    [InlineData("<x>a&amp;b</x>", "a&b")]
    [InlineData("<x>a&lt;b</x>", "a<b")]
    [InlineData("<x>a&gt;b</x>", "a>b")]
    [InlineData("<x>&quot;ok&quot;</x>", "\"ok\"")]
    [InlineData("<x>&apos;ok&apos;</x>", "'ok'")]
    [InlineData("<x>&#65;</x>", "A")]
    [InlineData("<x>&#x41;</x>", "A")]
    public void Parse_DecodesEntityReferences(string source, string expectedValue)
    {
        XmlSourceElement el = XmlSourceParser.Parse(source);
        ((XmlSourceText)el.Children[0]).Value.Should().Be(expectedValue);
    }

    [Fact]
    public void Parse_PreservesXmlDeclaration_RootSpanStartsAfterIt()
    {
        const string Src = "<?xml version=\"1.0\"?>\n<root/>";

        XmlSourceElement root = XmlSourceParser.Parse(Src);

        root.Span.Start.Should().Be(Src.IndexOf("<root", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_SkipsCommentsAndWhitespace()
    {
        const string Src = "<!-- intro -->\n<root>\n  <a>1</a>\n  <!-- between -->\n  <b>2</b>\n</root>";

        XmlSourceElement root = XmlSourceParser.Parse(Src);

        root.RequireElement("a");
        root.RequireElement("b");
    }

    [Fact]
    public void Parse_CdataSection_ExposesRawPayloadAsText()
    {
        const string Src = "<x><![CDATA[<<raw & stuff>>]]></x>";

        XmlSourceElement el = XmlSourceParser.Parse(Src);

        el.Children.Should().HaveCount(1);
        ((XmlSourceText)el.Children[0]).Value.Should().Be("<<raw & stuff>>");
    }

    [Theory]
    [InlineData("")]
    [InlineData("<")]
    [InlineData("<root>")]
    [InlineData("<root></mismatch>")]
    [InlineData("<root attr=>")]
    [InlineData("<root attr=\"unterminated>")]
    [InlineData("<root></root><extra/>")]
    public void Parse_InvalidXml_Throws(string input)
    {
        Action act = () => XmlSourceParser.Parse(input);
        act.Should().Throw<XmlParseException>();
    }

    [Fact]
    public void Parse_DocType_Skipped_RootSpanStartsAfterIt()
    {
        const string Src =
            "<?xml version=\"1.0\"?>\n" +
            "<!DOCTYPE library [\n" +
            "    <!ENTITY company \"TestCorp\">\n" +
            "]>\n" +
            "<library/>";

        XmlSourceElement root = XmlSourceParser.Parse(Src);

        root.Name.Should().Be("library");
        root.Span.Start.Should().Be(Src.IndexOf("<library", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_UnknownEntityReference_PreservedAsLiteralText()
    {
        const string Src = "<x>Hello &company; World</x>";

        XmlSourceElement el = XmlSourceParser.Parse(Src);

        XmlSourceText text = (XmlSourceText)el.Children[0];
        text.Value.Should().Be("Hello &company; World");
    }

    [Fact]
    public void RequireElement_ThrowsWhenMissing()
    {
        XmlSourceElement root = XmlSourceParser.Parse("<root><a>1</a></root>");
        Action act = () => root.RequireElement("missing");
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void RequireAttribute_ThrowsWhenMissing()
    {
        XmlSourceElement root = XmlSourceParser.Parse("<root id=\"1\"/>");
        Action act = () => root.RequireAttribute("missing");
        act.Should().Throw<InvalidOperationException>();
    }
}
