using System.Collections.Immutable;
using System.Threading;

using FluentAssertions;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

using Structura.Generator;

using Xunit;

namespace Structura.UnitTests.Generator;

/// <summary>
/// Source-text tests for JSON's heterogeneous-field behaviour inside an
/// object-array item type: optional-scalar IsPresent guard, throwing setter
/// on absent keys (the V1 "no insertion" contract), nullable-promotion
/// across observations, and conflict-drop when two observations disagree on
/// a primitive kind. Mirrors XML's heterogeneous behaviour established in
/// Step 8.
/// </summary>
public sealed class JsonHeterogeneousFieldsTests
{
    [Fact]
    public void OptionalField_HasIsPresentField_AndUsesFindProperty()
    {
        // 'note' present only in second item → optional → emitter uses
        // FindProperty (not RequireProperty) so the constructor doesn't
        // throw on the first item's missing key.
        const string Src =
            "{\"items\":[{\"id\":1},{\"id\":2,\"note\":\"x\"}]}";

        string source = Generate(Src);

        source.Should().Contain("private readonly bool _noteIsPresent;");
        source.Should().Contain("FindProperty(\"note\")");
        source.Should().Contain("_noteIsPresent = noteProp is not null");
    }

    [Fact]
    public void OptionalField_AbsentSetter_ThrowsStructuraMutationExceptionWithFieldName()
    {
        // The throwing setter must mention BOTH the C# property name and
        // the original JSON key so users can locate the missing source span.
        const string Src =
            "{\"items\":[{\"id\":1},{\"id\":2,\"note\":\"x\"}]}";

        string source = Generate(Src);

        source.Should().Contain("if (!_noteIsPresent)");
        source.Should().Contain("throw new StructuraMutationException(");
        source.Should().Contain("'Note'");
        source.Should().Contain("'note'");
    }

    [Fact]
    public void OptionalScalar_DefaultsToTypeDefault_WhenAbsent()
    {
        // First item's getter returns the field default (string.Empty / 0L /
        // false / 0m / null) rather than throwing — read remains safe even
        // when the source key is absent.
        const string Src =
            "{\"items\":[" +
            "{\"id\":1}," +
            "{\"id\":2,\"note\":\"x\",\"qty\":5,\"price\":1.5,\"flag\":true,\"hint\":null}" +
            "]}";

        string source = Generate(Src);

        // Each optional default branch emitted in the ctor:
        source.Should().Contain("noteProp is null ? string.Empty");
        source.Should().Contain("qtyProp is null ? 0L");
        source.Should().Contain("priceProp is null ? 0m");
        source.Should().Contain("flagProp is null ? false");
        source.Should().Contain("hintProp is null ? null");
    }

    [Fact]
    public void RequiredField_UsesRequireProperty_AndHasNoIsPresentField()
    {
        // 'id' present in both observations → in RequiredKeys → emitter
        // uses RequireProperty and never emits an IsPresent guard.
        const string Src =
            "{\"items\":[{\"id\":1,\"sku\":\"A\"},{\"id\":2,\"sku\":\"B\"}]}";

        string source = Generate(Src);

        source.Should().Contain("RequireProperty(\"id\")");
        source.Should().Contain("RequireProperty(\"sku\")");
        source.Should().NotContain("_idIsPresent");
        source.Should().NotContain("_skuIsPresent");
    }

    [Fact]
    public void NullablePromotion_NullObservationPlusStringObservation_BecomesNullableString()
    {
        // 'middle_name' is null in the first item, "Q" in the second → the
        // unioned shape is string?, with FindProperty returning null when
        // absent (here it's always present, but kind merge promotes).
        const string Src =
            "{\"items\":[" +
            "{\"id\":1,\"middle_name\":null}," +
            "{\"id\":2,\"middle_name\":\"Q\"}" +
            "]}";

        string source = Generate(Src);

        source.Should().Contain("public string? MiddleName");
        source.Should().Contain("middleNameProp.Value is JsonSourceString s ? s.Value : null");
    }

    [Fact]
    public void ConflictingPrimitiveKinds_AcrossObservations_DropFieldFromUnion()
    {
        // 'qty' is long in the first item, "5" string in the second. The two
        // primitive kinds cannot merge → the field is dropped from the union
        // shape rather than being emitted with an unsafe cast at parse time.
        const string Src =
            "{\"items\":[" +
            "{\"id\":1,\"qty\":2}," +
            "{\"id\":2,\"qty\":\"two\"}" +
            "]}";

        string source = Generate(Src);

        source.Should().Contain("public sealed partial class Item");
        source.Should().NotContain("Qty");
        source.Should().NotContain("\"qty\"");
    }

    [Fact]
    public void DisjointScalars_AcrossObservations_BothAppearAsOptional()
    {
        // Item observations share no scalar keys at all (other than nothing).
        // The unioned shape has both as optional with IsPresent guards.
        const string Src =
            "{\"items\":[{\"a\":1},{\"b\":2}]}";

        string source = Generate(Src);

        source.Should().Contain("_aIsPresent");
        source.Should().Contain("_bIsPresent");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string Generate(string jsonContent)
    {
        GeneratorDriverRunResult result = RunGenerator("hetero.json", jsonContent);
        result.GeneratedTrees.Should().HaveCount(1);
        return result.GeneratedTrees[0].GetText().ToString();
    }

    private static GeneratorDriverRunResult RunGenerator(string fileName, string jsonContent)
    {
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            references: new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            });

        var generator = new StructuraJsonGenerator();
        var additionalText = new InMemoryAdditionalText(fileName, jsonContent);

        var driver = CSharpGeneratorDriver.Create(
                generators: new[] { generator.AsSourceGenerator() },
                additionalTexts: ImmutableArray.Create<AdditionalText>(additionalText))
            .RunGenerators(compilation);

        return driver.GetRunResult();
    }

    private sealed class InMemoryAdditionalText : AdditionalText
    {
        private readonly SourceText _text;

        public InMemoryAdditionalText(string path, string content)
        {
            Path = path;
            _text = SourceText.From(content, System.Text.Encoding.UTF8);
        }

        public override string Path { get; }

        public override SourceText? GetText(CancellationToken cancellationToken = default)
        {
            return _text;
        }
    }
}
