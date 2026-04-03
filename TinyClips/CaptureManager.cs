using System.Drawing;
using System.Drawing.Imaging;
using TinyClips.Capture;
using TinyClips.Models;
using TinyClips.Services;
using TinyClips.Views;

namespace TinyClips;

/// <summary>
/// Central coordinator for all capture flows.
/// Owns recorders, writers, and manages the capture-time window lifecycle.
/// Mirrors the macOS CaptureManager from TinyClipsApp.swift.
/// </summary>
public sealed class CaptureManager : IDisposable
{
    // MARK: - State

    public bool IsRecording { get; private set; }
    public event Action<bool>? IsRecordingChanged;

    private VideoRecorder? _videoRecorder;
    private GifWriter? _gifWriter;
    private CapturePickerWindow? _pickerWindow;
    private StartRecordingWindow? _startWindow;
    private StopRecordingWindow? _stopWindow;
    private ScreenshotEditorWindow? _editorWindow;
    private VideoTrimmerWindow? _videoTrimmerWindow;
    private GifTrimmerWindow? _gifTrimmerWindow;
    private CaptureRegion? _pendingRegion;
    private readonly HotKeyManager _hotKeyManager = new();

    // MARK: - Initialization

    public CaptureManager()
    {
        AppLog.Info("CaptureManager initialized");
        _hotKeyManager.Initialize();
        RegisterHotKeys();
    }

    private void RegisterHotKeys()
    {
        var s = CaptureSettings.Instance;
        _hotKeyManager.RegisterCaptureHotKeys(
            screenshotVk: s.ScreenshotHotKeyVk, screenshotMod: s.ScreenshotHotKeyMod,
            onScreenshot: () => { if (!IsRecording) TakeScreenshot(); },
            videoVk: s.VideoHotKeyVk, videoMod: s.VideoHotKeyMod,
            onRecordVideo: () => { if (!IsRecording) StartVideoRecording(); },
            gifVk: s.GifHotKeyVk, gifMod: s.GifHotKeyMod,
            onRecordGif: () => { if (!IsRecording) StartGifRecording(); });

        UpdateStopHotKey();
    }

    public void ReloadHotKeys() => RegisterHotKeys();

    private void SetRecording(bool value)
    {
        IsRecording = value;
        IsRecordingChanged?.Invoke(value);
        UpdateStopHotKey();
    }

    private void UpdateStopHotKey()
    {
        if (IsRecording)
            _hotKeyManager.RegisterStopHotKey(StopRecording);
        else
            _hotKeyManager.UnregisterStopHotKey();
    }

    // MARK: - Screenshot Flow

    public void TakeScreenshot()
    {
        _ = TakeScreenshotAsync();
    }

    private async Task TakeScreenshotAsync()
    {
        PrepareForNewCapture();

        if (!PermissionManager.IsCaptureSupported())
        {
            NotificationService.Instance.ShowErrorNotification("Screen capture not supported on this device.");
            return;
        }

        ShowPicker(CaptureType.Screenshot);
    }

    private async Task PerformScreenshotCapture(CapturePickerMode mode, bool countdownEnabled, int countdownDuration)
    {
        CaptureRegion? region = null;

        switch (mode)
        {
            case CapturePickerMode.Region:
                region = await RegionSelectorWindow.SelectRegionAsync();
                if (region == null) { ShowPicker(CaptureType.Screenshot); return; }
                break;

            case CapturePickerMode.Screen:
                region = CaptureRegion.FullScreenAtCursor();
                break;

            case CapturePickerMode.Window:
                region = await WindowSelectorWindow.SelectWindowAsync();
                if (region == null) { ShowPicker(CaptureType.Screenshot); return; }
                break;
        }

        if (region == null) return;

        if (countdownEnabled && countdownDuration > 0)
        {
            await CountdownWindow.RunCountdownAsync(countdownDuration);
        }

        try
        {
            var settings = CaptureSettings.Instance;

            if (settings.ShowScreenshotEditor)
            {
                var bitmap = await ScreenshotCapture.CaptureRegionToBitmapAsync(region);
                ShowScreenshotEditor(bitmap);
                return;
            }

            string path = SaveService.Instance.GeneratePath(CaptureType.Screenshot);
            await ScreenshotCapture.CaptureRegionAsync(region, path);
            AppLog.Info($"Screenshot saved: {path}");
            SaveService.Instance.HandleSavedFile(path, CaptureType.Screenshot);
        }
        catch (Exception ex)
        {
            NotificationService.Instance.ShowErrorNotification($"Screenshot failed: {ex.Message}");
            AppLog.Error("Screenshot capture failed", ex);
        }

        // Reopen picker for additional captures
        ShowPicker(CaptureType.Screenshot);
    }

    // MARK: - Video Flow

    public void StartVideoRecording()
    {
        _ = StartVideoRecordingAsync();
    }

    private async Task StartVideoRecordingAsync()
    {
        PrepareForNewCapture();

        if (!PermissionManager.IsCaptureSupported())
        {
            NotificationService.Instance.ShowErrorNotification("Screen capture not supported on this device.");
            return;
        }

        ShowPicker(CaptureType.Video);
    }

    private void BeginVideoRecording(CaptureRegion region, bool countdownEnabled, int countdownDuration)
    {
        _pendingRegion = region;

        var startWin = new StartRecordingWindow(CaptureType.Video);
        startWin.OnStart = () =>
        {
            _startWindow = null;
            _ = DoVideoRecording(region, countdownEnabled, countdownDuration);
        };
        startWin.OnCancelled = () =>
        {
            _startWindow = null;
            _pendingRegion = null;
        };
        _startWindow = startWin;
        startWin.Activate();
    }

    private async Task DoVideoRecording(CaptureRegion region, bool countdownEnabled, int countdownDuration)
    {
        if (countdownEnabled && countdownDuration > 0)
        {
            await CountdownWindow.RunCountdownAsync(countdownDuration);
        }

        try
        {
            var settings = CaptureSettings.Instance;
            string path = SaveService.Instance.GeneratePath(CaptureType.Video);
            var recorder = new VideoRecorder();
            _videoRecorder = recorder;
            SetRecording(true);

            recorder.OnElapsedChanged = elapsed =>
            {
                App.Current.MainDispatcherQueue?.TryEnqueue(() =>
                    _stopWindow?.UpdateElapsed(elapsed));
            };

            await recorder.StartAsync(region.ScreenRect, path, settings.VideoFrameRate,
                recordAudio: settings.RecordSystemAudio);
            AppLog.Info($"Video recording started: {path}");
            ShowStopPanel();
        }
        catch (Exception ex)
        {
            SetRecording(false);
            NotificationService.Instance.ShowErrorNotification($"Video recording failed: {ex.Message}");
            AppLog.Error("Video recording failed", ex);
        }
    }

    // MARK: - GIF Flow

    public void StartGifRecording()
    {
        _ = StartGifRecordingAsync();
    }

    private async Task StartGifRecordingAsync()
    {
        PrepareForNewCapture();

        if (!PermissionManager.IsCaptureSupported())
        {
            NotificationService.Instance.ShowErrorNotification("Screen capture not supported on this device.");
            return;
        }

        ShowPicker(CaptureType.Gif);
    }

    private void BeginGifRecording(CaptureRegion region, bool countdownEnabled, int countdownDuration)
    {
        _pendingRegion = region;

        var startWin = new StartRecordingWindow(CaptureType.Gif);
        startWin.OnStart = () =>
        {
            _startWindow = null;
            _ = DoGifRecording(region, countdownEnabled, countdownDuration);
        };
        startWin.OnCancelled = () =>
        {
            _startWindow = null;
            _pendingRegion = null;
        };
        _startWindow = startWin;
        startWin.Activate();
    }

    private async Task DoGifRecording(CaptureRegion region, bool countdownEnabled, int countdownDuration)
    {
        if (countdownEnabled && countdownDuration > 0)
        {
            await CountdownWindow.RunCountdownAsync(countdownDuration);
        }

        try
        {
            string path = SaveService.Instance.GeneratePath(CaptureType.Gif);
            var writer = new GifWriter();
            _gifWriter = writer;
            SetRecording(true);

            writer.OnElapsedChanged = elapsed =>
            {
                App.Current.MainDispatcherQueue?.TryEnqueue(() =>
                    _stopWindow?.UpdateElapsed(elapsed));
            };

            await writer.StartAsync(region.ScreenRect, path);
            AppLog.Info($"GIF recording started: {path}");
            ShowStopPanel();
        }
        catch (Exception ex)
        {
            SetRecording(false);
            NotificationService.Instance.ShowErrorNotification($"GIF recording failed: {ex.Message}");
            AppLog.Error("GIF recording failed", ex);
        }
    }

    // MARK: - Stop Recording

    public void StopRecording()
    {
        _ = StopRecordingFlow();
    }

    private async Task StopRecordingFlow()
    {
        if (_videoRecorder is { } recorder)
        {
            try
            {
                string? path = await recorder.StopAsync();
                if (path != null)
                {
                    AppLog.Info($"Video recording stopped: {path}");
                    if (CaptureSettings.Instance.ShowVideoTrimmer)
                        ShowVideoTrimmer(path);
                    else
                        SaveService.Instance.HandleSavedFile(path, CaptureType.Video);
                }
            }
            catch (Exception ex)
            {
                NotificationService.Instance.ShowErrorNotification($"Video save failed: {ex.Message}");
                AppLog.Error("Video stop/save failed", ex);
            }
            _videoRecorder = null;
        }

        if (_gifWriter is { } writer)
        {
            try
            {
                string path = await writer.StopAsync();
                AppLog.Info($"GIF recording stopped: {path}");
                if (CaptureSettings.Instance.ShowGifTrimmer)
                    ShowGifTrimmer(path);
                else
                    SaveService.Instance.HandleSavedFile(path, CaptureType.Gif);
            }
            catch (Exception ex)
            {
                NotificationService.Instance.ShowErrorNotification($"GIF save failed: {ex.Message}");
                AppLog.Error("GIF stop/save failed", ex);
            }
            _gifWriter = null;
        }

        SetRecording(false);
        DismissStopPanel();
        _pendingRegion = null;
    }

    // MARK: - Picker

    private void ShowPicker(CaptureType type)
    {
        DismissPicker();
        var settings = CaptureSettings.Instance;

        var picker = new CapturePickerWindow(
            type,
            settings.IsCountdownEnabled(type),
            settings.CountdownDuration(type));

        picker.OnCapture = (mode, countdownEnabled, countdownDuration) =>
        {
            DismissPicker();
            _ = HandlePickerResult(type, mode, countdownEnabled, countdownDuration);
        };
        picker.OnCancelled = () => DismissPicker();

        _pickerWindow = picker;
        picker.Activate();
    }

    private async Task HandlePickerResult(CaptureType type, CapturePickerMode mode, bool countdownEnabled, int countdownDuration)
    {
        CaptureRegion? region = null;

        if (type == CaptureType.Screenshot)
        {
            await PerformScreenshotCapture(mode, countdownEnabled, countdownDuration);
            return;
        }

        // Video/GIF: get region first
        switch (mode)
        {
            case CapturePickerMode.Region:
                region = await RegionSelectorWindow.SelectRegionAsync();
                if (region == null) { ShowPicker(type); return; }
                break;

            case CapturePickerMode.Screen:
                region = CaptureRegion.FullScreenAtCursor();
                break;

            case CapturePickerMode.Window:
                region = await WindowSelectorWindow.SelectWindowAsync();
                if (region == null) { ShowPicker(type); return; }
                break;
        }

        if (region == null) return;

        switch (type)
        {
            case CaptureType.Video:
                BeginVideoRecording(region, countdownEnabled, countdownDuration);
                break;
            case CaptureType.Gif:
                BeginGifRecording(region, countdownEnabled, countdownDuration);
                break;
        }
    }

    private void DismissPicker()
    {
        try { _pickerWindow?.Close(); } catch (Exception ex) { AppLog.Error("Close picker failed", ex); }
        _pickerWindow = null;
    }

    // MARK: - Stop Panel

    private void ShowStopPanel()
    {
        DismissStopPanel();
        var panel = new StopRecordingWindow();
        panel.OnStop = StopRecording;
        _stopWindow = panel;
        panel.Activate();
        panel.StartTimer();
    }

    private void DismissStopPanel()
    {
        try { _stopWindow?.Close(); } catch (Exception ex) { AppLog.Error("Close stop panel failed", ex); }
        _stopWindow = null;
    }

    // MARK: - Screenshot Editor

    private void ShowScreenshotEditor(System.Drawing.Bitmap bitmap)
    {
        DismissEditor();
        var editor = new ScreenshotEditorWindow(bitmap);
        editor.OnSaved = path =>
        {
            _editorWindow = null;
            SaveService.Instance.HandleSavedFile(path, CaptureType.Screenshot);
            ShowPicker(CaptureType.Screenshot);
        };
        editor.OnDiscarded = () =>
        {
            _editorWindow = null;
            ShowPicker(CaptureType.Screenshot);
        };
        _editorWindow = editor;
        editor.Activate();
    }

    private void DismissEditor()
    {
        try { _editorWindow?.Close(); } catch (Exception ex) { AppLog.Error("Close editor failed", ex); }
        _editorWindow = null;
    }

    // MARK: - Video Trimmer

    private void ShowVideoTrimmer(string videoPath)
    {
        try
        {
            DismissVideoTrimmer();
            var trimmer = new VideoTrimmerWindow(videoPath);
            trimmer.OnSaved = path =>
            {
                _videoTrimmerWindow = null;
                SaveService.Instance.HandleSavedFile(path, CaptureType.Video);
            };
            trimmer.OnDiscarded = () =>
            {
                _videoTrimmerWindow = null;
            };
            _videoTrimmerWindow = trimmer;
            trimmer.Activate();
        }
        catch (Exception ex)
        {
            NotificationService.Instance.ShowErrorNotification($"Could not open video trimmer: {ex.Message}");
            SaveService.Instance.HandleSavedFile(videoPath, CaptureType.Video);
        }
    }

    private void DismissVideoTrimmer()
    {
        try { _videoTrimmerWindow?.Close(); } catch (Exception ex) { AppLog.Error("Close video trimmer failed", ex); }
        _videoTrimmerWindow = null;
    }

    // MARK: - GIF Trimmer

    private void ShowGifTrimmer(string gifPath)
    {
        DismissGifTrimmer();
        var trimmer = new GifTrimmerWindow(gifPath);
        trimmer.OnSaved = path =>
        {
            _gifTrimmerWindow = null;
            SaveService.Instance.HandleSavedFile(path, CaptureType.Gif);
        };
        trimmer.OnDiscarded = () =>
        {
            _gifTrimmerWindow = null;
        };
        _gifTrimmerWindow = trimmer;
        trimmer.Activate();
    }

    private void DismissGifTrimmer()
    {
        try { _gifTrimmerWindow?.Close(); } catch (Exception ex) { AppLog.Error("Close GIF trimmer failed", ex); }
        _gifTrimmerWindow = null;
    }

    // MARK: - Cleanup

    private void PrepareForNewCapture()
    {
        DismissPicker();
        DismissEditor();
        DismissVideoTrimmer();
        DismissGifTrimmer();
        try { _startWindow?.Close(); } catch (Exception ex) { AppLog.Error("Close start window failed", ex); }
        _startWindow = null;
        _pendingRegion = null;

        if (IsRecording)
        {
            _ = StopRecordingFlow();
        }
        else
        {
            DismissStopPanel();
        }
    }

    // MARK: - Settings & Onboarding

    private SettingsWindow? _settingsWindow;

    public void ShowSettings()
    {
        try
        {
            if (_settingsWindow != null)
            {
                _settingsWindow.Activate();
                return;
            }
            _settingsWindow = new SettingsWindow();
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Activate();
        }
        catch (Exception ex)
        {
            AppLog.Error("Failed to open Settings window", ex);
            _settingsWindow = null;
            NotificationService.Instance.ShowErrorNotification(
                "Settings Error",
                $"Could not open settings: {ex.Message}");
        }
    }

    public void ShowOnboarding()
    {
        // TODO: Implement onboarding wizard
        // For now, mark as completed
        CaptureSettings.Instance.HasCompletedOnboarding = true;
        CaptureSettings.Instance.Save();
    }

    private void ShowOnboardingIfNeeded()
    {
        if (CaptureSettings.Instance.HasCompletedOnboarding) return;
        // TODO: Show onboarding wizard on first launch
        CaptureSettings.Instance.HasCompletedOnboarding = true;
        CaptureSettings.Instance.Save();
    }

    public void Dispose()
    {
        _hotKeyManager.Dispose();
    }
}
