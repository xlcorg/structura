using FluentAssertions;

using Structura.Reporting;
using Structura.Runtime;

using Xunit;

namespace Structura.UnitTests.Reporting;

public sealed class SimpleReporterTests
{
    [Fact]
    public void Print_NoChanges_WritesNoChangesMessage()
    {
        var doc = new FakeStructuraDocument("anything", new List<DocumentChange>());
        var sw = new StringWriter();

        SimpleReporter.Print(doc, sw);

        sw.ToString().Trim().Should().Be("(no changes)");
    }

    [Fact]
    public void Print_OneChange_FormatsPathOldNew()
    {
        var doc = new FakeStructuraDocument(
            "{ \"currency\": \"RUB\" }",
            new List<DocumentChange> {
                new DocumentChange("/currency", new TextSpan(15, 5), "\"RUB\"", "\"USD\""),
            });
        var sw = new StringWriter();

        SimpleReporter.Print(doc, sw);

        var output = sw.ToString();
        output.Should().Contain("1 change(s):");
        output.Should().Contain("  /currency: \"RUB\" → \"USD\"");
    }

    [Fact]
    public void Print_MultipleChanges_PrintsCountHeaderAndOneLinePerChange()
    {
        var doc = new FakeStructuraDocument(
            "irrelevant",
            new List<DocumentChange> {
                new DocumentChange("/version", new TextSpan(0, 1), "7", "42"),
                new DocumentChange("/currency", new TextSpan(2, 5), "\"RUB\"", "\"USD\""),
                new DocumentChange("/is_priority", new TextSpan(8, 4), "true", "false"),
            });
        var sw = new StringWriter();

        SimpleReporter.Print(doc, sw);

        var output = sw.ToString();
        output.Should().StartWith("3 change(s):");
        output.Should().Contain("/version: 7 → 42");
        output.Should().Contain("/currency: \"RUB\" → \"USD\"");
        output.Should().Contain("/is_priority: true → false");
    }

    [Fact]
    public void Print_NullDocument_Throws()
    {
        Action act = () => SimpleReporter.Print(null!, new StringWriter());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Print_NullWriter_Throws()
    {
        var doc = new FakeStructuraDocument("x", new List<DocumentChange>());
        Action act = () => SimpleReporter.Print(doc, null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
