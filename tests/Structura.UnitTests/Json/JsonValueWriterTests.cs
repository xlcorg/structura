using FluentAssertions;

using Structura.Runtime.Json;

using Xunit;

namespace Structura.UnitTests.Json;

public sealed class JsonValueWriterTests
{
    [Fact]
    public void WriteString_QuotesAndEscapesValue()
    {
        JsonValueWriter.WriteString("hello").Should().Be("\"hello\"");
    }

    [Theory]
    [InlineData("\"", "\"\\\"\"")]
    [InlineData("\\", "\"\\\\\"")]
    [InlineData("\b", "\"\\b\"")]
    [InlineData("\f", "\"\\f\"")]
    [InlineData("\n", "\"\\n\"")]
    [InlineData("\r", "\"\\r\"")]
    [InlineData("\t", "\"\\t\"")]
    public void WriteString_KnownEscapes(string input, string expected)
    {
        JsonValueWriter.WriteString(input).Should().Be(expected);
    }

    [Fact]
    public void WriteString_OtherControlChars_EmitUnicodeEscape()
    {
        JsonValueWriter.WriteString("").Should().Be("\"\\u0001\"");
        JsonValueWriter.WriteString("").Should().Be("\"\\u001F\"");
    }

    [Fact]
    public void WriteString_Null_EmitsNullLiteral()
    {
        JsonValueWriter.WriteString(null).Should().Be(JsonValueWriter.NullLiteral);
    }

    [Fact]
    public void WriteInt64_FormatsInvariant()
    {
        JsonValueWriter.WriteInt64(42).Should().Be("42");
        JsonValueWriter.WriteInt64(-7).Should().Be("-7");
        JsonValueWriter.WriteInt64(long.MaxValue).Should().Be("9223372036854775807");
    }

    [Fact]
    public void WriteBoolean()
    {
        JsonValueWriter.WriteBoolean(true).Should().Be("true");
        JsonValueWriter.WriteBoolean(false).Should().Be("false");
    }

    [Fact]
    public void WriteDouble_RoundTripFormat()
    {
        JsonValueWriter.WriteDouble(3.14).Should().Be("3.14");
        JsonValueWriter.WriteDouble(-0.5).Should().Be("-0.5");
    }

    [Fact]
    public void WriteDecimal_PreservesScale()
    {
        JsonValueWriter.WriteDecimal(15499.95m).Should().Be("15499.95");
    }
}
