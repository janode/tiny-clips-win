using System.Globalization;

namespace TinyClips.Helpers;

/// <summary>
/// Pure calculation helpers extracted from GifTrimmerWindow.
/// All methods are static and UI-free for testability.
/// </summary>
internal static class GifTrimHelper
{
    /// <summary>
    /// Calculates the output height preserving aspect ratio, with even-rounding and a minimum of 2.
    /// </summary>
    public static int CalculateOutputHeight(int originalWidth, int originalHeight, int outputWidth)
    {
        int outH = originalWidth > 0
            ? (int)Math.Round((double)originalHeight / originalWidth * outputWidth)
            : originalHeight;
        outH = Math.Max(1, outH & ~1);
        if (outH < 2) outH = 2;
        return outH;
    }

    /// <summary>
    /// Estimates the GIF file size after trimming and resizing.
    /// Uses a sqrt scale ratio which models GIF compression better than linear.
    /// </summary>
    public static long EstimateFileSize(
        int selectedFrames, int frameCount,
        int outputWidth, int outputHeight,
        int originalWidth, int originalHeight,
        long originalFileSize)
    {
        double ratio = (double)selectedFrames / frameCount;
        double scaleRatio = originalWidth > 0
            ? (double)(outputWidth * outputHeight) / (originalWidth * originalHeight)
            : 1.0;
        return (long)(originalFileSize * ratio * Math.Sqrt(scaleRatio));
    }

    /// <summary>
    /// Formats a byte count as a human-readable file size string.
    /// </summary>
    public static string FormatFileSize(long bytes)
    {
        var ci = CultureInfo.InvariantCulture;
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return string.Format(ci, "{0:F1} KB", bytes / 1024.0);
        return string.Format(ci, "{0:F1} MB", bytes / (1024.0 * 1024.0));
    }

    /// <summary>
    /// Determines whether the GIF has been modified from its original state.
    /// </summary>
    public static bool HasChanges(int startFrame, int endFrame, int frameCount, int outputWidth, int originalWidth)
    {
        return startFrame > 1 || endFrame < frameCount || outputWidth != originalWidth;
    }

    /// <summary>
    /// Calculates the start and end fractions (0..1) for the frame range bar.
    /// Uses 1-based frame indices.
    /// </summary>
    public static (double StartFraction, double EndFraction) RangeBarFractions(
        int startFrame, int endFrame, int frameCount)
    {
        if (frameCount <= 0)
            return (0, 1);

        double startFrac = (startFrame - 1.0) / frameCount;
        double endFrac = (double)endFrame / frameCount;
        return (startFrac, endFrac);
    }
}
