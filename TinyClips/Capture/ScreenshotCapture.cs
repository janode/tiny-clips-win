using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using TinyClips.Models;

namespace TinyClips.Capture;

/// <summary>
/// Captures screenshots using GDI+ BitBlt — simple, proven, zero extra dependencies.
/// Supports full screen, region crop, and window capture.
/// </summary>
public static class ScreenshotCapture
{
    /// <summary>
    /// Capture a specific region of a monitor.
    /// </summary>
    public static Task<string> CaptureRegionAsync(CaptureRegion region, string outputPath)
    {
        return Task.Run(() =>
        {
            var rect = region.ScreenRect;
            using var bitmap = CaptureScreenRect(rect);
            SaveBitmap(bitmap, outputPath);
            return outputPath;
        });
    }

    /// <summary>
    /// Capture an entire monitor identified by its bounds.
    /// </summary>
    public static Task<string> CaptureScreenAsync(Rectangle screenBounds, string outputPath)
    {
        return Task.Run(() =>
        {
            using var bitmap = CaptureScreenRect(screenBounds);
            SaveBitmap(bitmap, outputPath);
            return outputPath;
        });
    }

    /// <summary>
    /// Capture a specific window by handle.
    /// </summary>
    public static Task<string> CaptureWindowAsync(nint hwnd, string outputPath)
    {
        return Task.Run(() =>
        {
            GetWindowRect(hwnd, out var rect);
            var bounds = new Rectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
            using var bitmap = CaptureScreenRect(bounds);
            SaveBitmap(bitmap, outputPath);
            return outputPath;
        });
    }

    private static Bitmap CaptureScreenRect(Rectangle rect)
    {
        var bitmap = new Bitmap(rect.Width, rect.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, rect.Size, CopyPixelOperation.SourceCopy);
        return bitmap;
    }

    private static void SaveBitmap(Bitmap bitmap, string outputPath)
    {
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var settings = CaptureSettings.Instance;

        if (settings.ScreenshotFormat == Models.ImageFormat.Jpeg)
        {
            var encoder = ImageCodecInfo.GetImageEncoders()
                .FirstOrDefault(c => c.FormatID == System.Drawing.Imaging.ImageFormat.Jpeg.Guid);

            if (encoder != null)
            {
                var encoderParams = new EncoderParameters(1);
                encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, (long)settings.JpegQuality);
                bitmap.Save(outputPath, encoder, encoderParams);
            }
            else
            {
                bitmap.Save(outputPath, System.Drawing.Imaging.ImageFormat.Jpeg);
            }
        }
        else
        {
            bitmap.Save(outputPath, System.Drawing.Imaging.ImageFormat.Png);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }
}
