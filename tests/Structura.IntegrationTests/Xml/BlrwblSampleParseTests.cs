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
}
