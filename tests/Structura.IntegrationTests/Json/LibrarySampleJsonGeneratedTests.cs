using System.IO;
using System.Linq;

using FluentAssertions;

using Structura.Generated;
using Structura.Runtime;

using Xunit;

namespace Structura.IntegrationTests.Json;

/// <summary>
/// End-to-end tests for the source-generator-produced
/// <see cref="LibrarySampleJson"/> model, exercising the Step 9 JSON pipeline
/// against <c>library.sample.json</c>: optional scalar fields with throwing
/// setter, nullable-string promotion at depth (Publisher.Address.City),
/// primitive arrays of strings and longs, and silent-drop for empty arrays.
/// </summary>
public sealed class LibrarySampleJsonGeneratedTests
{
    private static string LoadSample()
    {
        return File.ReadAllText("library.sample.json");
    }

    // ── Round-trip ────────────────────────────────────────────────────────────

    [Fact]
    public void NoMutation_ToJson_IsByteIdentical()
    {
        string json = LoadSample();
        var library = json.ParseJson<LibrarySampleJson>();

        library.ToJson().Should().Be(json);
        ((IStructuraDocument)library).Changes.Should().BeEmpty();
    }

    // ── Root scalars ──────────────────────────────────────────────────────────

    [Fact]
    public void RootScalars_AreReadable()
    {
        var library = LoadSample().ParseJson<LibrarySampleJson>();

        library.Name.Should().Be("Public Library");
        library.Created.Should().Be("2026-01-15");
        library.Updated.Should().BeNull();
    }

    [Fact]
    public void RootScalar_Mutation_PatchesValueLiteral()
    {
        string json = LoadSample();
        var library = json.ParseJson<LibrarySampleJson>();

        library.Name = "City Library";

        DocumentChange change = ((IStructuraDocument)library).Changes.Single();
        change.Path.Should().Be("/name");
        change.NewText.Should().Be("\"City Library\"");
    }

    // ── Books collection ──────────────────────────────────────────────────────

    [Fact]
    public void BooksCollection_HasThreeItems()
    {
        var library = LoadSample().ParseJson<LibrarySampleJson>();
        library.Books.Should().HaveCount(3);
    }

    [Fact]
    public void BookRequiredFields_AreReadable()
    {
        var library = LoadSample().ParseJson<LibrarySampleJson>();

        library.Books[0].Id.Should().Be("B-001");
        library.Books[0].Title.Should().Be("War and Peace");
        library.Books[0].Year.Should().Be(1869);
        library.Books[0].Rating.Should().Be(4.8m);
    }

    // ── Optional subtitle (heterogeneous field union) ─────────────────────────

    [Fact]
    public void Subtitle_PresentBook_ReadsValue()
    {
        var library = LoadSample().ParseJson<LibrarySampleJson>();
        library.Books[0].Subtitle.Should().Be("A historical novel");
        library.Books[2].Subtitle.Should().Be("Le Tour du monde en quatre-vingts jours");
    }

    [Fact]
    public void Subtitle_AbsentBook_ReadsEmptyString()
    {
        var library = LoadSample().ParseJson<LibrarySampleJson>();
        library.Books[1].Subtitle.Should().Be(string.Empty);
    }

    [Fact]
    public void Subtitle_AbsentBook_SetterThrowsStructuraMutationException()
    {
        var library = LoadSample().ParseJson<LibrarySampleJson>();

        Action act = () => { library.Books[1].Subtitle = "anything"; };

        act.Should().Throw<StructuraMutationException>()
            .WithMessage("*Subtitle*");
    }

    [Fact]
    public void Subtitle_PresentBook_SetterRecordsAtIndexedPath()
    {
        string json = LoadSample();
        var library = json.ParseJson<LibrarySampleJson>();

        library.Books[0].Subtitle = "Roman v shesti tomakh";

        ((IStructuraDocument)library).Changes.Single().Path
            .Should().Be("/books/0/subtitle");
    }

    // ── Nested-in-item nullable-promoted scalar ───────────────────────────────

    [Fact]
    public void Publisher_Address_City_NullablePromotion_ReadsNullForSecondBook()
    {
        var library = LoadSample().ParseJson<LibrarySampleJson>();

        library.Books[0].Publisher.Address.City.Should().Be("Moscow");
        library.Books[1].Publisher.Address.City.Should().BeNull();
        library.Books[2].Publisher.Address.City.Should().Be("Paris");
    }

    [Fact]
    public void Publisher_Address_City_SetToString_PatchesAtDeepPath()
    {
        string json = LoadSample();
        var library = json.ParseJson<LibrarySampleJson>();

        library.Books[1].Publisher.Address.City = "Saint Petersburg";

        DocumentChange change = ((IStructuraDocument)library).Changes.Single();
        change.Path.Should().Be("/books/1/publisher/address/city");
        change.OldText.Should().Be("null");
        change.NewText.Should().Be("\"Saint Petersburg\"");

        string modified = library.ToJson();
        modified[..change.Span.Start].Should().Be(json[..change.Span.Start]);
        modified[(change.Span.Start + change.NewText.Length)..]
            .Should().Be(json[change.Span.End..]);
    }

    [Fact]
    public void Publisher_Address_Country_RequiredString_RoundTrips()
    {
        // 'country' is present in every observation → required → no
        // IsPresent guard, plain read/write.
        var library = LoadSample().ParseJson<LibrarySampleJson>();

        library.Books[0].Publisher.Address.Country.Should().Be("Russia");
        library.Books[2].Publisher.Address.Country.Should().Be("France");
    }

    // ── Primitive arrays ──────────────────────────────────────────────────────

    [Fact]
    public void Tags_PrimitiveArrayOfStrings_IsEnumerable()
    {
        var library = LoadSample().ParseJson<LibrarySampleJson>();
        library.Tags.Should().Equal("fiction", "classic");
    }

    [Fact]
    public void Years_PrimitiveArrayOfLongs_IsEnumerable()
    {
        var library = LoadSample().ParseJson<LibrarySampleJson>();
        library.Years.Should().Equal(1865L, 1872L);
    }

    [Fact]
    public void Tags_PropertyType_IsIReadOnlyList()
    {
        // V1 contract: collection property is declared IReadOnlyList<T> with
        // no setter — insertion is deferred to Step 10's insertion-aware
        // patcher. Use reflection on the property declaration so the test
        // pins the *declared* shape, not the runtime concrete backing type.
        System.Reflection.PropertyInfo tags =
            typeof(LibrarySampleJson).GetProperty("Tags")!;

        tags.PropertyType.Should().Be(typeof(IReadOnlyList<string>));
        tags.GetSetMethod(nonPublic: true).Should().BeNull();
    }

    // ── Untouched-region byte-identity ────────────────────────────────────────

    [Fact]
    public void NestedMutation_LeavesUntouchedRegions_ByteIdentical()
    {
        string json = LoadSample();
        var library = json.ParseJson<LibrarySampleJson>();

        library.Books[0].Year = 1867;
        string modified = library.ToJson();

        DocumentChange change = ((IStructuraDocument)library).Changes.Single();
        modified[..change.Span.Start].Should().Be(json[..change.Span.Start]);
        modified[(change.Span.Start + change.NewText.Length)..]
            .Should().Be(json[change.Span.End..]);
    }

    [Fact]
    public void MultipleMutations_OrderedByDocumentPosition()
    {
        string json = LoadSample();
        var library = json.ParseJson<LibrarySampleJson>();

        library.Books[2].Publisher.Address.City = "Lyon";
        library.Name = "City Library";
        library.Books[0].Year = 1867;

        IReadOnlyList<DocumentChange> changes = ((IStructuraDocument)library).Changes;
        changes.Select(c => c.Path).Should().Equal(
            "/name",
            "/books/0/year",
            "/books/2/publisher/address/city");
    }
}
