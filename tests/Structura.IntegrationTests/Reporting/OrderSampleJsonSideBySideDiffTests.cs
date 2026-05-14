using FluentAssertions;

using Structura.Generated;
using Structura.Reporting;
using Structura.Runtime;

using Xunit;

namespace Structura.IntegrationTests.Reporting;

public sealed class OrderSampleJsonSideBySideDiffTests
{
    private static string LoadSample() => File.ReadAllText("order.sample.json");

    private static void RenderSideBySide(IStructuraDocument doc, StringWriter sw, int totalWidth)
    {
        var options = new DiffReporterOptions { Layout = DiffReporterLayout.SideBySide };
        DiffReporter.RenderTo(doc, sw, options, totalWidth, useColor: false, useUnicode: true);
    }

    [Fact]
    public void DiffReporter_SideBySide_NoMutation_PrintsNoChanges()
    {
        var order = LoadSample().ParseJson<OrderSampleJson>();
        var sw = new StringWriter();

        DiffReporter.Print(order, sw, new DiffReporterOptions { Layout = DiffReporterLayout.SideBySide });

        sw.ToString().Trim().Should().Be("(no changes)");
    }

    [Fact]
    public void DiffReporter_SideBySide_DocumentName_FromSourceFileName()
    {
        var order = LoadSample().ParseJson<OrderSampleJson>();
        order.Currency = "USD";
        var sw = new StringWriter();

        RenderSideBySide(order, sw, totalWidth: 200);

        sw.ToString().Should().Contain("Patched order.sample.json with");
    }

    [Fact]
    public void DiffReporter_SideBySide_RealMutations_BannerAndExpectedRows()
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

        RenderSideBySide(order, sw, totalWidth: 200);

        string output = sw.ToString();
        output.Should().Contain("Patched order.sample.json with 8 additions and 8 removals");
        output.Should().Contain("\"version\": 7,");
        output.Should().Contain("\"version\": 42,");
        output.Should().Contain("\"currency\": \"RUB\"");
        output.Should().Contain("\"currency\": \"USD\"");
        output.Should().Contain(" │ ");
    }
}
