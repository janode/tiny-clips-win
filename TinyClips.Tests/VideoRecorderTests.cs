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
}
