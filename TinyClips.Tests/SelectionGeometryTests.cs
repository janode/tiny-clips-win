using TinyClips.Helpers;
using Xunit;

namespace TinyClips.Tests;

/// <summary>
/// Tests for SelectionGeometry — rectangle normalization and minimum size checks.
/// </summary>
public class SelectionGeometryTests
{
    // MARK: - NormalizeRect

    [Fact]
    public void NormalizeRect_TopLeftToBottomRight_ReturnsCorrect()
    {
        var rect = SelectionGeometry.NormalizeRect(
            new System.Drawing.Point(10, 20),
            new System.Drawing.Point(100, 200));

        Assert.Equal(10, rect.X);
        Assert.Equal(20, rect.Y);
        Assert.Equal(90, rect.Width);
        Assert.Equal(180, rect.Height);
    }

    [Fact]
    public void NormalizeRect_BottomRightToTopLeft_ReturnsCorrect()
    {
        var rect = SelectionGeometry.NormalizeRect(
            new System.Drawing.Point(100, 200),
            new System.Drawing.Point(10, 20));

        Assert.Equal(10, rect.X);
        Assert.Equal(20, rect.Y);
        Assert.Equal(90, rect.Width);
        Assert.Equal(180, rect.Height);
    }

    [Fact]
    public void NormalizeRect_TopRightToBottomLeft()
    {
        var rect = SelectionGeometry.NormalizeRect(
            new System.Drawing.Point(100, 20),
            new System.Drawing.Point(10, 200));

        Assert.Equal(10, rect.X);
        Assert.Equal(20, rect.Y);
        Assert.Equal(90, rect.Width);
        Assert.Equal(180, rect.Height);
    }

    [Fact]
    public void NormalizeRect_BottomLeftToTopRight()
    {
        var rect = SelectionGeometry.NormalizeRect(
            new System.Drawing.Point(10, 200),
            new System.Drawing.Point(100, 20));

        Assert.Equal(10, rect.X);
        Assert.Equal(20, rect.Y);
        Assert.Equal(90, rect.Width);
        Assert.Equal(180, rect.Height);
    }

    [Fact]
    public void NormalizeRect_SamePoint_ReturnsZeroSize()
    {
        var rect = SelectionGeometry.NormalizeRect(
            new System.Drawing.Point(50, 50),
            new System.Drawing.Point(50, 50));

        Assert.Equal(0, rect.Width);
        Assert.Equal(0, rect.Height);
    }

    // MARK: - MeetsMinimumSize

    [Theory]
    [InlineData(10, 10, true)]
    [InlineData(100, 100, true)]
    [InlineData(9, 10, false)]
    [InlineData(10, 9, false)]
    [InlineData(0, 0, false)]
    [InlineData(5, 5, false)]
    public void MeetsMinimumSize_DefaultThreshold(int width, int height, bool expected)
    {
        Assert.Equal(expected, SelectionGeometry.MeetsMinimumSize(width, height));
    }

    [Theory]
    [InlineData(20, 20, 20, true)]
    [InlineData(19, 20, 20, false)]
    [InlineData(20, 19, 20, false)]
    public void MeetsMinimumSize_CustomThreshold(int width, int height, int minimum, bool expected)
    {
        Assert.Equal(expected, SelectionGeometry.MeetsMinimumSize(width, height, minimum));
    }
}
