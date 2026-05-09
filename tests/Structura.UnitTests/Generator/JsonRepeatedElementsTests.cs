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
/// Source-text tests for JSON array emission: object-arrays vs primitive-arrays,
/// item-class naming (singularisation, "data" → "DataItem" fallback), nested
/// arrays inside item shapes, and diagnostic dispatch (STR0010/STR0011) for
/// arrays that appear at depth.
/// </summary>
public sealed class JsonRepeatedElementsTests
{
    // ── Object arrays — item-class naming ────────────────────────────────────

    [Fact]
    public void ObjectArray_PluralKey_DepluralisesItemClassName()
    {
        const string src = "{\"books\":[{\"id\":\"a\"}]}";

        string source = Generate(src);

        source.Should().Contain("public sealed partial class Book");
        source.Should().Contain("IReadOnlyList<Book> Books");
    }

    [Fact]
    public void ObjectArray_NonPluralKey_FallsBackToItemSuffix()
    {
        // 'data' has no trailing 's' to drop — emitter must avoid CS0102 by
        // suffixing with "Item" rather than reusing "Data" for both property
        // and class.
        const string src = "{\"data\":[{\"k\":\"v\"}]}";

        string source = Generate(src);

        source.Should().Contain("public sealed partial class DataItem");
        source.Should().Contain("IReadOnlyList<DataItem> Data");
    }

    // ── Primitive arrays ─────────────────────────────────────────────────────

    [Fact]
    public void PrimitiveArray_OfDecimals_EmitsIReadOnlyListDecimal()
    {
        // A decimal is detected when at least one observed number has a
        // fractional component or scientific notation.
        const string src = "{\"prices\":[1.5,2.0,3.25]}";

        string source = Generate(src);

        source.Should().Contain("IReadOnlyList<decimal> Prices");
    }

    [Fact]
    public void PrimitiveArray_NoItemClassEmitted_BecauseLeavesAreScalars()
    {
        // Sanity — primitive arrays must not produce an item class.
        const string src = "{\"tags\":[\"a\",\"b\"]}";

        string source = Generate(src);

        source.Should().NotContain("partial class Tag");
        source.Should().NotContain("partial class Tags");
    }

    // ── Nested arrays inside an item ─────────────────────────────────────────

    [Fact]
    public void ItemContainsNestedPrimitiveArray_EmitsCollectionInsideItemClass()
    {
        // items[].tags is an array of strings inside the item shape. The Item
        // class must expose IReadOnlyList<string> Tags, and the per-item path
        // composition is the runtime concern.
        const string src = "{\"items\":[{\"sku\":\"A\",\"tags\":[\"x\",\"y\"]}]}";

        string source = Generate(src);

        source.Should().Contain("public sealed partial class Item");
        source.Should().Contain("IReadOnlyList<string> Tags");
    }

    [Fact]
    public void ItemContainsNestedObjectArray_EmitsRecursiveItemClass()
    {
        // items[].lines is itself an object-array, generating a "Line" class
        // nested in the parent class output.
        const string src =
            "{\"items\":[{\"id\":1,\"lines\":[{\"qty\":2}]}]}";

        string source = Generate(src);

        source.Should().Contain("public sealed partial class Item");
        source.Should().Contain("public sealed partial class Line");
        source.Should().Contain("IReadOnlyList<Line> Lines");
    }

    // ── Per-item path composition ────────────────────────────────────────────

    [Fact]
    public void ObjectArray_ItemPathPrefix_IsPrebakedSingleLiteral()
    {
        // Step 9 main chunk decided to bake the trailing slash into the
        // path-prefix literal: "/items/" + idx, not "/items" + "/" + idx.
        const string src = "{\"items\":[{\"id\":1}]}";

        string source = Generate(src);

        source.Should().Contain("\"/items/\" + idx_items");
        source.Should().NotContain("\"/items\" + \"/\"");
    }

    // ── Diagnostics from arrays at depth ─────────────────────────────────────

    [Fact]
    public void EmptyArray_InsideArrayItem_StillFiresSTR0011()
    {
        // The diagnostic walker must recurse into items[].x to surface
        // empty-array warnings at any depth.
        const string src =
            "{\"items\":[{\"id\":1,\"flags\":[]}]}";

        GeneratorDriverRunResult result = RunGenerator("doc.json", src);

        result.Diagnostics.Should().ContainSingle(d => d.Id == "STR0011")
            .Which.GetMessage().Should().Contain("flags");
    }

    [Fact]
    public void HeterogeneousArray_InsideNestedObject_StillFiresSTR0010()
    {
        const string src =
            "{\"customer\":{\"mixed\":[1,\"two\"]}}";

        GeneratorDriverRunResult result = RunGenerator("doc.json", src);

        result.Diagnostics.Should().ContainSingle(d => d.Id == "STR0010")
            .Which.GetMessage().Should().Contain("mixed");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string Generate(string jsonContent)
    {
        GeneratorDriverRunResult result = RunGenerator("repeated.json", jsonContent);
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

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
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
