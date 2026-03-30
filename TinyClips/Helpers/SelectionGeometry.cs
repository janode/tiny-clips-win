using System.Drawing;

namespace TinyClips.Helpers;

/// <summary>
/// Pure geometry helpers extracted from RegionSelectorWindow.
/// Normalizes selection rectangles and enforces minimum sizes.
/// </summary>
internal static class SelectionGeometry
{
    /// <summary>
    /// Normalizes two drag points into a rectangle with positive width/height,
    /// regardless of drag direction.
    /// </summary>
    public static Rectangle NormalizeRect(Point start, Point end)
    {
        int left = Math.Min(start.X, end.X);
        int top = Math.Min(start.Y, end.Y);
        int right = Math.Max(start.X, end.X);
        int bottom = Math.Max(start.Y, end.Y);
        return new Rectangle(left, top, right - left, bottom - top);
    }

    /// <summary>
    /// Returns true if the selection meets the minimum dimension requirement.
    /// </summary>
    public static bool MeetsMinimumSize(int width, int height, int minimumPixels = 10)
    {
        return width >= minimumPixels && height >= minimumPixels;
    }
}
