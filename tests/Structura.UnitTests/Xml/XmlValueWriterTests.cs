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

    [Fact]
    public void WriteInt64_FormatsInvariant()
    {
        XmlValueWriter.WriteInt64(42).Should().Be("42");
        XmlValueWriter.WriteInt64(-7).Should().Be("-7");
        XmlValueWriter.WriteInt64(long.MaxValue).Should().Be("9223372036854775807");
    }

    [Fact]
    public void WriteDecimal_PreservesScale()
    {
        XmlValueWriter.WriteDecimal(15499.95m).Should().Be("15499.95");
    }

    [Fact]
    public void WriteBoolean_LowercaseLiterals()
    {
        XmlValueWriter.WriteBoolean(true).Should().Be("true");
        XmlValueWriter.WriteBoolean(false).Should().Be("false");
    }
}
