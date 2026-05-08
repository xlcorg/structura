using System.Linq;

using FluentAssertions;

using Structura.Generator;

using Xunit;

namespace Structura.UnitTests.Generator;

/// <summary>
/// Drives <see cref="GeneratorJsonParser.ParseRootInfo"/> — the recursive
/// schema builder introduced in Step 9 foundation. Owns the parser-side
/// behaviour: scalar kinds, nested objects, array classification, nullable
/// promotion. Diagnostic reporting and emitter consumption are tested in
/// later commits.
/// </summary>
public sealed class JsonParseRootInfoTests
{
    [Fact]
    public void RootScalars_AreClassifiedByKind()
    {
        const string src = "{\"name\":\"Alice\",\"age\":30,\"price\":9.99,\"active\":true,\"notes\":null}";

        JsonRootInfo? info = GeneratorJsonParser.ParseRootInfo(src);

        info.Should().NotBeNull();
        info!.Root.Scalars.Should().HaveCount(5);

        Kind(info, "name").Should().Be(JsonGenScalarKind.String);
        Kind(info, "age").Should().Be(JsonGenScalarKind.Long);
        Kind(info, "price").Should().Be(JsonGenScalarKind.Decimal);
        Kind(info, "active").Should().Be(JsonGenScalarKind.Boolean);
        Kind(info, "notes").Should().Be(JsonGenScalarKind.NullableString);
    }

    [Fact]
    public void NestedObject_BecomesChildJsonGenObject()
    {
        const string src = "{\"name\":\"Lib\",\"address\":{\"city\":\"AMS\",\"zip\":\"1011AB\"}}";

        JsonRootInfo? info = GeneratorJsonParser.ParseRootInfo(src);

        info.Should().NotBeNull();
        info!.Root.NestedObjects.Should().HaveCount(1);

        JsonGenNestedObject nested = info.Root.NestedObjects.Single();
        nested.Name.Should().Be("address");
        nested.Object.Scalars.Should().HaveCount(2);
        nested.Object.Scalars.Single(s => s.Name == "city").Kind.Should().Be(JsonGenScalarKind.String);
        nested.Object.Scalars.Single(s => s.Name == "zip").Kind.Should().Be(JsonGenScalarKind.String);
    }

    [Fact]
    public void ArrayOfStrings_BecomesPrimitiveCollection()
    {
        const string src = "{\"tags\":[\"fiction\",\"classic\"]}";

        JsonRootInfo? info = GeneratorJsonParser.ParseRootInfo(src);

        info.Should().NotBeNull();
        info!.Root.Collections.Should().HaveCount(1);

        JsonGenCollection coll = info.Root.Collections.Single();
        coll.Name.Should().Be("tags");
        coll.ItemKind.Should().Be(JsonGenItemKind.Primitive);
        coll.PrimitiveItemKind.Should().Be(JsonGenScalarKind.String);
    }

    [Fact]
    public void ArrayOfLongs_BecomesPrimitiveCollection()
    {
        const string src = "{\"years\":[1865,1872]}";

        JsonRootInfo? info = GeneratorJsonParser.ParseRootInfo(src);

        info!.Root.Collections.Single().PrimitiveItemKind.Should().Be(JsonGenScalarKind.Long);
    }

    [Fact]
    public void EmptyArray_BecomesEmptyCollection()
    {
        const string src = "{\"archive\":[]}";

        JsonRootInfo? info = GeneratorJsonParser.ParseRootInfo(src);

        JsonGenCollection coll = info!.Root.Collections.Single();
        coll.ItemKind.Should().Be(JsonGenItemKind.Empty);
        coll.PrimitiveItemKind.Should().BeNull();
        coll.ObjectItem.Should().BeNull();
    }

    [Fact]
    public void ArrayOfObjects_BecomesObjectCollectionWithUnionFields()
    {
        // Two items observed:
        //   - {id: 1, sku: "A"}            → both id and sku present
        //   - {id: 2, sku: "B", note: "x"} → note present only in second
        // Union: id (long, required), sku (string, required), note (string, optional).
        const string src =
            "{\"items\":[" +
            "{\"id\":1,\"sku\":\"A\"}," +
            "{\"id\":2,\"sku\":\"B\",\"note\":\"x\"}" +
            "]}";

        JsonRootInfo? info = GeneratorJsonParser.ParseRootInfo(src);

        JsonGenCollection coll = info!.Root.Collections.Single();
        coll.ItemKind.Should().Be(JsonGenItemKind.Object);
        coll.ObjectItem.Should().NotBeNull();
        coll.ObjectItem!.Scalars.Should().HaveCount(3);

        coll.ObjectItem.Scalars.Single(s => s.Name == "id").Kind.Should().Be(JsonGenScalarKind.Long);
        coll.ObjectItem.Scalars.Single(s => s.Name == "sku").Kind.Should().Be(JsonGenScalarKind.String);
        coll.ObjectItem.Scalars.Single(s => s.Name == "note").Kind.Should().Be(JsonGenScalarKind.String);

        coll.ObjectItem.RequiredKeys.Should().BeEquivalentTo(new[] { "id", "sku" });
    }

    [Fact]
    public void ArrayOfObjects_NullablePromotion_ForNullObservation()
    {
        // city: "AMS" in first item, null in second → string? in the union.
        const string src =
            "{\"books\":[" +
            "{\"city\":\"AMS\"}," +
            "{\"city\":null}" +
            "]}";

        JsonRootInfo? info = GeneratorJsonParser.ParseRootInfo(src);

        JsonGenObject? itemShape = info!.Root.Collections.Single().ObjectItem;
        itemShape!.Scalars.Single(s => s.Name == "city").Kind.Should().Be(JsonGenScalarKind.NullableString);
    }

    [Fact]
    public void HeterogeneousPrimitiveArray_BecomesHeterogeneousCollection()
    {
        const string src = "{\"mixed\":[1,\"two\"]}";

        JsonRootInfo? info = GeneratorJsonParser.ParseRootInfo(src);

        info!.Root.Collections.Single().ItemKind.Should().Be(JsonGenItemKind.Heterogeneous);
    }

    [Fact]
    public void MixedObjectAndPrimitiveArray_BecomesHeterogeneousCollection()
    {
        const string src = "{\"mixed\":[{\"a\":1},\"two\"]}";

        JsonRootInfo? info = GeneratorJsonParser.ParseRootInfo(src);

        info!.Root.Collections.Single().ItemKind.Should().Be(JsonGenItemKind.Heterogeneous);
    }

    private static JsonGenScalarKind Kind(JsonRootInfo info, string name)
    {
        return info.Root.Scalars.Single(s => s.Name == name).Kind;
    }
}
