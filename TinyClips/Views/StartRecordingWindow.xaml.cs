using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using TinyClips.Helpers;
using TinyClips.Models;
using Windows.System;
using WinRT.Interop;

namespace TinyClips.Views;

/// <summary>
/// Floating panel shown before recording starts. User clicks Start to begin.
/// Enter to start, Esc to cancel.
/// </summary>
public sealed partial class StartRecordingWindow : Window
{
    private bool _didComplete;

    public Action? OnStart { get; set; }
    public Action? OnCancelled { get; set; }

    public StartRecordingWindow(CaptureType captureType)
    {
        InitializeComponent();

        ModeLabel.Text = captureType == CaptureType.Gif ? "Record GIF" : "Record Video";
        ModeIcon.Glyph = captureType == CaptureType.Gif ? "\uF4A9" : "\uE714";

        ConfigureWindow();
        Content.KeyDown += OnKeyDown;
        Closed += OnWindowClosed;
    }

    private void ConfigureWindow()
    {
        var hwnd = WindowNative.GetWindowHandle(this);

        var exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE,
            exStyle | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_TOPMOST);

        ExtendsContentIntoTitleBar = true;
        this.SetTitleBar(new Microsoft.UI.Xaml.Shapes.Rectangle { Height = 0 });
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMinimizable = false;
            presenter.IsMaximizable = false;
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsAlwaysOnTop = true;
        }

        // DPI-aware sizing immediately (before Activate)
        double scale = NativeMethods.GetDpiForWindow(hwnd) / 96.0;
        int w = (int)(320 * scale);
        int h = (int)(52 * scale);
        AppWindow.Resize(new Windows.Graphics.SizeInt32(w, h));

        // Position at the same location as the capture picker for visual continuity
        var settings = CaptureSettings.Instance;
        if (settings.PickerPositionX.HasValue && settings.PickerPositionY.HasValue)
        {
            // Center horizontally relative to where the picker was (picker is wider)
            int pickerWidth = (int)(630 * scale);
            int cx = settings.PickerPositionX.Value + (pickerWidth - w) / 2;
            int cy = settings.PickerPositionY.Value;
            AppWindow.Move(new Windows.Graphics.PointInt32(cx, cy));
        }
        else
        {
            var area = DisplayArea.Primary;
            if (area != null)
            {
                int cx = area.WorkArea.X + (area.WorkArea.Width - w) / 2;
                int cy = area.WorkArea.Y + (int)(60 * scale);
                AppWindow.Move(new Windows.Graphics.PointInt32(cx, cy));
            }
        }
    }

    private void OnStartClick(object sender, RoutedEventArgs e) => FinishStart();

    private void OnCancelClick(object sender, RoutedEventArgs e) => FinishCancel();

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Enter:
                FinishStart();
                e.Handled = true;
                break;
            case VirtualKey.Escape:
                FinishCancel();
                e.Handled = true;
                break;
        }
    }

    private void FinishStart()
    {
        if (_didComplete) return;
        _didComplete = true;
        OnStart?.Invoke();
        Close();
    }

    private void FinishCancel()
    {
        if (_didComplete) return;
        _didComplete = true;
        OnCancelled?.Invoke();
        Close();
    }

    private void OnWindowClosed(object sender, WindowEventArgs e)
    {
        if (!_didComplete)
        {
            OnCancelled?.Invoke();
        }
        OnStart = null;
        OnCancelled = null;
    }
}
