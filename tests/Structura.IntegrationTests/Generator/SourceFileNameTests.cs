using FluentAssertions;

using Structura.Generated;

using Xunit;

namespace Structura.IntegrationTests.Generator;

public sealed class SourceFileNameTests
{
    [Fact]
    public void OrderSampleJson_SourceFileName_MatchesSample()
    {
        OrderSampleJson.SourceFileName.Should().Be("order.sample.json");
    }

    [Fact]
    public void BlrwblSampleXml_SourceFileName_MatchesSample()
    {
        BlrwblSampleXml.SourceFileName.Should().Be("blrwbl.sample.xml");
    }

    [Fact]
    public void LibrarySampleJson_SourceFileName_MatchesSample()
    {
        LibrarySampleJson.SourceFileName.Should().Be("library.sample.json");
    }

    [Fact]
    public void LibrarySampleXml_SourceFileName_MatchesSample()
    {
        LibrarySampleXml.SourceFileName.Should().Be("library.sample.xml");
    }
}
