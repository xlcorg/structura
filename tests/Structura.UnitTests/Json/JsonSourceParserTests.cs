using FluentAssertions;

using Structura.Runtime;
using Structura.Runtime.Json;

using Xunit;

namespace Structura.UnitTests.Json;

public sealed class JsonSourceParserTests
{
    [Fact]
    public void Parse_String_PreservesLiteralSpanIncludingQuotes()
    {
        var src = "\"hello\"";
        var node = JsonSourceParser.Parse(src);

        var s = node.Should().BeOfType<JsonSourceString>().Which;
        s.Value.Should().Be("hello");
        s.Span.Should().Be(new TextSpan(0, src.Length));
    }

    [Fact]
    public void Parse_StringWithEscapes_DecodesValueAndCoversWholeLiteral()
    {
        var src = "\"a\\nb\\\"c\\u0041\"";
        var node = JsonSourceParser.Parse(src);

        var s = (JsonSourceString)node;
        s.Value.Should().Be("a\nb\"cA");
        s.Span.Should().Be(new TextSpan(0, src.Length));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-7")]
    [InlineData("3.14")]
    [InlineData("-0.5")]
    [InlineData("1e10")]
    [InlineData("1E+5")]
    [InlineData("-2.5e-3")]
    public void Parse_Number_PreservesOriginalLiteral(string literal)
    {
        var node = JsonSourceParser.Parse(literal);
        var n = node.Should().BeOfType<JsonSourceNumber>().Which;
        n.Literal.Should().Be(literal);
        n.Span.Should().Be(new TextSpan(0, literal.Length));
    }

    [Fact]
    public void Parse_TrueFalseNull()
    {
        JsonSourceParser.Parse("true").Should().BeOfType<JsonSourceBoolean>().Which.Value.Should().BeTrue();
        JsonSourceParser.Parse("false").Should().BeOfType<JsonSourceBoolean>().Which.Value.Should().BeFalse();
        JsonSourceParser.Parse("null").Should().BeOfType<JsonSourceNull>();
    }

    [Fact]
    public void Parse_Object_ExposesKeysAndValueSpans()
    {
        var src = "{ \"a\": 1, \"b\": \"x\" }";
        var root = (JsonSourceObject)JsonSourceParser.Parse(src);

        root.Properties.Should().HaveCount(2);

        var a = root.Properties[0];
        a.Name.Should().Be("a");
        src.Substring(a.ValueSpan.Start, a.ValueSpan.Length).Should().Be("1");

        var b = root.Properties[1];
        b.Name.Should().Be("b");
        src.Substring(b.ValueSpan.Start, b.ValueSpan.Length).Should().Be("\"x\"");
    }

    [Fact]
    public void Parse_NestedObject_HasCorrectInnerSpans()
    {
        var src = "{ \"outer\": { \"inner\": 42 } }";
        var root = (JsonSourceObject)JsonSourceParser.Parse(src);
        var outer = (JsonSourceObject)root.RequireProperty("outer").Value;
        var inner = (JsonSourceNumber)outer.RequireProperty("inner").Value;

        inner.Literal.Should().Be("42");
        src.Substring(inner.Span.Start, inner.Span.Length).Should().Be("42");
    }

    [Fact]
    public void Parse_Array_PreservesItemOrder()
    {
        var arr = (JsonSourceArray)JsonSourceParser.Parse("[1, 2, 3]");
        arr.Items.Select(static i => ((JsonSourceNumber)i).Literal).Should().Equal("1", "2", "3");
    }

    [Fact]
    public void Parse_EmptyObjectAndArray()
    {
        ((JsonSourceObject)JsonSourceParser.Parse("{}")).Properties.Should().BeEmpty();
        ((JsonSourceArray)JsonSourceParser.Parse("[]")).Items.Should().BeEmpty();
    }

    [Fact]
    public void Parse_LeadingAndTrailingWhitespace_IsAllowed()
    {
        var node = JsonSourceParser.Parse("\n  42  \n");
        ((JsonSourceNumber)node).Literal.Should().Be("42");
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("[")]
    [InlineData("{\"a\"}")]
    [InlineData("\"unterminated")]
    [InlineData("{\"a\": 1,}")]   // trailing comma
    [InlineData("[1, 2,]")]
    [InlineData("undefined")]
    [InlineData("01")]            // leading zero followed by digit not technically wrong by us but our parser allows; test other invalid
    [InlineData("1.")]            // missing fraction digits
    [InlineData("1e")]            // missing exponent digits
    [InlineData("\"a\"\"b\"")]    // two roots
    public void Parse_InvalidJson_Throws(string input)
    {
        var act = () => JsonSourceParser.Parse(input);
        act.Should().Throw<JsonParseException>();
    }

    [Fact]
    public void Parse_RequireProperty_ThrowsWhenMissing()
    {
        var root = (JsonSourceObject)JsonSourceParser.Parse("{\"a\":1}");
        var act = () => root.RequireProperty("missing");
        act.Should().Throw<InvalidOperationException>();
    }
}
