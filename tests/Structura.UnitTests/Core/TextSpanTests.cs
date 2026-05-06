using FluentAssertions;

using Structura.Runtime;

using Xunit;

namespace Structura.UnitTests.Core;

public sealed class TextSpanTests
{
    [Fact]
    public void End_IsStartPlusLength()
    {
        new TextSpan(3, 5).End.Should().Be(8);
    }

    [Fact]
    public void FromBounds_ProducesEqualSpan()
    {
        TextSpan.FromBounds(3, 8).Should().Be(new TextSpan(3, 5));
    }

    [Fact]
    public void FromBounds_ThrowsWhenEndBeforeStart()
    {
        Func<TextSpan> act = () => TextSpan.FromBounds(8, 3);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0, 5, 5, 5, false)] // touching end-to-start
    [InlineData(0, 5, 4, 2, true)]  // overlapping
    [InlineData(0, 5, 5, 0, false)] // zero-length at boundary
    [InlineData(0, 0, 0, 0, false)] // both zero-length
    [InlineData(0, 5, 1, 2, true)]  // contained
    public void IntersectsWith(int aStart, int aLen, int bStart, int bLen, bool expected)
    {
        new TextSpan(aStart, aLen).IntersectsWith(new TextSpan(bStart, bLen)).Should().Be(expected);
    }

    [Fact]
    public void ToString_RendersHalfOpenInterval()
    {
        new TextSpan(3, 5).ToString().Should().Be("[3..8)");
    }
}
