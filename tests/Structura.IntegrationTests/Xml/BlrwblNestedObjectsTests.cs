using FluentAssertions;

using Structura.Generated;
using Structura.Runtime;

using Xunit;

namespace Structura.IntegrationTests.Xml;

/// <summary>
/// Acceptance criterion for Step 10: every nested-object element on the
/// effective root of <c>blrwbl.sample.xml</c> (Document, Shipper, Receiver,
/// FreightPayer, ShipFrom, ShipTo, Transporter, Carrier, Total) is exposed
/// as a typed property, scalars inside them read correctly, and mutations
/// preserve byte-equality of untouched regions.
/// </summary>
public sealed class BlrwblNestedObjectsTests
{
    private static string LoadSample()
    {
        return File.ReadAllText("blrwbl.sample.xml");
    }

    // ── Read every nested object ─────────────────────────────────────────────

    [Fact]
    public void Document_ScalarChildren_ReadCorrectly()
    {
        var doc = LoadSample().ParseXml<BlrwblSampleXml>();

        doc.Document.DocumentID.Should().Be("234/20012");
        doc.Document.DocumentDate.Should().Be("20150113");
        doc.Document.DocumentName.Should().Be("Опись содержимого контейнера");
    }

    [Fact]
    public void Shipper_ScalarChildren_ReadCorrectly()
    {
        var doc = LoadSample().ParseXml<BlrwblSampleXml>();

        doc.Shipper.GLN.Should().Be("4810987000544");
        doc.Shipper.Name.Should().Be("ОАО «Минский завод»");
        doc.Shipper.VATRegistrationNumber.Should().Be("788888855");
        doc.Shipper.Contact.Should().Be("Директор Иванов И.И.");
    }

    [Fact]
    public void Receiver_ScalarChildren_ReadCorrectly()
    {
        var doc = LoadSample().ParseXml<BlrwblSampleXml>();

        doc.Receiver.GLN.Should().Be("4810117000635");
        doc.Receiver.Name.Should().Be("ОАО «Улыбка»");
        doc.Receiver.VATRegistrationNumber.Should().Be("666777755");
    }

    [Fact]
    public void FreightPayer_ScalarChildren_ReadCorrectly()
    {
        var doc = LoadSample().ParseXml<BlrwblSampleXml>();

        doc.FreightPayer.GLN.Should().Be("4812409900009");
        doc.FreightPayer.Name.Should().Be("ОАО «Известный Поставщик»");
    }

    [Fact]
    public void ShipFrom_ScalarChildren_ReadCorrectly()
    {
        var doc = LoadSample().ParseXml<BlrwblSampleXml>();

        doc.ShipFrom.GLN.Should().Be("4810989000009");
        doc.ShipFrom.Contact.Should().Be("Заведующий складом Петров И. И.");
    }

    [Fact]
    public void ShipTo_ScalarChildren_ReadCorrectly()
    {
        var doc = LoadSample().ParseXml<BlrwblSampleXml>();

        doc.ShipTo.GLN.Should().Be("4810047000002");
    }

    [Fact]
    public void Transporter_ScalarChildren_ReadCorrectly()
    {
        var doc = LoadSample().ParseXml<BlrwblSampleXml>();

        doc.Transporter.GLN.Should().Be("4812409900009");
        doc.Transporter.Name.Should().Be("ООО «Перевозки»");
    }

    [Fact]
    public void Carrier_ScalarChildren_ReadCorrectly()
    {
        var doc = LoadSample().ParseXml<BlrwblSampleXml>();

        doc.Carrier.TransportContact.Should().Be("Сидоров И. И.");
        doc.Carrier.ProxyID.Should().Be("22-2012");
        doc.Carrier.QuantityTrip.Should().Be("3");
        doc.Carrier.TransportID.Should().Be("BMW, TP 0524-7");
    }

    [Fact]
    public void Total_ScalarChildren_ReadCorrectly()
    {
        var doc = LoadSample().ParseXml<BlrwblSampleXml>();

        doc.Total.TotalAmount.Should().Be("600.00");
        doc.Total.TotalLineItem.Should().Be("10");
        doc.Total.TotalGrossWeight.Should().Be("1.02");
    }

    // ── Mutate scalars inside nested objects ─────────────────────────────────

    [Fact]
    public void MutatingNestedScalar_PathUsesNestedPrefix()
    {
        var doc = LoadSample().ParseXml<BlrwblSampleXml>();
        doc.Shipper.GLN = "9999988880001";

        DocumentChange change = ((IStructuraDocument)doc).Changes.Single();
        change.Path.Should().Be("/Shipper/GLN");
        change.OldText.Should().Be("4810987000544");
        change.NewText.Should().Be("9999988880001");
    }

    [Fact]
    public void MutatingNestedScalar_WritesValueVerbatim()
    {
        var doc = LoadSample().ParseXml<BlrwblSampleXml>();
        doc.Total.TotalAmount = "700.00";

        string modified = doc.ToXml();
        modified.Should().Contain("<TotalAmount>700.00</TotalAmount>");
        modified.Should().NotContain("<TotalAmount>600.00</TotalAmount>");
    }

    [Fact]
    public void MutatingMultipleNestedScalars_OrderedByDocumentPosition()
    {
        var doc = LoadSample().ParseXml<BlrwblSampleXml>();

        doc.Total.TotalAmount = "700.00";
        doc.Document.DocumentID = "X-001";
        doc.Shipper.GLN = "9999988880001";

        // Document precedes Shipper precedes Total in the source.
        IReadOnlyList<DocumentChange> changes = ((IStructuraDocument)doc).Changes;
        changes.Select(c => c.Path).Should().Equal(
            "/Document/DocumentID",
            "/Shipper/GLN",
            "/Total/TotalAmount");
    }

    // ── Byte-equality outside mutated spans ──────────────────────────────────

    [Fact]
    public void MutatingNestedScalar_PreservesUntouchedBytes()
    {
        string source = LoadSample();
        var doc = source.ParseXml<BlrwblSampleXml>();

        doc.Document.DocumentID = "X-001";

        string modified = doc.ToXml();
        DocumentChange change = ((IStructuraDocument)doc).Changes.Single();

        modified[..change.Span.Start].Should().Be(source[..change.Span.Start]);
        modified[(change.Span.Start + change.NewText.Length)..]
            .Should().Be(source[change.Span.End..]);
    }

    [Fact]
    public void NoMutations_RoundTripIsByteIdentical()
    {
        string source = LoadSample();
        var doc = source.ParseXml<BlrwblSampleXml>();

        doc.ToXml().Should().Be(source);
        ((IStructuraDocument)doc).Changes.Should().BeEmpty();
    }
}
