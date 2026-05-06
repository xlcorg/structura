using System.Collections.Immutable;
using System.Threading;

using FluentAssertions;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

using Structura.Generator;

using Xunit;

namespace Structura.UnitTests.Generator;

public sealed class StructuraJsonGeneratorTests
{
    private const string MinimalSample =
        "{\n" +
        "  \"currency\": \"RUB\",\n" +
        "  \"version\": 7,\n" +
        "  \"is_priority\": true,\n" +
        "  \"total_amount\": 15499.95,\n" +
        "  \"notes\": null,\n" +
        "  \"customer\": { \"name\": \"Alice\" },\n" +
        "  \"items\": [1, 2]\n" +
        "}";

    [Fact]
    public void Generator_EmitsOneSourceFile_NamedAfterSampleFile()
    {
        var result = RunGenerator("order.sample.json", MinimalSample);
        result.GeneratedTrees.Should().HaveCount(1);
        result.GeneratedTrees[0].FilePath.Should().EndWith("OrderSampleJson.g.cs");
    }

    [Fact]
    public void Generator_ProducesNoDiagnostics()
    {
        var result = RunGenerator("order.sample.json", MinimalSample);
        result.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void Generator_EmitsClassDeclaration()
    {
        var source = GetGeneratedSource("order.sample.json", MinimalSample);
        source.Should().Contain("class OrderSampleJson");
        source.Should().Contain("IStructuraJsonDocument<OrderSampleJson>");
        source.Should().Contain("IStructuraDocument");
    }

    [Fact]
    public void Generator_EmitsStringProperty()
    {
        var source = GetGeneratedSource("order.sample.json", MinimalSample);
        source.Should().Contain("string Currency");
    }

    [Fact]
    public void Generator_EmitsLongProperty()
    {
        var source = GetGeneratedSource("order.sample.json", MinimalSample);
        source.Should().Contain("long Version");
    }

    [Fact]
    public void Generator_EmitsBoolProperty()
    {
        var source = GetGeneratedSource("order.sample.json", MinimalSample);
        source.Should().Contain("bool IsPriority");
    }

    [Fact]
    public void Generator_EmitsDecimalProperty()
    {
        var source = GetGeneratedSource("order.sample.json", MinimalSample);
        source.Should().Contain("decimal TotalAmount");
    }

    [Fact]
    public void Generator_EmitsNullableStringProperty_ForNullSampleValue()
    {
        var source = GetGeneratedSource("order.sample.json", MinimalSample);
        source.Should().Contain("string? Notes");
    }

    [Fact]
    public void Generator_SkipsObjectProperty()
    {
        var source = GetGeneratedSource("order.sample.json", MinimalSample);
        // "customer" object → no C# property emitted
        source.Should().NotContain("Customer\n").And.NotContain("Customer\r");
        // But ensure "Currency" IS there to rule out false negatives
        source.Should().Contain("Currency");
    }

    [Fact]
    public void Generator_SkipsArrayProperty()
    {
        var source = GetGeneratedSource("order.sample.json", MinimalSample);
        // "items" array → no C# property emitted
        source.Should().NotContain("Items\n").And.NotContain("Items\r");
    }

    [Fact]
    public void Generator_EmitsNamespace_StructuraGenerated()
    {
        var source = GetGeneratedSource("order.sample.json", MinimalSample);
        source.Should().Contain("namespace Structura.Generated");
    }

    [Fact]
    public void Generator_IgnoresNonSampleJsonFiles()
    {
        var result = RunGenerator("config.json", "{}");
        result.GeneratedTrees.Should().BeEmpty();
    }

    [Fact]
    public void Generator_HandlesEmptyObject()
    {
        var result = RunGenerator("empty.sample.json", "{}");
        result.GeneratedTrees.Should().HaveCount(1);
        result.Diagnostics.Should().BeEmpty();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

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

    private static string GetGeneratedSource(string fileName, string jsonContent)
    {
        var result = RunGenerator(fileName, jsonContent);
        result.GeneratedTrees.Should().HaveCount(1);
        return result.GeneratedTrees[0].GetText().ToString();
    }

    // ── In-memory AdditionalText stub ────────────────────────────────────────

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
            => _text;
    }
}
