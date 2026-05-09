using FluentAssertions;

using Structura.Reporting.Internal;

using Xunit;

namespace Structura.UnitTests.Reporting;

public sealed class SideBySideRowBuilderTests
{
    private static readonly IReadOnlyList<ColumnRange> NoRanges =
        Array.Empty<ColumnRange>();

    [Fact]
    public void Build_EmptyInput_ReturnsEmpty()
    {
        var rows = SideBySideRowBuilder.Build(Array.Empty<DiffLine>());

        rows.Should().BeEmpty();
    }

    [Fact]
    public void Build_ContextOnly_BothSidesEqual()
    {
        var line = new DiffLine(DiffLineKind.Context, 1, 1, "x", NoRanges);

        var rows = SideBySideRowBuilder.Build(new[] { line });

        rows.Should().HaveCount(1);
        rows[0].Left.Should().Be(line);
        rows[0].Right.Should().Be(line);
    }

    [Fact]
    public void Build_HunkSeparator_BothSidesAreSeparator()
    {
        var sep = new DiffLine(DiffLineKind.HunkSeparator, 0, 0, string.Empty, NoRanges);

        var rows = SideBySideRowBuilder.Build(new[] { sep });

        rows.Should().HaveCount(1);
        rows[0].Left.Should().Be(sep);
        rows[0].Right.Should().Be(sep);
    }

    [Fact]
    public void Build_RemovedAddedPair_OneRowWithBothSides()
    {
        var rem = new DiffLine(DiffLineKind.Removed, 5, 0, "old", NoRanges);
        var add = new DiffLine(DiffLineKind.Added, 0, 5, "new", NoRanges);

        var rows = SideBySideRowBuilder.Build(new[] { rem, add });

        rows.Should().HaveCount(1);
        rows[0].Left.Should().Be(rem);
        rows[0].Right.Should().Be(add);
    }

    [Fact]
    public void Build_RemovedTwoAddedFour_TopAlignedFourRowsTwoLeftNull()
    {
        var rem1 = new DiffLine(DiffLineKind.Removed, 5, 0, "rem1", NoRanges);
        var rem2 = new DiffLine(DiffLineKind.Removed, 6, 0, "rem2", NoRanges);
        var add1 = new DiffLine(DiffLineKind.Added, 0, 5, "add1", NoRanges);
        var add2 = new DiffLine(DiffLineKind.Added, 0, 6, "add2", NoRanges);
        var add3 = new DiffLine(DiffLineKind.Added, 0, 7, "add3", NoRanges);
        var add4 = new DiffLine(DiffLineKind.Added, 0, 8, "add4", NoRanges);

        var rows = SideBySideRowBuilder.Build(new[] { rem1, rem2, add1, add2, add3, add4 });

        rows.Should().HaveCount(4);
        rows[0].Left.Should().Be(rem1);
        rows[0].Right.Should().Be(add1);
        rows[1].Left.Should().Be(rem2);
        rows[1].Right.Should().Be(add2);
        rows[2].Left.Should().BeNull();
        rows[2].Right.Should().Be(add3);
        rows[3].Left.Should().BeNull();
        rows[3].Right.Should().Be(add4);
    }

    [Fact]
    public void Build_RemovedOnlyNoAdded_RightSideNull()
    {
        var rem = new DiffLine(DiffLineKind.Removed, 5, 0, "old", NoRanges);

        var rows = SideBySideRowBuilder.Build(new[] { rem });

        rows.Should().HaveCount(1);
        rows[0].Left.Should().Be(rem);
        rows[0].Right.Should().BeNull();
    }

    [Fact]
    public void Build_AddedOnlyNoRemoved_LeftSideNull()
    {
        var add = new DiffLine(DiffLineKind.Added, 0, 5, "new", NoRanges);

        var rows = SideBySideRowBuilder.Build(new[] { add });

        rows.Should().HaveCount(1);
        rows[0].Left.Should().BeNull();
        rows[0].Right.Should().Be(add);
    }

    [Fact]
    public void Build_ContextRemovedAddedContext_ThreeRowsInOrder()
    {
        var ctxBefore = new DiffLine(DiffLineKind.Context, 4, 4, "before", NoRanges);
        var rem = new DiffLine(DiffLineKind.Removed, 5, 0, "old", NoRanges);
        var add = new DiffLine(DiffLineKind.Added, 0, 5, "new", NoRanges);
        var ctxAfter = new DiffLine(DiffLineKind.Context, 6, 6, "after", NoRanges);

        var rows = SideBySideRowBuilder.Build(new[] { ctxBefore, rem, add, ctxAfter });

        rows.Should().HaveCount(3);
        rows[0].Left.Should().Be(ctxBefore);
        rows[0].Right.Should().Be(ctxBefore);
        rows[1].Left.Should().Be(rem);
        rows[1].Right.Should().Be(add);
        rows[2].Left.Should().Be(ctxAfter);
        rows[2].Right.Should().Be(ctxAfter);
    }
}
