using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace TinyClips.Tests;

/// <summary>
/// Tests for the GIF frame → byte[] → image pipeline.
/// Regression test for Bug #1: GIF trimmer showed black frames because the
/// MemoryStream was disposed before the UI thread read from it.
///
/// The fix converts the frame to a byte[] inside the Task.Run, so
/// the stream lifetime doesn't cross thread boundaries. These tests
/// verify that the byte[] conversion produces valid PNG data.
/// </summary>
public class GifFrameConversionTests
{
    [Fact]
    public void FrameToPngBytes_ProducesValidData()
    {
        // Create a simple 10x10 red image (simulates a GIF frame)
        using var image = new Image<Rgba32>(10, 10, new Rgba32(255, 0, 0, 255));

        // This is the same pattern used in the fixed DisplayFrameAsync
        byte[] pngBytes;
        using (var ms = new MemoryStream())
        {
            image.SaveAsPng(ms);
            pngBytes = ms.ToArray();
        }

        // Must produce non-empty valid PNG data
        Assert.NotEmpty(pngBytes);
        Assert.True(pngBytes.Length > 8, "PNG data too small to be valid");

        // Verify PNG signature (first 8 bytes)
        byte[] pngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        Assert.Equal(pngSignature, pngBytes[..8]);
    }

    [Fact]
    public void FrameToPngBytes_CanBeReloadedAsImage()
    {
        // Create a 20x15 image
        using var original = new Image<Rgba32>(20, 15, new Rgba32(0, 128, 255, 255));

        byte[] pngBytes;
        using (var ms = new MemoryStream())
        {
            original.SaveAsPng(ms);
            pngBytes = ms.ToArray();
        }

        // Reload from bytes — this is what the UI thread does (minus the WinUI BitmapImage part)
        using var reloaded = Image.Load<Rgba32>(pngBytes);
        Assert.Equal(20, reloaded.Width);
        Assert.Equal(15, reloaded.Height);
    }

    [Fact]
    public void ByteArraySurvivesStreamDisposal()
    {
        // This is the core of the bug: after `using var ms` exits, ms.ToArray() data must survive
        byte[] pngBytes;

        using (var image = new Image<Rgba32>(5, 5, new Rgba32(0, 255, 0, 255)))
        {
            using var ms = new MemoryStream();
            image.SaveAsPng(ms);
            pngBytes = ms.ToArray();
            // ms is about to be disposed here
        }

        // pngBytes must still be valid after the MemoryStream is disposed
        Assert.NotEmpty(pngBytes);
        using var reloaded = Image.Load<Rgba32>(pngBytes);
        Assert.Equal(5, reloaded.Width);
    }

    [Fact]
    public async Task ConcurrentFrameConversion_NoRaceCondition()
    {
        // Simulate the fixed pattern: multiple frames converted concurrently
        // Each Task.Run produces a byte[], no shared mutable state
        using var gif = new Image<Rgba32>(10, 10);
        // Add a second frame
        gif.Frames.AddFrame(new Image<Rgba32>(10, 10, new Rgba32(255, 0, 0, 255)).Frames[0]);

        var tasks = Enumerable.Range(0, gif.Frames.Count).Select(i => Task.Run(() =>
        {
            using var frame = gif.Frames.CloneFrame(i);
            using var ms = new MemoryStream();
            frame.SaveAsPng(ms);
            return ms.ToArray();
        })).ToArray();

        var results = await Task.WhenAll(tasks);

        foreach (var bytes in results)
        {
            Assert.NotEmpty(bytes);
            // Each result should independently produce valid PNG
            using var img = Image.Load<Rgba32>(bytes);
            Assert.Equal(10, img.Width);
        }
    }
}
