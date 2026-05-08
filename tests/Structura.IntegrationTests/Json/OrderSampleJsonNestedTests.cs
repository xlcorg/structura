using System.IO;
using System.Linq;

using FluentAssertions;

using Structura.Generated;
using Structura.Runtime;

using Xunit;

namespace Structura.IntegrationTests.Json;

/// <summary>
/// End-to-end tests for the Step 9 JSON pipeline: nested object mutations,
/// nested-in-array-item mutations, deep JSON Pointer paths, optional-field
/// throwing setter, nullable-string promotion across array observations,
/// and byte-identity of untouched regions.
/// </summary>
public sealed class OrderSampleJsonNestedTests
{
    private static string LoadSample()
    {
        return File.ReadAllText("order.sample.json");
    }

    // ── Nested object mutations ───────────────────────────────────────────────

    [Fact]
    public void NestedScalar_CustomerFirstName_PatchesValueAndRecordsPointer()
    {
        string json = LoadSample();
        var order = json.ParseJson<OrderSampleJson>();

        order.Customer.FirstName.Should().Be("Artem");
        order.Customer.FirstName = "Ivan";

        DocumentChange change = ((IStructuraDocument)order).Changes.Single();
        change.Path.Should().Be("/customer/first_name");
        change.OldText.Should().Be("\"Artem\"");
        change.NewText.Should().Be("\"Ivan\"");

        string modified = order.ToJson();
        modified.Should().Contain("\"first_name\": \"Ivan\"");
        modified.Should().Contain("\"last_name\": \"Sorokovikov\"");
    }

    [Fact]
    public void DeeplyNestedScalar_CustomerPreferencesMarketingConsent_PatchesAtDepth()
    {
        string json = LoadSample();
        var order = json.ParseJson<OrderSampleJson>();

        order.Customer.Preferences.MarketingConsent.Should().BeTrue();
        order.Customer.Preferences.MarketingConsent = false;

        DocumentChange change = ((IStructuraDocument)order).Changes.Single();
        change.Path.Should().Be("/customer/preferences/marketing-consent");
        change.NewText.Should().Be("false");
    }

    [Fact]
    public void NestedObjectMutation_PreservesUntouchedRegions_ByteIdentical()
    {
        string json = LoadSample();
        var order = json.ParseJson<OrderSampleJson>();

        order.BillingAddress.City = "Rotterdam";
        string modified = order.ToJson();

        DocumentChange change = ((IStructuraDocument)order).Changes.Single();
        modified[..change.Span.Start].Should().Be(json[..change.Span.Start]);
        modified[(change.Span.Start + change.NewText.Length)..]
            .Should().Be(json[change.Span.End..]);
    }

    // ── Array item mutations ──────────────────────────────────────────────────

    [Fact]
    public void ItemScalarMutation_FirstItem_RecordsIndexedPointer()
    {
        string json = LoadSample();
        var order = json.ParseJson<OrderSampleJson>();

        order.Items.Should().HaveCount(2);
        order.Items[0].Quantity.Should().Be(1);
        order.Items[0].Quantity = 2;

        DocumentChange change = ((IStructuraDocument)order).Changes.Single();
        change.Path.Should().Be("/items/0/quantity");
        change.NewText.Should().Be("2");
    }

    [Fact]
    public void ItemScalarMutation_SecondItem_UsesIndexOne()
    {
        string json = LoadSample();
        var order = json.ParseJson<OrderSampleJson>();

        order.Items[1].Sku = "NB-002-revB";

        ((IStructuraDocument)order).Changes.Single().Path
            .Should().Be("/items/1/sku");
    }

    // ── Nested-in-item nullable-promoted scalar ───────────────────────────────

    [Fact]
    public void NestedInItem_CountryCode_PromotedToNullable_ReadsNullForSecondItem()
    {
        // items[0].manufacturer.country_code is "CN", items[1] is null. The
        // unioned ManufacturerType has string? CountryCode.
        var order = LoadSample().ParseJson<OrderSampleJson>();

        order.Items[0].Manufacturer.CountryCode.Should().Be("CN");
        order.Items[1].Manufacturer.CountryCode.Should().BeNull();
    }

    [Fact]
    public void NestedInItem_CountryCode_SetToString_WritesQuotedLiteral()
    {
        string json = LoadSample();
        var order = json.ParseJson<OrderSampleJson>();

        // Replace the null in items[1] with a real value.
        order.Items[1].Manufacturer.CountryCode = "DE";

        DocumentChange change = ((IStructuraDocument)order).Changes.Single();
        change.Path.Should().Be("/items/1/manufacturer/country_code");
        change.OldText.Should().Be("null");
        change.NewText.Should().Be("\"DE\"");

        order.ToJson().Should().Contain("\"country_code\": \"DE\"");
    }

    // ── Heterogeneous field union ─────────────────────────────────────────────

    [Fact]
    public void OptionalItemField_PresentInSecond_AbsentInFirst_SetterThrowsForFirst()
    {
        // 'note' lives on items[1] only. The Item class exposes Note for both
        // items but the absent-key setter must throw to honour the V1
        // no-insertion contract.
        var order = LoadSample().ParseJson<OrderSampleJson>();

        Action act = () => { order.Items[0].Note = "anything"; };

        act.Should().Throw<StructuraMutationException>()
            .WithMessage("*Note*");
    }

    [Fact]
    public void OptionalItemField_PresentInSecond_SetterRecordsAndPatches()
    {
        string json = LoadSample();
        var order = json.ParseJson<OrderSampleJson>();

        order.Items[1].Note.Should().Be("Replacement unit");
        order.Items[1].Note = "Replaced under warranty";

        DocumentChange change = ((IStructuraDocument)order).Changes.Single();
        change.Path.Should().Be("/items/1/note");
        change.NewText.Should().Be("\"Replaced under warranty\"");
    }

    [Fact]
    public void OptionalItemField_AbsentInFirst_GetterReturnsTypeDefault()
    {
        // The getter is non-throwing — V1 lets readers see the default value
        // (empty string for String) so callers can treat absence uniformly.
        var order = LoadSample().ParseJson<OrderSampleJson>();
        order.Items[0].Note.Should().Be(string.Empty);
    }

    // ── Cross-region mutations ────────────────────────────────────────────────

    [Fact]
    public void MultipleNestedMutations_AreOrderedByDocumentPosition()
    {
        string json = LoadSample();
        var order = json.ParseJson<OrderSampleJson>();

        order.Customer.FirstName = "Ivan";
        order.BillingAddress.City = "Rotterdam";
        order.Items[0].Quantity = 7;

        IReadOnlyList<DocumentChange> changes = ((IStructuraDocument)order).Changes;
        changes.Should().HaveCount(3);
        changes.Select(c => c.Path).Should().Equal(
            "/customer/first_name",
            "/billing_address/city",
            "/items/0/quantity");
    }
}
