using FluentAssertions;

using Structura.Reporting;
using Structura.Reporting.Internal.Highlighting;
using Structura.Runtime;

using Xunit;

namespace Structura.UnitTests.Reporting;

public sealed class DiffReporterTests
{
    private const string Source =
        "{\n" +
        "  \"name\": \"Alice\",\n" +
        "  \"age\": 30,\n" +
        "  \"city\": \"Paris\"\n" +
        "}";

    private static FakeStructuraDocument MakeDoc()
    {
        int ageOffset = Source.IndexOf("30", System.StringComparison.Ordinal);
        var c = new DocumentChange("/age", new TextSpan(ageOffset, 2), "30", "42");
        return new FakeStructuraDocument(Source, new[] { c }, documentName: "test.json")
        {
            CurrentTextOverride = Source[..ageOffset] + "42" + Source[(ageOffset + 2)..],
        };
    }

    [Fact]
    public void DiffReporterOptions_Defaults_LayoutIsAuto()
    {
        var options = new DiffReporterOptions();

        options.Layout.Should().Be(DiffReporterLayout.Auto);
    }

    [Fact]
    public void DiffReporterOptions_Defaults_SyntaxHighlightIsTrue()
    {
        var options = new DiffReporterOptions();

        options.SyntaxHighlight.Should().BeTrue();
    }

    [Fact]
    public void Print_NoChanges_WritesNoChangesMessage()
    {
        var doc = new FakeStructuraDocument(Source, System.Array.Empty<DocumentChange>(), documentName: "test.json");
        var sw = new System.IO.StringWriter();

        DiffReporter.Print(doc, sw);

        sw.ToString().Trim().Should().Be("(no changes)");
    }

    [Fact]
    public void Print_LayoutUnified_BannerAndUnifiedHunk()
    {
        var doc = MakeDoc();
        var sw = new System.IO.StringWriter();

        var options = new DiffReporterOptions { Layout = DiffReporterLayout.Unified };
        DiffReporter.Print(doc, sw, options);

        string output = sw.ToString();
        output.Should().Contain("● Patched");
        output.Should().Contain("└ Patched test.json with 1 addition and 1 removal");
        output.Should().Contain(" - ");
        output.Should().Contain(" + ");
        output.Should().Contain("\"age\": 30,");
        output.Should().Contain("\"age\": 42,");
        // Unified layout does not emit the SBS separator.
        output.Should().NotContain(" │ ");
    }

    [Fact]
    public void Print_LayoutSideBySide_BothColumnsShowAgeLine()
    {
        var doc = MakeDoc();
        var sw = new System.IO.StringWriter();

        var options = new DiffReporterOptions { Layout = DiffReporterLayout.SideBySide };
        DiffReporter.Print(doc, sw, options);

        string output = sw.ToString();
        output.Should().Contain(" │ ");
        output.Should().Contain("\"age\": 30,");
        output.Should().Contain("\"age\": 42,");
    }

    [Fact]
    public void Print_StringWriter_NoAnsiEscapes()
    {
        var doc = MakeDoc();
        var sw = new System.IO.StringWriter();

        DiffReporter.Print(doc, sw, new DiffReporterOptions { Layout = DiffReporterLayout.Unified });

        sw.ToString().Should().NotContain("\x1b");
    }

    [Fact]
    public void Print_NullDocument_Throws()
    {
        System.Action act = () => DiffReporter.Print(null!, new System.IO.StringWriter());
        act.Should().Throw<System.ArgumentNullException>();
    }

    [Fact]
    public void Print_NullWriter_Throws()
    {
        var doc = new FakeStructuraDocument("x", System.Array.Empty<DocumentChange>());
        System.Action act = () => DiffReporter.Print(doc, writer: null!);
        act.Should().Throw<System.ArgumentNullException>();
    }

    [Fact]
    public void RenderTo_ColorEnabled_SyntaxHighlightOn_AppliesKeyFg()
    {
        var doc = MakeDoc();
        var sw = new System.IO.StringWriter();

        DiffReporter.RenderTo(
            doc,
            sw,
            new DiffReporterOptions { Layout = DiffReporterLayout.Unified },
            terminalWidth: 120,
            useColor: true,
            useUnicode: true);

        string output = sw.ToString();
        output.Should().Contain(SyntaxPalette.Bright(TokenKind.Key));
    }

    [Fact]
    public void RenderTo_ColorEnabled_SyntaxHighlightOff_NoTokenFg()
    {
        var doc = MakeDoc();
        var sw = new System.IO.StringWriter();

        var options = new DiffReporterOptions
        {
            Layout = DiffReporterLayout.Unified,
            SyntaxHighlight = false,
        };
        DiffReporter.RenderTo(doc, sw, options, terminalWidth: 120, useColor: true, useUnicode: true);

        string output = sw.ToString();
        output.Should().NotContain(SyntaxPalette.Bright(TokenKind.Key));
        output.Should().NotContain(SyntaxPalette.Bright(TokenKind.Number));
    }

    [Fact]
    public void DiffReporterOptions_Defaults_LeadingBlankLineIsTrue()
    {
        var options = new DiffReporterOptions();

        options.LeadingBlankLine.Should().BeTrue();
    }

    [Fact]
    public void Print_DefaultOptions_StartsWithBlankLine()
    {
        var doc = MakeDoc();
        var sw = new System.IO.StringWriter();

        DiffReporter.Print(doc, sw);

        string output = sw.ToString();
        output.Should().StartWith(System.Environment.NewLine);
    }

    [Fact]
    public void Print_LeadingBlankLineFalse_StartsWithBannerDot()
    {
        var doc = MakeDoc();
        var sw = new System.IO.StringWriter();

        var options = new DiffReporterOptions
        {
            Layout = DiffReporterLayout.Unified,
            LeadingBlankLine = false,
        };
        DiffReporter.Print(doc, sw, options);

        string output = sw.ToString();
        // The TextWriter overload uses useUnicode = true, so the dot is `●`.
        output.Should().StartWith("●");
    }

    [Fact]
    public void DiffReporterOptions_Defaults_HorizontalRuleIsFalse()
    {
        var options = new DiffReporterOptions();

        options.HorizontalRule.Should().BeFalse();
    }

    [Fact]
    public void RenderTo_HorizontalRuleTrue_Unicode_WritesUnicodeRuleAtTerminalWidth()
    {
        var doc = MakeDoc();
        var sw = new System.IO.StringWriter();

        var options = new DiffReporterOptions
        {
            Layout = DiffReporterLayout.Unified,
            LeadingBlankLine = false,
            HorizontalRule = true,
        };
        DiffReporter.RenderTo(doc, sw, options, terminalWidth: 80, useColor: false, useUnicode: true);

        string output = sw.ToString();
        string expectedRule = new string('─', 80);
        string expectedRuleLine = expectedRule + System.Environment.NewLine;
        string expectedRuleAndDot = expectedRuleLine + "●";
        output.Should().StartWith(expectedRuleLine);
        // Banner is the next thing after the rule.
        output.Should().Contain(expectedRuleAndDot);
    }

    [Fact]
    public void RenderTo_HorizontalRuleTrue_Ascii_WritesAsciiRuleAtTerminalWidth()
    {
        var doc = MakeDoc();
        var sw = new System.IO.StringWriter();

        var options = new DiffReporterOptions
        {
            Layout = DiffReporterLayout.Unified,
            LeadingBlankLine = false,
            HorizontalRule = true,
        };
        DiffReporter.RenderTo(doc, sw, options, terminalWidth: 80, useColor: false, useUnicode: false);

        string output = sw.ToString();
        string expectedRule = new string('-', 80);
        string expectedRuleLine = expectedRule + System.Environment.NewLine;
        string expectedRuleAndStar = expectedRuleLine + "*";
        output.Should().StartWith(expectedRuleLine);
        output.Should().Contain(expectedRuleAndStar);
    }

    [Fact]
    public void RenderTo_BothSeparatorsEnabled_OrderIsBlankThenRuleThenBanner()
    {
        var doc = MakeDoc();
        var sw = new System.IO.StringWriter();

        var options = new DiffReporterOptions
        {
            Layout = DiffReporterLayout.Unified,
            LeadingBlankLine = true,
            HorizontalRule = true,
        };
        DiffReporter.RenderTo(doc, sw, options, terminalWidth: 80, useColor: false, useUnicode: true);

        string output = sw.ToString();
        var newlineSeparators = new[] { System.Environment.NewLine };
        string[] lines = output.Split(newlineSeparators, System.StringSplitOptions.None);

        string expectedRule = new string('─', 80);
        lines[0].Should().BeEmpty();
        lines[1].Should().Be(expectedRule);
        lines[2].Should().StartWith("●");
    }

    [Fact]
    public void RenderTo_NoChanges_BothSeparatorsEnabled_EmitsBlankAndRuleBeforeNoChangesMessage()
    {
        var emptyDoc = new FakeStructuraDocument(Source, System.Array.Empty<DocumentChange>(), documentName: "test.json");
        var sw = new System.IO.StringWriter();

        var options = new DiffReporterOptions
        {
            Layout = DiffReporterLayout.Unified,
            LeadingBlankLine = true,
            HorizontalRule = true,
        };
        DiffReporter.RenderTo(emptyDoc, sw, options, terminalWidth: 80, useColor: false, useUnicode: true);

        string output = sw.ToString();
        var newlineSeparators = new[] { System.Environment.NewLine };
        string[] lines = output.Split(newlineSeparators, System.StringSplitOptions.None);
        string expectedRule = new string('─', 80);

        lines[0].Should().BeEmpty();
        lines[1].Should().Be(expectedRule);
        lines[2].Should().Be("(no changes)");
    }
}
