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
/// Covers the four collection shapes (wrapper-as-nested-type, plural-wrapper-flat,
/// sibling-group, pure-text-leaf), recursive item collections, path composition,
/// pluralization, empty containers, and heterogeneous item field unions.
/// </summary>
public sealed class RepeatedElementsTests
{
    [Fact]
    public void Shape1_WrapperBecomesNestedType_AndCollectionLivesInside()
    {
        const string src =
            "<root><currency>BYN</currency>" +
            "<DespatchAdviceLogisticUnitLineItem>" +
            "<LineItem><n>1</n></LineItem>" +
            "<LineItem><n>2</n></LineItem>" +
            "</DespatchAdviceLogisticUnitLineItem></root>";

        string source = GetGeneratedSource("waybill.xml", src);

        source.Should().Contain("class DespatchAdviceLogisticUnitLineItemGroup");
        source.Should().Contain("IReadOnlyList<LineItem> LineItems");
        source.Should().Contain("DespatchAdviceLogisticUnitLineItemGroup DespatchAdviceLogisticUnitLineItem");
    }

    [Fact]
    public void Shape1_NestedAccess_HasWrapperLayerInPath()
    {
        const string src =
            "<root><currency>BYN</currency>" +
            "<DespatchAdviceLogisticUnitLineItem>" +
            "<LineItem><n>1</n></LineItem>" +
            "<LineItem><n>2</n></LineItem>" +
            "</DespatchAdviceLogisticUnitLineItem></root>";

        string source = GetGeneratedSource("waybill.xml", src);

        // Wrapper ctor passes path prefix "…/DespatchAdviceLogisticUnitLineItem"
        // and the inner list uses "…/LineItems/" + index.
        source.Should().Contain("\"/DespatchAdviceLogisticUnitLineItem\"");
        source.Should().Contain("\"/LineItems/\"");
    }

    [Fact]
    public void Shape2_PluralWrapper_FlattensAway()
    {
        // "books" == "book" + "s" → flat style: no BooksGroup, just Books property.
        const string src =
            "<root><version>1</version>" +
            "<books><book><title>T1</title></book><book><title>T2</title></book></books>" +
            "</root>";

        string source = GetGeneratedSource("library.xml", src);

        source.Should().Contain("IReadOnlyList<Book> Books");
        source.Should().NotContain("BooksGroup");
    }

    [Fact]
    public void Shape3_SiblingGroup_AlongsideScalars_ExposesAsCollection()
    {
        // Two <line> elements appear as direct siblings — no wrapper.
        const string src =
            "<order><currency>USD</currency>" +
            "<line><qty>5</qty></line>" +
            "<line><qty>3</qty></line>" +
            "</order>";

        string source = GetGeneratedSource("order.xml", src);

        source.Should().Contain("string Currency");
        source.Should().Contain("IReadOnlyList<Line> Lines");
        source.Should().Contain("class Line");
    }

    [Fact]
    public void Shape3_SingleOccurrence_BecomesNestedObject()
    {
        // A single structural element with heterogeneous children is a
        // nested object (Step 10), not a collection. The generator should
        // emit a typed property and class, but no IReadOnlyList<Meta>.
        const string src =
            "<order><currency>USD</currency>" +
            "<meta><key>a</key><val>b</val></meta>" +
            "</order>";

        string source = GetGeneratedSource("order.xml", src);

        source.Should().Contain("string Currency");
        source.Should().Contain("MetaType Meta");
        source.Should().Contain("class MetaType");
        source.Should().NotContain("IReadOnlyList<Meta>");
    }

    [Fact]
    public void Shape4_PureTextLeafItems_EmitListOfString()
    {
        // <genres> wrapper containing pure-text <genre> children → IReadOnlyList<string>.
        const string src =
            "<root><version>1</version>" +
            "<genres><genre>fiction</genre><genre>scifi</genre></genres>" +
            "</root>";

        string source = GetGeneratedSource("catalog.xml", src);

        source.Should().Contain("IReadOnlyList<string> Genres");
        source.Should().Contain("long Version");
        // Pure-text-leaf: no item class is emitted.
        source.Should().NotContain("class Genre");
    }

    [Fact]
    public void Recursive_ItemHasItsOwnCollection()
    {
        // <order> items each contain a <lines> collection of <line> items.
        const string src =
            "<root><orders>" +
            "<order><id>1</id><lines><line><qty>5</qty></line><line><qty>3</qty></line></lines></order>" +
            "<order><id>2</id><lines><line><qty>1</qty></line></lines></order>" +
            "</orders></root>";

        string source = GetGeneratedSource("store.xml", src);

        source.Should().Contain("class Order");
        source.Should().Contain("class Line");
        source.Should().Contain("IReadOnlyList<Line> Lines");
        source.Should().Contain("long Id");
        source.Should().Contain("long Qty");
    }

    [Fact]
    public void Path_NestedCollection_HasComposedIndices()
    {
        const string src =
            "<root><orders>" +
            "<order><id>1</id><lines><line><qty>5</qty></line></lines></order>" +
            "<order><id>2</id><lines><line><qty>1</qty></line></lines></order>" +
            "</orders></root>";

        string source = GetGeneratedSource("store.xml", src);

        // The Line setter records _pathPrefix + "/Qty" where _pathPrefix
        // was built from the parent + "/Lines/" + index.
        source.Should().Contain("\"/Lines/\"");
        source.Should().Contain("\"/Qty\"");
    }

    [Fact]
    public void Pluralization_AppendsSExceptWhenAlreadyEndsInS()
    {
        // "book" → "Books" (adds 's').
        const string bookSrc =
            "<root><version>1</version>" +
            "<books><book><id>1</id></book></books></root>";
        string source = GetGeneratedSource("p.xml", bookSrc);
        source.Should().Contain("IReadOnlyList<Book> Books");

        // "Class" ends in 's' → Pluralize keeps it without adding another 's'.
        const string classSrc =
            "<root><version>1</version>" +
            "<classes><class><id>1</id></class></classes></root>";
        string source2 = GetGeneratedSource("p2.xml", classSrc);
        source2.Should().NotContain("Classs");
        source2.Should().Contain("IReadOnlyList<Class>");
    }

    [Fact]
    public void EmptyContainer_NotACollection()
    {
        // <empty></empty> has zero children → TryClassifyAsWrapper returns null.
        const string src = "<root><version>1</version><empty></empty></root>";
        string source = GetGeneratedSource("e.xml", src);

        source.Should().Contain("long Version");
        source.Should().NotContain("class Empty");
        source.Should().NotContain("IReadOnlyList<Empty>");
        source.Should().NotContain("IReadOnlyList<string> Empty");
    }

    [Fact]
    public void HeterogeneousItems_EmitFieldUnion_DefaultsForMissing()
    {
        // item1 has <extra>, item2 does not → Extra is in the union type
        // with an _extraIsPresent guard; the setter throws StructuraMutationException.
        const string src =
            "<root><items>" +
            "<item><id>1</id><extra>x</extra></item>" +
            "<item><id>2</id></item>" +
            "</items></root>";

        string source = GetGeneratedSource("h.xml", src);

        source.Should().Contain("class Item");
        source.Should().Contain("long Id");
        source.Should().Contain("string Extra");
        source.Should().Contain("_extraIsPresent");
        source.Should().Contain("StructuraMutationException");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string GetGeneratedSource(string fileName, string xmlContent)
    {
        GeneratorDriverRunResult result = RunGenerator(fileName, xmlContent);
        result.GeneratedTrees.Should().HaveCount(1);
        return result.GeneratedTrees[0].GetText().ToString();
    }

    private static GeneratorDriverRunResult RunGenerator(string fileName, string xmlContent)
    {
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            references: new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            });

        var generator = new StructuraXmlGenerator();
        var additionalText = new InMemoryAdditionalText(fileName, xmlContent);

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
