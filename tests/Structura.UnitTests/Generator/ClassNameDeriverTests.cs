using FluentAssertions;

using Structura.Generator;

using Xunit;

namespace Structura.UnitTests.Generator;

public sealed class ClassNameDeriverTests
{
    [Theory]
    [InlineData("order.sample.json",   "OrderSampleJson")]
    [InlineData("customer.sample.json","CustomerSampleJson")]
    [InlineData("invoice.json",        "InvoiceJson")]
    [InlineData("simple.json",         "SimpleJson")]
    public void Derive_CommonFileNames(string fileName, string expected)
    {
        ClassNameDeriver.Derive(fileName).Should().Be(expected);
    }

    [Fact]
    public void Derive_EmptyFileName_ReturnsFallback()
    {
        ClassNameDeriver.Derive(string.Empty).Should().Be("UnknownDocument");
    }

    [Fact]
    public void Derive_AlreadyPascalCase_Preserved()
    {
        ClassNameDeriver.Derive("Order.Sample.Json").Should().Be("OrderSampleJson");
    }
}
