using FluentAssertions;

using Structura.Generated;
using Structura.Reporting;
using Structura.Runtime;

using Xunit;

namespace Structura.IntegrationTests.Reporting;

/// <summary>
/// End-to-end tests through the generator-produced <see cref="OrderSampleJson"/>:
/// real <c>order.sample.json</c> → mutate → render via <see cref="DiffReporter"/>
/// in unified layout → assert plain-text output via <see cref="StringWriter"/>.
/// </summary>
public sealed class OrderSampleJsonReportingTests
{
    private static string LoadSample()
    {
        return File.ReadAllText("order.sample.json");
    }

    private static DiffReporterOptions Unified()
    {
        return new DiffReporterOptions { Layout = DiffReporterLayout.Unified };
    }

    [Fact]
    public void DiffReporter_NoMutation_PrintsNoChanges()
    {
        var order = LoadSample().ParseJson<OrderSampleJson>();
        var sw = new StringWriter();

        DiffReporter.Print(order, sw, Unified());

        sw.ToString().Trim().Should().Be("(no changes)");
    }

    [Fact]
    public void DiffReporter_DocumentName_FromSourceFileName()
    {
        var order = LoadSample().ParseJson<OrderSampleJson>();
        order.Currency = "USD";
        var sw = new StringWriter();

        DiffReporter.Print(order, sw, Unified());

        sw.ToString().Should().Contain("Patched order.sample.json with");
    }

    [Fact]
    public void DiffReporter_RealMutations_BannerAndHunks()
    {
        var order = LoadSample().ParseJson<OrderSampleJson>();
        order.Currency = "USD";
        order.Version = 42;
        order.IsPriority = false;
        order.Customer.FirstName = "Ivan";
        order.Customer.Preferences.MarketingConsent = false;
        order.BillingAddress.City = "Rotterdam";
        order.Items[0].Quantity = 2;
        order.Items[1].Manufacturer.CountryCode = "DE";
        var sw = new StringWriter();

        DiffReporter.Print(order, sw, Unified());

        string output = sw.ToString();
        output.Should().Contain("Patched order.sample.json with 8 additions and 8 removals");
        output.Should().Contain(" - ").And.Contain(" + ");
        output.Should().Contain("\"RUB\"");
        output.Should().Contain("\"USD\"");
        output.Should().Contain("Rotterdam");
        output.Should().Contain("Ivan");
    }
}
