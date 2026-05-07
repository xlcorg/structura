using FluentAssertions;

using Structura.Generated;
using Structura.Runtime;
using Structura.Runtime.Xml;

using Xunit;

namespace Structura.IntegrationTests.Xml;

/// <summary>
/// Real-world XML parse contract: the user added <c>blrwbl.sample.xml</c>
/// (a Belarus electronic waybill) and asked to "make sure it parses
/// correctly". These tests exercise the runtime parser and the
/// generator-produced <see cref="BlrwblSampleXml"/> against the actual
/// file, copied next to the test runner via a <c>&lt;Content … Link&gt;</c>
/// in the integration test csproj.
/// </summary>
public sealed class BlrwblSampleParseTests
{
    private static string LoadSample()
    {
        return File.ReadAllText("blrwbl.sample.xml");
    }

    [Fact]
    public void RuntimeParser_AcceptsTheRealFile_NoExceptionsAndCorrectRoot()
    {
        XmlSourceElement root = XmlSourceParser.Parse(LoadSample());

        root.Name.Should().Be("BLRWBL");
    }

    [Fact]
    public void RuntimeParser_RootHasExactlyOneDeliveryNoteChild()
    {
        XmlSourceElement root = XmlSourceParser.Parse(LoadSample());

        XmlSourceElement[] elementChildren = root.Children
            .OfType<XmlSourceElement>()
            .ToArray();

        elementChildren.Should().ContainSingle();
        elementChildren[0].Name.Should().Be("DeliveryNote");
    }

    [Fact]
    public void RuntimeParser_DeepNavigation_ResolvesShipperName()
    {
        XmlSourceElement root = XmlSourceParser.Parse(LoadSample());

        XmlSourceElement shipperName = root
            .RequireElement("DeliveryNote")
            .RequireElement("Shipper")
            .RequireElement("Name");

        shipperName.IsPureText.Should().BeTrue();
        ((XmlSourceText)shipperName.Children[0]).Value.Should().Be("ОАО «Минский завод»");
    }

    [Fact]
    public void RuntimeParser_HandlesMultipleLineItems()
    {
        XmlSourceElement root = XmlSourceParser.Parse(LoadSample());

        XmlSourceElement[] lineItems = root
            .RequireElement("DeliveryNote")
            .RequireElement("DespatchAdviceLogisticUnitLineItem")
            .Children
            .OfType<XmlSourceElement>()
            .Where(e => e.Name == "LineItem")
            .ToArray();

        lineItems.Should().HaveCount(2);
    }

    [Fact]
    public void GeneratedModel_ParseFromXml_DoesNotThrow()
    {
        var doc = LoadSample().ParseXml<BlrwblSampleXml>();
        doc.Should().NotBeNull();
    }

    [Fact]
    public void GeneratedModel_ToXml_ByteIdenticalWithSource_NoMutation()
    {
        string source = LoadSample();
        var doc = source.ParseXml<BlrwblSampleXml>();

        doc.ToXml().Should().Be(source);
        ((IStructuraDocument)doc).Changes.Should().BeEmpty();
    }

    // ── Mutation contract (wrapper-flattening exposes 13 scalars) ────────────

    [Fact]
    public void GeneratedModel_FlatteningExposesCurrency_AsScalarProperty()
    {
        var doc = LoadSample().ParseXml<BlrwblSampleXml>();
        doc.Currency.Should().Be("BYN");
    }

    [Fact]
    public void GeneratedModel_StringPropertyWithSlashAndHyphen_RoundTrips()
    {
        // ContractID = "56/456-6678" — has both '/' and '-', must classify as string.
        var doc = LoadSample().ParseXml<BlrwblSampleXml>();
        doc.ContractID.Should().Be("56/456-6678");
    }

    [Fact]
    public void MutatingCurrency_PatchesOnlyTheInnerSpan()
    {
        string source = LoadSample();
        var doc = source.ParseXml<BlrwblSampleXml>();

        doc.Currency = "USD";

        string modified = doc.ToXml();
        modified.Should().Contain("<Currency>USD</Currency>");
        // The original BYN literal is gone.
        modified.Should().NotContain("<Currency>BYN</Currency>");

        DocumentChange change = ((IStructuraDocument)doc).Changes.Single();
        change.OldText.Should().Be("BYN");
        change.NewText.Should().Be("USD");

        // Bytes outside the changed span are byte-identical with the original.
        modified[..change.Span.Start].Should().Be(source[..change.Span.Start]);
        modified[(change.Span.Start + change.NewText.Length)..]
            .Should().Be(source[change.Span.End..]);
    }

    [Fact]
    public void Changes_PathIsRootRelative()
    {
        var doc = LoadSample().ParseXml<BlrwblSampleXml>();
        doc.Currency = "USD";

        ((IStructuraDocument)doc).Changes.Single().Path.Should().Be("/Currency");
    }

    [Fact]
    public void MutatingScalarLong_FormatsInvariant()
    {
        var doc = LoadSample().ParseXml<BlrwblSampleXml>();
        doc.SealID = 99999;

        doc.ToXml().Should().Contain("<SealID>99999</SealID>");
    }

    [Fact]
    public void MultipleMutations_PatchedTogetherWithRootRelativePaths()
    {
        var doc = LoadSample().ParseXml<BlrwblSampleXml>();
        doc.Currency = "USD";
        doc.SealID = 99999;

        IReadOnlyList<DocumentChange> changes = ((IStructuraDocument)doc).Changes;
        // Sorted by Span.Start — SealID (line 69) precedes Currency (line 71).
        changes.Select(c => c.Path).Should().Equal("/SealID", "/Currency");

        string modified = doc.ToXml();
        modified.Should().Contain("<SealID>99999</SealID>");
        modified.Should().Contain("<Currency>USD</Currency>");
    }

    [Fact]
    public void BlrwblFullDemo_OnlyMutatedRegionsDiffer()
    {
        string source = LoadSample();
        var doc = source.ParseXml<BlrwblSampleXml>();

        doc.SealID = 99999;
        doc.Currency = "USD";
        doc.DespatchAdviceLogisticUnitLineItem.LineItems[1].LineItemNumber = 42;

        string modified = doc.ToXml();

        IReadOnlyList<DocumentChange> changes = ((IStructuraDocument)doc).Changes;
        changes.Should().HaveCount(3);
        // Changes are sorted by Span.Start: SealID < Currency < LineItems[1].LineItemNumber.
        changes[0].Path.Should().Be("/SealID");
        changes[1].Path.Should().Be("/Currency");
        changes[2].Path.Should().Be("/DespatchAdviceLogisticUnitLineItem/LineItems/1/LineItemNumber");

        // Walk through the changes and verify that the bytes between them
        // (and before the first / after the last) are byte-identical to the original.
        int sourcePos = 0;
        int modPos = 0;
        foreach (DocumentChange ch in changes)
        {
            int beforeLen = ch.Span.Start - sourcePos;
            modified.Substring(modPos, beforeLen).Should().Be(source.Substring(sourcePos, beforeLen));
            modPos += beforeLen + ch.NewText.Length;
            sourcePos = ch.Span.End;
        }
        modified[modPos..].Should().Be(source[sourcePos..]);
    }
}
