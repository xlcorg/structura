using System.IO;
using System.Reflection;

using FluentAssertions;

using Structura.Generated;
using Structura.Runtime;

using Xunit;

namespace Structura.IntegrationTests.Json;

/// <summary>
/// Round-trip behaviour for primitive-array collections (string, long) at
/// root and nested-in-item depth. Pins the V1 read-only contract: collection
/// properties are declared <c>IReadOnlyList&lt;T&gt;</c> with no setter,
/// since insertion-aware patching is deferred to Step 10.
/// </summary>
public sealed class JsonPrimitiveArrayTests
{
    private static string LoadOrder()
    {
        return File.ReadAllText("order.sample.json");
    }

    private static string LoadLibrary()
    {
        return File.ReadAllText("library.sample.json");
    }

    // ── Root-level primitive arrays ───────────────────────────────────────────

    [Fact]
    public void Library_Tags_StringArray_EnumeratesItems()
    {
        var library = LoadLibrary().ParseJson<LibrarySampleJson>();

        library.Tags.Should().HaveCount(2);
        library.Tags[0].Should().Be("fiction");
        library.Tags[1].Should().Be("classic");
    }

    [Fact]
    public void Library_Years_LongArray_EnumeratesItems()
    {
        var library = LoadLibrary().ParseJson<LibrarySampleJson>();

        library.Years.Should().HaveCount(2);
        library.Years[0].Should().Be(1865L);
        library.Years[1].Should().Be(1872L);
    }

    // ── Nested-in-item primitive arrays ───────────────────────────────────────

    [Fact]
    public void Order_ItemsIndexed_TagsCollection_IsAccessible()
    {
        var order = LoadOrder().ParseJson<OrderSampleJson>();

        order.Items[0].Tags.Should().Equal("electronics", "laptop");
        order.Items[1].Tags.Should().Equal("electronics", "laptop");
    }

    [Fact]
    public void Order_ItemsIndexed_SerialNumbersCollection_IsAccessible()
    {
        var order = LoadOrder().ParseJson<OrderSampleJson>();

        order.Items[0].SerialNumbers.Should().Equal("SN-A1B2C3");
        order.Items[1].SerialNumbers.Should().Equal("SN-D4E5F6");
    }

    // ── V1 read-only contract for collection properties ──────────────────────

    [Fact]
    public void Library_Tags_PropertyHasNoSetter()
    {
        // Collection properties are declared IReadOnlyList<T>; no public or
        // non-public setter. Insertion is out of V1 scope.
        PropertyInfo tags = typeof(LibrarySampleJson).GetProperty("Tags")!;

        tags.PropertyType.Should().Be(typeof(IReadOnlyList<string>));
        tags.GetSetMethod(nonPublic: true).Should().BeNull();
    }

    [Fact]
    public void Library_Books_ObjectArrayPropertyHasNoSetter()
    {
        // Object-array collections share the same V1 contract.
        PropertyInfo books = typeof(LibrarySampleJson).GetProperty("Books")!;

        books.PropertyType.GetGenericTypeDefinition()
            .Should().Be(typeof(IReadOnlyList<>));
        books.GetSetMethod(nonPublic: true).Should().BeNull();
    }

    // ── Round-trip with no mutation through an array ──────────────────────────

    [Fact]
    public void Order_RoundTrip_PreservesNestedPrimitiveArraysVerbatim()
    {
        // Untouched primitive-array regions are a key invariant: parsing
        // them must be lossless even though they are exposed as runtime
        // values.
        string json = LoadOrder();
        var order = json.ParseJson<OrderSampleJson>();

        // Touch a single root scalar and confirm the items[].tags / .serial_numbers
        // regions are byte-identical.
        order.Currency = "USD";
        string modified = order.ToJson();

        modified.Should().Contain("\"tags\": [\n        \"electronics\",\n        \"laptop\"\n      ]");
        modified.Should().Contain("\"serial_numbers\": [\n        \"SN-A1B2C3\"\n      ]");
        modified.Should().Contain("\"serial_numbers\": [\n        \"SN-D4E5F6\"\n      ]");
    }
}
