using FluentAssertions;

using Structura.Runtime;

using Xunit;

namespace Structura.IntegrationTests.Xml;

public sealed class SmallOrderXmlRoundTripTests
{
    private const string Source =
        "<?xml version=\"1.0\"?>\n" +
        "<order>\n" +
        "  <currency>RUB</currency>\n" +
        "  <version>7</version>\n" +
        "  <is_priority>true</is_priority>\n" +
        "</order>";

    [Fact]
    public void NoMutation_ToXml_IsByteIdentical()
    {
        var order = Source.ParseXml<SmallOrderXml>();

        order.ToXml().Should().Be(Source);
        ((IStructuraDocument)order).Changes.Should().BeEmpty();
    }

    [Fact]
    public void StringMutation_PatchesOnlyTheValueLiteral()
    {
        var order = Source.ParseXml<SmallOrderXml>();
        order.Currency = "USD";

        order.ToXml().Should().Be(
            "<?xml version=\"1.0\"?>\n" +
            "<order>\n" +
            "  <currency>USD</currency>\n" +
            "  <version>7</version>\n" +
            "  <is_priority>true</is_priority>\n" +
            "</order>");
    }

    [Fact]
    public void LongMutation_PatchesOnlyTheValueLiteral()
    {
        var order = Source.ParseXml<SmallOrderXml>();
        order.Version = 42;

        order.ToXml().Should().Be(
            "<?xml version=\"1.0\"?>\n" +
            "<order>\n" +
            "  <currency>RUB</currency>\n" +
            "  <version>42</version>\n" +
            "  <is_priority>true</is_priority>\n" +
            "</order>");
    }

    [Fact]
    public void BoolMutation_PatchesOnlyTheValueLiteral()
    {
        var order = Source.ParseXml<SmallOrderXml>();
        order.IsPriority = false;

        order.ToXml().Should().Be(
            "<?xml version=\"1.0\"?>\n" +
            "<order>\n" +
            "  <currency>RUB</currency>\n" +
            "  <version>7</version>\n" +
            "  <is_priority>false</is_priority>\n" +
            "</order>");
    }

    [Fact]
    public void MultipleMutations_PatchedTogether()
    {
        var order = Source.ParseXml<SmallOrderXml>();
        order.Currency = "EUR";
        order.Version = 99;
        order.IsPriority = false;

        order.ToXml().Should().Be(
            "<?xml version=\"1.0\"?>\n" +
            "<order>\n" +
            "  <currency>EUR</currency>\n" +
            "  <version>99</version>\n" +
            "  <is_priority>false</is_priority>\n" +
            "</order>");
    }

    [Fact]
    public void ResettingValueToOriginal_DropsEdit()
    {
        var order = Source.ParseXml<SmallOrderXml>();
        order.Currency = "USD";
        order.Currency = "RUB";

        order.ToXml().Should().Be(Source);
        ((IStructuraDocument)order).Changes.Should().BeEmpty();
    }

    [Fact]
    public void RepeatedMutation_KeepsLatestValue()
    {
        var order = Source.ParseXml<SmallOrderXml>();
        order.Currency = "USD";
        order.Currency = "EUR";

        order.ToXml().Should().Contain("<currency>EUR</currency>");
        ((IStructuraDocument)order).Changes.Should().ContainSingle()
            .Which.NewText.Should().Be("EUR");
    }

    [Fact]
    public void Changes_ExposeXPathLikePathsAndOriginalSpans()
    {
        var order = Source.ParseXml<SmallOrderXml>();
        order.Currency = "USD";
        order.Version = 42;

        IReadOnlyList<DocumentChange> changes = ((IStructuraDocument)order).Changes;

        // Sorted by Span.Start — currency appears before version in the source.
        changes.Select(c => c.Path).Should().Equal("/currency", "/version");

        DocumentChange currencyChange = changes[0];
        currencyChange.OldText.Should().Be("RUB");
        currencyChange.NewText.Should().Be("USD");
        Source.Substring(currencyChange.Span.Start, currencyChange.Span.Length)
            .Should().Be("RUB");
    }

    [Fact]
    public void UntouchedRegions_AreByteIdentical()
    {
        var order = Source.ParseXml<SmallOrderXml>();
        order.Currency = "USD";
        string modified = order.ToXml();

        DocumentChange change = ((IStructuraDocument)order).Changes.Single();

        modified[..change.Span.Start].Should().Be(Source[..change.Span.Start]);
        modified[(change.Span.Start + change.NewText.Length)..]
            .Should().Be(Source[change.Span.End..]);
    }

    [Fact]
    public void StringWithEntityRef_RoundTripsThroughDecodedValue()
    {
        const string SourceWithEntity =
            "<?xml version=\"1.0\"?>\n" +
            "<order>\n" +
            "  <currency>R&#85;B</currency>\n" +
            "  <version>1</version>\n" +
            "  <is_priority>false</is_priority>\n" +
            "</order>";

        var order = SourceWithEntity.ParseXml<SmallOrderXml>();

        // Decoded value matches the unescaped string.
        order.Currency.Should().Be("RUB");

        // Without mutation the original (escaped) literal is preserved verbatim.
        order.ToXml().Should().Be(SourceWithEntity);

        // Mutating writes a freshly encoded literal, replacing the original span.
        order.Currency = "USD";
        order.ToXml().Should().Be(
            "<?xml version=\"1.0\"?>\n" +
            "<order>\n" +
            "  <currency>USD</currency>\n" +
            "  <version>1</version>\n" +
            "  <is_priority>false</is_priority>\n" +
            "</order>");
    }
}
