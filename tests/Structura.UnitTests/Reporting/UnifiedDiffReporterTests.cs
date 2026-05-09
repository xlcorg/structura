using FluentAssertions;

using Structura.Reporting;
using Structura.Reporting.Internal.Highlighting;
using Structura.Runtime;

using Xunit;

namespace Structura.UnitTests.Reporting;

public sealed class UnifiedDiffReporterTests
{
    private const string Source =
        "{\n" +
        "  \"name\": \"Alice\",\n" +
        "  \"age\": 30,\n" +
        "  \"city\": \"Paris\"\n" +
        "}";

    [Fact]
    public void Print_NoChanges_WritesNoChangesMessage()
    {
        var doc = new FakeStructuraDocument(Source, System.Array.Empty<DocumentChange>(), documentName: "test.json");
        var sw = new System.IO.StringWriter();

        UnifiedDiffReporter.Print(doc, sw);

        sw.ToString().Trim().Should().Be("(no changes)");
    }

    [Fact]
    public void Print_SingleChange_BannerAndHunk()
    {
        int ageOffset = Source.IndexOf("30", System.StringComparison.Ordinal);
        var c = new DocumentChange("/age", new TextSpan(ageOffset, 2), "30", "42");
        var doc = new FakeStructuraDocument(
            Source,
            new[] { c },
            documentName: "test.json")
        {
            CurrentTextOverride = Source[..ageOffset] + "42" + Source[(ageOffset + 2)..],
        };
        var sw = new System.IO.StringWriter();

        UnifiedDiffReporter.Print(doc, sw);

        string output = sw.ToString();
        output.Should().Contain("● Patched");
        output.Should().Contain("└ Patched test.json with 1 addition and 1 removal");
        output.Should().Contain(" - ");
        output.Should().Contain(" + ");
        output.Should().Contain("\"age\": 30,");
        output.Should().Contain("\"age\": 42,");
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

        UnifiedDiffReporter.Print(doc, sw);

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

        UnifiedDiffReporter.Print(doc, sw);

        sw.ToString().Should().Contain("Patched alpha/beta.json with");
    }

    [Fact]
    public void Print_StringWriter_NoAnsiEscapes()
    {
        int ageOffset = Source.IndexOf("30", System.StringComparison.Ordinal);
        var c = new DocumentChange("/age", new TextSpan(ageOffset, 2), "30", "42");
        var doc = new FakeStructuraDocument(Source, new[] { c })
        {
            CurrentTextOverride = Source[..ageOffset] + "42" + Source[(ageOffset + 2)..],
        };
        var sw = new System.IO.StringWriter();

        UnifiedDiffReporter.Print(doc, sw);

        sw.ToString().Should().NotContain("\x1b");
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

        var options = new DiffReporterOptions { ShowFullFile = true };
        UnifiedDiffReporter.Print(doc, sw, options);

        string output = sw.ToString();
        output.Should().Contain("\"name\": \"Alice\",");
        output.Should().Contain("\"city\": \"Paris\"");
        output.Should().Contain("\"age\": 30,");
        output.Should().Contain("\"age\": 42,");
        output.Should().NotContain("…");
    }

    [Fact]
    public void Print_NullDocument_Throws()
    {
        System.Action act = () => UnifiedDiffReporter.Print(null!, new System.IO.StringWriter());
        act.Should().Throw<System.ArgumentNullException>();
    }

    [Fact]
    public void Print_NullWriter_Throws()
    {
        var doc = new FakeStructuraDocument("x", System.Array.Empty<DocumentChange>());
        System.Action act = () => UnifiedDiffReporter.Print(doc, writer: null!);
        act.Should().Throw<System.ArgumentNullException>();
    }

    [Fact]
    public void DiffReporterOptions_Defaults_SyntaxHighlightIsTrue()
    {
        var options = new DiffReporterOptions();

        options.SyntaxHighlight.Should().BeTrue();
    }

    [Fact]
    public void RenderTo_ColorEnabled_SyntaxHighlightOn_AppliesKeyAndNumberFg()
    {
        int ageOffset = Source.IndexOf("30", System.StringComparison.Ordinal);
        var change = new DocumentChange("/age", new TextSpan(ageOffset, 2), "30", "42");
        var changes = new[] { change };
        string current = Source[..ageOffset] + "42" + Source[(ageOffset + 2)..];
        var doc = new FakeStructuraDocument(Source, changes, documentName: "test.json")
        {
            CurrentTextOverride = current,
        };
        var sw = new System.IO.StringWriter();

        UnifiedDiffReporter.RenderTo(doc, sw, new DiffReporterOptions(), useColor: true, useUnicode: true);

        string output = sw.ToString();
        output.Should().Contain(SyntaxPalette.Bright(TokenKind.Key));
        // Number fg is suppressed inside the inline-highlight (the changed "30"/"42" span);
        // bright bg + bold is the change indicator there.
        output.Should().NotContain(SyntaxPalette.Bright(TokenKind.Number));
    }

    [Fact]
    public void RenderTo_ColorEnabled_SyntaxHighlightOff_NoTokenFg()
    {
        int ageOffset = Source.IndexOf("30", System.StringComparison.Ordinal);
        var change = new DocumentChange("/age", new TextSpan(ageOffset, 2), "30", "42");
        var changes = new[] { change };
        string current = Source[..ageOffset] + "42" + Source[(ageOffset + 2)..];
        var doc = new FakeStructuraDocument(Source, changes, documentName: "test.json")
        {
            CurrentTextOverride = current,
        };
        var sw = new System.IO.StringWriter();

        UnifiedDiffReporter.RenderTo(doc, sw, new DiffReporterOptions { SyntaxHighlight = false }, useColor: true, useUnicode: true);

        string output = sw.ToString();
        output.Should().NotContain(SyntaxPalette.Bright(TokenKind.Key));
        output.Should().NotContain(SyntaxPalette.Bright(TokenKind.Number));
    }
}
