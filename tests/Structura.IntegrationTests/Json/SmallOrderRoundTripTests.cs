using FluentAssertions;

using Structura.Runtime;

using Xunit;

namespace Structura.IntegrationTests.Json;

public sealed class SmallOrderRoundTripTests
{
    private const string Source =
        "{\n" +
        "  \"currency\": \"RUB\",\n" +
        "  \"version\": 7,\n" +
        "  \"is_priority\": true\n" +
        "}";

    [Fact]
    public void NoMutation_ToJson_IsByteIdentical()
    {
        var order = Source.ParseJson<SmallOrder>();

        order.ToJson().Should().Be(Source);
        ((IStructuraDocument)order).Changes.Should().BeEmpty();
    }

    [Fact]
    public void StringMutation_PatchesOnlyTheValueLiteral()
    {
        var order = Source.ParseJson<SmallOrder>();
        order.Currency = "USD";

        order.ToJson().Should().Be(
            "{\n" +
            "  \"currency\": \"USD\",\n" +
            "  \"version\": 7,\n" +
            "  \"is_priority\": true\n" +
            "}");
    }

    [Fact]
    public void NumberMutation_PatchesOnlyTheValueLiteral()
    {
        var order = Source.ParseJson<SmallOrder>();
        order.Version = 42;

        order.ToJson().Should().Be(
            "{\n" +
            "  \"currency\": \"RUB\",\n" +
            "  \"version\": 42,\n" +
            "  \"is_priority\": true\n" +
            "}");
    }

    [Fact]
    public void BooleanMutation_PatchesOnlyTheValueLiteral()
    {
        var order = Source.ParseJson<SmallOrder>();
        order.IsPriority = false;

        order.ToJson().Should().Be(
            "{\n" +
            "  \"currency\": \"RUB\",\n" +
            "  \"version\": 7,\n" +
            "  \"is_priority\": false\n" +
            "}");
    }

    [Fact]
    public void MultipleMutations_PatchedTogether()
    {
        var order = Source.ParseJson<SmallOrder>();
        order.Currency = "EUR";
        order.Version = 99;
        order.IsPriority = false;

        order.ToJson().Should().Be(
            "{\n" +
            "  \"currency\": \"EUR\",\n" +
            "  \"version\": 99,\n" +
            "  \"is_priority\": false\n" +
            "}");
    }

    [Fact]
    public void ResettingValueToOriginal_DropsEdit()
    {
        var order = Source.ParseJson<SmallOrder>();
        order.Currency = "USD";
        order.Currency = "RUB";

        order.ToJson().Should().Be(Source);
        ((IStructuraDocument)order).Changes.Should().BeEmpty();
    }

    [Fact]
    public void RepeatedMutation_KeepsLatestValue()
    {
        var order = Source.ParseJson<SmallOrder>();
        order.Currency = "USD";
        order.Currency = "EUR";

        order.ToJson().Should().Contain("\"currency\": \"EUR\"");
        ((IStructuraDocument)order).Changes.Should().ContainSingle()
            .Which.NewText.Should().Be("\"EUR\"");
    }

    [Fact]
    public void Changes_ExposeJsonPointerPathsAndOriginalSpans()
    {
        var order = Source.ParseJson<SmallOrder>();
        order.Currency = "USD";
        order.Version = 42;

        var changes = ((IStructuraDocument)order).Changes;

        changes.Select(c => c.Path).Should().Equal("/currency", "/version");
        var currencyChange = changes[0];
        currencyChange.OldText.Should().Be("\"RUB\"");
        currencyChange.NewText.Should().Be("\"USD\"");
        Source.Substring(currencyChange.Span.Start, currencyChange.Span.Length)
            .Should().Be("\"RUB\"");
    }

    [Fact]
    public void UntouchedRegions_AreByteIdentical()
    {
        var order = Source.ParseJson<SmallOrder>();
        order.Currency = "USD";
        var modified = order.ToJson();

        var change = ((IStructuraDocument)order).Changes.Single();

        modified[..change.Span.Start].Should().Be(Source[..change.Span.Start]);
        modified[(change.Span.Start + change.NewText.Length)..]
            .Should().Be(Source[change.Span.End..]);
    }

    [Fact]
    public void StringWithEscapes_RoundTripsThroughDecodedValue()
    {
        const string SourceWithEscape =
            "{\n" +
            "  \"currency\": \"R\\u0055B\",\n" +
            "  \"version\": 1,\n" +
            "  \"is_priority\": false\n" +
            "}";

        var order = SourceWithEscape.ParseJson<SmallOrder>();

        // Decoded value matches the unescaped string.
        order.Currency.Should().Be("RUB");

        // Without mutation the original (escaped) literal is preserved verbatim.
        order.ToJson().Should().Be(SourceWithEscape);

        // Mutating writes a freshly encoded literal, replacing the original span.
        order.Currency = "USD";
        order.ToJson().Should().Be(
            "{\n" +
            "  \"currency\": \"USD\",\n" +
            "  \"version\": 1,\n" +
            "  \"is_priority\": false\n" +
            "}");
    }
}
