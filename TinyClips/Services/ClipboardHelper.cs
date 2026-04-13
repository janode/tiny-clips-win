using TinyClips.Models;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace TinyClips.Services;

/// <summary>
/// Clipboard operations: copy screenshots as bitmap, video/GIF as file references.
/// </summary>
public static class ClipboardHelper
{
    public static void CopyToClipboard(string filePath, CaptureType type)
    {
        try
        {
            var dataPackage = new DataPackage();

            if (type == CaptureType.Screenshot || type == CaptureType.Gif)
            {
                // Copy image as a bitmap via file reference — works in most apps
                var file = StorageFile.GetFileFromPathAsync(filePath).AsTask().GetAwaiter().GetResult();
                var stream = Windows.Storage.Streams.RandomAccessStreamReference.CreateFromFile(file);
                dataPackage.SetBitmap(stream);
            }

            // Always include the file reference
            var storageFile = StorageFile.GetFileFromPathAsync(filePath).AsTask().GetAwaiter().GetResult();
            dataPackage.SetStorageItems(new[] { storageFile });

            Clipboard.SetContent(dataPackage);
            Clipboard.Flush();
        }
        catch (Exception ex)
        {
            AppLog.Error("Clipboard copy failed", ex);
        }
    }
}
