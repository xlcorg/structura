using FluentAssertions;

using Structura.Reporting;
using Structura.Runtime;

using Xunit;

namespace Structura.UnitTests.Reporting;

public sealed class ConsoleDiffReporterTests
{
    private const string Source =
        "{\n" +
        "  \"currency\": \"RUB\",\n" +
        "  \"version\": 7,\n" +
        "  \"is_priority\": true\n" +
        "}";

    [Fact]
    public void Print_NoChanges_WritesNoChangesMessage()
    {
        var doc = new FakeStructuraDocument(Source, new List<DocumentChange>());
        var sw = new StringWriter();

        ConsoleDiffReporter.Print(doc, sw);

        sw.ToString().Trim().Should().Be("(no changes)");
    }

    [Fact]
    public void Print_OneChange_EmitsHunkHeaderAndDiffLines()
    {
        // "  \"currency\": \"RUB\","
        //   ^line 2, span starts at offset 15 → "RUB"
        int currencyValueOffset = Source.IndexOf("\"RUB\"", StringComparison.Ordinal);
        var change = new DocumentChange(
            "/currency",
            new TextSpan(currencyValueOffset, 5),
            "\"RUB\"",
            "\"USD\"");

        var doc = new FakeStructuraDocument(Source, new List<DocumentChange> { change });
        var sw = new StringWriter();

        ConsoleDiffReporter.Print(doc, sw);

        var output = sw.ToString();
        output.Should().Contain("@@ /currency (line 2) @@");
        output.Should().Contain("-   \"currency\": \"RUB\",");
        output.Should().Contain("+   \"currency\": \"USD\",");
    }

    [Fact]
    public void Print_ChangeOnFirstLine_LineNumberIs1()
    {
        const string SingleLine = "\"RUB\"";
        var change = new DocumentChange("/", new TextSpan(0, 5), "\"RUB\"", "\"USD\"");
        var doc = new FakeStructuraDocument(SingleLine, new List<DocumentChange> { change });
        var sw = new StringWriter();

        ConsoleDiffReporter.Print(doc, sw);

        sw.ToString().Should().Contain("(line 1)");
    }

    [Fact]
    public void Print_MultipleChanges_BlankLineBetweenHunks()
    {
        int currencyOffset = Source.IndexOf("\"RUB\"", StringComparison.Ordinal);
        int versionOffset = Source.IndexOf('7');
        var changes = new List<DocumentChange>
        {
            new DocumentChange("/currency", new TextSpan(currencyOffset, 5), "\"RUB\"", "\"USD\""),
            new DocumentChange("/version", new TextSpan(versionOffset, 1), "7", "42"),
        };

        var doc = new FakeStructuraDocument(Source, changes);
        var sw = new StringWriter();

        ConsoleDiffReporter.Print(doc, sw);

        var output = sw.ToString();
        output.Should().Contain("@@ /currency");
        output.Should().Contain("@@ /version");

        // Each hunk has 3 lines (header + minus + plus). With one blank line
        // between hunks the buffer ends up with exactly 7 non-final lines.
        string[] lines = output.Replace("\r", string.Empty).TrimEnd('\n').Split('\n');
        lines.Should().HaveCount(7);
        lines[3].Should().BeEmpty(); // separator between hunks
    }

    [Fact]
    public void Print_NullDocument_Throws()
    {
        Action act = () => ConsoleDiffReporter.Print(null!, new StringWriter());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Print_NullWriter_Throws()
    {
        var doc = new FakeStructuraDocument("x", new List<DocumentChange>());
        Action act = () => ConsoleDiffReporter.Print(doc, null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
