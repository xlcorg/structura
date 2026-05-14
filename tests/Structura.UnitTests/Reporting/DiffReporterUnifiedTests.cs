using FluentAssertions;

using Structura.Reporting;
using Structura.Runtime;

using Xunit;

namespace Structura.UnitTests.Reporting;

public sealed class DiffReporterUnifiedTests
{
    private const string Source =
        "{\n" +
        "  \"name\": \"Alice\",\n" +
        "  \"age\": 30,\n" +
        "  \"city\": \"Paris\"\n" +
        "}";

    private static DiffReporterOptions Unified()
    {
        return new DiffReporterOptions { Layout = DiffReporterLayout.Unified };
    }

    [Fact]
    public void Print_MultipleChanges_BannerUsesPlural()
    {
        int nameOffset = Source.IndexOf("\"Alice\"", System.StringComparison.Ordinal);
        int ageOffset = Source.IndexOf("30", System.StringComparison.Ordinal);
        var changes = new[]
        {
            new DocumentChange("/name", new TextSpan(nameOffset, 7), "\"Alice\"", "\"Bob\""),
            new DocumentChange("/age", new TextSpan(ageOffset, 2), "30", "42"),
        };
        string current = Source[..nameOffset] + "\"Bob\"" + Source[(nameOffset + 7)..ageOffset] + "42" + Source[(ageOffset + 2)..];
        var doc = new FakeStructuraDocument(Source, changes, documentName: "test.json")
        {
            CurrentTextOverride = current,
        };
        var sw = new System.IO.StringWriter();

        DiffReporter.Print(doc, sw, Unified());

        sw.ToString().Should().Contain("with 2 additions and 2 removals");
    }

    [Fact]
    public void Print_DocumentNameFromFake_AppearsInBanner()
    {
        int ageOffset = Source.IndexOf("30", System.StringComparison.Ordinal);
        var c = new DocumentChange("/age", new TextSpan(ageOffset, 2), "30", "42");
        var doc = new FakeStructuraDocument(Source, new[] { c }, documentName: "alpha/beta.json")
        {
            CurrentTextOverride = Source[..ageOffset] + "42" + Source[(ageOffset + 2)..],
        };
        var sw = new System.IO.StringWriter();

        DiffReporter.Print(doc, sw, Unified());

        sw.ToString().Should().Contain("Patched alpha/beta.json with");
    }

    [Fact]
    public void Print_ShowFullFile_RendersAllLines()
    {
        int ageOffset = Source.IndexOf("30", System.StringComparison.Ordinal);
        var c = new DocumentChange("/age", new TextSpan(ageOffset, 2), "30", "42");
        var doc = new FakeStructuraDocument(Source, new[] { c }, documentName: "test.json")
        {
            CurrentTextOverride = Source[..ageOffset] + "42" + Source[(ageOffset + 2)..],
        };
        var sw = new System.IO.StringWriter();

        var options = new DiffReporterOptions
        {
            Layout = DiffReporterLayout.Unified,
            ShowFullFile = true,
        };
        DiffReporter.Print(doc, sw, options);

        string output = sw.ToString();
        output.Should().Contain("\"name\": \"Alice\",");
        output.Should().Contain("\"city\": \"Paris\"");
        output.Should().Contain("\"age\": 30,");
        output.Should().Contain("\"age\": 42,");
        output.Should().NotContain("…");
    }
}
