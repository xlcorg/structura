using FluentAssertions;

using Structura.Reporting;
using Structura.Runtime;

using Xunit;

namespace Structura.UnitTests.Reporting;

public sealed class DiffReporterLayoutTests
{
    private const string Source =
        "{\n" +
        "  \"name\": \"Alice\",\n" +
        "  \"age\": 30,\n" +
        "  \"city\": \"Paris\"\n" +
        "}";

    private static FakeStructuraDocument MakeDoc(string source = Source, string newAgeLiteral = "42")
    {
        int ageOffset = source.IndexOf("30", System.StringComparison.Ordinal);
        var c = new DocumentChange("/age", new TextSpan(ageOffset, 2), "30", newAgeLiteral);
        return new FakeStructuraDocument(source, new[] { c }, documentName: "test.json")
        {
            CurrentTextOverride = source[..ageOffset] + newAgeLiteral + source[(ageOffset + 2)..],
        };
    }

    private static string Render(IStructuraDocument doc, int terminalWidth, DiffReporterLayout? forced = null)
    {
        var sw = new System.IO.StringWriter();
        var options = forced is DiffReporterLayout f
            ? new DiffReporterOptions { Layout = f }
            : new DiffReporterOptions();
        DiffReporter.RenderTo(doc, sw, options, terminalWidth, useColor: false, useUnicode: true);
        return sw.ToString();
    }

    [Fact]
    public void Auto_PicksSideBySide_When_NaturalWidthFits()
    {
        var doc = MakeDoc();
        string output = Render(doc, terminalWidth: 200);

        output.Should().Contain(" │ ");
        // Natural fit — no truncation indicator on the content lines.
        output.Should().NotContain("…");
    }

    [Fact]
    public void Auto_PicksSideBySide_When_AboveMinButBelowNatural()
    {
        // Long content forces truncation; 100 cols is above MinSbs but below
        // the natural side-by-side width, so the layout stays SBS with `…`.
        const string longSource =
            "{\n" +
            "  \"x\": \"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\n" +
            "  \"age\": 30\n" +
            "}";

        var doc = MakeDoc(longSource);
        string output = Render(doc, terminalWidth: 100);

        output.Should().Contain(" │ ");
        output.Should().Contain("…");
    }

    [Fact]
    public void Auto_PicksUnified_When_BelowMinSideBySide()
    {
        // 60 cols < 2*(1+3) + 3 + 2*40 = 91, so SBS is too cramped → Unified.
        var doc = MakeDoc();
        string output = Render(doc, terminalWidth: 60);

        output.Should().NotContain(" │ ");
        output.Should().Contain(" - ");
        output.Should().Contain(" + ");
    }

    [Fact]
    public void Auto_NarrowTerminal_FallsThroughToUnified_WithoutFloor()
    {
        // 80-wide terminal: below minSbsWidth (91), so Auto must pick Unified
        // — even though 80 is below FallbackTotalWidth (120), the heuristic
        // should not silently inflate the layout choice.
        var doc = MakeDoc();
        string output = Render(doc, terminalWidth: 80);

        output.Should().NotContain(" │ ");
        output.Should().Contain(" - ");
        output.Should().Contain(" + ");
    }

    [Fact]
    public void Explicit_Unified_Forces_UnifiedEvenAtWideTerminal()
    {
        var doc = MakeDoc();
        string output = Render(doc, terminalWidth: 200, forced: DiffReporterLayout.Unified);

        output.Should().NotContain(" │ ");
    }

    [Fact]
    public void Explicit_SideBySide_Forces_SbsEvenAtNarrowTerminal()
    {
        var doc = MakeDoc();
        string output = Render(doc, terminalWidth: 40, forced: DiffReporterLayout.SideBySide);

        output.Should().Contain(" │ ");
    }
}
