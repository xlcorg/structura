using System.Collections.Immutable;
using System.Threading;

using FluentAssertions;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

using Structura.Generator;

using Xunit;

namespace Structura.UnitTests.Generator;

public sealed class StructuraXmlGeneratorTests
{
    private const string MinimalSample =
        "<?xml version=\"1.0\"?>\n" +
        "<order>\n" +
        "  <currency>RUB</currency>\n" +
        "  <version>7</version>\n" +
        "  <is_priority>true</is_priority>\n" +
        "  <total_amount>15499.95</total_amount>\n" +
        "  <customer><name>Alice</name></customer>\n" +
        "</order>";

    [Fact]
    public void Generator_EmitsOneSourceFile_NamedAfterSampleFile()
    {
        GeneratorDriverRunResult result = RunGenerator("blrwbl.sample.xml", "<BLRWBL/>");
        result.GeneratedTrees.Should().HaveCount(1);
        result.GeneratedTrees[0].FilePath.Should().EndWith("BlrwblSampleXml.g.cs");
    }

    [Fact]
    public void Generator_ProducesNoDiagnostics()
    {
        GeneratorDriverRunResult result = RunGenerator("order.xml", MinimalSample);
        result.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void Generator_EmitsClassDeclaration()
    {
        string source = GetGeneratedSource("order.xml", MinimalSample);
        source.Should().Contain("class OrderXml");
        source.Should().Contain("IStructuraXmlDocument<OrderXml>");
        source.Should().Contain("IStructuraDocument");
    }

    [Fact]
    public void Generator_EmitsScalarProperty_FromRootChildElement()
    {
        string source = GetGeneratedSource("order.xml", MinimalSample);
        source.Should().Contain("string Currency");
        source.Should().Contain("string Version");
        source.Should().Contain("string IsPriority");
        source.Should().Contain("string TotalAmount");
        source.Should().NotContain("long Version");
        source.Should().NotContain("bool IsPriority");
        source.Should().NotContain("decimal TotalAmount");
    }

    [Fact]
    public void Generator_SkipsResidualMixedContentElement()
    {
        // <title lang="ru">War</title> mixes a non-xmlns attribute with text
        // content — that's the residual STR0009 case. The element must NOT
        // surface as a property on the model.
        const string src =
            "<order>\n" +
            "  <currency>RUB</currency>\n" +
            "  <title lang=\"ru\">War</title>\n" +
            "</order>";

        string source = GetGeneratedSource("order.xml", src);
        source.Should().NotContain(" Title ");
        source.Should().NotContain("TitleType");
    }

    [Fact]
    public void Generator_EmitsScalarProperty_FromRootAttribute()
    {
        const string src = "<order id=\"42\" status=\"paid\"/>";
        string source = GetGeneratedSource("order.xml", src);

        source.Should().Contain("string Id");
        source.Should().Contain("string Status");
        source.Should().Contain("/@id");
        source.Should().Contain("/@status");
    }

    [Fact]
    public void Generator_HandlesEmptyRoot()
    {
        GeneratorDriverRunResult result = RunGenerator("empty.xml", "<empty/>");
        result.GeneratedTrees.Should().HaveCount(1);
        result.Diagnostics.Should().BeEmpty();
    }

    [Theory]
    [InlineData("data.txt")]
    [InlineData("config.json")]
    [InlineData("readme.md")]
    public void Generator_IgnoresNonXmlFiles(string fileName)
    {
        GeneratorDriverRunResult result = RunGenerator(fileName, "irrelevant");
        result.GeneratedTrees.Should().BeEmpty();
    }

    [Fact]
    public void Generator_SkipsUnparseableXml()
    {
        // Missing close tag → parser fails → generator emits nothing
        // rather than an uncompilable class.
        GeneratorDriverRunResult result = RunGenerator("broken.xml", "<root>");
        result.GeneratedTrees.Should().BeEmpty();
    }

    [Fact]
    public void Generator_RootNameDifferentFromClassName_EmitsValidationCheck()
    {
        const string src = "<DeliveryNote><id>1</id></DeliveryNote>";
        string source = GetGeneratedSource("waybill.xml", src);

        // The generated ParseFromXml validates the root element name matches
        // the literal name from the sample file.
        source.Should().Contain("DeliveryNote");
        source.Should().Contain("Expected <DeliveryNote> at root");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

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

    private static string GetGeneratedSource(string fileName, string xmlContent)
    {
        GeneratorDriverRunResult result = RunGenerator(fileName, xmlContent);
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
