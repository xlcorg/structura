using FluentAssertions;

using Structura.Runtime;

using Xunit;

namespace Structura.UnitTests.Core;

public sealed class StructuraDocumentContextTests
{
    private const string Source = "{\"currency\":\"RUB\"}";

    private static TextSpan SpanOf(string source, string fragment)
    {
        return new TextSpan(source.IndexOf(fragment, StringComparison.Ordinal), fragment.Length);
    }

    [Fact]
    public void NoMutation_ApplyEdits_ReturnsSourceByteIdentical()
    {
        var ctx = new StructuraDocumentContext(Source);

        ctx.HasChanges.Should().BeFalse();
        ctx.ApplyEdits().Should().Be(Source);
        ctx.Changes.Should().BeEmpty();
    }

    [Fact]
    public void Record_PatchesOnlyTheTargetSpan()
    {
        var ctx = new StructuraDocumentContext(Source);
        TextSpan span = SpanOf(Source, "RUB");

        ctx.Record("/currency", span, "USD");

        ctx.HasChanges.Should().BeTrue();
        ctx.ApplyEdits().Should().Be("{\"currency\":\"USD\"}");

        DocumentChange? change = ctx.Changes.Should().ContainSingle().Which;
        change.Path.Should().Be("/currency");
        change.Span.Should().Be(span);
        change.OldText.Should().Be("RUB");
        change.NewText.Should().Be("USD");
    }

    [Fact]
    public void Record_ResettingToOriginal_DropsChange()
    {
        var ctx = new StructuraDocumentContext(Source);
        TextSpan span = SpanOf(Source, "RUB");

        ctx.Record("/currency", span, "USD");
        ctx.Record("/currency", span, "RUB");

        ctx.HasChanges.Should().BeFalse();
        ctx.ApplyEdits().Should().Be(Source);
        ctx.Changes.Should().BeEmpty();
    }

    [Fact]
    public void Record_SecondMutationAtSamePath_ReplacesEdit()
    {
        var ctx = new StructuraDocumentContext(Source);
        TextSpan span = SpanOf(Source, "RUB");

        ctx.Record("/currency", span, "USD");
        ctx.Record("/currency", span, "EUR");

        ctx.ApplyEdits().Should().Be("{\"currency\":\"EUR\"}");
        ctx.Changes.Should().ContainSingle().Which.NewText.Should().Be("EUR");
    }

    [Fact]
    public void Changes_AreOrderedByOriginalSpan()
    {
        const string multi = "{\"a\":\"1\",\"b\":\"2\",\"c\":\"3\"}";
        var ctx = new StructuraDocumentContext(multi);

        ctx.Record("/c", SpanOf(multi, "3"), "Z");
        ctx.Record("/a", SpanOf(multi, "1"), "X");
        ctx.Record("/b", SpanOf(multi, "2"), "Y");

        ctx.Changes.Select(static c => c.Path).Should().Equal("/a", "/b", "/c");
        ctx.ApplyEdits().Should().Be("{\"a\":\"X\",\"b\":\"Y\",\"c\":\"Z\"}");
    }
}
