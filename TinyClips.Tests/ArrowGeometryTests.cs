using TinyClips.Helpers;
using Xunit;

namespace TinyClips.Tests;

/// <summary>
/// Tests for ArrowGeometry — arrowhead vertex calculation and hex color parsing.
/// </summary>
public class ArrowGeometryTests
{
    // MARK: - CalculateArrowhead

    [Fact]
    public void CalculateArrowhead_HorizontalLine_ReturnsCorrectVertices()
    {
        var result = ArrowGeometry.CalculateArrowhead(0, 0, 100, 0, 3);
        Assert.NotNull(result);

        var (tip, left, right) = result.Value;
        Assert.Equal(100, tip.X, 1);
        Assert.Equal(0, tip.Y, 1);

        // Unit vector is (1,0), so:
        // baseX = 100 - 1*12 = 88, baseY = 0
        // left  = (88 + 0*8, 0 - 1*8) = (88, -8)
        // right = (88 - 0*8, 0 + 1*8) = (88,  8)
        Assert.Equal(88, left.X, 1);
        Assert.Equal(-8, left.Y, 1);
        Assert.Equal(88, right.X, 1);
        Assert.Equal(8, right.Y, 1);
    }

    [Fact]
    public void CalculateArrowhead_VerticalLine_ReturnsCorrectVertices()
    {
        var result = ArrowGeometry.CalculateArrowhead(0, 0, 0, 100, 3);
        Assert.NotNull(result);

        var (tip, left, right) = result.Value;
        Assert.Equal(0, tip.X, 1);
        Assert.Equal(100, tip.Y, 1);

        // Unit vector is (0,1), so headLen=12, headW=8:
        // baseX = 0, baseY = 100 - 12 = 88
        // left  = (0 + 1*8, 88 - 0*8) = (8, 88)
        // right = (0 - 1*8, 88 + 0*8) = (-8, 88)
        Assert.Equal(8, left.X, 1);
        Assert.Equal(88, left.Y, 1);
        Assert.Equal(-8, right.X, 1);
        Assert.Equal(88, right.Y, 1);
    }

    [Fact]
    public void CalculateArrowhead_TooShort_ReturnsNull()
    {
        var result = ArrowGeometry.CalculateArrowhead(50, 50, 50, 50, 3);
        Assert.Null(result);
    }

    [Fact]
    public void CalculateArrowhead_LargerThickness_ScalesHead()
    {
        var result = ArrowGeometry.CalculateArrowhead(0, 0, 100, 0, 5);
        Assert.NotNull(result);

        var (tip, left, right) = result.Value;
        // headLength = max(5*4, 12) = 20, headWidth = max(5*2.5, 8) = 12.5
        double expectedBaseX = 100 - 20;
        Assert.Equal(expectedBaseX, left.X, 1);
        Assert.Equal(-12.5, left.Y, 1);
        Assert.Equal(expectedBaseX, right.X, 1);
        Assert.Equal(12.5, right.Y, 1);
    }

    [Fact]
    public void CalculateArrowhead_DiagonalLine_TipMatchesEnd()
    {
        var result = ArrowGeometry.CalculateArrowhead(0, 0, 100, 100, 3);
        Assert.NotNull(result);

        var (tip, _, _) = result.Value;
        Assert.Equal(100, tip.X, 1);
        Assert.Equal(100, tip.Y, 1);
    }

    [Fact]
    public void CalculateArrowhead_ReversedDirection_Works()
    {
        var result = ArrowGeometry.CalculateArrowhead(100, 100, 0, 0, 3);
        Assert.NotNull(result);

        var (tip, _, _) = result.Value;
        Assert.Equal(0, tip.X, 1);
        Assert.Equal(0, tip.Y, 1);
    }

    // MARK: - ParseHexColor

    [Fact]
    public void ParseHexColor_WithHash_ParsesCorrectly()
    {
        var (r, g, b) = ArrowGeometry.ParseHexColor("#FF3B30");
        Assert.Equal(255, r);
        Assert.Equal(59, g);
        Assert.Equal(48, b);
    }

    [Fact]
    public void ParseHexColor_WithoutHash_ParsesCorrectly()
    {
        var (r, g, b) = ArrowGeometry.ParseHexColor("007AFF");
        Assert.Equal(0, r);
        Assert.Equal(122, g);
        Assert.Equal(255, b);
    }

    [Fact]
    public void ParseHexColor_Black()
    {
        var (r, g, b) = ArrowGeometry.ParseHexColor("000000");
        Assert.Equal(0, r);
        Assert.Equal(0, g);
        Assert.Equal(0, b);
    }

    [Fact]
    public void ParseHexColor_White()
    {
        var (r, g, b) = ArrowGeometry.ParseHexColor("FFFFFF");
        Assert.Equal(255, r);
        Assert.Equal(255, g);
        Assert.Equal(255, b);
    }
}
