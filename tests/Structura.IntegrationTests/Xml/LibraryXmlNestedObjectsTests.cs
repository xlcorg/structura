using FluentAssertions;

using Structura.Generated;
using Structura.Runtime;

using Xunit;

namespace Structura.IntegrationTests.Xml;

/// <summary>
/// Step 10 coverage of nested objects on the library sample: a
/// namespace-prefixed nested object on the root (<c>meta:info</c>),
/// a structural nested object inside a collection item
/// (<c>book → author</c>), and three-level descent through a
/// wrapped collection plus item-level nested object
/// (<c>book → reviews → review → reviewer</c>).
/// </summary>
public sealed class LibraryXmlNestedObjectsTests
{
    private static string LoadSample()
    {
        return File.ReadAllText("library.sample.xml");
    }

    // ── Root-level namespace-prefixed nested object ──────────────────────────

    [Fact]
    public void MetaInfo_IsExposedAsNestedObjectWithSanitizedName()
    {
        var doc = LoadSample().ParseXml<LibrarySampleXml>();

        doc.MetaInfo.MetaTotalItems.Should().Be("5");
    }

    [Fact]
    public void MetaInfo_NamespacedScalar_RoundTripsLiteralKey()
    {
        string source = LoadSample();
        var doc = source.ParseXml<LibrarySampleXml>();

        doc.MetaInfo.MetaTotalItems = "99";
        string modified = doc.ToXml();

        modified.Should().Contain("<meta:total-items>99</meta:total-items>");
        modified.Should().NotContain("<meta:total-items>5</meta:total-items>");
    }

    [Fact]
    public void Statistics_IsExposedAsNestedObjectOnRoot()
    {
        var doc = LoadSample().ParseXml<LibrarySampleXml>();

        doc.Statistics.Total.Should().Be("5");
        doc.Statistics.Available.Should().Be("3");
        doc.Statistics.Currencies.Should().Be("RUB,USD,JPY,EUR");
    }

    // ── Nested object inside a collection item (Author per Book) ─────────────

    [Fact]
    public void Book_AuthorNested_ReadsScalarsForFirstBook()
    {
        var doc = LoadSample().ParseXml<LibrarySampleXml>();

        doc.Books[0].Author.FirstName.Should().Be("Лев");
        doc.Books[0].Author.LastName.Should().Be("Толстой");
    }

    [Fact]
    public void Book_AuthorNested_PathContainsItemIndex()
    {
        var doc = LoadSample().ParseXml<LibrarySampleXml>();

        doc.Books[1].Author.FirstName = "Eric";

        DocumentChange change = ((IStructuraDocument)doc).Changes.Single();
        change.Path.Should().Be("/Books/1/Author/FirstName");
        change.OldText.Should().Be("George");
        change.NewText.Should().Be("Eric");
    }

    [Fact]
    public void Book_AuthorNested_EmptyLastName_ReadsAsEmptyString()
    {
        // Book b004 has <last-name></last-name> — the scalar accessor must
        // not crash on Children[0] when the element body is empty.
        var doc = LoadSample().ParseXml<LibrarySampleXml>();

        doc.Books[3].Author.LastName.Should().Be(string.Empty);
        doc.Books[3].Author.FirstName.Should().Be("Аноним");
    }

    // ── Three-level nesting: Book → Reviews → Review → Reviewer ──────────────

    [Fact]
    public void Book_ReviewerNested_DeepReadFromB005()
    {
        // Only b005 has <reviews>. Items[4] is b005 in document order.
        var doc = LoadSample().ParseXml<LibrarySampleXml>();
        IReadOnlyList<LibrarySampleXml.Review> b005Reviews = doc.Books[4].Reviews;

        b005Reviews.Should().HaveCount(2);
        b005Reviews[0].Reviewer.Name.Should().Be("Иван Петров");
        b005Reviews[0].Reviewer.Verified.Should().Be("true");
        b005Reviews[1].Reviewer.Name.Should().Be("Anna Smith");
        b005Reviews[1].Reviewer.Verified.Should().Be("false");
    }

    [Fact]
    public void Book_ReviewerNested_MutationPathSpansThreeLevels()
    {
        var doc = LoadSample().ParseXml<LibrarySampleXml>();

        doc.Books[4].Reviews[1].Reviewer.Name = "A. Smith";

        DocumentChange change = ((IStructuraDocument)doc).Changes.Single();
        change.Path.Should().Be("/Books/4/Reviews/1/Reviewer/Name");
        change.OldText.Should().Be("Anna Smith");
    }
}
