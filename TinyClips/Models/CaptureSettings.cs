using System.Text.Json;

namespace TinyClips.Models;

/// <summary>
/// Central settings store — mirrors the macOS CaptureSettings singleton.
/// Persisted as JSON in the app's local data folder.
/// </summary>
public sealed class CaptureSettings
{
    private static readonly Lazy<CaptureSettings> _instance = new(() =>
    {
        var settings = Load();
        return settings;
    });

    public static CaptureSettings Instance => _instance.Value;

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TinyClips", "settings.json");

    // MARK: - General

    public string SaveDirectory { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
    public string FileNameTemplate { get; set; } = "TinyClips {date} at {time}";
    public bool CopyScreenshotToClipboard { get; set; } = true;
    public bool CopyVideoToClipboard { get; set; } = false;
    public bool CopyGifToClipboard { get; set; } = false;
    public bool ShowInExplorer { get; set; } = false;
    public bool ShowSaveNotifications { get; set; } = true;
    public bool OpenAfterCapture { get; set; } = true;
    public bool LaunchAtStartup { get; set; } = false;
    public bool AlwaysCaptureMainDisplay { get; set; } = false;

    // MARK: - Screenshot

    public ImageFormat ScreenshotFormat { get; set; } = ImageFormat.Png;
    public int ScreenshotScale { get; set; } = 100;
    public int JpegQuality { get; set; } = 85;
    public bool ScreenshotCountdownEnabled { get; set; } = false;
    public int ScreenshotCountdownDuration { get; set; } = 3;

    // MARK: - Video

    public int VideoFrameRate { get; set; } = 30;
    public bool RecordSystemAudio { get; set; } = false;
    public bool ShowRegionIndicator { get; set; } = true;
    public bool VideoCountdownEnabled { get; set; } = true;
    public int VideoCountdownDuration { get; set; } = 3;

    // MARK: - GIF

    public double GifFrameRate { get; set; } = 10;
    public int GifMaxWidth { get; set; } = 640;
    public bool GifCountdownEnabled { get; set; } = true;
    public int GifCountdownDuration { get; set; } = 3;

    // MARK: - Shortcuts (Win32 virtual key codes + modifier flags)
    // Defaults: Ctrl+Alt+Shift+5/6/7

    public int ScreenshotHotKeyVk { get; set; } = 0x35; // VK_5
    public int ScreenshotHotKeyMod { get; set; } = 0x0007; // MOD_CONTROL | MOD_ALT | MOD_SHIFT
    public int VideoHotKeyVk { get; set; } = 0x36; // VK_6
    public int VideoHotKeyMod { get; set; } = 0x0007;
    public int GifHotKeyVk { get; set; } = 0x37; // VK_7
    public int GifHotKeyMod { get; set; } = 0x0007;

    // MARK: - Panel Positions (null = use default centering)

    public int? PickerPositionX { get; set; }
    public int? PickerPositionY { get; set; }
    public int? StartPanelPositionX { get; set; }
    public int? StartPanelPositionY { get; set; }
    public int? StopPanelPositionX { get; set; }
    public int? StopPanelPositionY { get; set; }

    // MARK: - State

    public bool HasCompletedOnboarding { get; set; } = false;
    public DateTime? LastUpdateCheckUtc { get; set; }

    // MARK: - Persistence

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsPath)!;
            Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // Settings save failure is non-fatal
        }
    }

    private static CaptureSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<CaptureSettings>(json) ?? new CaptureSettings();
            }
        }
        catch
        {
            // Corrupted settings — start fresh
        }
        return new CaptureSettings();
    }

    public void ResetToDefaults()
    {
        var fresh = new CaptureSettings();
        // Copy all properties from fresh defaults
        SaveDirectory = fresh.SaveDirectory;
        FileNameTemplate = fresh.FileNameTemplate;
        CopyScreenshotToClipboard = fresh.CopyScreenshotToClipboard;
        CopyVideoToClipboard = fresh.CopyVideoToClipboard;
        CopyGifToClipboard = fresh.CopyGifToClipboard;
        ShowInExplorer = fresh.ShowInExplorer;
        ShowSaveNotifications = fresh.ShowSaveNotifications;
        OpenAfterCapture = fresh.OpenAfterCapture;
        LaunchAtStartup = fresh.LaunchAtStartup;
        AlwaysCaptureMainDisplay = fresh.AlwaysCaptureMainDisplay;
        ScreenshotFormat = fresh.ScreenshotFormat;
        ScreenshotScale = fresh.ScreenshotScale;
        JpegQuality = fresh.JpegQuality;
        ScreenshotCountdownEnabled = fresh.ScreenshotCountdownEnabled;
        ScreenshotCountdownDuration = fresh.ScreenshotCountdownDuration;
        VideoFrameRate = fresh.VideoFrameRate;
        RecordSystemAudio = fresh.RecordSystemAudio;
        ShowRegionIndicator = fresh.ShowRegionIndicator;
        VideoCountdownEnabled = fresh.VideoCountdownEnabled;
        VideoCountdownDuration = fresh.VideoCountdownDuration;
        GifFrameRate = fresh.GifFrameRate;
        GifMaxWidth = fresh.GifMaxWidth;
        GifCountdownEnabled = fresh.GifCountdownEnabled;
        GifCountdownDuration = fresh.GifCountdownDuration;
        ScreenshotHotKeyVk = fresh.ScreenshotHotKeyVk;
        ScreenshotHotKeyMod = fresh.ScreenshotHotKeyMod;
        VideoHotKeyVk = fresh.VideoHotKeyVk;
        VideoHotKeyMod = fresh.VideoHotKeyMod;
        GifHotKeyVk = fresh.GifHotKeyVk;
        GifHotKeyMod = fresh.GifHotKeyMod;
        PickerPositionX = fresh.PickerPositionX;
        PickerPositionY = fresh.PickerPositionY;
        StartPanelPositionX = fresh.StartPanelPositionX;
        StartPanelPositionY = fresh.StartPanelPositionY;
        StopPanelPositionX = fresh.StopPanelPositionX;
        StopPanelPositionY = fresh.StopPanelPositionY;
        HasCompletedOnboarding = false;
        LastUpdateCheckUtc = null;
        Save();
    }

    public bool ShouldCopyToClipboard(CaptureType type) => type switch
    {
        CaptureType.Screenshot => CopyScreenshotToClipboard,
        CaptureType.Video => CopyVideoToClipboard,
        CaptureType.Gif => CopyGifToClipboard,
        _ => false
    };

    public bool IsCountdownEnabled(CaptureType type) => type switch
    {
        CaptureType.Screenshot => ScreenshotCountdownEnabled,
        CaptureType.Video => VideoCountdownEnabled,
        CaptureType.Gif => GifCountdownEnabled,
        _ => false
    };

    public int CountdownDuration(CaptureType type) => type switch
    {
        CaptureType.Screenshot => ScreenshotCountdownDuration,
        CaptureType.Video => VideoCountdownDuration,
        CaptureType.Gif => GifCountdownDuration,
        _ => 3
    };
}

public enum CaptureType
{
    Screenshot,
    Video,
    Gif
}

public enum ImageFormat
{
    Png,
    Jpeg
}

public enum CapturePickerMode
{
    Region,
    Screen,
    Window
}

public static class CaptureTypeExtensions
{
    public static string FileExtension(this CaptureType type) => type switch
    {
        CaptureType.Screenshot => CaptureSettings.Instance.ScreenshotFormat == ImageFormat.Png ? "png" : "jpg",
        CaptureType.Video => "mp4",
        CaptureType.Gif => "gif",
        _ => "png"
    };

    public static string Label(this CaptureType type) => type switch
    {
        CaptureType.Screenshot => "Screenshot",
        CaptureType.Video => "Video",
        CaptureType.Gif => "GIF",
        _ => "Capture"
    };
}
