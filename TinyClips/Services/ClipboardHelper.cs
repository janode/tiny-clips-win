using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using TinyClips.Helpers;
using TinyClips.Models;

namespace TinyClips.Services;

/// <summary>
/// Clipboard operations via Win32 API — reliable in unpackaged WinUI 3 desktop apps.
/// Images use PNG + CF_DIB formats (compatible with Slack, Teams, Discord, etc.).
/// Videos use CF_HDROP (file reference) only.
/// </summary>
public static class ClipboardHelper
{
    private static readonly uint CF_PNG = NativeMethods.RegisterClipboardFormat("PNG");

    [StructLayout(LayoutKind.Sequential)]
    private struct DROPFILES
    {
        public int pFiles;
        public int X;
        public int Y;
        public int fNC;
        public int fWide;
    }

    public static void CopyToClipboard(string filePath, CaptureType type)
    {
        try
        {
            if (!TryOpenClipboard())
            {
                AppLog.Error("Clipboard: OpenClipboard failed after retries");
                return;
            }

            try
            {
                NativeMethods.EmptyClipboard();

                if (type == CaptureType.Screenshot || type == CaptureType.Gif)
                {
                    // Use PNG + DIB for image types — universally supported by modern apps
                    using var bmp = new Bitmap(filePath);
                    SetPngData(bmp);
                    SetDibData(bmp);
                }
                else
                {
                    // Video: file reference only
                    SetFileDropData(filePath);
                }
            }
            finally
            {
                NativeMethods.CloseClipboard();
            }

            AppLog.Info($"Clipboard: copied {type} — {filePath}");
        }
        catch (Exception ex)
        {
            AppLog.Error("Clipboard copy failed", ex);
        }
    }

    /// <summary>
    /// Copy an in-memory bitmap to the clipboard (PNG + DIB, no file reference).
    /// Used when the file doesn't exist on disk yet (e.g. screenshot editor path).
    /// </summary>
    public static void CopyBitmapToClipboard(Bitmap bitmap)
    {
        try
        {
            if (!TryOpenClipboard())
            {
                AppLog.Error("Clipboard: OpenClipboard failed (bitmap)");
                return;
            }

            try
            {
                NativeMethods.EmptyClipboard();
                SetPngData(bitmap);
                SetDibData(bitmap);
            }
            finally
            {
                NativeMethods.CloseClipboard();
            }

            AppLog.Info("Clipboard: copied screenshot bitmap");
        }
        catch (Exception ex)
        {
            AppLog.Error("Clipboard: CopyBitmapToClipboard failed", ex);
        }
    }

    // MARK: - PNG format (registered "PNG" clipboard format)

    private static void SetPngData(Bitmap bmp)
    {
        try
        {
            using var ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            var pngBytes = ms.ToArray();

            var hGlobal = NativeMethods.GlobalAlloc(NativeMethods.GHND, (nuint)pngBytes.Length);
            if (hGlobal == nint.Zero) { AppLog.Error("Clipboard: GlobalAlloc failed for PNG"); return; }

            var ptr = NativeMethods.GlobalLock(hGlobal);
            if (ptr == nint.Zero) { NativeMethods.GlobalFree(hGlobal); return; }

            try { Marshal.Copy(pngBytes, 0, ptr, pngBytes.Length); }
            finally { NativeMethods.GlobalUnlock(hGlobal); }

            if (NativeMethods.SetClipboardData(CF_PNG, hGlobal) == nint.Zero)
            {
                NativeMethods.GlobalFree(hGlobal);
                AppLog.Error("Clipboard: SetClipboardData(PNG) failed");
            }
        }
        catch (Exception ex) { AppLog.Error("Clipboard: SetPngData failed", ex); }
    }

    // MARK: - CF_DIB (device-independent bitmap, fallback for older apps)

    private static void SetDibData(Bitmap bmp)
    {
        try
        {
            // Lock the bitmap bits and build a DIB (BITMAPINFOHEADER + pixel data)
            var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            var bmpData = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

            try
            {
                int stride = bmpData.Stride;
                int imageSize = Math.Abs(stride) * bmp.Height;
                int headerSize = 40; // sizeof(BITMAPINFOHEADER)
                int totalSize = headerSize + imageSize;

                var hGlobal = NativeMethods.GlobalAlloc(NativeMethods.GHND, (nuint)totalSize);
                if (hGlobal == nint.Zero) { AppLog.Error("Clipboard: GlobalAlloc failed for DIB"); return; }

                var ptr = NativeMethods.GlobalLock(hGlobal);
                if (ptr == nint.Zero) { NativeMethods.GlobalFree(hGlobal); return; }

                try
                {
                    // BITMAPINFOHEADER — bottom-up DIB
                    Marshal.WriteInt32(ptr, 0, headerSize);       // biSize
                    Marshal.WriteInt32(ptr, 4, bmp.Width);        // biWidth
                    Marshal.WriteInt32(ptr, 8, bmp.Height);       // biHeight (positive = bottom-up)
                    Marshal.WriteInt16(ptr, 12, 1);               // biPlanes
                    Marshal.WriteInt16(ptr, 14, 32);              // biBitCount
                    Marshal.WriteInt32(ptr, 16, 0);               // biCompression = BI_RGB
                    Marshal.WriteInt32(ptr, 20, imageSize);       // biSizeImage
                    // Rest of header is zero (GHND zeroes memory)

                    // Copy pixels row by row, flipping vertically (GDI+ is top-down, DIB is bottom-up)
                    int absStride = Math.Abs(stride);
                    var rowBuffer = new byte[absStride];
                    for (int y = 0; y < bmp.Height; y++)
                    {
                        var srcRow = bmpData.Scan0 + (y * stride);
                        var dstRow = ptr + headerSize + ((bmp.Height - 1 - y) * absStride);
                        Marshal.Copy(srcRow, rowBuffer, 0, absStride);
                        Marshal.Copy(rowBuffer, 0, dstRow, absStride);
                    }
                }
                finally { NativeMethods.GlobalUnlock(hGlobal); }

                if (NativeMethods.SetClipboardData(NativeMethods.CF_DIB, hGlobal) == nint.Zero)
                {
                    NativeMethods.GlobalFree(hGlobal);
                    AppLog.Error("Clipboard: SetClipboardData(CF_DIB) failed");
                }
            }
            finally { bmp.UnlockBits(bmpData); }
        }
        catch (Exception ex) { AppLog.Error("Clipboard: SetDibData failed", ex); }
    }

    // MARK: - File Drop (CF_HDROP) — used for video only

    private static void SetFileDropData(string filePath)
    {
        try
        {
            var dropFiles = new DROPFILES
            {
                pFiles = Marshal.SizeOf<DROPFILES>(),
                fWide = 1
            };

            var filePathBytes = Encoding.Unicode.GetBytes(filePath + "\0\0");
            var totalSize = dropFiles.pFiles + filePathBytes.Length;

            var hGlobal = NativeMethods.GlobalAlloc(NativeMethods.GHND, (nuint)totalSize);
            if (hGlobal == nint.Zero) { AppLog.Error("Clipboard: GlobalAlloc failed for DROPFILES"); return; }

            var ptr = NativeMethods.GlobalLock(hGlobal);
            if (ptr == nint.Zero) { NativeMethods.GlobalFree(hGlobal); return; }

            try
            {
                Marshal.StructureToPtr(dropFiles, ptr, false);
                Marshal.Copy(filePathBytes, 0, ptr + dropFiles.pFiles, filePathBytes.Length);
            }
            finally { NativeMethods.GlobalUnlock(hGlobal); }

            if (NativeMethods.SetClipboardData(NativeMethods.CF_HDROP, hGlobal) == nint.Zero)
            {
                NativeMethods.GlobalFree(hGlobal);
                AppLog.Error("Clipboard: SetClipboardData(CF_HDROP) failed");
            }
        }
        catch (Exception ex) { AppLog.Error("Clipboard: SetFileDropData failed", ex); }
    }

    // MARK: - Helpers

    private static bool TryOpenClipboard()
    {
        for (int i = 0; i < 10; i++)
        {
            if (NativeMethods.OpenClipboard(nint.Zero))
                return true;
            Thread.Yield();
        }
        return false;
    }
}
