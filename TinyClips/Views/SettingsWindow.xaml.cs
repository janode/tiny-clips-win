using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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

    // All setting pages
    private readonly StackPanel[] _pages;

    public SettingsWindow()
    {
        InitializeComponent();
        Title = "TinyClips Settings";
        _settings = CaptureSettings.Instance;

        _pages = [GeneralPage, ScreenshotPage, VideoPage, GifPage, ShortcutsPage, AboutPage];

        // DPI-aware sizing immediately (before Activate)
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        double scale = Helpers.NativeMethods.GetDpiForWindow(hwnd) / 96.0;
        AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(900 * scale), (int)(620 * scale)));

        LoadSettings();
        _isLoading = false;

        // Select first item
        NavView.SelectedItem = NavView.MenuItems[0];
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

        // GIF
        SelectFpsComboItem(GifFpsCombo, _settings.GifFrameRate);
        GifMaxWidthBox.Value = _settings.GifMaxWidth;
        GifCountdownToggle.IsOn = _settings.GifCountdownEnabled;
        GifCountdownBox.Value = _settings.GifCountdownDuration;

        // Shortcuts
        UpdateShortcutLabels();

        // About
        VersionLabel.Text = "Version 1.0.0";
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

    private void UpdateShortcutLabels()
    {
        ScreenshotShortcutLabel.Text = FormatHotKey(_settings.ScreenshotHotKeyMod, _settings.ScreenshotHotKeyVk);
        VideoShortcutLabel.Text = FormatHotKey(_settings.VideoHotKeyMod, _settings.VideoHotKeyVk);
        GifShortcutLabel.Text = FormatHotKey(_settings.GifHotKeyMod, _settings.GifHotKeyVk);
    }

    private static string FormatHotKey(int mod, int vk)
    {
        var parts = new System.Collections.Generic.List<string>();
        if ((mod & 0x0002) != 0) parts.Add("Ctrl");
        if ((mod & 0x0001) != 0) parts.Add("Alt");
        if ((mod & 0x0004) != 0) parts.Add("Shift");
        if ((mod & 0x0008) != 0) parts.Add("Win");

        // Virtual key to readable name
        string keyName = vk switch
        {
            >= 0x30 and <= 0x39 => ((char)vk).ToString(), // 0-9
            >= 0x41 and <= 0x5A => ((char)vk).ToString(), // A-Z
            >= 0x70 and <= 0x87 => $"F{vk - 0x6F}",      // F1-F24
            0xBE => ".",
            0xBC => ",",
            _ => $"0x{vk:X2}"
        };
        parts.Add(keyName);
        return string.Join(" + ", parts);
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

    // MARK: - Shortcuts Events

    private void OnResetShortcuts(object sender, RoutedEventArgs e)
    {
        _settings.ResetToDefaults();
        LoadSettings();
        SaveSettings();
    }

    // MARK: - Save

    private void SaveSettings()
    {
        _settings.Save();
    }
}
