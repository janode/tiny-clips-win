using TinyClips.Helpers;
using Xunit;

namespace TinyClips.Tests;

/// <summary>
/// Tests for DPI-aware coordinate transforms.
/// Regression tests for Bug #3: screenshot editor rendered at wrong resolution
/// because raw pixel dimensions were treated as DIPs.
/// </summary>
public class DpiScalingTests
{
    // MARK: - PhysicalToDip

    [Theory]
    [InlineData(1920, 1.0, 1920)]     // 100% scaling — no change
    [InlineData(1920, 1.5, 1280)]     // 150% scaling — 1920/1.5 = 1280 DIPs
    [InlineData(1920, 2.0, 960)]      // 200% scaling — 1920/2.0 = 960 DIPs
    [InlineData(2560, 1.25, 2048)]    // 125% scaling
    public void PhysicalToDip_ScalesCorrectly(double physicalPx, double dpiScale, double expectedDip)
    {
        double dip = DpiHelper.PhysicalToDip(physicalPx, dpiScale);
        Assert.Equal(expectedDip, dip, precision: 1);
    }

    [Fact]
    public void PhysicalToDip_ZeroScale_ReturnsOriginal()
    {
        // Guard against divide-by-zero — should return original value
        Assert.Equal(1920, DpiHelper.PhysicalToDip(1920, 0));
    }

    // MARK: - DipToPhysical

    [Theory]
    [InlineData(100, 1.0, 100f)]     // 100% — no change
    [InlineData(100, 1.5, 150f)]     // 150% — DIP 100 → 150 physical px
    [InlineData(100, 2.0, 200f)]     // 200%
    [InlineData(50, 1.25, 62.5f)]    // 125%
    public void DipToPhysical_ScalesCorrectly(double dip, double dpiScale, float expectedPx)
    {
        float px = DpiHelper.DipToPhysical(dip, dpiScale);
        Assert.Equal(expectedPx, px, precision: 1);
    }

    [Fact]
    public void RoundTrip_PhysicalToDipAndBack_PreservesValue()
    {
        // If we convert physical → DIP → physical, we should get the original
        double original = 2560;
        double dpiScale = 1.5;

        double dip = DpiHelper.PhysicalToDip(original, dpiScale);
        float backToPhysical = DpiHelper.DipToPhysical(dip, dpiScale);

        Assert.Equal((float)original, backToPhysical, precision: 0);
    }

    // MARK: - CalculateEditorWindowSize

    [Fact]
    public void EditorWindowSize_At100Percent_MatchesImagePlusPadding()
    {
        // At 100% DPI, window should be imageWidth + toolbar padding
        var (w, h) = DpiHelper.CalculateEditorWindowSize(
            imageWidthPx: 1920, imageHeightPx: 1080,
            dpiScale: 1.0,
            maxScreenW: 2560, maxScreenH: 1440);

        Assert.Equal(1960, w);  // 1920 + 40
        Assert.Equal(1220, h);  // 1080 + 140
    }

    [Fact]
    public void EditorWindowSize_At150Percent_DoesNotDoubleScale()
    {
        // This was the original bug: the code did _imageWidth * dpiScale
        // which double-scaled because _imageWidth is already in physical pixels
        var (w, h) = DpiHelper.CalculateEditorWindowSize(
            imageWidthPx: 1920, imageHeightPx: 1080,
            dpiScale: 1.5,
            maxScreenW: 3840, maxScreenH: 2160);

        // 1920 + 40*1.5 = 1920 + 60 = 1980 (NOT 1920*1.5 + 60 = 2940)
        Assert.Equal(1980, w);
        Assert.Equal(1290, h); // 1080 + 140*1.5 = 1080 + 210
    }

    [Fact]
    public void EditorWindowSize_SmallImage_EnforcesMinimumSize()
    {
        var (w, h) = DpiHelper.CalculateEditorWindowSize(
            imageWidthPx: 100, imageHeightPx: 100,
            dpiScale: 1.0,
            maxScreenW: 2560, maxScreenH: 1440);

        Assert.Equal(600, w);  // min 600 DIPs * 1.0
        Assert.Equal(400, h);  // min 400 DIPs * 1.0
    }

    [Fact]
    public void EditorWindowSize_SmallImageAt200Percent_ScalesMinimum()
    {
        var (w, h) = DpiHelper.CalculateEditorWindowSize(
            imageWidthPx: 100, imageHeightPx: 100,
            dpiScale: 2.0,
            maxScreenW: 5120, maxScreenH: 2880);

        Assert.Equal(1200, w); // 600 * 2.0
        Assert.Equal(800, h);  // 400 * 2.0
    }

    [Fact]
    public void EditorWindowSize_LargeImage_CapsToScreenBounds()
    {
        var (w, h) = DpiHelper.CalculateEditorWindowSize(
            imageWidthPx: 5000, imageHeightPx: 3000,
            dpiScale: 1.0,
            maxScreenW: 2560, maxScreenH: 1440);

        // Should be capped to 85% of screen (already applied as maxScreenW/H)
        Assert.Equal(2560, w);
        Assert.Equal(1440, h);
    }

    // MARK: - Compositing coordinate regression

    [Fact]
    public void AnnotationCoordinates_MustScale_ForCompositing()
    {
        // A user draws at canvas position (100, 50) in DIPs.
        // On a 150% display, the bitmap pixel coordinate should be (150, 75).
        double canvasX = 100;
        double canvasY = 50;
        double dpiScale = 1.5;

        float bitmapX = DpiHelper.DipToPhysical(canvasX, dpiScale);
        float bitmapY = DpiHelper.DipToPhysical(canvasY, dpiScale);

        Assert.Equal(150f, bitmapX);
        Assert.Equal(75f, bitmapY);
    }

    [Fact]
    public void AnnotationCoordinates_At100Percent_NoChange()
    {
        // At 100% DPI, canvas coordinates == bitmap coordinates
        float bitmapX = DpiHelper.DipToPhysical(200, 1.0);
        Assert.Equal(200f, bitmapX);
    }
}
