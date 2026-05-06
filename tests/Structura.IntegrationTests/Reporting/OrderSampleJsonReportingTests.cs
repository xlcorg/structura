using FluentAssertions;

using Structura.Generated;
using Structura.Reporting;
using Structura.Runtime;

using Xunit;

namespace Structura.IntegrationTests.Reporting;

/// <summary>
/// End-to-end tests through the generator-produced <see cref="OrderSampleJson"/>:
/// real <c>order.sample.json</c> → mutate → run reporters → assert plain-text
/// output via <see cref="StringWriter"/>.
/// </summary>
public sealed class OrderSampleJsonReportingTests
{
    private static string LoadSample()
    {
        return File.ReadAllText("order.sample.json");
    }

    // ── SimpleReporter ─────────────────────────────────────────────────────

    [Fact]
    public void SimpleReporter_NoMutation_PrintsNoChanges()
    {
        var order = LoadSample().ParseJson<OrderSampleJson>();
        var sw = new StringWriter();

        SimpleReporter.Print(order, sw);

        sw.ToString().Trim().Should().Be("(no changes)");
    }

    [Fact]
    public void SimpleReporter_AfterCurrencyMutation_LineIncludesPathAndLiterals()
    {
        var order = LoadSample().ParseJson<OrderSampleJson>();
        order.Currency = "USD";
        var sw = new StringWriter();

        SimpleReporter.Print(order, sw);

        string output = sw.ToString();
        output.Should().Contain("1 change(s):");
        output.Should().Contain("/currency: \"RUB\" → \"USD\"");
    }

    [Fact]
    public void SimpleReporter_MultipleMutations_OneLinePerChange()
    {
        var order = LoadSample().ParseJson<OrderSampleJson>();
        order.Currency = "USD";
        order.Version = 42;
        order.IsPriority = false;
        var sw = new StringWriter();

        SimpleReporter.Print(order, sw);

        string output = sw.ToString();
        output.Should().StartWith("3 change(s):");
        output.Should().Contain("/currency: \"RUB\" → \"USD\"");
        output.Should().Contain("/version: 7 → 42");
        output.Should().Contain("/is_priority: true → false");
    }

    // ── ConsoleDiffReporter ───────────────────────────────────────────────

    [Fact]
    public void ConsoleDiffReporter_NoMutation_PrintsNoChanges()
    {
        var order = LoadSample().ParseJson<OrderSampleJson>();
        var sw = new StringWriter();

        ConsoleDiffReporter.Print(order, sw);

        sw.ToString().Trim().Should().Be("(no changes)");
    }

    [Fact]
    public void ConsoleDiffReporter_AfterCurrencyMutation_HunkContainsBothLiterals()
    {
        var order = LoadSample().ParseJson<OrderSampleJson>();
        order.Currency = "USD";
        var sw = new StringWriter();

        ConsoleDiffReporter.Print(order, sw);

        string output = sw.ToString();
        output.Should().Contain("@@ /currency (line ");
        output.Should().Contain("- ").And.Contain("\"RUB\"");
        output.Should().Contain("+ ").And.Contain("\"USD\"");
    }

    [Fact]
    public void ConsoleDiffReporter_MultipleMutations_OneHunkPerChange()
    {
        var order = LoadSample().ParseJson<OrderSampleJson>();
        order.Currency = "USD";
        order.Version = 42;
        order.IsPriority = false;
        var sw = new StringWriter();

        ConsoleDiffReporter.Print(order, sw);

        string output = sw.ToString();

        // Three @@ hunk headers, in document-position order:
        // is_priority (line 7), version (line 8), currency (line 10).
        int firstHeader = output.IndexOf("@@ /is_priority", StringComparison.Ordinal);
        int secondHeader = output.IndexOf("@@ /version", StringComparison.Ordinal);
        int thirdHeader = output.IndexOf("@@ /currency", StringComparison.Ordinal);

        firstHeader.Should().BeGreaterThanOrEqualTo(0);
        secondHeader.Should().BeGreaterThan(firstHeader);
        thirdHeader.Should().BeGreaterThan(secondHeader);
    }
}
