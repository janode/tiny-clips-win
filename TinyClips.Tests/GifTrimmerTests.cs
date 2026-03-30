using TinyClips.Helpers;
using Xunit;

namespace TinyClips.Tests;

/// <summary>
/// Tests for GifTrimHelper — pure logic extracted from GifTrimmerWindow.
/// </summary>
public class GifTrimmerTests
{
    // MARK: - Output Height (Aspect Ratio Preservation)

    [Theory]
    [InlineData(1920, 1080, 640, 360)]   // 16:9 → 640×360
    [InlineData(1280, 720, 640, 360)]    // 16:9 → 640×360
    [InlineData(800, 600, 400, 300)]     // 4:3 → 400×300
    [InlineData(500, 500, 250, 250)]     // Square stays square
    [InlineData(1080, 1920, 540, 960)]   // Portrait (9:16)
    public void OutputHeight_PreservesAspectRatio(int srcW, int srcH, int outW, int expectedH)
    {
        int outH = GifTrimHelper.CalculateOutputHeight(srcW, srcH, outW);
        Assert.Equal(expectedH, outH);
    }

    [Fact]
    public void OutputHeight_ZeroSourceWidth_FallsBackToOriginalHeight()
    {
        int outH = GifTrimHelper.CalculateOutputHeight(0, 480, 320);
        Assert.Equal(480, outH);
    }

    // MARK: - Even-Rounding of Dimensions

    [Theory]
    [InlineData(641, 640)]
    [InlineData(640, 640)]
    [InlineData(639, 638)]
    [InlineData(3, 2)]
    [InlineData(1, 0)]    // Falls below 2, needs floor clamp
    public void EvenRounding_BitwiseAnd_RoundsDown(int input, int expected)
    {
        int result = input & ~1;
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(0, 2)]
    [InlineData(2, 2)]
    [InlineData(100, 100)]
    public void EvenRounding_WithMinimumFloor(int input, int expected)
    {
        int result = input & ~1;
        result = Math.Max(2, result);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void OutputWidth_MinimumIs16_ClampedToEven()
    {
        // GifTrimmerWindow clamps output width: min 16, then even-round
        int val = 15;
        if (val < 16) val = 16;
        val = val & ~1;
        if (val < 2) val = 2;
        Assert.Equal(16, val);
    }

    // MARK: - Full Dimension Pipeline

    [Fact]
    public void DimensionPipeline_Realistic_ProducesEvenDimensions()
    {
        int outW = 641;
        outW = outW & ~1;
        if (outW < 2) outW = 2;

        int outH = GifTrimHelper.CalculateOutputHeight(1920, 1080, outW);

        Assert.Equal(640, outW);
        Assert.Equal(360, outH);
        Assert.Equal(0, outW % 2);
        Assert.Equal(0, outH % 2);
    }

    // MARK: - Frame Range Validation

    [Fact]
    public void StartFrame_Clamped_BelowEndFrame()
    {
        int endFrame = 50;
        int val = 60; // Trying to set start beyond end

        if (val >= endFrame) val = endFrame - 1;
        if (val < 1) val = 1;

        Assert.Equal(49, val);
    }

    [Fact]
    public void StartFrame_MinimumIsOne()
    {
        int endFrame = 2;
        int val = 0;

        if (val < 1) val = 1;
        if (val >= endFrame) val = endFrame - 1;

        Assert.Equal(1, val);
    }

    [Fact]
    public void EndFrame_Clamped_AboveStartFrame()
    {
        int startFrame = 20, frameCount = 100;
        int val = 15; // Trying to set end before start

        if (val <= startFrame) val = startFrame + 1;
        if (val > frameCount) val = frameCount;

        Assert.Equal(21, val);
    }

    [Fact]
    public void EndFrame_MaximumIsFrameCount()
    {
        int startFrame = 1, frameCount = 100;
        int val = 150;

        if (val > frameCount) val = frameCount;
        if (val <= startFrame) val = startFrame + 1;

        Assert.Equal(100, val);
    }

    // MARK: - Frame Index Conversion (1-based → 0-based)

    [Theory]
    [InlineData(1, 0)]    // First frame
    [InlineData(10, 9)]   // Middle frame
    [InlineData(100, 99)] // Last frame
    public void FrameIndex_OneBased_To_ZeroBased(int uiFrame, int expectedIdx)
    {
        int idx = uiFrame - 1;
        Assert.Equal(expectedIdx, idx);
    }

    // MARK: - Selected Frame Count

    [Theory]
    [InlineData(1, 100, 100)]   // Full range
    [InlineData(1, 1, 1)]       // Single frame
    [InlineData(10, 50, 41)]    // Trimmed range
    [InlineData(50, 100, 51)]   // Second half
    public void SelectedFrameCount_Inclusive(int start, int end, int expected)
    {
        int count = end - start + 1;
        Assert.Equal(expected, count);
    }

    // MARK: - HasChanges

    [Fact]
    public void HasChanges_NoModifications_ReturnsFalse()
    {
        Assert.False(GifTrimHelper.HasChanges(1, 100, 100, 640, 640));
    }

    [Fact]
    public void HasChanges_StartFrameChanged_ReturnsTrue()
    {
        Assert.True(GifTrimHelper.HasChanges(5, 100, 100, 640, 640));
    }

    [Fact]
    public void HasChanges_EndFrameChanged_ReturnsTrue()
    {
        Assert.True(GifTrimHelper.HasChanges(1, 80, 100, 640, 640));
    }

    [Fact]
    public void HasChanges_WidthChanged_ReturnsTrue()
    {
        Assert.True(GifTrimHelper.HasChanges(1, 100, 100, 320, 640));
    }

    // MARK: - File Size Estimation

    [Fact]
    public void FileSizeEstimate_FullRange_NoResize_EqualsOriginal()
    {
        long estimated = GifTrimHelper.EstimateFileSize(100, 100, 640, 480, 640, 480, 2_000_000);
        Assert.Equal(2_000_000, estimated);
    }

    [Fact]
    public void FileSizeEstimate_HalfFrames_RoughlyHalfSize()
    {
        long estimated = GifTrimHelper.EstimateFileSize(50, 100, 640, 480, 640, 480, 2_000_000);
        Assert.Equal(1_000_000, estimated);
    }

    [Fact]
    public void FileSizeEstimate_HalfWidth_SmallerThanLinear()
    {
        long estimated = GifTrimHelper.EstimateFileSize(100, 100, 320, 240, 640, 480, 2_000_000);
        // scaleRatio = 0.25, sqrt(0.25) = 0.5, so estimated = 1MB
        Assert.Equal(1_000_000, estimated);
        Assert.True(estimated < 2_000_000);
    }

    // MARK: - FormatFileSize

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1.0 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1048576, "1.0 MB")]
    [InlineData(2621440, "2.5 MB")]
    public void FormatFileSize_VariousSizes_FormatsCorrectly(long bytes, string expected)
    {
        Assert.Equal(expected, GifTrimHelper.FormatFileSize(bytes));
    }

    // MARK: - Range Bar Fractions

    [Theory]
    [InlineData(1, 100, 100, 0.0, 1.0)]     // Full range
    [InlineData(25, 75, 100, 0.24, 0.75)]    // Middle portion
    [InlineData(1, 50, 100, 0.0, 0.5)]       // First half
    [InlineData(50, 100, 100, 0.49, 1.0)]    // Second half
    public void RangeBar_CalculatesFractions(int start, int end, int total,
        double expectedStartFrac, double expectedEndFrac)
    {
        var (startFrac, endFrac) = GifTrimHelper.RangeBarFractions(start, end, total);

        Assert.Equal(expectedStartFrac, startFrac, 2);
        Assert.Equal(expectedEndFrac, endFrac, 2);
    }
}
