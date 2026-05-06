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
    public void Generator_ProcessesPlainJsonFile_NoSampleInfix()
    {
        // Filter is now ".json" — plain "customer.json" must produce a model.
        var result = RunGenerator("customer.json", "{ \"name\": \"Alice\" }");
        result.GeneratedTrees.Should().HaveCount(1);
        result.GeneratedTrees[0].FilePath.Should().EndWith("CustomerJson.g.cs");
        result.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void Generator_ProcessesAnyJsonExtension_RegardlessOfInfix()
    {
        // "config.json" used to be ignored; now it's a first-class input.
        var result = RunGenerator("config.json", "{ \"port\": 8080 }");
        result.GeneratedTrees.Should().HaveCount(1);
        result.GeneratedTrees[0].FilePath.Should().EndWith("ConfigJson.g.cs");
    }

    [Fact]
    public void Generator_ProcessesMultipleJsonFiles_OnePerFile()
    {
        var result = RunGenerator(new[]
        {
            ("order.json",    "{ \"id\": 1 }"),
            ("customer.json", "{ \"name\": \"Bob\" }"),
        });

        result.GeneratedTrees.Should().HaveCount(2);
        result.GeneratedTrees.Select(t => t.FilePath)
            .Should().Contain(p => p.EndsWith("OrderJson.g.cs"))
            .And.Contain(p => p.EndsWith("CustomerJson.g.cs"));
    }

    [Theory]
    [InlineData("data.txt")]
    [InlineData("config.xml")]
    [InlineData("readme.md")]
    [InlineData("schema.yaml")]
    public void Generator_IgnoresNonJsonFiles(string fileName)
    {
        var result = RunGenerator(fileName, "irrelevant content");
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
        => RunGenerator(new[] { (fileName, jsonContent) });

    private static GeneratorDriverRunResult RunGenerator(
        (string fileName, string jsonContent)[] files)
    {
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            references: new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            });

        var generator = new StructuraJsonGenerator();

        var additionalTexts = files
            .Select(f => (AdditionalText)new InMemoryAdditionalText(f.fileName, f.jsonContent))
            .ToImmutableArray();

        var driver = CSharpGeneratorDriver.Create(
                generators: new[] { generator.AsSourceGenerator() },
                additionalTexts: additionalTexts)
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
