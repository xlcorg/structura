using FluentAssertions;

using Structura.Runtime;

using Xunit;

namespace Structura.UnitTests.Core;

public sealed class TextEditListTests
{
    [Fact]
    public void Apply_NoEdits_ReturnsSourceUnchanged()
    {
        var list = new TextEditList();
        list.Apply("hello world").Should().Be("hello world");
        list.HasEdits.Should().BeFalse();
    }

    [Fact]
    public void Apply_SingleEdit_RewritesSpanOnly()
    {
        var list = new TextEditList();
        list.Set(new TextEdit(new TextSpan(6, 5), "there"));
        list.Apply("hello world").Should().Be("hello there");
    }

    [Fact]
    public void Apply_MultipleNonOverlappingEdits_AppliedInOrder()
    {
        var list = new TextEditList();
        list.Set(new TextEdit(new TextSpan(0, 5), "HELLO"));
        list.Set(new TextEdit(new TextSpan(6, 5), "WORLD"));
        list.Apply("hello world").Should().Be("HELLO WORLD");
    }

    [Fact]
    public void Apply_PreservesUntouchedSurrounding()
    {
        var source = "  prefix  [target]  suffix  ";
        var span = new TextSpan(source.IndexOf("target"), "target".Length);

        var list = new TextEditList();
        list.Set(new TextEdit(span, "TARGET"));

        string patched = list.Apply(source);

        patched.Should().Be("  prefix  [TARGET]  suffix  ");
        patched[..span.Start].Should().Be(source[..span.Start]);
        patched[(span.Start + "TARGET".Length)..].Should().Be(source[span.End..]);
    }

    [Fact]
    public void Set_OverwritesEditAtSameSpan()
    {
        var list = new TextEditList();
        list.Set(new TextEdit(new TextSpan(6, 5), "first"));
        list.Set(new TextEdit(new TextSpan(6, 5), "second"));
        list.Apply("hello world").Should().Be("hello second");
    }

    [Fact]
    public void Apply_OverlappingEditsWithDifferentSpans_Throws()
    {
        var list = new TextEditList();
        list.Set(new TextEdit(new TextSpan(0, 5), "foo"));
        list.Set(new TextEdit(new TextSpan(3, 5), "bar"));

        Func<string> act = () => list.Apply("hello world");
        act.Should().Throw<InvalidOperationException>().WithMessage("*Overlapping*");
    }

    [Fact]
    public void Validate_OverlappingEditsWithDifferentSpans_Throws()
    {
        var list = new TextEditList();
        list.Set(new TextEdit(new TextSpan(0, 5), "foo"));
        list.Set(new TextEdit(new TextSpan(3, 5), "bar"));

        Action act = () => list.Validate();
        act.Should().Throw<InvalidOperationException>().WithMessage("*Overlapping*");
    }

    [Fact]
    public void Validate_NonOverlappingEdits_DoesNotThrow()
    {
        var list = new TextEditList();
        list.Set(new TextEdit(new TextSpan(0, 5), "foo"));
        list.Set(new TextEdit(new TextSpan(5, 5), "bar"));

        Action act = () => list.Validate();
        act.Should().NotThrow();
    }

    [Fact]
    public void Snapshot_OverlappingEdits_DoesNotThrow()
    {
        var list = new TextEditList();
        list.Set(new TextEdit(new TextSpan(0, 5), "foo"));
        list.Set(new TextEdit(new TextSpan(3, 5), "bar"));

        Action act = () => list.Snapshot();
        act.Should().NotThrow();
    }

    [Fact]
    public void Snapshot_ReturnsEditsSortedBySpanStart()
    {
        var list = new TextEditList();
        list.Set(new TextEdit(new TextSpan(10, 1), "c"));
        list.Set(new TextEdit(new TextSpan(0, 1), "a"));
        list.Set(new TextEdit(new TextSpan(5, 1), "b"));

        IReadOnlyList<TextEdit> snapshot = list.Snapshot();

        snapshot.Select(e => e.Replacement).Should().ContainInOrder("a", "b", "c");
    }

    [Fact]
    public void Apply_EditPastEndOfSource_Throws()
    {
        var list = new TextEditList();
        list.Set(new TextEdit(new TextSpan(8, 100), "x"));

        Func<string> act = () => list.Apply("hello world");
        act.Should().Throw<InvalidOperationException>().WithMessage("*past source length*");
    }

    [Fact]
    public void Remove_DropsEdit()
    {
        var list = new TextEditList();
        list.Set(new TextEdit(new TextSpan(6, 5), "there"));
        list.Remove(new TextSpan(6, 5)).Should().BeTrue();
        list.HasEdits.Should().BeFalse();
        list.Apply("hello world").Should().Be("hello world");
    }
}
