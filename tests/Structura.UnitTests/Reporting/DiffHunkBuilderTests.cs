using FluentAssertions;

using Structura.Reporting;
using Structura.Reporting.Internal;
using Structura.Runtime;

using Xunit;

namespace Structura.UnitTests.Reporting;

public sealed class DiffHunkBuilderTests
{
    private const string Source =
        "{\n" +
        "  \"name\": \"Alice\",\n" +
        "  \"age\": 30,\n" +
        "  \"city\": \"Paris\",\n" +
        "  \"role\": \"admin\"\n" +
        "}";

    [Fact]
    public void Build_SingleChange_EmitsContextMinusPlusContext()
    {
        // Replace 30 with 42 on line 3 (0-based offset of '3' in "30" is 25).
        int ageOffset = Source.IndexOf("30", System.StringComparison.Ordinal);
        var change = new DocumentChange("/age", new TextSpan(ageOffset, 2), "30", "42");
        var doc = new FakeStructuraDocument(
            Source,
            new[] { change },
            documentName: "fake.json")
        {
            CurrentTextOverride = Source[..ageOffset] + "42" + Source[(ageOffset + 2)..],
        };

        var lines = new DiffHunkBuilder().Build(doc, new UnifiedDiffOptions());

        // Source has 6 lines (indices 0..5). Change on line 2 (0-based).
        // ContextLines=3: pre-context = lines 0,1 (capped at start);
        // post-context = lines 3,4,5 (3 available). 2 + 1 + 1 + 3 = 7 lines.
        lines.Should().HaveCount(7);
        lines[0].Kind.Should().Be(DiffLineKind.Context);
        lines[0].LineNumber.Should().Be(1);
        lines[0].Content.Should().Be("{");
        lines[1].Kind.Should().Be(DiffLineKind.Context);
        lines[1].LineNumber.Should().Be(2);
        lines[2].Kind.Should().Be(DiffLineKind.Removed);
        lines[2].LineNumber.Should().Be(3);
        lines[2].Content.Should().Be("  \"age\": 30,");
        lines[3].Kind.Should().Be(DiffLineKind.Added);
        lines[3].LineNumber.Should().Be(3);
        lines[3].Content.Should().Be("  \"age\": 42,");
        lines[4].Kind.Should().Be(DiffLineKind.Context);
        lines[4].LineNumber.Should().Be(4);
        lines[5].Kind.Should().Be(DiffLineKind.Context);
        lines[5].LineNumber.Should().Be(5);
        lines[6].Kind.Should().Be(DiffLineKind.Context);
        lines[6].LineNumber.Should().Be(6);
        lines[6].Content.Should().Be("}");
    }

    [Fact]
    public void Build_TwoNearbyChanges_MergedIntoOneHunk()
    {
        int nameOffset = Source.IndexOf("\"Alice\"", System.StringComparison.Ordinal);
        int ageOffset = Source.IndexOf("30", System.StringComparison.Ordinal);
        var changeName = new DocumentChange("/name", new TextSpan(nameOffset, 7), "\"Alice\"", "\"Bob\"");
        var changeAge = new DocumentChange("/age", new TextSpan(ageOffset, 2), "30", "42");
        string current = Source[..nameOffset] + "\"Bob\"" + Source[(nameOffset + 7)..ageOffset] + "42" + Source[(ageOffset + 2)..];
        var doc = new FakeStructuraDocument(Source, new[] { changeName, changeAge })
        {
            CurrentTextOverride = current,
        };

        var lines = new DiffHunkBuilder().Build(doc, new UnifiedDiffOptions());

        lines.Where(l => l.Kind == DiffLineKind.HunkSeparator).Should().BeEmpty();
        lines.Where(l => l.Kind == DiffLineKind.Removed).Should().HaveCount(2);
        lines.Where(l => l.Kind == DiffLineKind.Added).Should().HaveCount(2);
    }

    [Fact]
    public void Build_TwoFarChanges_TwoHunksSeparatedByEllipsis()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine("  \"a\": 1,");
        for (var i = 0; i < 30; i++)
        {
            sb.AppendLine($"  \"f{i}\": 0,");
        }
        sb.AppendLine("  \"z\": 9");
        sb.Append("}");
        string text = sb.ToString();

        int aOffset = text.IndexOf("1,", System.StringComparison.Ordinal);
        int zOffset = text.IndexOf("9", System.StringComparison.Ordinal);
        var ca = new DocumentChange("/a", new TextSpan(aOffset, 1), "1", "2");
        var cz = new DocumentChange("/z", new TextSpan(zOffset, 1), "9", "8");
        string current = text[..aOffset] + "2" + text[(aOffset + 1)..zOffset] + "8" + text[(zOffset + 1)..];
        var doc = new FakeStructuraDocument(text, new[] { ca, cz })
        {
            CurrentTextOverride = current,
        };

        var lines = new DiffHunkBuilder().Build(doc, new UnifiedDiffOptions());

        lines.Where(l => l.Kind == DiffLineKind.HunkSeparator).Should().HaveCount(1);
    }

    [Fact]
    public void Build_ChangeAtFileStart_TruncatesPreContext()
    {
        const string text = "{\n  \"x\": 1\n}";
        int xOffset = text.IndexOf("1", System.StringComparison.Ordinal);
        var c = new DocumentChange("/x", new TextSpan(xOffset, 1), "1", "2");
        var doc = new FakeStructuraDocument(text, new[] { c })
        {
            CurrentTextOverride = text[..xOffset] + "2" + text[(xOffset + 1)..],
        };

        var lines = new DiffHunkBuilder().Build(doc, new UnifiedDiffOptions { ContextLines = 3 });

        int contextBefore = 0;
        foreach (DiffLine l in lines)
        {
            if (l.Kind == DiffLineKind.Removed) { break; }
            if (l.Kind == DiffLineKind.Context) { contextBefore++; }
        }
        contextBefore.Should().Be(1);
    }

    [Fact]
    public void Build_ContextLinesZero_NoContext()
    {
        int ageOffset = Source.IndexOf("30", System.StringComparison.Ordinal);
        var c = new DocumentChange("/age", new TextSpan(ageOffset, 2), "30", "42");
        var doc = new FakeStructuraDocument(Source, new[] { c })
        {
            CurrentTextOverride = Source[..ageOffset] + "42" + Source[(ageOffset + 2)..],
        };

        var lines = new DiffHunkBuilder().Build(doc, new UnifiedDiffOptions { ContextLines = 0 });

        lines.Should().HaveCount(2);
        lines[0].Kind.Should().Be(DiffLineKind.Removed);
        lines[1].Kind.Should().Be(DiffLineKind.Added);
    }

    [Fact]
    public void Build_ChangeAtFileEnd_TruncatesPostContext()
    {
        const string text = "{\n  \"x\": 1,\n  \"y\": 2\n}";
        int yOffset = text.IndexOf("2", System.StringComparison.Ordinal);
        var c = new DocumentChange("/y", new TextSpan(yOffset, 1), "2", "9");
        var doc = new FakeStructuraDocument(text, new[] { c })
        {
            CurrentTextOverride = text[..yOffset] + "9" + text[(yOffset + 1)..],
        };

        var lines = new DiffHunkBuilder().Build(doc, new UnifiedDiffOptions { ContextLines = 3 });

        int contextAfter = 0;
        bool seenAdded = false;
        foreach (DiffLine l in lines)
        {
            if (l.Kind == DiffLineKind.Added) { seenAdded = true; continue; }
            if (seenAdded && l.Kind == DiffLineKind.Context) { contextAfter++; }
        }
        contextAfter.Should().Be(1);
    }

    [Fact]
    public void Build_MultiLineReplacement_OneHunkMultipleSigils()
    {
        int ageOffset = Source.IndexOf("30", System.StringComparison.Ordinal);
        var c = new DocumentChange("/age", new TextSpan(ageOffset, 2), "30", "30,\n  \"new\": true");
        string current = Source[..ageOffset] + "30,\n  \"new\": true" + Source[(ageOffset + 2)..];
        var doc = new FakeStructuraDocument(Source, new[] { c })
        {
            CurrentTextOverride = current,
        };

        var lines = new DiffHunkBuilder().Build(doc, new UnifiedDiffOptions());

        lines.Where(l => l.Kind == DiffLineKind.Removed).Should().HaveCount(1);
        lines.Where(l => l.Kind == DiffLineKind.Added).Should().HaveCount(2);
        lines.Where(l => l.Kind == DiffLineKind.HunkSeparator).Should().BeEmpty();
    }

    [Fact]
    public void Build_TwoChangesSameLine_OneMinusOnePlus()
    {
        int keyOffset = Source.IndexOf("\"age\"", System.StringComparison.Ordinal);
        int valOffset = Source.IndexOf("30", System.StringComparison.Ordinal);
        var c1 = new DocumentChange("/age", new TextSpan(keyOffset, 5), "\"age\"", "\"years\"");
        var c2 = new DocumentChange("/age", new TextSpan(valOffset, 2), "30", "42");
        string current = Source[..keyOffset] + "\"years\"" + Source[(keyOffset + 5)..valOffset] + "42" + Source[(valOffset + 2)..];
        var doc = new FakeStructuraDocument(Source, new[] { c1, c2 })
        {
            CurrentTextOverride = current,
        };

        var lines = new DiffHunkBuilder().Build(doc, new UnifiedDiffOptions());

        lines.Where(l => l.Kind == DiffLineKind.Removed).Should().HaveCount(1);
        lines.Where(l => l.Kind == DiffLineKind.Added).Should().HaveCount(1);
        lines.First(l => l.Kind == DiffLineKind.Added).Content.Should().Contain("years").And.Contain("42");
    }

    [Fact]
    public void Build_SingleLineChange_InlineHighlight_HasExpectedColumnRange()
    {
        // Replace 30 with 42 — expect highlight on the "30"/"42" substring at
        // exactly its column position within the line.
        int ageOffset = Source.IndexOf("30", System.StringComparison.Ordinal);
        // The line "  \"age\": 30," — "30" starts at column 9 (0-based: 2 spaces, '"', a, g, e, '"', ':', ' ' = 8 chars before).
        const int expectedColumn = 9;
        const int expectedLength = 2;

        var c = new DocumentChange("/age", new TextSpan(ageOffset, 2), "30", "42");
        var doc = new FakeStructuraDocument(Source, new[] { c })
        {
            CurrentTextOverride = Source[..ageOffset] + "42" + Source[(ageOffset + 2)..],
        };

        var lines = new DiffHunkBuilder().Build(doc, new UnifiedDiffOptions());

        DiffLine removed = lines.First(l => l.Kind == DiffLineKind.Removed);
        removed.InlineHighlights.Should().HaveCount(1);
        removed.InlineHighlights[0].Start.Should().Be(expectedColumn);
        removed.InlineHighlights[0].Length.Should().Be(expectedLength);

        DiffLine added = lines.First(l => l.Kind == DiffLineKind.Added);
        added.InlineHighlights.Should().HaveCount(1);
        added.InlineHighlights[0].Start.Should().Be(expectedColumn);
        added.InlineHighlights[0].Length.Should().Be(expectedLength);
    }

    [Fact]
    public void Build_InlineHighlightDisabled_NoHighlights()
    {
        int ageOffset = Source.IndexOf("30", System.StringComparison.Ordinal);
        var c = new DocumentChange("/age", new TextSpan(ageOffset, 2), "30", "42");
        var doc = new FakeStructuraDocument(Source, new[] { c })
        {
            CurrentTextOverride = Source[..ageOffset] + "42" + Source[(ageOffset + 2)..],
        };

        var lines = new DiffHunkBuilder().Build(doc, new UnifiedDiffOptions { InlineHighlight = false });

        foreach (DiffLine l in lines)
        {
            l.InlineHighlights.Should().BeEmpty();
        }
    }

    [Fact]
    public void Build_TwoChangesWithUntouchedLinesBetween_EmitsContextWithinHunk()
    {
        // 7-line file, change line 1 ("name") and line 4 ("city") — 3 lines apart,
        // inside the merge gap (default ContextLines=3, so gap=6).
        // Expectation: lines 2-3 are emitted ONCE as Context, not twice as Removed+Added.
        const string text =
            "{\n" +
            "  \"name\": \"Alice\",\n" +
            "  \"x\": 1,\n" +
            "  \"y\": 2,\n" +
            "  \"city\": \"Paris\",\n" +
            "  \"z\": 9\n" +
            "}";
        int nameOffset = text.IndexOf("\"Alice\"", System.StringComparison.Ordinal);
        int cityOffset = text.IndexOf("\"Paris\"", System.StringComparison.Ordinal);
        var c1 = new DocumentChange("/name", new TextSpan(nameOffset, 7), "\"Alice\"", "\"Bob\"");
        var c2 = new DocumentChange("/city", new TextSpan(cityOffset, 7), "\"Paris\"", "\"Lyon\"");
        string current = text[..nameOffset] + "\"Bob\"" + text[(nameOffset + 7)..cityOffset] + "\"Lyon\"" + text[(cityOffset + 7)..];
        var doc = new FakeStructuraDocument(text, new[] { c1, c2 })
        {
            CurrentTextOverride = current,
        };

        var lines = new DiffHunkBuilder().Build(doc, new UnifiedDiffOptions());

        // Single hunk (no separator), 2 Removed, 2 Added.
        lines.Where(l => l.Kind == DiffLineKind.HunkSeparator).Should().BeEmpty();
        lines.Where(l => l.Kind == DiffLineKind.Removed).Should().HaveCount(2);
        lines.Where(l => l.Kind == DiffLineKind.Added).Should().HaveCount(2);

        // Inside the hunk we have two intra-hunk Context lines for the unchanged
        // "x" and "y" entries. Total Context lines = pre + intra + post.
        // pre-context: line 0 "{" → 1 line.
        // intra-context: lines 2 ("  \"x\": 1,") and 3 ("  \"y\": 2,") → 2 lines.
        // post-context: lines 5 ("  \"z\": 9") and 6 ("}") → 2 lines.
        lines.Where(l => l.Kind == DiffLineKind.Context).Should().HaveCount(5);

        // Verify the intra-hunk context lines have correct content.
        lines.Where(l => l.Kind == DiffLineKind.Context && l.Content.Contains("\"x\"")).Should().HaveCount(1);
        lines.Where(l => l.Kind == DiffLineKind.Context && l.Content.Contains("\"y\"")).Should().HaveCount(1);
    }
}
