using FluentAssertions;

using Structura.Generated;
using Structura.Runtime;

using Xunit;

namespace Structura.IntegrationTests.Xml;

/// <summary>
/// Integration contract for the two-level collection hierarchy in
/// <c>blrwbl.sample.xml</c>: the Wrapper-style
/// <c>DespatchAdviceLogisticUnitLineItem → LineItems</c> and the
/// SiblingGroup-style <c>LineItemExtraFields</c> nested within each
/// <c>LineItem</c>.
/// </summary>
public sealed class BlrwblLineItemTests
{
    private static string LoadSample()
    {
        return File.ReadAllText("blrwbl.sample.xml");
    }

    [Fact]
    public void Iterate_DespatchAdviceLogisticUnitLineItem_LineItems_YieldsTwoItems()
    {
        var doc = LoadSample().ParseXml<BlrwblSampleXml>();
        doc.DespatchAdviceLogisticUnitLineItem.LineItems.Should().HaveCount(2);
    }

    [Fact]
    public void LineItem_HasExpectedScalarTypes()
    {
        var doc = LoadSample().ParseXml<BlrwblSampleXml>();
        BlrwblSampleXml.LineItem item = doc.DespatchAdviceLogisticUnitLineItem.LineItems[0];

        // Compile-time type assertions via local variables.
        string number = item.LineItemNumber;
        string name = item.LineItemName;
        string weight = item.GrossWeightValue;

        number.Should().Be("1");
        name.Should().Be("Хлеб «Ласунок»");
        weight.Should().Be("0.01");
    }

    [Fact]
    public void LineItem_LineItemExtraFields_YieldsTwoNestedItems()
    {
        var doc = LoadSample().ParseXml<BlrwblSampleXml>();
        doc.DespatchAdviceLogisticUnitLineItem.LineItems[0]
            .LineItemExtraFields.Should().HaveCount(2);
    }

    [Fact]
    public void LineItemExtraField_FieldName_IsString_FieldValue_IsString()
    {
        var doc = LoadSample().ParseXml<BlrwblSampleXml>();
        BlrwblSampleXml.LineItemExtraField field = doc.DespatchAdviceLogisticUnitLineItem.LineItems[0].LineItemExtraFields[0];

        // Compile-time type assertions.
        string fieldName = field.FieldName;
        string fieldValue = field.FieldValue;

        fieldName.Should().Be("InventoryId");
        fieldValue.Should().Be("123456");
    }

    [Fact]
    public void MutatingLineItemNumber_PatchesOnlyTheItemSpan()
    {
        string source = LoadSample();
        var doc = source.ParseXml<BlrwblSampleXml>();

        doc.DespatchAdviceLogisticUnitLineItem.LineItems[1].LineItemNumber = "42";
        string modified = doc.ToXml();

        modified.Should().Contain("<LineItemNumber>42</LineItemNumber>");
        modified.Should().NotContain("<LineItemNumber>2</LineItemNumber>");

        DocumentChange change = ((IStructuraDocument)doc).Changes.Single();
        change.OldText.Should().Be("2");
        change.NewText.Should().Be("42");

        // Bytes outside the change span are byte-identical.
        modified[..change.Span.Start].Should().Be(source[..change.Span.Start]);
        modified[(change.Span.Start + change.NewText.Length)..]
            .Should().Be(source[change.Span.End..]);
    }

    [Fact]
    public void MutatingNestedFieldValue_PatchesOnlyTheNestedItemSpan()
    {
        string source = LoadSample();
        var doc = source.ParseXml<BlrwblSampleXml>();

        doc.DespatchAdviceLogisticUnitLineItem.LineItems[1].LineItemExtraFields[0].FieldValue = "999";
        string modified = doc.ToXml();

        DocumentChange change = ((IStructuraDocument)doc).Changes.Single();
        change.OldText.Should().Be("123455");
        change.NewText.Should().Be("999");

        modified[..change.Span.Start].Should().Be(source[..change.Span.Start]);
        modified[(change.Span.Start + change.NewText.Length)..]
            .Should().Be(source[change.Span.End..]);
    }

    [Fact]
    public void Changes_Path_IncludesWrapperAndIndex()
    {
        var doc = LoadSample().ParseXml<BlrwblSampleXml>();
        doc.DespatchAdviceLogisticUnitLineItem.LineItems[1].LineItemExtraFields[0].FieldValue = "999";

        string path = ((IStructuraDocument)doc).Changes.Single().Path;
        path.Should().Be(
            "/DespatchAdviceLogisticUnitLineItem/LineItems/1/LineItemExtraFields/0/FieldValue");
    }

    [Fact]
    public void MutatingMultipleLevels_PatchedTogether()
    {
        var doc = LoadSample().ParseXml<BlrwblSampleXml>();
        doc.SealID = "99999";
        doc.DespatchAdviceLogisticUnitLineItem.LineItems[0].LineItemNumber = "100";
        doc.DespatchAdviceLogisticUnitLineItem.LineItems[1].LineItemExtraFields[0].FieldValue = "999";

        string modified = doc.ToXml();
        modified.Should().Contain("<SealID>99999</SealID>");
        modified.Should().Contain("<LineItemNumber>100</LineItemNumber>");

        IReadOnlyList<DocumentChange> changes = ((IStructuraDocument)doc).Changes;
        changes.Should().HaveCount(3);
        // Sorted by Span.Start (ascending source offset).
        changes[0].Path.Should().Be("/SealID");
        changes[1].Path.Should().Be("/DespatchAdviceLogisticUnitLineItem/LineItems/0/LineItemNumber");
        changes[2].Path.Should().Be(
            "/DespatchAdviceLogisticUnitLineItem/LineItems/1/LineItemExtraFields/0/FieldValue");
    }

    [Fact]
    public void UntouchedItems_AreByteIdentical()
    {
        string source = LoadSample();
        var doc = source.ParseXml<BlrwblSampleXml>();

        // Mutate only LineItem[1]; LineItem[0] must remain byte-identical.
        doc.DespatchAdviceLogisticUnitLineItem.LineItems[1].LineItemNumber = "42";
        string modified = doc.ToXml();

        DocumentChange change = ((IStructuraDocument)doc).Changes.Single();
        // Everything before the change (includes all of LineItem[0]) is unchanged.
        modified[..change.Span.Start].Should().Be(source[..change.Span.Start]);
        modified[(change.Span.Start + change.NewText.Length)..]
            .Should().Be(source[change.Span.End..]);
    }
}
