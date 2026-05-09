using FluentAssertions;

using Structura.Generated;
using Structura.Reporting;
using Structura.Runtime;

using Xunit;

namespace Structura.IntegrationTests.Reporting;

public sealed class OrderSampleJsonSideBySideDiffTests
{
    private static string LoadSample() =>
        File.ReadAllText("order.sample.json");

    [Fact]
    public void SideBySideDiffReporter_NoMutation_PrintsNoChanges()
    {
        var order = LoadSample().ParseJson<OrderSampleJson>();
        var sw = new StringWriter();

        SideBySideDiffReporter.Print(order, sw);

        sw.ToString().Trim().Should().Be("(no changes)");
    }

    [Fact]
    public void SideBySideDiffReporter_DocumentName_FromSourceFileName()
    {
        var order = LoadSample().ParseJson<OrderSampleJson>();
        order.Currency = "USD";
        var sw = new StringWriter();

        SideBySideDiffReporter.Print(order, sw, new SideBySideDiffOptions { TotalWidth = 200 });

        string output = sw.ToString();
        output.Should().Contain("Patched(order.sample.json)");
        output.Should().Contain("Patched order.sample.json with");
    }

    [Fact]
    public void SideBySideDiffReporter_RealMutations_BannerAndExpectedRows()
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

        SideBySideDiffReporter.Print(order, sw, new SideBySideDiffOptions { TotalWidth = 200 });

        string output = sw.ToString();
        output.Should().Contain("Patched order.sample.json with 8 additions and 8 removals");
        output.Should().Contain("\"version\": 7,");
        output.Should().Contain("\"version\": 42,");
        output.Should().Contain("\"currency\": \"RUB\"");
        output.Should().Contain("\"currency\": \"USD\"");
        output.Should().Contain(" │ ");
    }
}
