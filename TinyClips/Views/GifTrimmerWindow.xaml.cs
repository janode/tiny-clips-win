using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using TinyClips.Helpers;
using TinyClips.Models;
using TinyClips.Services;
using WinRT.Interop;
using ISImage = SixLabors.ImageSharp.Image;

namespace TinyClips.Views;

/// <summary>
/// Post-capture GIF trimmer. Loads the recorded GIF, plays back frames,
/// and lets the user trim the frame range and resize before saving.
/// Uses SixLabors.ImageSharp for frame access and re-encoding.
/// </summary>
public sealed partial class GifTrimmerWindow : Window
{
    // MARK: - State

    private readonly string _gifPath;
    private Image<Rgba32>? _gif;
    private int _frameCount;
    private int _originalWidth;
    private int _originalHeight;
    private long _originalFileSize;

    private int _startFrame = 1;  // 1-based for UI
    private int _endFrame = 1;
    private int _outputWidth;
    private int _currentPreviewFrame;
    private bool _isPlaying = true;
    private bool _isLoadingFrame;
    private bool _isUpdating;
    private bool _didCallback;
    private bool _isRendering;

    private DispatcherTimer? _previewTimer;

    // Callbacks
    public Action<string>? OnSaved;
    public Action? OnDiscarded;

    // MARK: - Construction

    public GifTrimmerWindow(string gifPath)
    {
        _gifPath = gifPath;
        InitializeComponent();
        Title = "TinyClips — Trim GIF";

        Closed += OnWindowClosed;
        ConfigureWindowSize();
        _ = LoadGifAsync();
    }

    private bool HasChanges =>
        _startFrame > 1 ||
        _endFrame < _frameCount ||
        _outputWidth != _originalWidth;

    // MARK: - Window Configuration

    private void ConfigureWindowSize()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        double dpiScale = NativeMethods.GetDpiForWindow(hwnd) / 96.0;

        var monitor = NativeMethods.GetMonitorRectAtCursor();
        int maxW = (int)(monitor.Width * 0.8);
        int maxH = (int)(monitor.Height * 0.8);

        int w = Math.Min((int)(800 * dpiScale), maxW);
        int h = Math.Min((int)(650 * dpiScale), maxH);

        AppWindow.Resize(new Windows.Graphics.SizeInt32(w, h));
        AppWindow.Move(new Windows.Graphics.PointInt32(
            monitor.Left + (monitor.Width - w) / 2,
            monitor.Top + (monitor.Height - h) / 2));
    }

    private async Task LoadGifAsync()
    {
        try
        {
            _originalFileSize = new FileInfo(_gifPath).Length;

            _gif = await Task.Run(() => ISImage.Load<Rgba32>(_gifPath));
            _frameCount = _gif.Frames.Count;
            _originalWidth = _gif.Width;
            _originalHeight = _gif.Height;
            _outputWidth = _originalWidth;
            _startFrame = 1;
            _endFrame = _frameCount;
            _currentPreviewFrame = 1;

            // Configure UI controls
            _isUpdating = true;
            StartFrameBox.Maximum = _frameCount;
            StartFrameBox.Value = 1;
            EndFrameBox.Maximum = _frameCount;
            EndFrameBox.Value = _frameCount;
            OutputWidthBox.Value = _originalWidth;
            ScrubSlider.Maximum = _frameCount;
            ScrubSlider.Value = 1;
            _isUpdating = false;

            UpdateInfoDisplay();
            UpdateRangeBar();

            // Display first frame
            await DisplayFrameAsync(1);

            // Start preview animation
            int frameDelay = 100; // default 10fps
            if (_gif.Frames.Count > 0)
            {
                var gifMeta = _gif.Frames.RootFrame.Metadata.GetGifMetadata();
                if (gifMeta.FrameDelay > 0)
                    frameDelay = gifMeta.FrameDelay * 10; // centiseconds → milliseconds
            }
            frameDelay = Math.Max(30, frameDelay); // minimum 30ms

            _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(frameDelay) };
            _previewTimer.Tick += OnPreviewTimerTick;
            _previewTimer.Start();

            // Register keyboard shortcuts
            Content.KeyDown += OnKeyDown;
        }
        catch (Exception ex)
        {
            NotificationService.Instance.ShowErrorNotification($"Failed to load GIF: {ex.Message}");
        }
    }

    // MARK: - Preview Playback

    private void OnPlayPauseClick(object sender, RoutedEventArgs e)
    {
        TogglePlayPause();
    }

    private void TogglePlayPause()
    {
        _isPlaying = !_isPlaying;
        PlayPauseIcon.Glyph = _isPlaying ? "\uE769" : "\uE768"; // Pause : Play
    }

    private async void OnPreviewTimerTick(object? sender, object e)
    {
        if (!_isPlaying || _isLoadingFrame || _gif == null || _isRendering) return;

        _currentPreviewFrame++;
        if (_currentPreviewFrame > _endFrame)
            _currentPreviewFrame = _startFrame;

        _isUpdating = true;
        ScrubSlider.Value = _currentPreviewFrame;
        _isUpdating = false;

        await DisplayFrameAsync(_currentPreviewFrame);
        FrameLabel.Text = $"Frame {_currentPreviewFrame} / {_frameCount}";
    }

    private async Task DisplayFrameAsync(int frameIndex)
    {
        if (_gif == null || frameIndex < 1 || frameIndex > _gif.Frames.Count) return;

        _isLoadingFrame = true;
        try
        {
            // Render frame to PNG bytes on background thread
            byte[] pngBytes = await Task.Run(() =>
            {
                using var frameImage = _gif.Frames.CloneFrame(frameIndex - 1); // 0-based
                using var ms = new MemoryStream();
                frameImage.SaveAsPng(ms);
                return ms.ToArray();
            });

            // Create BitmapImage on UI thread from the byte array
            var ras = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            using (var writer = ras.AsStreamForWrite())
            {
                await writer.WriteAsync(pngBytes, 0, pngBytes.Length);
                await writer.FlushAsync();
            }
            ras.Seek(0);
            var bi = new BitmapImage();
            bi.SetSource(ras);
            GifPreviewImage.Source = bi;
        }
        catch { }
        finally
        {
            _isLoadingFrame = false;
        }
    }

    private void OnScrubSliderChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_isUpdating || _gif == null) return;
        int frame = (int)ScrubSlider.Value;
        if (frame < 1) frame = 1;
        if (frame > _frameCount) frame = _frameCount;
        _currentPreviewFrame = frame;
        FrameLabel.Text = $"Frame {frame} / {_frameCount}";
        _ = DisplayFrameAsync(frame);
    }

    // MARK: - Frame Range

    private void OnStartFrameChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_isUpdating || _gif == null) return;
        int val = (int)StartFrameBox.Value;
        if (val < 1) val = 1;
        if (val >= _endFrame) val = _endFrame - 1;
        if (val < 1) val = 1;
        _startFrame = val;
        if (_currentPreviewFrame < _startFrame)
            _currentPreviewFrame = _startFrame;
        UpdateInfoDisplay();
        UpdateRangeBar();
    }

    private void OnEndFrameChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_isUpdating || _gif == null) return;
        int val = (int)EndFrameBox.Value;
        if (val > _frameCount) val = _frameCount;
        if (val <= _startFrame) val = _startFrame + 1;
        if (val > _frameCount) val = _frameCount;
        _endFrame = val;
        if (_currentPreviewFrame > _endFrame)
            _currentPreviewFrame = _endFrame;
        UpdateInfoDisplay();
        UpdateRangeBar();
    }

    // MARK: - Resize

    private void OnOutputWidthChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_isUpdating || _gif == null) return;
        int val = (int)OutputWidthBox.Value;
        if (val < 16) val = 16;
        // Ensure even dimensions
        val = val & ~1;
        if (val < 2) val = 2;
        _outputWidth = val;
        UpdateInfoDisplay();
    }

    // MARK: - UI Updates

    private void OnRangeContainerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateRangeBar();
    }

    private void UpdateRangeBar()
    {
        if (_frameCount <= 0) return;
        double width = RangeContainer.ActualWidth;
        if (width <= 0) return;

        double startFrac = (_startFrame - 1.0) / _frameCount;
        double endFrac = (double)_endFrame / _frameCount;

        RangeBar.Margin = new Thickness(width * startFrac, 0, 0, 0);
        RangeBar.Width = Math.Max(0, width * (endFrac - startFrac));
    }

    private void UpdateInfoDisplay()
    {
        int selectedFrames = _endFrame - _startFrame + 1;
        FrameCountLabel.Text = $"{selectedFrames} of {_frameCount} frames";

        int outH = _originalWidth > 0
            ? (int)Math.Round((double)_originalHeight / _originalWidth * _outputWidth)
            : _originalHeight;
        outH = Math.Max(1, outH & ~1);
        if (outH < 2) outH = 2;

        DimensionsLabel.Text = $"→ {_outputWidth} × {outH}  (original: {_originalWidth} × {_originalHeight})";

        // Rough file size estimate
        double ratio = (double)selectedFrames / _frameCount;
        double scaleRatio = _originalWidth > 0
            ? (double)(_outputWidth * outH) / (_originalWidth * _originalHeight)
            : 1.0;
        long estimated = (long)(_originalFileSize * ratio * Math.Sqrt(scaleRatio));
        InfoLabel.Text = $"{selectedFrames} frames · ~{FormatFileSize(estimated)} estimated";
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
        if (_isRendering || _gif == null) return;

        try
        {
            if (HasChanges)
            {
                _isRendering = true;
                _previewTimer?.Stop();
                RenderOverlay.Visibility = Visibility.Visible;
                SaveButton.IsEnabled = false;

                var tempPath = _gifPath + $".trim_{Guid.NewGuid():N}.gif";
                await TrimGifAsync(tempPath);

                // Dispose the loaded GIF before file operations
                _gif.Dispose();
                _gif = null;

                File.Delete(_gifPath);
                File.Move(tempPath, _gifPath);
            }
            else
            {
                _gif.Dispose();
                _gif = null;
            }

            _didCallback = true;
            OnSaved?.Invoke(_gifPath);
            Close();
        }
        catch (Exception ex)
        {
            _isRendering = false;
            RenderOverlay.Visibility = Visibility.Collapsed;
            SaveButton.IsEnabled = true;
            NotificationService.Instance.ShowErrorNotification($"GIF trim failed: {ex.Message}");
        }
    }

    private void DiscardAndClose()
    {
        _gif?.Dispose();
        _gif = null;

        try { File.Delete(_gifPath); } catch { }

        _didCallback = true;
        OnDiscarded?.Invoke();
        Close();
    }

    // MARK: - GIF Encoding

    private async Task TrimGifAsync(string outputPath)
    {
        var gif = _gif!;
        int startIdx = _startFrame - 1; // Convert to 0-based
        int endIdx = _endFrame - 1;
        int outW = _outputWidth;
        int srcW = _originalWidth;
        int srcH = _originalHeight;
        int outH = srcW > 0
            ? (int)Math.Round((double)srcH / srcW * outW)
            : srcH;
        outH = Math.Max(2, outH & ~1);
        if (outW < 2) outW = 2;
        bool needsResize = outW != srcW || outH != srcH;

        await Task.Run(() =>
        {
            // Build output from first selected frame
            using var firstFrame = gif.Frames.CloneFrame(startIdx);
            if (needsResize)
                firstFrame.Mutate(x => x.Resize(outW, outH));

            firstFrame.Metadata.GetGifMetadata().RepeatCount = 0; // Loop forever
            firstFrame.Frames.RootFrame.Metadata.GetGifMetadata().FrameDelay =
                gif.Frames[startIdx].Metadata.GetGifMetadata().FrameDelay;

            // Append remaining selected frames
            for (int i = startIdx + 1; i <= endIdx; i++)
            {
                using var frame = gif.Frames.CloneFrame(i);
                if (needsResize)
                    frame.Mutate(x => x.Resize(outW, outH));

                var added = firstFrame.Frames.AddFrame(frame.Frames.RootFrame);
                added.Metadata.GetGifMetadata().FrameDelay =
                    gif.Frames[i].Metadata.GetGifMetadata().FrameDelay;
            }

            var dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            firstFrame.SaveAsGif(outputPath);
        });
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

        if (e.Key == Windows.System.VirtualKey.Space)
        {
            TogglePlayPause();
            e.Handled = true;
        }
    }

    // MARK: - Cleanup

    private void OnWindowClosed(object sender, WindowEventArgs e)
    {
        _previewTimer?.Stop();
        _previewTimer = null;

        _gif?.Dispose();
        _gif = null;

        if (!_didCallback)
        {
            try { File.Delete(_gifPath); } catch { }
            OnDiscarded?.Invoke();
        }
    }

    // MARK: - Helpers

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F1} MB";
    }
}
