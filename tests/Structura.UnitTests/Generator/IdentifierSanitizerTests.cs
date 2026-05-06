using System.Collections.Generic;

using FluentAssertions;

using Structura.Generator;

using Xunit;

namespace Structura.UnitTests.Generator;

public sealed class IdentifierSanitizerTests
{
    private static string Sanitize(string key)
    {
        return IdentifierSanitizer.Sanitize(key, new HashSet<string>());
    }

    [Theory]
    [InlineData("order_id",           "OrderId")]
    [InlineData("external-id",        "ExternalId")]
    [InlineData("created_at_utc",     "CreatedAtUtc")]
    [InlineData("is_priority",        "IsPriority")]
    [InlineData("marketing-consent",  "MarketingConsent")]
    [InlineData("currency",           "Currency")]
    [InlineData("status",             "Status")]
    [InlineData("version",            "Version")]
    [InlineData("total_amount",       "TotalAmount")]
    public void Sanitize_CommonKeys(string key, string expected)
    {
        Sanitize(key).Should().Be(expected);
    }

    [Fact]
    public void Sanitize_DigitStartingKey_PrefixesUnderscore()
    {
        Sanitize("2nd_line").Should().Be("_2ndLine");
    }

    [Fact]
    public void Sanitize_EmptyKey_ReturnsFallback()
    {
        Sanitize(string.Empty).Should().Be("_Field");
    }

    [Fact]
    public void Sanitize_AllSeparators_ReturnsFallback()
    {
        Sanitize("---").Should().Be("_Field");
        Sanitize("___").Should().Be("_Field");
    }

    [Fact]
    public void Sanitize_AllDigits_PrefixesUnderscore()
    {
        Sanitize("123").Should().Be("_123");
    }

    [Fact]
    public void Sanitize_CollisionResolution_AppendsSuffix()
    {
        var used = new HashSet<string>();

        string first  = IdentifierSanitizer.Sanitize("currency", used);
        string second = IdentifierSanitizer.Sanitize("currency", used);
        string third  = IdentifierSanitizer.Sanitize("currency", used);

        first.Should().Be("Currency");
        second.Should().Be("Currency2");
        third.Should().Be("Currency3");
    }

    [Fact]
    public void Sanitize_AddsNameToUsedSet()
    {
        var used = new HashSet<string>();
        IdentifierSanitizer.Sanitize("currency", used);
        used.Should().Contain("Currency");
    }
}
