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
/// Verifies that <see cref="StructuraDiagnostics"/> descriptors STR0001–STR0011
/// are emitted by the generators under the expected conditions.
/// </summary>
public sealed class StructuraDiagnosticsTests
{
    // ── Error diagnostics ─────────────────────────────────────────────────────

    [Fact]
    public void Generator_InvalidXml_EmitsSTR0002()
    {
        // Unclosed root tag — parser cannot recover.
        GeneratorDriverRunResult result = RunXmlGenerator("broken.xml", "<root>");

        result.GeneratedTrees.Should().BeEmpty();
        result.Diagnostics.Should().ContainSingle(d => d.Id == "STR0002")
            .Which.Severity.Should().Be(DiagnosticSeverity.Error);
    }

    [Fact]
    public void Generator_InvalidJson_EmitsSTR0001()
    {
        // Non-JSON input to the JSON generator.
        GeneratorDriverRunResult result = RunJsonGenerator("broken.json", "not json");

        result.GeneratedTrees.Should().BeEmpty();
        result.Diagnostics.Should().ContainSingle(d => d.Id == "STR0001")
            .Which.Severity.Should().Be(DiagnosticSeverity.Error);
    }

    // ── Warning diagnostics ───────────────────────────────────────────────────

    [Fact]
    public void Generator_DtdPresent_EmitsSTR0006Warning()
    {
        const string Src = "<!DOCTYPE root [<!ENTITY foo \"bar\">]><root><a>1</a></root>";
        GeneratorDriverRunResult result = RunXmlGenerator("dtd.xml", Src);

        result.Diagnostics.Should().Contain(d => d.Id == "STR0006")
            .Which.Severity.Should().Be(DiagnosticSeverity.Warning);
    }

    [Fact]
    public void Generator_UnknownEntity_EmitsSTR0007Warning()
    {
        const string Src = "<root><a>&unknown;</a></root>";
        GeneratorDriverRunResult result = RunXmlGenerator("entity.xml", Src);

        result.Diagnostics.Should().Contain(d => d.Id == "STR0007")
            .Which.Severity.Should().Be(DiagnosticSeverity.Warning);
    }

    [Fact]
    public void Generator_XmlnsDeclaration_EmitsSTR0008Warning()
    {
        const string Src = "<root xmlns=\"http://example.com\"><a>1</a></root>";
        GeneratorDriverRunResult result = RunXmlGenerator("ns.xml", Src);

        result.Diagnostics.Should().Contain(d => d.Id == "STR0008")
            .Which.Severity.Should().Be(DiagnosticSeverity.Warning);
    }

    [Fact]
    public void Generator_NestedStructural_EmitsSTR0009Warning()
    {
        // <nested attr="..." >text</nested> is the residual case after Step 10:
        // an attribute alongside text content (mixed content) is not modelled,
        // so the element is skipped with STR0009. A pure-structural element
        // like <nested><x>1</x><y>2</y></nested> would now be a nested object
        // and produce no warning.
        const string Src = "<root><a>1</a><nested attr=\"x\">text</nested></root>";
        GeneratorDriverRunResult result = RunXmlGenerator("structural.xml", Src);

        result.Diagnostics.Should().Contain(d => d.Id == "STR0009")
            .Which.Severity.Should().Be(DiagnosticSeverity.Warning);
    }

    [Fact]
    public void Generator_NestedStructural_DeduplicatesPerParentType()
    {
        // Two <item> elements both have a <meta lang="...">text</meta> child
        // (text+attribute residual) → only one STR0009 for the
        // ("Item", "meta") pair.
        const string Src =
            "<root><items>" +
            "<item><id>1</id><meta lang=\"a\">x</meta></item>" +
            "<item><id>2</id><meta lang=\"b\">y</meta></item>" +
            "</items></root>";

        GeneratorDriverRunResult result = RunXmlGenerator("dedup.xml", Src);

        result.Diagnostics.Count(d => d.Id == "STR0009").Should().Be(1);
    }

    [Fact]
    public void Generator_PureStructuralElement_DoesNotEmitSTR0009()
    {
        // After Step 10, a single-occurrence structural element with no
        // attributes is a nested object — no STR0009 should fire.
        const string Src = "<root><a>1</a><nested><x>1</x><y>2</y></nested></root>";
        GeneratorDriverRunResult result = RunXmlGenerator("nested.xml", Src);

        result.Diagnostics.Should().NotContain(d => d.Id == "STR0009");
    }

    [Fact]
    public void Generator_HeterogeneousJsonArray_EmitsSTR0010Warning()
    {
        // Mixed-shape array — primitive and object items are incompatible
        // for V1, so the property must be skipped with STR0010.
        const string Src = "{\"v\":1,\"mixed\":[{\"a\":1},2]}";
        GeneratorDriverRunResult result = RunJsonGenerator("hetero.json", Src);

        Diagnostic diag = result.Diagnostics
            .Should().ContainSingle(d => d.Id == "STR0010").Subject;
        diag.Severity.Should().Be(DiagnosticSeverity.Warning);
        diag.GetMessage().Should().Contain("mixed");
    }

    [Fact]
    public void Generator_EmptyJsonArray_EmitsSTR0011Warning()
    {
        const string Src = "{\"v\":1,\"archive\":[]}";
        GeneratorDriverRunResult result = RunJsonGenerator("empty.json", Src);

        Diagnostic diag = result.Diagnostics
            .Should().ContainSingle(d => d.Id == "STR0011").Subject;
        diag.Severity.Should().Be(DiagnosticSeverity.Warning);
        diag.GetMessage().Should().Contain("archive");
    }

    [Fact]
    public void STR0009_Message_IsFormatNeutral_NoXmlAngleBrackets()
    {
        // Foundation rewrote STR0009 to be reusable across formats: no '<…>'
        // decoration around element names, no V1 phrase. The XML generator
        // still emits it for residual text+attribute / mixed-content cases,
        // but the wording is format-neutral.
        const string Src = "<root><a>1</a><nested attr=\"x\">text</nested></root>";
        GeneratorDriverRunResult result = RunXmlGenerator("structural.xml", Src);

        Diagnostic diag = result.Diagnostics.Should()
            .ContainSingle(d => d.Id == "STR0009").Subject;

        string message = diag.GetMessage();
        message.Should().NotContain("<");
        message.Should().NotContain(">");
        message.Should().Contain("nested generation is not supported");
    }

    [Fact]
    public void Diagnostic_Location_PointsAtFile()
    {
        GeneratorDriverRunResult result = RunXmlGenerator("myfile.xml", "<root>");

        Diagnostic diag = result.Diagnostics.Should()
            .ContainSingle(d => d.Id == "STR0002").Subject;

        diag.Location.GetMappedLineSpan().Path.Should().Be("myfile.xml");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static GeneratorDriverRunResult RunXmlGenerator(string fileName, string content)
    {
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            references: new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            });

        var generator = new StructuraXmlGenerator();
        var additionalText = new InMemoryAdditionalText(fileName, content);

        var driver = CSharpGeneratorDriver.Create(
                generators: new[] { generator.AsSourceGenerator() },
                additionalTexts: ImmutableArray.Create<AdditionalText>(additionalText))
            .RunGenerators(compilation);

        return driver.GetRunResult();
    }

    private static GeneratorDriverRunResult RunJsonGenerator(string fileName, string content)
    {
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            references: new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            });

        var generator = new StructuraJsonGenerator();
        var additionalText = new InMemoryAdditionalText(fileName, content);

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
