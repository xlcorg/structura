using FluentAssertions;

using Structura.Reporting;

using Xunit;

namespace Structura.UnitTests.Reporting;

public sealed class DiffReporterTests
{
    [Fact]
    public void DiffReporterOptions_Defaults_LayoutIsAuto()
    {
        var options = new DiffReporterOptions();

        options.Layout.Should().Be(DiffReporterLayout.Auto);
    }
}
