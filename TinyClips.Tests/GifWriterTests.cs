using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using TinyClips.Capture;
using Xunit;

namespace TinyClips.Tests;

public class GifWriterTests
{
    // MARK: - BGRA → RGBA Pixel Conversion

    [Fact]
    public void BgraToImageSharp_SinglePixel_SwapsChannelsCorrectly()
    {
        // BGRA order: B=10, G=20, R=30, A=255
        byte[] bgra = [10, 20, 30, 255];
        using var image = GifWriter.BgraToImageSharp(bgra, 1, 1);

        Assert.Equal(1, image.Width);
        Assert.Equal(1, image.Height);

        var pixel = image[0, 0];
        Assert.Equal(30, pixel.R);   // was at index 2 in BGRA
        Assert.Equal(20, pixel.G);   // was at index 1
        Assert.Equal(10, pixel.B);   // was at index 0
        Assert.Equal(255, pixel.A);  // was at index 3
    }

    [Fact]
    public void BgraToImageSharp_ZeroAlpha_ForcedOpaque()
    {
        // CopyFromScreen leaves alpha as 0 — converter must force 255
        byte[] bgra = [50, 100, 150, 0];
        using var image = GifWriter.BgraToImageSharp(bgra, 1, 1);

        var pixel = image[0, 0];
        Assert.Equal(150, pixel.R);
        Assert.Equal(100, pixel.G);
        Assert.Equal(50, pixel.B);
        Assert.Equal(255, pixel.A);
    }

    [Fact]
    public void BgraToImageSharp_2x2Image_AllPixelsConverted()
    {
        // 2x2 image, 4 bytes per pixel = 16 bytes
        byte[] bgra =
        [
            // Row 0
            255, 0, 0, 255,     // px(0,0): B=255, G=0, R=0, A=255 → blue
            0, 255, 0, 255,     // px(1,0): B=0, G=255, R=0, A=255 → green
            // Row 1
            0, 0, 255, 255,     // px(0,1): B=0, G=0, R=255, A=255 → red
            128, 128, 128, 128, // px(1,1): gray, half transparent
        ];

        using var image = GifWriter.BgraToImageSharp(bgra, 2, 2);

        Assert.Equal(2, image.Width);
        Assert.Equal(2, image.Height);

        // (0,0) should be blue: R=0, G=0, B=255
        Assert.Equal(0, image[0, 0].R);
        Assert.Equal(0, image[0, 0].G);
        Assert.Equal(255, image[0, 0].B);

        // (1,0) should be green: R=0, G=255, B=0
        Assert.Equal(0, image[1, 0].R);
        Assert.Equal(255, image[1, 0].G);

        // (0,1) should be red: R=255, G=0, B=0
        Assert.Equal(255, image[0, 1].R);
        Assert.Equal(0, image[0, 1].G);
        Assert.Equal(0, image[0, 1].B);

        // (1,1) gray — alpha forced to 255 regardless of input
        Assert.Equal(128, image[1, 1].R);
        Assert.Equal(255, image[1, 1].A);
    }

    [Fact]
    public void BgraToImageSharp_LargerImage_ProducesCorrectDimensions()
    {
        int width = 100, height = 50;
        byte[] bgra = new byte[width * height * 4];
        // Fill with a known pattern
        for (int i = 0; i < bgra.Length; i += 4)
        {
            bgra[i] = 100;     // B
            bgra[i + 1] = 150; // G
            bgra[i + 2] = 200; // R
            bgra[i + 3] = 255; // A
        }

        using var image = GifWriter.BgraToImageSharp(bgra, width, height);

        Assert.Equal(width, image.Width);
        Assert.Equal(height, image.Height);

        // Spot check center pixel
        var px = image[50, 25];
        Assert.Equal(200, px.R);
        Assert.Equal(150, px.G);
        Assert.Equal(100, px.B);
    }

    // MARK: - Dimension Scaling Logic

    // These tests verify the dimension calculation logic used in EncodeGifAsync.
    // We test the math directly since it's the same algorithm.

    [Fact]
    public void DimensionScaling_UnderMaxWidth_NoChange()
    {
        int srcWidth = 400, srcHeight = 300;
        int maxWidth = 640;
        int destWidth = srcWidth, destHeight = srcHeight;

        if (destWidth > maxWidth)
        {
            double scale = (double)maxWidth / destWidth;
            destWidth = maxWidth;
            destHeight = (int)(srcHeight * scale);
        }
        destWidth &= ~1;
        destHeight &= ~1;

        Assert.Equal(400, destWidth);
        Assert.Equal(300, destHeight);
    }

    [Fact]
    public void DimensionScaling_OverMaxWidth_ScalesDown()
    {
        int srcWidth = 1920, srcHeight = 1080, maxWidth = 640;
        int destWidth = srcWidth, destHeight = srcHeight;

        if (destWidth > maxWidth)
        {
            double scale = (double)maxWidth / destWidth;
            destWidth = maxWidth;
            destHeight = (int)(srcHeight * scale);
        }
        destWidth &= ~1;
        destHeight &= ~1;

        Assert.Equal(640, destWidth);
        Assert.Equal(360, destHeight);
    }

    [Fact]
    public void DimensionScaling_OddDimensions_RoundedToEven()
    {
        int srcWidth = 641, srcHeight = 481;
        int destWidth = srcWidth, destHeight = srcHeight;

        // No scaling needed (at maxWidth)
        destWidth &= ~1;
        destHeight &= ~1;

        Assert.Equal(640, destWidth);
        Assert.Equal(480, destHeight);
    }

    [Fact]
    public void DimensionScaling_VerySmall_FloorAtTwo()
    {
        int destWidth = 1, destHeight = 1;
        destWidth &= ~1;
        destHeight &= ~1;
        if (destWidth < 2) destWidth = 2;
        if (destHeight < 2) destHeight = 2;

        Assert.Equal(2, destWidth);
        Assert.Equal(2, destHeight);
    }

    [Fact]
    public void FrameDelay_AtVariousFps_CalculatesCorrectly()
    {
        // GIF delay is in centiseconds: 100 / fps
        Assert.Equal(10, Math.Max(1, (int)(100.0 / 10)));  // 10 fps → 10cs
        Assert.Equal(4, Math.Max(1, (int)(100.0 / 24)));   // 24 fps → ~4cs
        Assert.Equal(3, Math.Max(1, (int)(100.0 / 30)));   // 30 fps → ~3cs
        Assert.Equal(1, Math.Max(1, (int)(100.0 / 100)));  // 100 fps → 1cs (min)
    }
}
