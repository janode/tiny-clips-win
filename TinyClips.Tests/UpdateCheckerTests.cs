using TinyClips.Services;
using Xunit;

namespace TinyClips.Tests;

public class UpdateCheckerTests
{
    // MARK: - ParseVersion with 'v' prefix

    [Fact]
    public void ParseVersion_WithVPrefix_ReturnsParsedVersion()
    {
        var v = UpdateChecker.ParseVersion("v1.2.3");
        Assert.NotNull(v);
        Assert.Equal(1, v!.Major);
        Assert.Equal(2, v.Minor);
        Assert.Equal(3, v.Build);
    }

    [Fact]
    public void ParseVersion_WithoutPrefix_ReturnsParsedVersion()
    {
        var v = UpdateChecker.ParseVersion("2.0.0");
        Assert.NotNull(v);
        Assert.Equal(2, v!.Major);
        Assert.Equal(0, v.Minor);
        Assert.Equal(0, v.Build);
    }

    [Fact]
    public void ParseVersion_FourPart_ReturnsAllParts()
    {
        var v = UpdateChecker.ParseVersion("v1.0.3.42");
        Assert.NotNull(v);
        Assert.Equal(1, v!.Major);
        Assert.Equal(0, v.Minor);
        Assert.Equal(3, v.Build);
        Assert.Equal(42, v.Revision);
    }

    // MARK: - Invalid version strings

    [Theory]
    [InlineData("invalid")]
    [InlineData("v")]
    [InlineData("")]
    [InlineData("v.1.2")]
    [InlineData("abc.def.ghi")]
    public void ParseVersion_InvalidString_ReturnsNull(string tag)
    {
        Assert.Null(UpdateChecker.ParseVersion(tag));
    }

    // MARK: - Version comparison (used by UpdateChecker logic)

    [Fact]
    public void ParseVersion_NewerVersion_IsGreater()
    {
        var current = UpdateChecker.ParseVersion("v1.0.0");
        var latest = UpdateChecker.ParseVersion("v1.0.1");
        Assert.True(latest > current);
    }

    [Fact]
    public void ParseVersion_SameVersion_NotGreater()
    {
        var current = UpdateChecker.ParseVersion("v1.0.0");
        var latest = UpdateChecker.ParseVersion("v1.0.0");
        Assert.False(latest > current);
    }

    [Fact]
    public void ParseVersion_OlderVersion_NotGreater()
    {
        var current = UpdateChecker.ParseVersion("v2.0.0");
        var latest = UpdateChecker.ParseVersion("v1.5.0");
        Assert.False(latest > current);
    }

    [Fact]
    public void ParseVersion_MajorBump_IsGreater()
    {
        var current = UpdateChecker.ParseVersion("v1.9.9");
        var latest = UpdateChecker.ParseVersion("v2.0.0");
        Assert.True(latest > current);
    }
}
