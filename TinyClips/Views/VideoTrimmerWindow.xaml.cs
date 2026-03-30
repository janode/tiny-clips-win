using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using TinyClips.Helpers;
using TinyClips.Models;
using TinyClips.Services;
using Windows.Media.Core;
using Windows.Media.Editing;
using Windows.Media.Playback;
using Windows.Media.Transcoding;
using Windows.Storage;
using WinRT.Interop;

namespace TinyClips.Views;

/// <summary>
/// Post-capture video trimmer. Plays the recorded video and lets the user
/// set start/end trim points before saving. Uses Windows.Media.Editing
/// MediaComposition for frame-accurate trimming.
/// </summary>
public sealed partial class VideoTrimmerWindow : Window
{
    // MARK: - State

    private readonly string _videoPath;
    private MediaPlayer? _mediaPlayer;
    private DispatcherTimer? _positionTimer;
    private TimeSpan _duration;
    private TimeSpan _trimStart = TimeSpan.Zero;
    private TimeSpan _trimEnd = TimeSpan.Zero; // Zero = end of video
    private bool _isUpdatingSlider;
    private bool _isPreviewMode;
    private bool _didCallback;
    private bool _isRendering;

    // Callbacks
    public Action<string>? OnSaved;
    public Action? OnDiscarded;

    // MARK: - Construction

    public VideoTrimmerWindow(string videoPath)
    {
        _videoPath = videoPath;
        InitializeComponent();
        Title = "TinyClips — Trim Video";

        Closed += OnWindowClosed;
        ConfigureWindowSize();
        _ = LoadVideoAsync();
    }

    private TimeSpan EffectiveTrimEnd => VideoTrimHelper.EffectiveTrimEnd(_trimEnd, _duration);
    private bool HasTrim => VideoTrimHelper.HasTrim(_trimStart, _trimEnd, _duration);

    // MARK: - Window Configuration

    private void ConfigureWindowSize()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        double dpiScale = NativeMethods.GetDpiForWindow(hwnd) / 96.0;

        var monitor = NativeMethods.GetMonitorRectAtCursor();
        int maxW = (int)(monitor.Width * 0.8);
        int maxH = (int)(monitor.Height * 0.8);

        int w = Math.Min((int)(900 * dpiScale), maxW);
        int h = Math.Min((int)(650 * dpiScale), maxH);

        AppWindow.Resize(new Windows.Graphics.SizeInt32(w, h));
        AppWindow.Move(new Windows.Graphics.PointInt32(
            monitor.Left + (monitor.Width - w) / 2,
            monitor.Top + (monitor.Height - h) / 2));
    }

    private async Task LoadVideoAsync()
    {
        try
        {
            _mediaPlayer = new MediaPlayer();
            _mediaPlayer.Source = MediaSource.CreateFromUri(new Uri(_videoPath));
            _mediaPlayer.AutoPlay = false;
            _mediaPlayer.Volume = 0; // Screen recordings — mute by default

            _mediaPlayer.MediaOpened += (s, e) =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    _duration = _mediaPlayer.NaturalDuration;
                    _trimEnd = TimeSpan.Zero; // Means "full duration"
                    PositionSlider.Maximum = _duration.TotalSeconds;
                    UpdateTimeDisplay();
                    UpdateTrimDisplay();
                    UpdateTrimRangeBar();
                });
            };

            VideoPlayer.SetMediaPlayer(_mediaPlayer);

            // Start position tracking timer
            _positionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _positionTimer.Tick += OnPositionTimerTick;
            _positionTimer.Start();

            // Register keyboard shortcuts
            Content.KeyDown += OnKeyDown;
        }
        catch (Exception ex)
        {
            NotificationService.Instance.ShowErrorNotification($"Failed to load video: {ex.Message}");
        }
    }

    // MARK: - Playback Controls

    private void OnPlayPauseClick(object sender, RoutedEventArgs e)
    {
        TogglePlayPause();
    }

    private void TogglePlayPause()
    {
        if (_mediaPlayer == null) return;

        if (_mediaPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing)
        {
            _mediaPlayer.Pause();
            PlayPauseIcon.Glyph = "\uE768"; // Play
        }
        else
        {
            _isPreviewMode = false;
            _mediaPlayer.Play();
            PlayPauseIcon.Glyph = "\uE769"; // Pause
        }
    }

    // MARK: - Position Tracking

    private void OnPositionTimerTick(object? sender, object e)
    {
        if (_mediaPlayer == null || _isRendering) return;

        var session = _mediaPlayer.PlaybackSession;

        // Preview mode: stop at trim end
        if (_isPreviewMode && session.PlaybackState == MediaPlaybackState.Playing)
        {
            if (session.Position >= EffectiveTrimEnd)
            {
                _mediaPlayer.Pause();
                _isPreviewMode = false;
                PlayPauseIcon.Glyph = "\uE768";
            }
        }

        _isUpdatingSlider = true;
        PositionSlider.Value = session.Position.TotalSeconds;
        _isUpdatingSlider = false;

        UpdateTimeDisplay();
    }

    private void OnPositionSliderChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isUpdatingSlider || _mediaPlayer == null) return;
        _mediaPlayer.Position = TimeSpan.FromSeconds(PositionSlider.Value);
        UpdateTimeDisplay();
    }

    private void UpdateTimeDisplay()
    {
        if (_mediaPlayer == null) return;
        var pos = _mediaPlayer.PlaybackSession.Position;
        TimeLabel.Text = $"{FormatTime(pos)} / {FormatTime(_duration)}";
    }

    // MARK: - Trim Range

    private void OnTrimRangeContainerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateTrimRangeBar();
    }

    private void UpdateTrimRangeBar()
    {
        if (_duration.TotalSeconds <= 0) return;
        double width = TrimRangeContainer.ActualWidth;
        if (width <= 0) return;

        var (startFrac, endFrac) = VideoTrimHelper.TrimRangeFractions(_trimStart, EffectiveTrimEnd, _duration);

        TrimRangeBar.Margin = new Thickness(width * startFrac, 0, 0, 0);
        TrimRangeBar.Width = Math.Max(0, width * (endFrac - startFrac));
    }

    private void UpdateTrimDisplay()
    {
        TrimStartTime.Text = FormatTime(_trimStart);
        TrimEndTime.Text = FormatTime(EffectiveTrimEnd);
        var selected = EffectiveTrimEnd - _trimStart;
        TrimDuration.Text = $"{selected.TotalSeconds:F1}s";
    }

    private void OnSetStartClick(object sender, RoutedEventArgs e)
    {
        if (_mediaPlayer == null) return;
        var pos = _mediaPlayer.PlaybackSession.Position;
        if (pos >= EffectiveTrimEnd) return; // Start must be before end
        _trimStart = pos;
        UpdateTrimDisplay();
        UpdateTrimRangeBar();
    }

    private void OnSetEndClick(object sender, RoutedEventArgs e)
    {
        if (_mediaPlayer == null) return;
        var pos = _mediaPlayer.PlaybackSession.Position;
        if (pos <= _trimStart) return; // End must be after start
        _trimEnd = pos;
        UpdateTrimDisplay();
        UpdateTrimRangeBar();
    }

    private void OnResetTrimClick(object sender, RoutedEventArgs e)
    {
        _trimStart = TimeSpan.Zero;
        _trimEnd = TimeSpan.Zero;
        UpdateTrimDisplay();
        UpdateTrimRangeBar();
    }

    private void OnPreviewTrimClick(object sender, RoutedEventArgs e)
    {
        if (_mediaPlayer == null || _duration.TotalSeconds <= 0) return;
        _mediaPlayer.Position = _trimStart;
        _isPreviewMode = true;
        _mediaPlayer.Play();
        PlayPauseIcon.Glyph = "\uE769";
    }

    // MARK: - Save & Discard

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        _ = SaveAndClose();
    }

    private void OnDiscardClick(object sender, RoutedEventArgs e)
    {
        DiscardAndClose();
    }

    private async Task SaveAndClose()
    {
        if (_isRendering) return;

        try
        {
            if (HasTrim)
            {
                // Stop playback before trimming
                _mediaPlayer?.Pause();
                PlayPauseIcon.Glyph = "\uE768";

                _isRendering = true;
                RenderOverlay.Visibility = Visibility.Visible;
                SaveButton.IsEnabled = false;

                // Dispose media player to release file lock
                _mediaPlayer?.Pause();
                VideoPlayer.SetMediaPlayer(null);
                _mediaPlayer?.Dispose();
                _mediaPlayer = null;

                var tempPath = _videoPath + $".trim_{Guid.NewGuid():N}.mp4";
                await TrimVideoAsync(_videoPath, tempPath, _trimStart, EffectiveTrimEnd);

                // Replace original with trimmed
                File.Delete(_videoPath);
                File.Move(tempPath, _videoPath);
            }
            else
            {
                // Dispose player to release file lock
                _mediaPlayer?.Pause();
                VideoPlayer.SetMediaPlayer(null);
                _mediaPlayer?.Dispose();
                _mediaPlayer = null;
            }

            _didCallback = true;
            OnSaved?.Invoke(_videoPath);
            Close();
        }
        catch (Exception ex)
        {
            _isRendering = false;
            RenderOverlay.Visibility = Visibility.Collapsed;
            SaveButton.IsEnabled = true;
            NotificationService.Instance.ShowErrorNotification($"Video trim failed: {ex.Message}");
        }
    }

    private void DiscardAndClose()
    {
        // Release player and delete the recorded file
        _mediaPlayer?.Pause();
        VideoPlayer.SetMediaPlayer(null);
        _mediaPlayer?.Dispose();
        _mediaPlayer = null;

        try { File.Delete(_videoPath); } catch { }

        _didCallback = true;
        OnDiscarded?.Invoke();
        Close();
    }

    // MARK: - Media Foundation Trim

    private static async Task TrimVideoAsync(string inputPath, string outputPath, TimeSpan start, TimeSpan end)
    {
        var inputFile = await StorageFile.GetFileFromPathAsync(inputPath);
        var clip = await MediaClip.CreateFromFileAsync(inputFile);

        clip.TrimTimeFromStart = start;
        var trimFromEnd = clip.OriginalDuration - end;
        if (trimFromEnd > TimeSpan.Zero)
            clip.TrimTimeFromEnd = trimFromEnd;

        var composition = new MediaComposition();
        composition.Clips.Add(clip);

        var outputDir = Path.GetDirectoryName(outputPath)!;
        var outputFolder = await StorageFolder.GetFolderFromPathAsync(outputDir);
        var outputFile = await outputFolder.CreateFileAsync(
            Path.GetFileName(outputPath),
            CreationCollisionOption.ReplaceExisting);

        var result = await composition.RenderToFileAsync(outputFile, MediaTrimmingPreference.Precise);
        if (result != TranscodeFailureReason.None)
            throw new InvalidOperationException($"Transcode failed: {result}");
    }

    // MARK: - Keyboard Shortcuts

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_isRendering) return;

        var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(
            Windows.System.VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

        if (ctrl && e.Key == Windows.System.VirtualKey.S)
        {
            _ = SaveAndClose();
            e.Handled = true;
            return;
        }

        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            DiscardAndClose();
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Windows.System.VirtualKey.Space:
                TogglePlayPause();
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.I:
                OnSetStartClick(this, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.O:
                OnSetEndClick(this, new RoutedEventArgs());
                e.Handled = true;
                break;
        }
    }

    // MARK: - Cleanup

    private void OnWindowClosed(object sender, WindowEventArgs e)
    {
        _positionTimer?.Stop();
        _positionTimer = null;

        _mediaPlayer?.Pause();
        VideoPlayer.SetMediaPlayer(null);
        _mediaPlayer?.Dispose();
        _mediaPlayer = null;

        if (!_didCallback)
        {
            try { File.Delete(_videoPath); } catch { }
            OnDiscarded?.Invoke();
        }
    }

    // MARK: - Helpers

    private static string FormatTime(TimeSpan t) => VideoTrimHelper.FormatTime(t);
}
