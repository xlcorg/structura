using System.Collections.Immutable;
using System.Linq;
using System.Threading;

using FluentAssertions;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

using Structura.Generator;

using Xunit;

namespace Structura.UnitTests.Generator;

/// <summary>
/// Source-text tests for <see cref="JsonModelEmitter"/> covering the new
/// Step 9 emission paths: nested objects, primitive/object array collections,
/// heterogeneous field unions in array items, and diagnostic dispatch for
/// empty / heterogeneous arrays. Runtime-execution behaviour (parse → mutate →
/// byte-equality) is covered by integration tests in Step 9 tests commit.
/// </summary>
public sealed class JsonModelEmitterTests
{
    // ── Nested objects ────────────────────────────────────────────────────────

    [Fact]
    public void NestedObject_GeneratesNestedPartialClass()
    {
        const string Src = "{\"customer\":{\"first_name\":\"Alice\",\"age\":30}}";

        string source = Generate(Src);

        source.Should().Contain("public sealed partial class CustomerType");
        source.Should().Contain("string FirstName");
        source.Should().Contain("long Age");
    }

    [Fact]
    public void NestedObject_RootHasPropertyForNestedType()
    {
        const string Src = "{\"customer\":{\"first_name\":\"Alice\"}}";

        string source = Generate(Src);

        source.Should().Contain("public CustomerType Customer");
    }

    [Fact]
    public void NestedObject_RootCtor_PassesPathPrefixToNestedCtor()
    {
        const string Src = "{\"customer\":{\"first_name\":\"Alice\"}}";

        string source = Generate(Src);

        source.Should().Contain("new CustomerType(_ctx, \"/customer\"");
    }

    [Fact]
    public void NestedObject_SetterRecordsNestedJsonPointer()
    {
        const string Src = "{\"customer\":{\"first_name\":\"Alice\"}}";

        string source = Generate(Src);

        source.Should().Contain("_pathPrefix + \"/first_name\"");
    }

    [Fact]
    public void NestedObject_DeeplyNested_GeneratesEachLevel()
    {
        const string Src = "{\"customer\":{\"prefs\":{\"newsletter\":true}}}";

        string source = Generate(Src);

        source.Should().Contain("public sealed partial class CustomerType");
        source.Should().Contain("public sealed partial class PrefsType");
        source.Should().Contain("public PrefsType Prefs");
        source.Should().Contain("bool Newsletter");
    }

    [Fact]
    public void NestedObject_KebabCaseKey_EscapedInPath()
    {
        // marketing-consent contains no '/' or '~' — JSON Pointer is /marketing-consent.
        const string Src = "{\"prefs\":{\"marketing-consent\":true}}";

        string source = Generate(Src);

        source.Should().Contain("bool MarketingConsent");
        source.Should().Contain("_pathPrefix + \"/marketing-consent\"");
    }

    // ── Primitive arrays ─────────────────────────────────────────────────────

    [Fact]
    public void PrimitiveArray_OfStrings_EmitsIReadOnlyListString()
    {
        const string Src = "{\"tags\":[\"a\",\"b\"]}";

        string source = Generate(Src);

        source.Should().Contain("IReadOnlyList<string> Tags");
    }

    [Fact]
    public void PrimitiveArray_OfLongs_EmitsIReadOnlyListLong()
    {
        const string Src = "{\"years\":[1865,1872]}";

        string source = Generate(Src);

        source.Should().Contain("IReadOnlyList<long> Years");
    }

    [Fact]
    public void PrimitiveArray_OfBooleans_EmitsIReadOnlyListBool()
    {
        const string Src = "{\"flags\":[true,false]}";

        string source = Generate(Src);

        source.Should().Contain("IReadOnlyList<bool> Flags");
    }

    // ── Object arrays ────────────────────────────────────────────────────────

    [Fact]
    public void ObjectArray_EmitsItemTypeAndCollectionProperty()
    {
        // The JSON key 'items' (already plural) becomes the property name
        // 'Items' and is depluralised to 'Item' for the item class — avoids
        // C# 'class same as property' confusion. Mirrors XML's Lines / Line.
        const string Src = "{\"items\":[{\"id\":1,\"sku\":\"A\"}]}";

        string source = Generate(Src);

        source.Should().Contain("public sealed partial class Item");
        source.Should().Contain("IReadOnlyList<Item> Items");
    }

    [Fact]
    public void ObjectArray_ItemPathPrefix_IncludesIndex()
    {
        const string Src = "{\"items\":[{\"id\":1}]}";

        string source = Generate(Src);

        // Root constructor builds each item with a pathPrefix of the form
        // "/items/0", "/items/1", ... composed at runtime.
        source.Should().Contain("\"/items/\"");
    }

    [Fact]
    public void ObjectArray_ItemSetter_UsesPathPrefix()
    {
        const string Src = "{\"items\":[{\"sku\":\"A\"}]}";

        string source = Generate(Src);

        source.Should().Contain("_pathPrefix + \"/sku\"");
    }

    // ── Heterogeneous field unions in array items ────────────────────────────

    [Fact]
    public void HeterogeneousItems_EmitFieldUnion_WithIsPresentGuard()
    {
        // First item has 'note', second does not → Note is in the union with
        // an _noteIsPresent guard and a throwing setter when absent.
        const string Src =
            "{\"items\":[" +
            "{\"id\":1,\"note\":\"x\"}," +
            "{\"id\":2}" +
            "]}";

        string source = Generate(Src);

        source.Should().Contain("string Note");
        source.Should().Contain("_noteIsPresent");
        source.Should().Contain("StructuraMutationException");
    }

    [Fact]
    public void HeterogeneousItems_RequiredFields_HaveNoIsPresentGuard()
    {
        // 'id' is present in both items → required, no IsPresent flag needed.
        const string Src =
            "{\"items\":[" +
            "{\"id\":1,\"sku\":\"A\"}," +
            "{\"id\":2,\"sku\":\"B\"}" +
            "]}";

        string source = Generate(Src);

        source.Should().NotContain("_idIsPresent");
        source.Should().NotContain("_skuIsPresent");
    }

    // ── Diagnostic dispatch for empty and heterogeneous arrays ───────────────

    [Fact]
    public void EmptyArray_EmitsSTR0011Warning_AndSkipsProperty()
    {
        const string Src = "{\"version\":1,\"archive\":[]}";

        GeneratorDriverRunResult result = RunGenerator("doc.json", Src);

        result.Diagnostics.Should().ContainSingle(d => d.Id == "STR0011")
            .Which.Severity.Should().Be(DiagnosticSeverity.Warning);
        GeneratedSource(result).Should().NotContain("Archive");
    }

    [Fact]
    public void HeterogeneousArray_EmitsSTR0010Warning_AndSkipsProperty()
    {
        const string Src = "{\"version\":1,\"mixed\":[1,\"two\"]}";

        GeneratorDriverRunResult result = RunGenerator("doc.json", Src);

        result.Diagnostics.Should().ContainSingle(d => d.Id == "STR0010")
            .Which.Severity.Should().Be(DiagnosticSeverity.Warning);
        GeneratedSource(result).Should().NotContain("Mixed");
    }

    // ── Existing root-level emission stays intact ────────────────────────────

    [Fact]
    public void RootScalars_StillEmitted_WithCustomerSibling()
    {
        // Sanity: nested object emission must not shadow root-level scalars.
        const string Src = "{\"currency\":\"USD\",\"customer\":{\"first_name\":\"Alice\"}}";

        string source = Generate(Src);

        source.Should().Contain("string Currency");
        source.Should().Contain("public sealed partial class CustomerType");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string Generate(string jsonContent)
    {
        GeneratorDriverRunResult result = RunGenerator("sample.json", jsonContent);
        result.GeneratedTrees.Should().HaveCount(1);
        return result.GeneratedTrees[0].GetText().ToString();
    }

    private static string GeneratedSource(GeneratorDriverRunResult result)
    {
        return result.GeneratedTrees.Single().GetText().ToString();
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
