using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Runtime.InteropServices;
using TinyClips.Models;
using TinyClips.Services;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace TinyClips.Views;

/// <summary>
/// Full settings window with NavigationView sidebar.
/// Matches the macOS SettingsView layout with tab-based navigation.
/// </summary>
public sealed partial class SettingsWindow : Window
{
    private readonly CaptureSettings _settings;
    private bool _isLoading = true;
    private Helpers.NativeMethods.SUBCLASSPROC? _subclassProc;
    private ShortcutRecorderControl _screenshotRecorder = null!;
    private ShortcutRecorderControl _videoRecorder = null!;
    private ShortcutRecorderControl _gifRecorder = null!;

    // All setting pages
    private readonly StackPanel[] _pages;

    public SettingsWindow()
    {
        InitializeComponent();
        Title = "TinyClips Settings";
        this.SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
        _settings = CaptureSettings.Instance;

        _pages = [GeneralPage, ScreenshotPage, VideoPage, GifPage, ShortcutsPage, AboutPage];

        // DPI-aware sizing immediately (before Activate)
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        double scale = Helpers.NativeMethods.GetDpiForWindow(hwnd) / 96.0;
        AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(900 * scale), (int)(700 * scale)));

        // Enforce minimum window size (720×460) via WM_GETMINMAXINFO
        _subclassProc = MinSizeSubclassProc;
        Helpers.NativeMethods.SetWindowSubclass(hwnd, _subclassProc, 0, 0);

        LoadSettings();
        _isLoading = false;

        // Select first item
        NavView.SelectedItem = NavView.MenuItems[0];
    }

    private nint MinSizeSubclassProc(nint hWnd, uint uMsg, nint wParam, nint lParam, nint uIdSubclass, nint dwRefData)
    {
        if (uMsg == Helpers.NativeMethods.WM_GETMINMAXINFO)
        {
            double scale = Helpers.NativeMethods.GetDpiForWindow(hWnd) / 96.0;
            var mmi = Marshal.PtrToStructure<Helpers.NativeMethods.MINMAXINFO>(lParam);
            mmi.ptMinTrackSize.X = (int)(720 * scale);
            mmi.ptMinTrackSize.Y = (int)(460 * scale);
            Marshal.StructureToPtr(mmi, lParam, false);
        }
        return Helpers.NativeMethods.DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    // MARK: - Navigation

    private void OnNavigationChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs e)
    {
        if (e.SelectedItem is NavigationViewItem item)
        {
            string? tag = item.Tag as string;
            foreach (var page in _pages)
                page.Visibility = Visibility.Collapsed;

            switch (tag)
            {
                case "General": GeneralPage.Visibility = Visibility.Visible; break;
                case "Screenshot": ScreenshotPage.Visibility = Visibility.Visible; break;
                case "Video": VideoPage.Visibility = Visibility.Visible; break;
                case "GIF": GifPage.Visibility = Visibility.Visible; break;
                case "Shortcuts": ShortcutsPage.Visibility = Visibility.Visible; break;
                case "About": AboutPage.Visibility = Visibility.Visible; break;
            }
        }
    }

    // MARK: - Load Settings

    private void LoadSettings()
    {
        // General
        SaveDirectoryBox.Text = _settings.SaveDirectory;
        FileNameTemplateBox.Text = _settings.FileNameTemplate;
        CopyToClipboardToggle.IsOn = _settings.CopyScreenshotToClipboard;
        ShowInExplorerToggle.IsOn = _settings.ShowInExplorer;
        ShowNotificationToggle.IsOn = _settings.ShowSaveNotifications;
        OpenAfterCaptureToggle.IsOn = _settings.OpenAfterCapture;
        LaunchAtLoginToggle.IsOn = _settings.LaunchAtStartup;

        // Screenshot
        ImageFormatCombo.SelectedIndex = _settings.ScreenshotFormat == ImageFormat.Png ? 0 : 1;
        JpegQualitySlider.Value = _settings.JpegQuality;
        JpegQualityPanel.Visibility = _settings.ScreenshotFormat == ImageFormat.Jpeg
            ? Visibility.Visible : Visibility.Collapsed;
        ScreenshotCountdownToggle.IsOn = _settings.ScreenshotCountdownEnabled;
        ScreenshotCountdownBox.Value = _settings.ScreenshotCountdownDuration;
        ScreenshotEditorToggle.IsOn = _settings.ShowScreenshotEditor;

        // Video
        SelectFpsComboItem(VideoFpsCombo, _settings.VideoFrameRate);
        SystemAudioToggle.IsOn = _settings.RecordSystemAudio;
        VideoCountdownToggle.IsOn = _settings.VideoCountdownEnabled;
        VideoCountdownBox.Value = _settings.VideoCountdownDuration;
        VideoTrimmerToggle.IsOn = _settings.ShowVideoTrimmer;

        // GIF
        SelectFpsComboItem(GifFpsCombo, _settings.GifFrameRate);
        GifMaxWidthBox.Value = _settings.GifMaxWidth;
        GifCountdownToggle.IsOn = _settings.GifCountdownEnabled;
        GifCountdownBox.Value = _settings.GifCountdownDuration;
        GifTrimmerToggle.IsOn = _settings.ShowGifTrimmer;

        // Shortcuts
        SetupShortcutRecorders();

        // About
        VersionLabel.Text = "Version 1.0.0";
        LoadAppIcon();

        UpdateFileNamePreview();
    }

    private static void SelectFpsComboItem(ComboBox combo, double fps)
    {
        for (int i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is ComboBoxItem item && item.Tag is string tag && double.TryParse(tag, out double val) && Math.Abs(val - fps) < 0.5)
            {
                combo.SelectedIndex = i;
                return;
            }
        }
        combo.SelectedIndex = 0;
    }

    // MARK: - Shortcut Recorders

    private void SetupShortcutRecorders()
    {
        _screenshotRecorder = new ShortcutRecorderControl();
        _screenshotRecorder.SetShortcut(_settings.ScreenshotHotKeyMod, _settings.ScreenshotHotKeyVk);
        _screenshotRecorder.OnShortcutChanged = (mod, vk) =>
        {
            _settings.ScreenshotHotKeyMod = mod;
            _settings.ScreenshotHotKeyVk = vk;
            SaveSettings();
            CheckShortcutConflicts();
            App.Current.CaptureManager.ReloadHotKeys();
        };
        ScreenshotRecorderHost.Content = _screenshotRecorder;

        _videoRecorder = new ShortcutRecorderControl();
        _videoRecorder.SetShortcut(_settings.VideoHotKeyMod, _settings.VideoHotKeyVk);
        _videoRecorder.OnShortcutChanged = (mod, vk) =>
        {
            _settings.VideoHotKeyMod = mod;
            _settings.VideoHotKeyVk = vk;
            SaveSettings();
            CheckShortcutConflicts();
            App.Current.CaptureManager.ReloadHotKeys();
        };
        VideoRecorderHost.Content = _videoRecorder;

        _gifRecorder = new ShortcutRecorderControl();
        _gifRecorder.SetShortcut(_settings.GifHotKeyMod, _settings.GifHotKeyVk);
        _gifRecorder.OnShortcutChanged = (mod, vk) =>
        {
            _settings.GifHotKeyMod = mod;
            _settings.GifHotKeyVk = vk;
            SaveSettings();
            CheckShortcutConflicts();
            App.Current.CaptureManager.ReloadHotKeys();
        };
        GifRecorderHost.Content = _gifRecorder;

        CheckShortcutConflicts();
    }

    private void CheckShortcutConflicts()
    {
        var shortcuts = new (string Name, int Mod, int Vk, TextBlock ConflictText)[]
        {
            ("Screenshot", _settings.ScreenshotHotKeyMod, _settings.ScreenshotHotKeyVk, ScreenshotConflictText),
            ("Video", _settings.VideoHotKeyMod, _settings.VideoHotKeyVk, VideoConflictText),
            ("GIF", _settings.GifHotKeyMod, _settings.GifHotKeyVk, GifConflictText)
        };

        for (int i = 0; i < shortcuts.Length; i++)
        {
            string? conflict = null;
            for (int j = 0; j < shortcuts.Length; j++)
            {
                if (i != j && shortcuts[i].Mod == shortcuts[j].Mod && shortcuts[i].Vk == shortcuts[j].Vk)
                {
                    conflict = $"Conflicts with {shortcuts[j].Name}";
                    break;
                }
            }
            if (conflict != null)
            {
                shortcuts[i].ConflictText.Text = conflict;
                shortcuts[i].ConflictText.Visibility = Visibility.Visible;
            }
            else
            {
                shortcuts[i].ConflictText.Visibility = Visibility.Collapsed;
            }
        }
    }

    // MARK: - General Events

    private async void OnBrowseFolder(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
        picker.FileTypeFilter.Add("*");

        var hwnd = WindowNative.GetWindowHandle(this);
        InitializeWithWindow.Initialize(picker, hwnd);

        var folder = await picker.PickSingleFolderAsync();
        if (folder != null)
        {
            _settings.SaveDirectory = folder.Path;
            SaveDirectoryBox.Text = folder.Path;
            SaveSettings();
        }
    }

    private void OnFileNameTemplateChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoading) return;
        _settings.FileNameTemplate = FileNameTemplateBox.Text;
        SaveSettings();
        UpdateFileNamePreview();
    }

    private void UpdateFileNamePreview()
    {
        FileNamePreviewText.Text = SaveService.Instance.NamingPreview();
    }

    private void OnCopyClipboardToggled(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        _settings.CopyScreenshotToClipboard = CopyToClipboardToggle.IsOn;
        _settings.CopyVideoToClipboard = CopyToClipboardToggle.IsOn;
        _settings.CopyGifToClipboard = CopyToClipboardToggle.IsOn;
        SaveSettings();
    }

    private void OnShowExplorerToggled(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        _settings.ShowInExplorer = ShowInExplorerToggle.IsOn;
        SaveSettings();
    }

    private void OnShowNotificationToggled(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        _settings.ShowSaveNotifications = ShowNotificationToggle.IsOn;
        SaveSettings();
    }

    private void OnOpenAfterCaptureToggled(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        _settings.OpenAfterCapture = OpenAfterCaptureToggle.IsOn;
        SaveSettings();
    }

    private void OnLaunchAtLoginToggled(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        _settings.LaunchAtStartup = LaunchAtLoginToggle.IsOn;
        LaunchAtLoginManager.SetEnabled(_settings.LaunchAtStartup);
        SaveSettings();
    }

    // MARK: - Screenshot Events

    private void OnImageFormatChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading) return;
        _settings.ScreenshotFormat = ImageFormatCombo.SelectedIndex == 0 ? ImageFormat.Png : ImageFormat.Jpeg;
        JpegQualityPanel.Visibility = _settings.ScreenshotFormat == ImageFormat.Jpeg
            ? Visibility.Visible : Visibility.Collapsed;
        SaveSettings();
    }

    private void OnJpegQualityChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_isLoading) return;
        _settings.JpegQuality = (int)JpegQualitySlider.Value;
        SaveSettings();
    }

    private void OnScreenshotCountdownToggled(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        _settings.ScreenshotCountdownEnabled = ScreenshotCountdownToggle.IsOn;
        SaveSettings();
    }

    private void OnScreenshotCountdownValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs e)
    {
        if (_isLoading) return;
        _settings.ScreenshotCountdownDuration = (int)ScreenshotCountdownBox.Value;
        SaveSettings();
    }

    private void OnScreenshotEditorToggled(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        _settings.ShowScreenshotEditor = ScreenshotEditorToggle.IsOn;
        SaveSettings();
    }

    // MARK: - Video Events

    private void OnVideoFpsChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading) return;
        if (VideoFpsCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag && int.TryParse(tag, out int fps))
        {
            _settings.VideoFrameRate = fps;
            SaveSettings();
        }
    }

    private void OnSystemAudioToggled(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        _settings.RecordSystemAudio = SystemAudioToggle.IsOn;
        SaveSettings();
    }

    private void OnVideoCountdownToggled(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        _settings.VideoCountdownEnabled = VideoCountdownToggle.IsOn;
        SaveSettings();
    }

    private void OnVideoCountdownValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs e)
    {
        if (_isLoading) return;
        _settings.VideoCountdownDuration = (int)VideoCountdownBox.Value;
        SaveSettings();
    }

    private void OnVideoTrimmerToggled(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        _settings.ShowVideoTrimmer = VideoTrimmerToggle.IsOn;
        SaveSettings();
    }

    // MARK: - GIF Events

    private void OnGifFpsChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading) return;
        if (GifFpsCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag && double.TryParse(tag, out double fps))
        {
            _settings.GifFrameRate = fps;
            SaveSettings();
        }
    }

    private void OnGifMaxWidthChanged(NumberBox sender, NumberBoxValueChangedEventArgs e)
    {
        if (_isLoading) return;
        _settings.GifMaxWidth = (int)GifMaxWidthBox.Value;
        SaveSettings();
    }

    private void OnGifCountdownToggled(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        _settings.GifCountdownEnabled = GifCountdownToggle.IsOn;
        SaveSettings();
    }

    private void OnGifCountdownValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs e)
    {
        if (_isLoading) return;
        _settings.GifCountdownDuration = (int)GifCountdownBox.Value;
        SaveSettings();
    }

    private void OnGifTrimmerToggled(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        _settings.ShowGifTrimmer = GifTrimmerToggle.IsOn;
        SaveSettings();
    }

    // MARK: - Shortcuts Events

    private void OnResetShortcuts(object sender, RoutedEventArgs e)
    {
        var fresh = new CaptureSettings();
        _settings.ScreenshotHotKeyVk = fresh.ScreenshotHotKeyVk;
        _settings.ScreenshotHotKeyMod = fresh.ScreenshotHotKeyMod;
        _settings.VideoHotKeyVk = fresh.VideoHotKeyVk;
        _settings.VideoHotKeyMod = fresh.VideoHotKeyMod;
        _settings.GifHotKeyVk = fresh.GifHotKeyVk;
        _settings.GifHotKeyMod = fresh.GifHotKeyMod;
        SaveSettings();

        _screenshotRecorder.SetShortcut(_settings.ScreenshotHotKeyMod, _settings.ScreenshotHotKeyVk);
        _videoRecorder.SetShortcut(_settings.VideoHotKeyMod, _settings.VideoHotKeyVk);
        _gifRecorder.SetShortcut(_settings.GifHotKeyMod, _settings.GifHotKeyVk);
        CheckShortcutConflicts();
        App.Current.CaptureManager.ReloadHotKeys();
    }

    // MARK: - Save

    private void SaveSettings()
    {
        _settings.Save();
    }

    private void LoadAppIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "tinyclips-store-icon.png");
        if (File.Exists(iconPath))
        {
            AppIcon.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(iconPath));
        }
    }
}
