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
/// Covers the wrapper-flattening rule: a single-element envelope wrapper
/// (zero attributes, zero pure-text children, one structural child) is
/// transparently descended through so the inner element's scalars become
/// the document's mutable properties.
/// </summary>
public sealed class WrapperFlatteningTests
{
    [Fact]
    public void Flatten_SingleWrapper_ExposesInnerScalars()
    {
        const string Src = "<wrapper><inner><a>1</a><b>two</b></inner></wrapper>";
        string source = GetGeneratedSource("envelope.xml", Src);

        // <inner>'s scalars become properties of the model.
        source.Should().Contain("long A");
        source.Should().Contain("string B");
        // The literal root <wrapper> name is still validated.
        source.Should().Contain("Expected <wrapper> at root");
        // Effective root <inner> is reached via RequireElement.
        source.Should().Contain("RequireElement(\"inner\")");
    }

    [Fact]
    public void Flatten_RecursiveWrapper_DescendsAllLevels()
    {
        const string Src = "<a><b><c><x>1</x></c></b></a>";
        string source = GetGeneratedSource("nested.xml", Src);

        source.Should().Contain("long X");
        // Three-level descent — both intermediate wrappers walked.
        source.Should()
            .Contain("RequireElement(\"b\")").And
            .Contain("RequireElement(\"c\")");
    }

    [Fact]
    public void Flatten_StopsAtAttributes()
    {
        const string Src = "<wrapper id=\"1\"><inner><a>1</a></inner></wrapper>";
        string source = GetGeneratedSource("attrs.xml", Src);

        // Literal root carries `id` → wrapper-flatten does not descend; <wrapper>
        // is the effective root, so `Id` becomes a scalar property here. Step 8
        // additionally classifies <inner> as a Pattern A wrapper-style collection,
        // so the generator emits an Inner property + InnerGroup nested type.
        source.Should().Contain("long Id");
        source.Should().Contain("InnerGroup Inner");
    }

    [Fact]
    public void Flatten_StopsAtPureTextChild()
    {
        // Root has a pure-text child <currency> AND a structural child
        // <customer> — wrapper-flattening at the literal root does NOT fire,
        // so <order> stays as the effective root with `Currency` exposed as a
        // scalar. Step 8 then sees <customer> as a Pattern A wrapper around
        // <name> and emits a Customer property + CustomerGroup nested type.
        const string Src = "<order><currency>RUB</currency><customer><name>Alice</name></customer></order>";
        string source = GetGeneratedSource("order.xml", Src);

        source.Should().Contain("string Currency");
        source.Should().Contain("CustomerGroup Customer");
    }

    [Fact]
    public void Flatten_StopsAtMultipleElementChildren()
    {
        // Two children → not a wrapper. Each becomes a scalar property.
        const string Src = "<order><a/><b/></order>";
        string source = GetGeneratedSource("order.xml", Src);

        // Self-closing children with no attributes are pure-text scalars
        // (string, empty content) — both should appear.
        source.Should().Contain("string A");
        source.Should().Contain("string B");
        // No wrapper descent — `effectiveRoot` is never introduced when the
        // literal root IS the data root.
        source.Should().NotContain("effectiveRoot");
    }

    [Fact]
    public void Flatten_PathPrefixIsRootRelative()
    {
        const string Src = "<BLRWBL><DeliveryNote><Currency>BYN</Currency></DeliveryNote></BLRWBL>";
        string source = GetGeneratedSource("blrwbl.sample.xml", Src);

        // Setter records "/Currency", not "/BLRWBL/DeliveryNote/Currency".
        source.Should().Contain("\"/Currency\"");
        source.Should().NotContain("\"/BLRWBL");
        source.Should().NotContain("\"/DeliveryNote");
    }

    [Fact]
    public void Flatten_GeneratesValidationForLiteralRoot()
    {
        // Even with flattening, the parse-time root-name validation refers
        // to the LITERAL root, not the effective root.
        const string Src = "<BLRWBL><DeliveryNote><Currency>BYN</Currency></DeliveryNote></BLRWBL>";
        string source = GetGeneratedSource("blrwbl.sample.xml", Src);

        source.Should().Contain("Expected <BLRWBL> at root");
        source.Should().NotContain("Expected <DeliveryNote>");
    }

    [Fact]
    public void Flatten_NoWrapper_NoEffectiveRootVariable()
    {
        // When the literal root IS the effective root, the emitted source
        // passes `root` directly to the constructor — no `effectiveRoot`
        // intermediate is generated.
        const string Src = "<order><currency>RUB</currency></order>";
        string source = GetGeneratedSource("order.xml", Src);

        source.Should().Contain("return new OrderXml(source, root);");
        source.Should().NotContain("effectiveRoot");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string GetGeneratedSource(string fileName, string xmlContent)
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

        GeneratorDriverRunResult result = driver.GetRunResult();
        result.GeneratedTrees.Should().HaveCount(1);
        return result.GeneratedTrees[0].GetText().ToString();
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
