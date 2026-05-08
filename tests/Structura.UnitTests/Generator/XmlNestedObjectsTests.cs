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
/// Source-text tests for the XML generator's nested-object emission path:
/// type emission with a <c>Type</c> suffix, recursive class generation,
/// path-prefix composition through multiple levels, namespace-prefixed
/// element name sanitisation, and the still-residual STR0009 cases.
/// Complements <see cref="StructuraXmlGeneratorTests"/> which covers the
/// basic root-scalar / collection emission.
/// </summary>
public sealed class XmlNestedObjectsTests
{
    // Inputs use heterogeneously-named children inside the nested element.
    // A single-child structural element is classified as a wrapper-style
    // collection (Step 8) instead of a nested object — we want to exercise
    // the new path, not the old one.

    [Fact]
    public void SingleNestedObject_EmitsTypedPropertyAndPartialClass()
    {
        const string Src =
            "<order><currency>USD</currency>" +
            "<customer><name>Alice</name><email>a@x</email></customer>" +
            "</order>";

        string source = Generate(Src);

        source.Should().Contain("public CustomerType Customer");
        source.Should().Contain("public sealed partial class CustomerType");
    }

    [Fact]
    public void NestedObject_RootCtor_PassesLiteralPathPrefix()
    {
        const string Src =
            "<order><currency>USD</currency>" +
            "<customer><name>Alice</name><email>a@x</email></customer>" +
            "</order>";

        string source = Generate(Src);

        // Root-level construction: literal path "/Customer", not _pathPrefix + …
        source.Should().Contain("new CustomerType(_ctx, \"/Customer\"");
    }

    [Fact]
    public void RecursiveNesting_ComposesPathAtEachDepth()
    {
        const string Src =
            "<order><currency>USD</currency>" +
            "<customer>" +
            "<address><city>NYC</city><zip>10001</zip></address>" +
            "<email>a@x</email>" +
            "</customer>" +
            "</order>";

        string source = Generate(Src);

        // Two nested classes are produced.
        source.Should().Contain("class CustomerType");
        source.Should().Contain("class AddressType");

        // Construction at each level uses the appropriate prefix.
        source.Should().Contain("new CustomerType(_ctx, \"/Customer\"");
        source.Should().Contain("new AddressType(_ctx, _pathPrefix + \"/Address\"");

        // Leaf scalar setter records via _pathPrefix.
        source.Should().Contain("_pathPrefix + \"/City\"");
    }

    [Fact]
    public void NamespacedElement_DerivesValidIdentifier()
    {
        // <meta:info> contains a colon — IdentifierSanitizer must collapse
        // it to MetaInfo / MetaInfoType, both valid C# names. The version
        // attribute on root is here to suppress the wrapper-chain descent
        // so meta:info actually classifies as a nested object.
        const string Src =
            "<root xmlns:meta=\"u\" version=\"1\">" +
            "<meta:info><meta:total>5</meta:total><meta:label>x</meta:label></meta:info>" +
            "</root>";

        string source = Generate(Src);

        source.Should().Contain("public MetaInfoType MetaInfo");
        source.Should().Contain("class MetaInfoType");
        // The literal element name is preserved in RequireElement so the
        // runtime resolves the actual namespaced child.
        source.Should().Contain("RequireElement(\"meta:info\")");
        // No raw colon should leak into the generated identifiers.
        source.Should().NotContain("MetaInfoType:");
        source.Should().NotContain("Meta:info");
    }

    [Fact]
    public void NestedObject_ScalarSetter_UsesRequireElementNotFind()
    {
        // Nested-object scalars are required by construction (single
        // observation) so the ctor uses RequireElement, not FindElement +
        // IsPresent guard.
        const string Src =
            "<order><currency>USD</currency>" +
            "<customer><name>Alice</name><email>a@x</email></customer>" +
            "</order>";

        string source = Generate(Src);

        source.Should().Contain("element.RequireElement(\"name\")");
        // The IsPresent flag is the item-scalar pattern; nested scalars
        // shouldn't carry one for their fields.
        source.Should().NotContain("_nameIsPresent");
    }

    [Fact]
    public void NestedObjectInsideCollectionItem_EmitsNestedTypeOnce()
    {
        // Each <book> has an <author> nested object with heterogeneous
        // children. The body should be built from the union of observations
        // in the items, and the type emitted exactly once at the outer
        // scope.
        const string Src =
            "<library><books>" +
            "<book><title>A</title><author><first>X</first><last>O</last></author></book>" +
            "<book><title>B</title><author><first>Y</first><last>P</last></author></book>" +
            "</books></library>";

        string source = Generate(Src);

        source.Should().Contain("public AuthorType Author");
        source.Should().Contain("class AuthorType");

        // Item ctor wires the Author with the per-item path prefix.
        source.Should().Contain("element.RequireElement(\"author\")");
        source.Should().Contain("new AuthorType(_ctx, _pathPrefix + \"/Author\"");

        // The class is emitted exactly once.
        int firstOccurrence = source.IndexOf("class AuthorType", System.StringComparison.Ordinal);
        int lastOccurrence = source.LastIndexOf("class AuthorType", System.StringComparison.Ordinal);
        firstOccurrence.Should().Be(lastOccurrence);
    }

    [Fact]
    public void EmptyStructuralElement_RemainsResidualSTR0009()
    {
        // A self-closing element with attributes has zero children — the
        // parser rejects it as a nested object and the generator surfaces
        // STR0009 instead. (Self-closing without attributes is a pure-text
        // scalar handled by ClassifyChild and is covered elsewhere.)
        const string Src = "<root><a>1</a><empty attr=\"x\"/></root>";

        GeneratorDriverRunResult result = RunGenerator("residual.xml", Src);

        result.Diagnostics.Should().Contain(d => d.Id == "STR0009");
    }

    [Fact]
    public void TextPlusAttribute_RemainsResidualSTR0009()
    {
        // <title lang="ru">War</title> mixes a non-xmlns attribute with text
        // content — Step 11 territory, still STR0009 in Step 10.
        const string Src = "<root><a>1</a><title lang=\"ru\">War</title></root>";

        GeneratorDriverRunResult result = RunGenerator("title.xml", Src);

        result.Diagnostics.Should().Contain(d => d.Id == "STR0009");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string Generate(string xmlContent)
    {
        GeneratorDriverRunResult result = RunGenerator("nested.xml", xmlContent);
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
