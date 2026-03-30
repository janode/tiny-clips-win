using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using TinyClips.Models;
using TinyClips.Services;

namespace TinyClips.ViewModels;

/// <summary>
/// ViewModel for SettingsWindow — wraps CaptureSettings with INotifyPropertyChanged
/// so the XAML can use x:Bind TwoWay. Each setter auto-saves to disk.
/// </summary>
public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private readonly CaptureSettings _settings = CaptureSettings.Instance;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    // MARK: - General

    public string SaveDirectory
    {
        get => _settings.SaveDirectory;
        set { _settings.SaveDirectory = value; Save(); OnPropertyChanged(); OnPropertyChanged(nameof(FileNamePreview)); }
    }

    public string FileNameTemplate
    {
        get => _settings.FileNameTemplate;
        set { _settings.FileNameTemplate = value; Save(); OnPropertyChanged(); OnPropertyChanged(nameof(FileNamePreview)); }
    }

    public string FileNamePreview => SaveService.Instance.NamingPreview();

    public bool CopyToClipboard
    {
        get => _settings.CopyScreenshotToClipboard;
        set
        {
            _settings.CopyScreenshotToClipboard = value;
            _settings.CopyVideoToClipboard = value;
            _settings.CopyGifToClipboard = value;
            Save();
            OnPropertyChanged();
        }
    }

    public bool ShowInExplorer
    {
        get => _settings.ShowInExplorer;
        set { _settings.ShowInExplorer = value; Save(); OnPropertyChanged(); }
    }

    public bool ShowSaveNotifications
    {
        get => _settings.ShowSaveNotifications;
        set { _settings.ShowSaveNotifications = value; Save(); OnPropertyChanged(); }
    }

    public bool OpenAfterCapture
    {
        get => _settings.OpenAfterCapture;
        set { _settings.OpenAfterCapture = value; Save(); OnPropertyChanged(); }
    }

    public bool LaunchAtStartup
    {
        get => _settings.LaunchAtStartup;
        set
        {
            _settings.LaunchAtStartup = value;
            LaunchAtLoginManager.SetEnabled(value);
            Save();
            OnPropertyChanged();
        }
    }

    // MARK: - Screenshot

    public int ImageFormatIndex
    {
        get => _settings.ScreenshotFormat == ImageFormat.Png ? 0 : 1;
        set
        {
            _settings.ScreenshotFormat = value == 0 ? ImageFormat.Png : ImageFormat.Jpeg;
            Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(JpegQualityVisible));
        }
    }

    public Visibility JpegQualityVisible
        => _settings.ScreenshotFormat == ImageFormat.Jpeg ? Visibility.Visible : Visibility.Collapsed;

    public double JpegQuality
    {
        get => _settings.JpegQuality;
        set { _settings.JpegQuality = (int)value; Save(); OnPropertyChanged(); }
    }

    public bool ScreenshotCountdownEnabled
    {
        get => _settings.ScreenshotCountdownEnabled;
        set { _settings.ScreenshotCountdownEnabled = value; Save(); OnPropertyChanged(); }
    }

    public double ScreenshotCountdownDuration
    {
        get => _settings.ScreenshotCountdownDuration;
        set { _settings.ScreenshotCountdownDuration = (int)value; Save(); OnPropertyChanged(); }
    }

    public bool ShowScreenshotEditor
    {
        get => _settings.ShowScreenshotEditor;
        set { _settings.ShowScreenshotEditor = value; Save(); OnPropertyChanged(); }
    }

    // MARK: - Video

    public int VideoFpsIndex
    {
        get => _settings.VideoFrameRate switch
        {
            15 => 0,
            24 => 1,
            30 => 2,
            60 => 3,
            _ => 2
        };
        set
        {
            _settings.VideoFrameRate = value switch
            {
                0 => 15,
                1 => 24,
                2 => 30,
                3 => 60,
                _ => 30
            };
            Save();
            OnPropertyChanged();
        }
    }

    public bool RecordSystemAudio
    {
        get => _settings.RecordSystemAudio;
        set { _settings.RecordSystemAudio = value; Save(); OnPropertyChanged(); }
    }

    public bool VideoCountdownEnabled
    {
        get => _settings.VideoCountdownEnabled;
        set { _settings.VideoCountdownEnabled = value; Save(); OnPropertyChanged(); }
    }

    public double VideoCountdownDuration
    {
        get => _settings.VideoCountdownDuration;
        set { _settings.VideoCountdownDuration = (int)value; Save(); OnPropertyChanged(); }
    }

    public bool ShowVideoTrimmer
    {
        get => _settings.ShowVideoTrimmer;
        set { _settings.ShowVideoTrimmer = value; Save(); OnPropertyChanged(); }
    }

    // MARK: - GIF

    public int GifFpsIndex
    {
        get
        {
            var fps = _settings.GifFrameRate;
            if (Math.Abs(fps - 5) < 0.5) return 0;
            if (Math.Abs(fps - 10) < 0.5) return 1;
            if (Math.Abs(fps - 15) < 0.5) return 2;
            if (Math.Abs(fps - 24) < 0.5) return 3;
            return 1;
        }
        set
        {
            _settings.GifFrameRate = value switch
            {
                0 => 5,
                1 => 10,
                2 => 15,
                3 => 24,
                _ => 10
            };
            Save();
            OnPropertyChanged();
        }
    }

    public double GifMaxWidth
    {
        get => _settings.GifMaxWidth;
        set { _settings.GifMaxWidth = (int)value; Save(); OnPropertyChanged(); }
    }

    public bool GifCountdownEnabled
    {
        get => _settings.GifCountdownEnabled;
        set { _settings.GifCountdownEnabled = value; Save(); OnPropertyChanged(); }
    }

    public double GifCountdownDuration
    {
        get => _settings.GifCountdownDuration;
        set { _settings.GifCountdownDuration = (int)value; Save(); OnPropertyChanged(); }
    }

    public bool ShowGifTrimmer
    {
        get => _settings.ShowGifTrimmer;
        set { _settings.ShowGifTrimmer = value; Save(); OnPropertyChanged(); }
    }

    private void Save() => _settings.Save();
}
