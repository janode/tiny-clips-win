namespace TinyClips.Helpers;

/// <summary>
/// Pure calculation helpers for DPI-aware coordinate transforms.
/// All methods are pure functions — no Win32 or UI dependencies.
/// </summary>
public static class DpiHelper
{
    /// <summary>
    /// Convert physical pixel dimension to DIP (device-independent pixel) dimension.
    /// </summary>
    public static double PhysicalToDip(double physicalPixels, double dpiScale) =>
        dpiScale > 0 ? physicalPixels / dpiScale : physicalPixels;

    /// <summary>
    /// Convert DIP coordinate to physical pixel coordinate for bitmap compositing.
    /// </summary>
    public static float DipToPhysical(double dip, double dpiScale) =>
        (float)(dip * dpiScale);

    /// <summary>
    /// Calculate desired window size in physical pixels for an image editor,
    /// given the image dimensions (in physical pixels), toolbar padding (in DIPs),
    /// DPI scale, and maximum screen bounds.
    /// </summary>
    public static (int Width, int Height) CalculateEditorWindowSize(
        int imageWidthPx, int imageHeightPx,
        double dpiScale,
        int maxScreenW, int maxScreenH,
        double toolbarWidthDip = 40, double toolbarHeightDip = 140,
        double minWidthDip = 600, double minHeightDip = 400)
    {
        // Image pixels map 1:1 to physical window pixels (since image at screen-size
        // occupies _imageWidth physical pixels on screen). Add toolbar padding in physical pixels.
        int desiredW = (int)Math.Min(imageWidthPx + toolbarWidthDip * dpiScale, maxScreenW);
        int desiredH = (int)Math.Min(imageHeightPx + toolbarHeightDip * dpiScale, maxScreenH);
        int w = Math.Max(desiredW, (int)(minWidthDip * dpiScale));
        int h = Math.Max(desiredH, (int)(minHeightDip * dpiScale));
        return (w, h);
    }
}
