using FluentAssertions;

using Structura.Generator;

using Xunit;

namespace Structura.UnitTests.Generator;

public sealed class JsonPointerEscaperTests
{
    [Theory]
    [InlineData("currency",       "/currency")]
    [InlineData("order_id",       "/order_id")]
    [InlineData("external-id",    "/external-id")]
    [InlineData("is_priority",    "/is_priority")]
    public void Escape_PlainKeys_PrependSlash(string key, string expected)
    {
        JsonPointerEscaper.Escape(key).Should().Be(expected);
    }

    [Fact]
    public void Escape_KeyWithSlash_EscapedAsTilde1()
    {
        JsonPointerEscaper.Escape("a/b").Should().Be("/a~1b");
    }

    [Fact]
    public void Escape_KeyWithTilde_EscapedAsTilde0()
    {
        JsonPointerEscaper.Escape("a~b").Should().Be("/a~0b");
    }

    [Fact]
    public void Escape_KeyWithBoth_EscapesTildeFirst()
    {
        JsonPointerEscaper.Escape("a~/b").Should().Be("/a~0~1b");
    }

    [Fact]
    public void Escape_EmptyKey_ReturnsSlash()
    {
        JsonPointerEscaper.Escape(string.Empty).Should().Be("/");
    }
}
