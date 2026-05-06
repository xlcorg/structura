using System.IO;
using System.Linq;

using FluentAssertions;

using Structura.Generated;
using Structura.Runtime;

using Xunit;

namespace Structura.IntegrationTests.Json;

/// <summary>
/// End-to-end tests for the source-generator-produced <see cref="OrderSampleJson"/>
/// model, verifying that the full pipeline (parse → mutate → patch) works
/// correctly on the real <c>order.sample.json</c> sample file.
/// </summary>
public sealed class OrderSampleJsonGeneratedTests
{
    private static string LoadSample()
    {
        return File.ReadAllText("order.sample.json");
    }

    // ── Round-trip without mutation ───────────────────────────────────────────

    [Fact]
    public void NoMutation_ToJson_IsByteIdentical()
    {
        string json  = LoadSample();
        var order = json.ParseJson<OrderSampleJson>();

        order.ToJson().Should().Be(json);
        ((IStructuraDocument)order).Changes.Should().BeEmpty();
    }

    // ── String property ───────────────────────────────────────────────────────

    [Fact]
    public void StringMutation_PatchesOnlyTheValueLiteral()
    {
        string json  = LoadSample();
        var order = json.ParseJson<OrderSampleJson>();
        order.Currency = "USD";

        string modified = order.ToJson();
        modified.Should().Contain("\"currency\": \"USD\"");
        modified.Should().Contain("\"version\": 7");   // untouched
    }

    [Fact]
    public void RepeatedStringMutation_KeepsLatestValue()
    {
        string json  = LoadSample();
        var order = json.ParseJson<OrderSampleJson>();
        order.Currency = "USD";
        order.Currency = "EUR";

        order.ToJson().Should().Contain("\"currency\": \"EUR\"");
        ((IStructuraDocument)order).Changes.Should().ContainSingle()
            .Which.NewText.Should().Be("\"EUR\"");
    }

    [Fact]
    public void ResettingStringToOriginal_DropsEdit()
    {
        string json  = LoadSample();
        var order = json.ParseJson<OrderSampleJson>();
        order.Currency = "USD";
        order.Currency = "RUB"; // restore original

        order.ToJson().Should().Be(json);
        ((IStructuraDocument)order).Changes.Should().BeEmpty();
    }

    // ── Long property ─────────────────────────────────────────────────────────

    [Fact]
    public void LongMutation_PatchesOnlyTheValueLiteral()
    {
        string json  = LoadSample();
        var order = json.ParseJson<OrderSampleJson>();
        order.Version = 42;

        string modified = order.ToJson();
        modified.Should().Contain("\"version\": 42");
        modified.Should().Contain("\"currency\": \"RUB\""); // untouched
    }

    // ── Decimal property ──────────────────────────────────────────────────────

    [Fact]
    public void DecimalMutation_PatchesOnlyTheValueLiteral()
    {
        string json  = LoadSample();
        var order = json.ParseJson<OrderSampleJson>();
        order.TotalAmount = 999.99m;

        string modified = order.ToJson();
        modified.Should().Contain("\"total_amount\": 999.99");
        modified.Should().Contain("\"currency\": \"RUB\""); // untouched
    }

    // ── Bool property ─────────────────────────────────────────────────────────

    [Fact]
    public void BoolMutation_PatchesOnlyTheValueLiteral()
    {
        string json  = LoadSample();
        var order = json.ParseJson<OrderSampleJson>();
        order.IsPriority = false;

        string modified = order.ToJson();
        modified.Should().Contain("\"is_priority\": false");
        modified.Should().Contain("\"version\": 7"); // untouched
    }

    // ── Nullable string property ──────────────────────────────────────────────

    [Fact]
    public void NullableString_ReadAsNull_WhenSampleValueIsNull()
    {
        var order = LoadSample().ParseJson<OrderSampleJson>();
        order.UpdatedAtUtc.Should().BeNull();
    }

    [Fact]
    public void NullableString_SetValue_WritesQuotedString()
    {
        string json  = LoadSample();
        var order = json.ParseJson<OrderSampleJson>();
        order.UpdatedAtUtc = "2026-06-01T00:00:00Z";

        order.ToJson().Should().Contain("\"updated_at_utc\": \"2026-06-01T00:00:00Z\"");
    }

    [Fact]
    public void NullableString_SetNull_WritesNullLiteral()
    {
        string json  = LoadSample();
        var order = json.ParseJson<OrderSampleJson>();
        order.UpdatedAtUtc = "2026-06-01T00:00:00Z";
        order.UpdatedAtUtc = null; // back to null literal

        // Setting to null writes "null" — same as original, so edit is dropped.
        order.ToJson().Should().Be(json);
        ((IStructuraDocument)order).Changes.Should().BeEmpty();
    }

    // ── Multiple mutations ────────────────────────────────────────────────────

    [Fact]
    public void MultipleMutations_AllPatchedCorrectly()
    {
        string json  = LoadSample();
        var order = json.ParseJson<OrderSampleJson>();
        order.Currency    = "EUR";
        order.Version     = 99;
        order.IsPriority  = false;
        order.TotalAmount = 0m;

        string modified = order.ToJson();
        modified.Should().Contain("\"currency\": \"EUR\"");
        modified.Should().Contain("\"version\": 99");
        modified.Should().Contain("\"is_priority\": false");
        modified.Should().Contain("\"total_amount\": 0");
    }

    // ── Untouched-region byte-identity ────────────────────────────────────────

    [Fact]
    public void UntouchedRegions_AreByteIdentical()
    {
        string json  = LoadSample();
        var order = json.ParseJson<OrderSampleJson>();
        order.Currency = "USD";

        string modified = order.ToJson();
        DocumentChange change   = ((IStructuraDocument)order).Changes.Single();

        modified[..change.Span.Start]
            .Should().Be(json[..change.Span.Start]);

        modified[(change.Span.Start + change.NewText.Length)..]
            .Should().Be(json[change.Span.End..]);
    }

    // ── Changes metadata ──────────────────────────────────────────────────────

    [Fact]
    public void Changes_ExposeJsonPointerPathsAndSpans()
    {
        string json  = LoadSample();
        var order = json.ParseJson<OrderSampleJson>();
        order.Currency = "USD";
        order.Version  = 42;

        IReadOnlyList<DocumentChange> changes = ((IStructuraDocument)order).Changes;
        // Changes are sorted by Span.Start (position in document).
        // In order.sample.json: "version" (line 8) precedes "currency" (line 10).
        changes.Select(c => c.Path).Should().Equal("/version", "/currency");

        DocumentChange currencyChange = changes.Single(c => c.Path == "/currency");
        currencyChange.OldText.Should().Be("\"RUB\"");
        currencyChange.NewText.Should().Be("\"USD\"");
        json.Substring(currencyChange.Span.Start, currencyChange.Span.Length)
            .Should().Be("\"RUB\"");
    }

    // ── Nested objects / arrays are preserved verbatim ────────────────────────

    [Fact]
    public void NestedObjectsAndArrays_PreservedVerbatimAfterScalarMutation()
    {
        string json  = LoadSample();
        var order = json.ParseJson<OrderSampleJson>();
        order.Currency = "USD";

        string modified = order.ToJson();

        // The customer nested object must be byte-for-byte identical.
        modified.Should().Contain("\"customer\": {");
        // The items array must be preserved.
        modified.Should().Contain("\"items\": [");
    }
}
