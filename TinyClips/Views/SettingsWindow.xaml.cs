using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Runtime.InteropServices;
using TinyClips.Helpers;
using TinyClips.Models;
using TinyClips.Services;
using TinyClips.ViewModels;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace TinyClips.Views;

/// <summary>
/// Full settings window with NavigationView sidebar.
/// Matches the macOS SettingsView layout with tab-based navigation.
/// Most settings are bound via x:Bind to SettingsViewModel.
/// </summary>
public sealed partial class SettingsWindow : Window
{
    public SettingsViewModel ViewModel { get; } = new();

    private readonly CaptureSettings _settings;
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
        try
        {
            if (Microsoft.UI.Composition.SystemBackdrops.MicaController.IsSupported())
                this.SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
        }
        catch (Exception ex)
        {
            AppLog.Error("Mica backdrop failed, using default", ex);
            this.SystemBackdrop = null;
        }
        _settings = CaptureSettings.Instance;

        _pages = [GeneralPage, ScreenshotPage, VideoPage, GifPage, ShortcutsPage, AboutPage];

        // DPI-aware sizing immediately (before Activate)
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        double scale = Helpers.NativeMethods.GetDpiForWindow(hwnd) / 96.0;
        AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(900 * scale), (int)(700 * scale)));

        // Enforce minimum window size (720×460) via WM_GETMINMAXINFO
        _subclassProc = MinSizeSubclassProc;
        Helpers.NativeMethods.SetWindowSubclass(hwnd, _subclassProc, 0, 0);

        LoadNonBoundSettings();

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

    // MARK: - Non-Bound Settings (shortcuts, about, browse folder, file template)

    private void LoadNonBoundSettings()
    {
        // General (non-bound controls)
        SaveDirectoryBox.Text = _settings.SaveDirectory;
        FileNameTemplateBox.Text = _settings.FileNameTemplate;

        // Shortcuts
        SetupShortcutRecorders();

        // About
        VersionLabel.Text = "Version 1.0.0";
        LoadAppIcon();
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
            _settings.Save();
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
            _settings.Save();
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
            _settings.Save();
            CheckShortcutConflicts();
            App.Current.CaptureManager.ReloadHotKeys();
        };
        GifRecorderHost.Content = _gifRecorder;

        CheckShortcutConflicts();
    }

    private void CheckShortcutConflicts()
    {
        var shortcuts = new ShortcutValidator.Shortcut[]
        {
            new("Screenshot", _settings.ScreenshotHotKeyMod, _settings.ScreenshotHotKeyVk),
            new("Video", _settings.VideoHotKeyMod, _settings.VideoHotKeyVk),
            new("GIF", _settings.GifHotKeyMod, _settings.GifHotKeyVk)
        };

        var textBlocks = new[] { ScreenshotConflictText, VideoConflictText, GifConflictText };
        var conflicts = ShortcutValidator.CheckAllConflicts(shortcuts);

        for (int i = 0; i < textBlocks.Length; i++)
        {
            if (conflicts[i] != null)
            {
                textBlocks[i].Text = conflicts[i]!;
                textBlocks[i].Visibility = Visibility.Visible;
            }
            else
            {
                textBlocks[i].Visibility = Visibility.Collapsed;
            }
        }
    }

    // MARK: - General Events (non-bound)

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
            ViewModel.SaveDirectory = folder.Path;
            SaveDirectoryBox.Text = folder.Path;
        }
    }

    private void OnFileNameTemplateChanged(object sender, TextChangedEventArgs e)
    {
        ViewModel.FileNameTemplate = FileNameTemplateBox.Text;
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
        _settings.Save();

        _screenshotRecorder.SetShortcut(_settings.ScreenshotHotKeyMod, _settings.ScreenshotHotKeyVk);
        _videoRecorder.SetShortcut(_settings.VideoHotKeyMod, _settings.VideoHotKeyVk);
        _gifRecorder.SetShortcut(_settings.GifHotKeyMod, _settings.GifHotKeyVk);
        CheckShortcutConflicts();
        App.Current.CaptureManager.ReloadHotKeys();
    }

    private void LoadAppIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "tinyclips-store-icon.png");
        if (File.Exists(iconPath))
        {
            AppIcon.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(iconPath));
        }
    }

    // MARK: - Diagnostic Log

    private void OnCopyLogClick(object sender, RoutedEventArgs e)
    {
        var log = AppLog.ReadLog();
        var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
        dp.SetText(log);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
    }

    private void OnOpenLogFolderClick(object sender, RoutedEventArgs e)
    {
        var logPath = AppLog.GetLogPath();
        var dir = Path.GetDirectoryName(logPath)!;
        if (Directory.Exists(dir))
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dir) { UseShellExecute = true });
    }
}
