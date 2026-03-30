namespace TinyClips.Helpers;

/// <summary>
/// Pure vector-math helpers for arrowhead geometry, extracted from ScreenshotEditorWindow.
/// Calculates arrowhead triangle vertices given a line's start/end points.
/// </summary>
internal static class ArrowGeometry
{
    /// <summary>
    /// Represents a 2D point with double precision.
    /// </summary>
    public readonly record struct PointD(double X, double Y);

    /// <summary>
    /// Calculates the three vertices of an arrowhead triangle at the tip of a line.
    /// Returns (tip, left, right) where tip is the arrow point and left/right are the base corners.
    /// Returns null if the line is too short (length &lt; 1).
    /// </summary>
    public static (PointD Tip, PointD Left, PointD Right)? CalculateArrowhead(
        double startX, double startY,
        double endX, double endY,
        double strokeThickness)
    {
        double dx = endX - startX;
        double dy = endY - startY;
        double length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 1) return null;

        double ux = dx / length;
        double uy = dy / length;

        double headLength = Math.Max(strokeThickness * 4, 12);
        double headWidth = Math.Max(strokeThickness * 2.5, 8);

        double baseX = endX - ux * headLength;
        double baseY = endY - uy * headLength;

        var tip = new PointD(endX, endY);
        var left = new PointD(baseX + uy * headWidth, baseY - ux * headWidth);
        var right = new PointD(baseX - uy * headWidth, baseY + ux * headWidth);

        return (tip, left, right);
    }

    /// <summary>
    /// Parses a 6-character hex color string (with optional # prefix) to ARGB components.
    /// </summary>
    public static (byte R, byte G, byte B) ParseHexColor(string hex)
    {
        hex = hex.TrimStart('#');
        byte r = Convert.ToByte(hex[0..2], 16);
        byte g = Convert.ToByte(hex[2..4], 16);
        byte b = Convert.ToByte(hex[4..6], 16);
        return (r, g, b);
    }
}
