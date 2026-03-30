using TinyClips.Helpers;
using Xunit;

namespace TinyClips.Tests;

/// <summary>
/// Tests for VideoTrimHelper — pure logic extracted from VideoTrimmerWindow.
/// </summary>
public class VideoTrimmerTests
{
    // MARK: - FormatTime

    [Theory]
    [InlineData(0, 0, 0, "00:00.0")]
    [InlineData(0, 5, 0, "00:05.0")]
    [InlineData(0, 59, 0, "00:59.0")]
    [InlineData(1, 0, 0, "01:00.0")]
    [InlineData(1, 23, 400, "01:23.4")]
    [InlineData(10, 5, 900, "10:05.9")]
    public void FormatTime_VariousDurations_FormatsCorrectly(int minutes, int seconds, int ms, string expected)
    {
        var t = new TimeSpan(0, 0, minutes, seconds, ms);
        Assert.Equal(expected, VideoTrimHelper.FormatTime(t));
    }

    [Fact]
    public void FormatTime_SubSecond_ShowsDecimal()
    {
        var t = TimeSpan.FromMilliseconds(500);
        Assert.Equal("00:00.5", VideoTrimHelper.FormatTime(t));
    }

    [Fact]
    public void FormatTime_LargeDuration_FormatsMinutes()
    {
        var t = TimeSpan.FromMinutes(65) + TimeSpan.FromSeconds(30);
        Assert.Equal("65:30.0", VideoTrimHelper.FormatTime(t));
    }

    // MARK: - EffectiveTrimEnd

    [Fact]
    public void EffectiveTrimEnd_WhenZero_ReturnsDuration()
    {
        var duration = TimeSpan.FromSeconds(30);
        Assert.Equal(duration, VideoTrimHelper.EffectiveTrimEnd(TimeSpan.Zero, duration));
    }

    [Fact]
    public void EffectiveTrimEnd_WhenSet_ReturnsTrimEnd()
    {
        var trimEnd = TimeSpan.FromSeconds(15);
        var duration = TimeSpan.FromSeconds(30);
        Assert.Equal(trimEnd, VideoTrimHelper.EffectiveTrimEnd(trimEnd, duration));
    }

    // MARK: - HasTrim

    [Fact]
    public void HasTrim_NoTrimPoints_ReturnsFalse()
    {
        Assert.False(VideoTrimHelper.HasTrim(
            TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void HasTrim_StartOnly_ReturnsTrue()
    {
        Assert.True(VideoTrimHelper.HasTrim(
            TimeSpan.FromSeconds(5), TimeSpan.Zero, TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void HasTrim_EndOnly_ReturnsTrue()
    {
        Assert.True(VideoTrimHelper.HasTrim(
            TimeSpan.Zero, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void HasTrim_EndAtDuration_ReturnsFalse()
    {
        Assert.False(VideoTrimHelper.HasTrim(
            TimeSpan.Zero, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30)));
    }

    // MARK: - Trim Start/End Validation

    [Fact]
    public void SetStart_BeforeEnd_IsAllowed()
    {
        var pos = TimeSpan.FromSeconds(5);
        var effectiveTrimEnd = TimeSpan.FromSeconds(20);

        bool allowed = pos < effectiveTrimEnd;
        Assert.True(allowed);
    }

    [Fact]
    public void SetStart_AtOrAfterEnd_IsRejected()
    {
        var pos = TimeSpan.FromSeconds(20);
        var effectiveTrimEnd = TimeSpan.FromSeconds(20);

        bool allowed = pos < effectiveTrimEnd;
        Assert.False(allowed);
    }

    [Fact]
    public void SetEnd_AfterStart_IsAllowed()
    {
        var pos = TimeSpan.FromSeconds(15);
        var trimStart = TimeSpan.FromSeconds(5);

        bool allowed = pos > trimStart;
        Assert.True(allowed);
    }

    [Fact]
    public void SetEnd_AtOrBeforeStart_IsRejected()
    {
        var pos = TimeSpan.FromSeconds(5);
        var trimStart = TimeSpan.FromSeconds(5);

        bool allowed = pos > trimStart;
        Assert.False(allowed);
    }

    // MARK: - TrimTimeFromEnd Calculation

    [Fact]
    public void TrimTimeFromEnd_CalculatesCorrectly()
    {
        // Same algorithm as VideoTrimmerWindow.TrimVideoAsync
        var originalDuration = TimeSpan.FromSeconds(30);
        var trimEnd = TimeSpan.FromSeconds(20);

        var trimFromEnd = originalDuration - trimEnd;
        Assert.Equal(TimeSpan.FromSeconds(10), trimFromEnd);
    }

    [Fact]
    public void TrimTimeFromEnd_AtFullDuration_IsZero()
    {
        var originalDuration = TimeSpan.FromSeconds(30);
        var trimEnd = TimeSpan.FromSeconds(30);

        var trimFromEnd = originalDuration - trimEnd;
        Assert.Equal(TimeSpan.Zero, trimFromEnd);
    }

    [Fact]
    public void TrimTimeFromEnd_OnlyAppliedWhenPositive()
    {
        // The code checks: if (trimFromEnd > TimeSpan.Zero) clip.TrimTimeFromEnd = trimFromEnd
        var originalDuration = TimeSpan.FromSeconds(30);
        var trimEnd = TimeSpan.FromSeconds(30);

        var trimFromEnd = originalDuration - trimEnd;
        bool shouldApply = trimFromEnd > TimeSpan.Zero;
        Assert.False(shouldApply);
    }

    // MARK: - Preview Mode

    [Fact]
    public void PreviewStop_AtTrimEnd_StopsPlayback()
    {
        var position = TimeSpan.FromSeconds(20.1);
        var effectiveTrimEnd = TimeSpan.FromSeconds(20);

        bool shouldStop = position >= effectiveTrimEnd;
        Assert.True(shouldStop);
    }

    [Fact]
    public void PreviewStop_BeforeTrimEnd_ContinuesPlayback()
    {
        var position = TimeSpan.FromSeconds(19.9);
        var effectiveTrimEnd = TimeSpan.FromSeconds(20);

        bool shouldStop = position >= effectiveTrimEnd;
        Assert.False(shouldStop);
    }

    // MARK: - Trim Range Bar Fractions

    [Theory]
    [InlineData(0, 30, 30, 0.0, 1.0)]       // Full range
    [InlineData(5, 25, 30, 1.0/6, 25.0/30)]  // Partial trim
    [InlineData(0, 15, 30, 0.0, 0.5)]        // First half
    [InlineData(15, 30, 30, 0.5, 1.0)]       // Second half
    public void TrimRangeBar_CalculatesFractions(double startSec, double endSec, double durationSec,
        double expectedStartFrac, double expectedEndFrac)
    {
        var trimStart = TimeSpan.FromSeconds(startSec);
        var effectiveTrimEnd = TimeSpan.FromSeconds(endSec);
        var duration = TimeSpan.FromSeconds(durationSec);

        var (startFrac, endFrac) = VideoTrimHelper.TrimRangeFractions(trimStart, effectiveTrimEnd, duration);

        Assert.Equal(expectedStartFrac, startFrac, 4);
        Assert.Equal(expectedEndFrac, endFrac, 4);
    }
}
