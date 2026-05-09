using FluentAssertions;

using Structura.Generated;
using Structura.Runtime;

using Xunit;

namespace Structura.IntegrationTests.Xml;

/// <summary>
/// Integration contract for <c>library.sample.xml</c> — a torture test with
/// DTD, custom entities, XML namespaces, heterogeneous book items, pure-text
/// genre collections, and nested reviews. Verifies the runtime parse contract,
/// absent-field defaults, the StructuraMutationException guard, and byte-identity
/// after single-item mutation.
/// </summary>
public sealed class LibrarySampleParseTests
{
    private static string LoadSample()
    {
        return File.ReadAllText("library.sample.xml");
    }

    [Fact]
    public void GeneratedModel_ParseFromXml_DoesNotThrow()
    {
        var library = LoadSample().ParseXml<LibrarySampleXml>();
        library.Should().NotBeNull();
    }

    [Fact]
    public void GeneratedModel_RootScalars_AreReadable()
    {
        var library = LoadSample().ParseXml<LibrarySampleXml>();

        library.Version.Should().Be(2.1m);
        library.Created.Should().Be("2026-05-07");
    }

    [Fact]
    public void GeneratedModel_BooksCollection_HasFiveItems()
    {
        var library = LoadSample().ParseXml<LibrarySampleXml>();
        library.Books.Should().HaveCount(5);
    }

    [Fact]
    public void Book_HeterogeneousFields_AllAccessible_DefaultsForMissing()
    {
        var library = LoadSample().ParseXml<LibrarySampleXml>();
        // b001 has no <out-of-stock> element → getter must return the default (empty string).
        string outOfStock = library.Books[0].OutOfStock;
        outOfStock.Should().Be(string.Empty);
    }

    [Fact]
    public void Book_AbsentFieldSetter_ThrowsStructuraMutationException()
    {
        var library = LoadSample().ParseXml<LibrarySampleXml>();
        // b001 (index 0) has no <out-of-stock> element — the V1 engine cannot
        // insert new spans, so the setter must throw loudly.
        Action act = () => { library.Books[0].OutOfStock = "yes"; };

        act.Should().Throw<StructuraMutationException>()
            .WithMessage("*OutOfStock*");
    }

    [Fact]
    public void Book_GenresPureTextLeafCollection_YieldsStrings()
    {
        var library = LoadSample().ParseXml<LibrarySampleXml>();
        IReadOnlyList<string> genres = library.Books[0].Genres;

        genres.Should().Equal("роман", "историческая проза");
    }

    [Fact]
    public void Book_GenresCollection_IsReadOnly()
    {
        var library = LoadSample().ParseXml<LibrarySampleXml>();
        // b004 (index 3) has no <genres> element → list must be empty, not null.
        IReadOnlyList<string> genres = library.Books[3].Genres;
        genres.Should().BeEmpty();
    }

    [Fact]
    public void Book_ReviewsCollection_HasContentForB005Only()
    {
        var library = LoadSample().ParseXml<LibrarySampleXml>();

        // Books without <reviews> element → empty list.
        library.Books[0].Reviews.Should().BeEmpty();
        library.Books[1].Reviews.Should().BeEmpty();
        library.Books[3].Reviews.Should().BeEmpty();

        // b005 (index 4) has two <review> children.
        library.Books[4].Reviews.Should().HaveCount(2);
    }

    [Fact]
    public void MutatingBookTitle_PatchesOnlyTheItemSpan()
    {
        string source = LoadSample();
        var library = source.ParseXml<LibrarySampleXml>();

        // b001's <title> has content "Война и мир" (lang attr is structural but
        // FindElement still finds it; InnerSpan covers the text content).
        library.Books[0].Title = "Мир и война";
        string modified = library.ToXml();

        DocumentChange change = ((IStructuraDocument)library).Changes.Single();
        change.OldText.Should().Be("Война и мир");
        change.NewText.Should().Be("Мир и война");

        modified[..change.Span.Start].Should().Be(source[..change.Span.Start]);
        modified[(change.Span.Start + change.NewText.Length)..]
            .Should().Be(source[change.Span.End..]);
    }

    [Fact]
    public void LibraryRoundTrip_ByteIdentical_NoMutation()
    {
        string source = LoadSample();
        var library = source.ParseXml<LibrarySampleXml>();

        library.ToXml().Should().Be(source);
        ((IStructuraDocument)library).Changes.Should().BeEmpty();
    }
}
