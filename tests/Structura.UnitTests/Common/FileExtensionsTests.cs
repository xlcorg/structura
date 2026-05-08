using FluentAssertions;

using Structura.Common;

using Xunit;

namespace Structura.UnitTests.Common;

public sealed class FileExtensionsTests
{
    [Fact]
    public void ReadAllText_RoundTripsContentWrittenToTempFile()
    {
        string path = Path.GetTempFileName();
        try
        {
            const string payload = "hello\nстрока\nмир\n";
            File.WriteAllText(path, payload);

            path.ReadAllText().Should().Be(payload);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void WriteAllText_WritesContentAndReturnsPath()
    {
        string path = Path.GetTempFileName();
        try
        {
            const string payload = "{\"x\":1}";
            string returned = path.WriteAllText(payload);

            returned.Should().Be(path);
            File.ReadAllText(path).Should().Be(payload);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AppendPath_CombinesSegments()
    {
        string combined = "root".AppendPath("a", "b", "c.json");
        combined.Should().Be(Path.Combine("root", "a", "b", "c.json"));
    }

    [Fact]
    public void AppendPath_RejectsRootedSegment()
    {
        Action act = () => "root".AppendPath(Path.GetTempPath());
        act.Should().Throw<ArgumentException>();
    }
}
