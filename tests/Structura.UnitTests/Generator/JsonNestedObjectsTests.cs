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
/// Source-text tests for the JSON generator's nested-object emission path:
/// recursive class generation, JSON Pointer composition through multiple
/// levels, RFC 6901 escaping of special characters in keys, identifier
/// collision sanitisation, and the required-key contract (RequireProperty
/// throws when absent — the V1 contract for required keys in nested objects).
/// Complements <see cref="JsonModelEmitterTests"/> which covers the basic
/// nested-object emission shape.
/// </summary>
public sealed class JsonNestedObjectsTests
{
    [Fact]
    public void NestedPath_ThreeLevels_ComposesAtEachDepth()
    {
        const string src =
            "{\"customer\":{\"preferences\":{\"marketing\":{\"consent\":true}}}}";

        string source = Generate(src);

        // Three nested classes produced.
        source.Should().Contain("class CustomerType");
        source.Should().Contain("class PreferencesType");
        source.Should().Contain("class MarketingType");

        // Path prefixes built at each level: root → "/customer", inside that
        // → _pathPrefix + "/preferences", inside that → _pathPrefix + "/marketing".
        source.Should().Contain("new CustomerType(_ctx, \"/customer\"");
        source.Should().Contain("new PreferencesType(_ctx, _pathPrefix + \"/preferences\"");
        source.Should().Contain("new MarketingType(_ctx, _pathPrefix + \"/marketing\"");

        // Leaf scalar setter records the deepest path component.
        source.Should().Contain("_pathPrefix + \"/consent\"");
    }

    [Fact]
    public void NestedRequiredKey_UsesRequireProperty_SoConstructorThrowsWhenAbsent()
    {
        // 'preferences' appears in the only observation → required → emitter
        // must use RequireProperty, which throws JsonParseException when the
        // key is missing in the parsed source. This is the V1 contract for
        // required nested objects.
        const string src = "{\"customer\":{\"preferences\":{\"opt_in\":true}}}";

        string source = Generate(src);

        source.Should().Contain("RequireProperty(\"customer\")");
        source.Should().Contain("RequireProperty(\"preferences\")");
    }

    [Fact]
    public void NestedKey_WithSlash_EscapedAsRfc6901_TildeOne()
    {
        // RFC 6901: '/' inside a reference token escapes as '~1'.
        // The emitter must bake the escaped form into the generated literal —
        // no runtime escaping for paths.
        const string src = "{\"customer\":{\"a/b\":\"x\"}}";

        string source = Generate(src);

        source.Should().Contain("_pathPrefix + \"/a~1b\"");
        source.Should().NotContain("\"/a/b\"");
    }

    [Fact]
    public void NestedKey_WithTilde_EscapedAsRfc6901_TildeZero()
    {
        // RFC 6901: '~' inside a reference token escapes as '~0'.
        const string src = "{\"customer\":{\"~weird\":\"x\"}}";

        string source = Generate(src);

        source.Should().Contain("_pathPrefix + \"/~0weird\"");
    }

    [Fact]
    public void NestedSiblingsSanitisingToSameIdentifier_GetUniqueNames()
    {
        // 'first-name' and 'first_name' both ToPascalCase → "FirstName".
        // IdentifierSanitizer.Sanitize must collision-resolve the second one
        // (current scheme: numeric suffix → "FirstName2"), and both original
        // keys must round-trip in JSON Pointer setters.
        const string src = "{\"customer\":{\"first-name\":\"a\",\"first_name\":\"b\"}}";

        string source = Generate(src);

        source.Should().Contain("public string FirstName\n");
        source.Should().Contain("public string FirstName2\n");

        source.Should().Contain("_pathPrefix + \"/first-name\"");
        source.Should().Contain("_pathPrefix + \"/first_name\"");
    }

    [Fact]
    public void NestedKey_StartingWithDigit_GetsValidCSharpIdentifier()
    {
        // '2nd_line' starts with a digit. ToPascalCase prepends an underscore
        // → "_2ndLine". The original key still appears verbatim in the
        // JSON Pointer literal.
        const string payload = "{\"address\":{\"2nd_line\":\"x\"}}";

        string source = Generate(payload);

        source.Should().Contain("public string _2ndLine\n");
        source.Should().Contain("_pathPrefix + \"/2nd_line\"");
    }

    [Fact]
    public void DeeplyNestedSetter_RecordsFullPath_ViaRuntimePathPrefix()
    {
        // Path composition is runtime-side: each nested type's setter
        // appends its key to its received _pathPrefix. The root never
        // hard-codes "/customer/preferences" — only "/customer". This test
        // pins that contract by checking the leaf scalar's setter only sees
        // its own segment and the prefix.
        const string src = "{\"customer\":{\"prefs\":{\"flag\":true}}}";

        string source = Generate(src);

        source.Should().Contain("_pathPrefix + \"/flag\"");
        source.Should().NotContain("\"/customer/prefs/flag\"");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string Generate(string jsonContent)
    {
        GeneratorDriverRunResult result = RunGenerator("nested.json", jsonContent);
        result.GeneratedTrees.Should().HaveCount(1);
        // Emitter uses StringBuilder.AppendLine → Environment.NewLine, so the
        // raw text is "\r\n" on Windows. Normalize to "\n" so assertions like
        // Contain("public string _2ndLine\n") work cross-platform.
        return result.GeneratedTrees[0].GetText().ToString().ReplaceLineEndings("\n");
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
