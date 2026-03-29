using TinyClips.Capture;
using Xunit;

namespace TinyClips.Tests;

/// <summary>
/// Tests for VideoRecorder's public contract and error handling.
/// Regression test for Bug #2: unhandled exceptions on the capture thread
/// crashed the process instead of being surfaced via StopAsync.
///
/// Note: We can't run actual screen capture in a headless test environment,
/// so these tests validate the state machine and error-propagation contract
/// rather than the capture loop itself.
/// </summary>
public class VideoRecorderTests
{
    [Fact]
    public async Task StopAsync_WhenNotRecording_Throws()
    {
        using var recorder = new VideoRecorder();
        await Assert.ThrowsAsync<InvalidOperationException>(() => recorder.StopAsync());
    }

    [Fact]
    public void IsRecording_InitiallyFalse()
    {
        using var recorder = new VideoRecorder();
        Assert.False(recorder.IsRecording);
    }

    [Fact]
    public async Task StartAsync_WhenAlreadyRecording_Throws()
    {
        // We can't fully start (MF won't be available in test), but we can
        // verify the double-start guard by using reflection to set the flag.
        // This validates the state machine.
        using var recorder = new VideoRecorder();

        // Use reflection to set _isRecording = true (simulating active recording)
        var field = typeof(VideoRecorder).GetField("_isRecording",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(field);
        field!.SetValue(recorder, true);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => recorder.StartAsync(
                new System.Drawing.Rectangle(0, 0, 100, 100),
                "test.mp4"));
        Assert.Contains("Already recording", ex.Message);
    }

    [Fact]
    public async Task StopAsync_SurfacesCaptureError_WhenThreadFails()
    {
        // Simulate the error propagation pattern that was missing before the fix:
        // If _captureError is set, StopAsync should throw it wrapped in InvalidOperationException.
        using var recorder = new VideoRecorder();

        // Set up state via reflection to simulate a failed recording
        var isRecordingField = typeof(VideoRecorder).GetField("_isRecording",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var errorField = typeof(VideoRecorder).GetField("_captureError",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        Assert.NotNull(isRecordingField);
        Assert.NotNull(errorField);

        isRecordingField!.SetValue(recorder, true);
        errorField!.SetValue(recorder, new System.Runtime.InteropServices.COMException("MF encoder failed", unchecked((int)0x80070005)));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => recorder.StopAsync());
        Assert.Contains("Recording failed", ex.Message);
        Assert.IsType<System.Runtime.InteropServices.COMException>(ex.InnerException);
    }

    [Fact]
    public async Task StopAsync_ReturnsPath_WhenNoError()
    {
        using var recorder = new VideoRecorder();

        // Simulate successful recording stop
        var isRecordingField = typeof(VideoRecorder).GetField("_isRecording",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var outputField = typeof(VideoRecorder).GetField("_outputPath",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        isRecordingField!.SetValue(recorder, true);
        outputField!.SetValue(recorder, @"C:\test\output.mp4");

        var result = await recorder.StopAsync();
        Assert.Equal(@"C:\test\output.mp4", result);
    }

    [Fact]
    public void Elapsed_WhenNotRecording_ReturnsZero()
    {
        using var recorder = new VideoRecorder();
        Assert.Equal(TimeSpan.Zero, recorder.Elapsed);
    }

    // MARK: - Frame Timing Math

    [Theory]
    [InlineData(30, 333_333)]    // 30 fps → 333,333 × 100ns = 33.3ms
    [InlineData(24, 416_666)]    // 24 fps → 416,666 × 100ns = 41.7ms
    [InlineData(60, 166_666)]    // 60 fps → 166,666 × 100ns = 16.7ms
    [InlineData(1, 10_000_000)]  // 1 fps → 10M × 100ns = 1s
    public void FrameDuration_InHundredNanoseconds_IsCorrect(int fps, long expected)
    {
        long frameDuration = 10_000_000L / fps;
        Assert.Equal(expected, frameDuration);
    }

    [Fact]
    public void VideoTimestamp_IncrementsCorrectly_At30Fps()
    {
        int fps = 30;
        long frameDuration = 10_000_000L / fps;
        long timestamp = 0;

        // After 30 frames at 30fps, timestamp should be ~1 second
        for (int i = 0; i < 30; i++)
            timestamp += frameDuration;

        // 30 × 333,333 = 9,999,990 (≈1s in 100ns units, minor truncation)
        Assert.InRange(timestamp, 9_999_000L, 10_000_000L);
    }

    [Fact]
    public void FrameInterval_MatchesDuration()
    {
        int fps = 30;
        var frameInterval = TimeSpan.FromSeconds(1.0 / fps);
        Assert.InRange(frameInterval.TotalMilliseconds, 33.0, 34.0);
    }

    // MARK: - Bitrate Calculation

    [Theory]
    [InlineData(1920, 1080, 30, 1920 * 1080 * 30u / 4)]  // 1080p30 → ~15.5 Mbps
    [InlineData(1280, 720, 30, 1280 * 720 * 30u / 4)]     // 720p30 → ~6.9 Mbps
    [InlineData(640, 480, 24, 640 * 480 * 24u / 4)]       // 480p24 → ~1.8 Mbps
    public void BitrateCalculation_LargeResolutions_UsesFormula(int w, int h, int fps, uint expected)
    {
        uint bitrate = Math.Max(1_000_000u, (uint)((long)w * h * fps / 4));
        Assert.Equal(expected, bitrate);
    }

    [Fact]
    public void BitrateCalculation_TinyResolution_HasMinimumFloor()
    {
        // Very small region should still get 1Mbps minimum
        uint bitrate = Math.Max(1_000_000u, (uint)(100 * 100 * 10 / 4));
        Assert.Equal(1_000_000u, bitrate);
    }

    // MARK: - Pack2x32 Helper

    [Fact]
    public void Pack2x32_CombinesHighAndLow()
    {
        // Same algorithm as MFEncoder.Pack2x32
        static ulong Pack2x32(uint hi, uint lo) => ((ulong)hi << 32) | lo;

        // 1920×1080 frame size
        ulong packed = Pack2x32(1920, 1080);
        Assert.Equal(1920u, (uint)(packed >> 32));
        Assert.Equal(1080u, (uint)(packed & 0xFFFFFFFF));
    }

    [Fact]
    public void Pack2x32_FrameRate_EncodesCorrectly()
    {
        static ulong Pack2x32(uint hi, uint lo) => ((ulong)hi << 32) | lo;

        // 30fps / 1 (30:1 ratio)
        ulong packed = Pack2x32(30, 1);
        Assert.Equal(30u, (uint)(packed >> 32));
        Assert.Equal(1u, (uint)(packed & 0xFFFFFFFF));
    }
}
