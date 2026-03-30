using System.Diagnostics;
using TinyClips.Helpers;
using TinyClips.Models;

namespace TinyClips.Services;

/// <summary>
/// Generates output file paths, handles file naming with template tokens,
/// and post-save actions (clipboard, explorer, notification).
/// </summary>
public sealed class SaveService : ISaveService
{
    public static SaveService Instance { get; } = new();

    private SaveService() { }

    /// <summary>
    /// Generate the full output URL for a capture.
    /// </summary>
    public string GeneratePath(CaptureType type)
    {
        return GeneratePath(type, type.FileExtension());
    }

    public string GeneratePath(CaptureType type, string fileExtension)
    {
        var settings = CaptureSettings.Instance;
        var directory = settings.SaveDirectory;

        Directory.CreateDirectory(directory);

        var filename = GenerateFileName(type, fileExtension);
        return GetUniquePath(directory, filename);
    }

    public string GenerateFileName(CaptureType type, string fileExtension, DateTime? date = null)
    {
        var settings = CaptureSettings.Instance;
        var now = date ?? DateTime.Now;
        var rawTemplate = settings.FileNameTemplate.Trim();
        var template = string.IsNullOrEmpty(rawTemplate) ? "TinyClips {date} at {time}" : rawTemplate;

        var stem = template
            .Replace("{app}", "TinyClips")
            .Replace("{type}", type.Label())
            .Replace("{date}", now.ToString("yyyy-MM-dd"))
            .Replace("{time}", now.ToString("HH.mm.ss"))
            .Replace("{datetime}", now.ToString("yyyy-MM-dd_HH.mm.ss"));

        stem = SanitizeFilenameStem(stem, now);

        var cleanExt = fileExtension.Trim('.', ' ').ToLowerInvariant();
        return string.IsNullOrEmpty(cleanExt) ? stem : $"{stem}.{cleanExt}";
    }

    public string NamingPreview(CaptureType type = CaptureType.Screenshot)
    {
        return GenerateFileName(type, type.FileExtension());
    }

    /// <summary>
    /// Handle all post-save actions: clipboard copy, show in Explorer, notification.
    /// </summary>
    public void HandleSavedFile(string filePath, CaptureType type)
    {
        var settings = CaptureSettings.Instance;

        if (settings.ShouldCopyToClipboard(type))
        {
            ClipboardHelper.CopyToClipboard(filePath, type);
        }

        if (settings.ShowInExplorer)
        {
            NativeMethods.ShowInExplorer(filePath);
        }

        if (settings.ShowSaveNotifications)
        {
            NotificationService.Instance.ShowSaveNotification(filePath, type);
        }

        if (settings.OpenAfterCapture)
        {
            Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
        }
    }

    public void ShowError(string message)
    {
        // Show a simple content dialog on the UI thread
        // For now, use a notification as a lightweight alternative
        NotificationService.Instance.ShowErrorNotification(message);
    }

    private static string SanitizeFilenameStem(string stem, DateTime fallbackDate)
    {
        var invalidChars = new[] { '/', '\\', ':', '?', '*', '"', '<', '>', '|' };
        var cleaned = stem;
        foreach (var c in invalidChars)
        {
            cleaned = cleaned.Replace(c, '-');
        }

        // Collapse multiple spaces
        while (cleaned.Contains("  "))
            cleaned = cleaned.Replace("  ", " ");

        cleaned = cleaned.Trim(' ', '.', '\n', '\t');

        if (string.IsNullOrEmpty(cleaned))
            cleaned = $"TinyClips {fallbackDate:yyyy-MM-dd_HH.mm.ss}";

        return cleaned;
    }

    private static string GetUniquePath(string directory, string filename)
    {
        var initialPath = Path.Combine(directory, filename);
        if (!File.Exists(initialPath))
            return initialPath;

        var ext = Path.GetExtension(filename);
        var stem = Path.GetFileNameWithoutExtension(filename);
        var suffix = 2;

        while (true)
        {
            var candidateName = $"{stem} {suffix}{ext}";
            var candidatePath = Path.Combine(directory, candidateName);
            if (!File.Exists(candidatePath))
                return candidatePath;
            suffix++;
        }
    }
}
