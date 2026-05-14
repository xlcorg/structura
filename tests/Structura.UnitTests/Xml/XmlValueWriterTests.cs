using FluentAssertions;

using Structura.Runtime.Xml;

using Xunit;

namespace Structura.UnitTests.Xml;

public sealed class XmlValueWriterTests
{
    [Theory]
    [InlineData("hello", "hello")]
    [InlineData("a&b", "a&amp;b")]
    [InlineData("a<b", "a&lt;b")]
    [InlineData("a>b", "a&gt;b")]
    [InlineData("&<>", "&amp;&lt;&gt;")]
    public void WriteElementText_EscapesAndPassesThrough(string input, string expected)
    {
        XmlValueWriter.WriteElementText(input).Should().Be(expected);
    }

    [Fact]
    public void WriteElementText_NullOrEmpty_ReturnsEmpty()
    {
        XmlValueWriter.WriteElementText(null).Should().BeEmpty();
        XmlValueWriter.WriteElementText(string.Empty).Should().BeEmpty();
    }

    [Theory]
    [InlineData("hello", "\"hello\"")]
    [InlineData("a\"b", "\"a&quot;b\"")]
    [InlineData("a&b", "\"a&amp;b\"")]
    [InlineData("a<b", "\"a&lt;b\"")]
    public void WriteAttributeValue_WrapsAndEscapes(string input, string expected)
    {
        XmlValueWriter.WriteAttributeValue(input).Should().Be(expected);
    }

    [Fact]
    public void WriteAttributeValue_NullOrEmpty_ReturnsEmptyQuotedLiteral()
    {
        XmlValueWriter.WriteAttributeValue(null).Should().Be("\"\"");
        XmlValueWriter.WriteAttributeValue(string.Empty).Should().Be("\"\"");
    }
}
