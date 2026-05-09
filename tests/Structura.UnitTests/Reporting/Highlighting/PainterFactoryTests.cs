using FluentAssertions;

using Structura.Reporting.Internal.Highlighting;
using Structura.Runtime;

using Xunit;

namespace Structura.UnitTests.Reporting.Highlighting;

public sealed class PainterFactoryTests
{
    private static FakeStructuraDocument MakeDoc(string documentName, string originalText = "{}") =>
        new FakeStructuraDocument(originalText, Array.Empty<DocumentChange>(), documentName);

    [Fact]
    public void For_SyntaxOff_ReturnsNullPainter()
    {
        var doc = MakeDoc("order.sample.json");

        IDiffSyntaxPainter painter = PainterFactory.For(doc, syntaxOn: false);

        painter.Should().BeSameAs(NullPainter.Instance);
    }

    [Fact]
    public void For_JsonByDocumentName_ReturnsJsonPainter()
    {
        var doc = MakeDoc("order.sample.json", originalText: "<not-json>");

        IDiffSyntaxPainter painter = PainterFactory.For(doc, syntaxOn: true);

        painter.Should().BeSameAs(JsonLinePainter.Instance);
    }

    [Fact]
    public void For_XmlByDocumentName_ReturnsXmlPainter()
    {
        var doc = MakeDoc("library.sample.xml", originalText: "{not-xml}");

        IDiffSyntaxPainter painter = PainterFactory.For(doc, syntaxOn: true);

        painter.Should().BeSameAs(XmlLinePainter.Instance);
    }

    [Fact]
    public void For_DocumentNameCaseInsensitive_RecognizesUppercaseExtension()
    {
        var doc = MakeDoc("Order.Sample.JSON");

        IDiffSyntaxPainter painter = PainterFactory.For(doc, syntaxOn: true);

        painter.Should().BeSameAs(JsonLinePainter.Instance);
    }

    [Fact]
    public void For_UnknownExtension_SniffsJsonByOpenBrace()
    {
        var doc = MakeDoc("data.txt", originalText: "  \n  { \"a\": 1 }");

        IDiffSyntaxPainter painter = PainterFactory.For(doc, syntaxOn: true);

        painter.Should().BeSameAs(JsonLinePainter.Instance);
    }

    [Fact]
    public void For_UnknownExtension_SniffsXmlByLessThan()
    {
        var doc = MakeDoc("data.txt", originalText: "\t<root/>");

        IDiffSyntaxPainter painter = PainterFactory.For(doc, syntaxOn: true);

        painter.Should().BeSameAs(XmlLinePainter.Instance);
    }

    [Fact]
    public void For_UnknownExtensionAndUnknownContent_ReturnsNullPainter()
    {
        var doc = MakeDoc("data.txt", originalText: "abcdef");

        IDiffSyntaxPainter painter = PainterFactory.For(doc, syntaxOn: true);

        painter.Should().BeSameAs(NullPainter.Instance);
    }

    [Fact]
    public void For_EmptyOriginalText_ReturnsNullPainter()
    {
        var doc = MakeDoc("data.txt", originalText: string.Empty);

        IDiffSyntaxPainter painter = PainterFactory.For(doc, syntaxOn: true);

        painter.Should().BeSameAs(NullPainter.Instance);
    }
}
