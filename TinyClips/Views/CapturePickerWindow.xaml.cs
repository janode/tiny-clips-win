using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using TinyClips.Helpers;
using TinyClips.Models;
using WinRT.Interop;

namespace TinyClips.Views;

/// <summary>
/// Floating pill-shaped picker for choosing capture mode (Region/Screen/Window)
/// with a countdown timer toggle. Mirrors the macOS CapturePickerPanel UX.
/// </summary>
public sealed partial class CapturePickerWindow : Window
{
    private readonly CaptureType _captureType;
    private bool _countdownEnabled;
    private int _countdownDuration;
    private bool _didComplete;

    public Action<CapturePickerMode, bool, int>? OnCapture { get; set; }
    public Action? OnCancelled { get; set; }

    public CapturePickerWindow(CaptureType captureType, bool countdownEnabled, int countdownDuration)
    {
        this.InitializeComponent();

        _captureType = captureType;
        _countdownEnabled = countdownEnabled;
        _countdownDuration = countdownDuration;

        // Set up window: borderless, topmost, compact
        var hwnd = WindowNative.GetWindowHandle(this);
        ConfigureWindowStyle(hwnd);
        UpdateModeDisplay();
        UpdateCountdownLabel();

        // Register keyboard shortcuts
        this.Content.KeyDown += OnKeyDown;

        // Size and position immediately using Win32 DPI (before Activate)
        double scale = NativeMethods.GetDpiForWindow(hwnd) / 96.0;
        int w = (int)(700 * scale);
        int h = (int)(68 * scale);
        AppWindow.Resize(new Windows.Graphics.SizeInt32(w, h));

        var settings = CaptureSettings.Instance;
        if (settings.PickerPositionX.HasValue && settings.PickerPositionY.HasValue)
            AppWindow.Move(new Windows.Graphics.PointInt32(settings.PickerPositionX.Value, settings.PickerPositionY.Value));
        else
            CenterAtTopOfScreen(hwnd, scale, w);

        Closed += OnWindowClosed;
    }

    private void ConfigureWindowStyle(nint hwnd)
    {
        // Hide title bar and border for floating pill look
        ExtendsContentIntoTitleBar = true;
        // Set a zero-height title bar so the drag region doesn't steal content space
        this.SetTitleBar(new Microsoft.UI.Xaml.Shapes.Rectangle { Height = 0 });
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
        }

        // Make it a tool window (no taskbar entry) and topmost
        var exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE,
            exStyle | NativeMethods.WS_EX_TOOLWINDOW);

        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST,
            0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE);
    }

    private void CenterAtTopOfScreen(nint hwnd, double scale, int w)
    {
        var monitorRect = NativeMethods.GetMonitorRectAtCursor();
        int x = monitorRect.Left + (monitorRect.Width - w) / 2;
        int y = monitorRect.Top + (int)(60 * scale);
        this.AppWindow.Move(new Windows.Graphics.PointInt32(x, y));
    }

    private void UpdateModeDisplay()
    {
        switch (_captureType)
        {
            case CaptureType.Screenshot:
                ModeIcon.Glyph = "\uE722"; // Camera
                ModeLabel.Text = "Screenshot";
                break;
            case CaptureType.Video:
                ModeIcon.Glyph = "\uE714"; // Video
                ModeLabel.Text = "Video";
                break;
            case CaptureType.Gif:
                ModeIcon.Glyph = "\uEB9F"; // Photo stack
                ModeLabel.Text = "GIF";
                break;
        }
    }

    private void UpdateCountdownLabel()
    {
        CountdownLabel.Text = _countdownEnabled ? $"{_countdownDuration}s" : "Off";
    }

    // MARK: - Button handlers

    private void OnRegionClick(object sender, RoutedEventArgs e)
        => FinishCapture(CapturePickerMode.Region);

    private void OnScreenClick(object sender, RoutedEventArgs e)
        => FinishCapture(CapturePickerMode.Screen);

    private void OnWindowClick(object sender, RoutedEventArgs e)
        => FinishCapture(CapturePickerMode.Window);

    private void OnCancelClick(object sender, RoutedEventArgs e)
        => FinishCancel();

    // MARK: - Countdown handlers

    private void OnCountdownOff(object sender, RoutedEventArgs e)
    {
        _countdownEnabled = false;
        UpdateCountdownLabel();
    }

    private void OnCountdown1(object sender, RoutedEventArgs e) => SetCountdown(1);
    private void OnCountdown2(object sender, RoutedEventArgs e) => SetCountdown(2);
    private void OnCountdown3(object sender, RoutedEventArgs e) => SetCountdown(3);
    private void OnCountdown5(object sender, RoutedEventArgs e) => SetCountdown(5);
    private void OnCountdown10(object sender, RoutedEventArgs e) => SetCountdown(10);

    private void SetCountdown(int seconds)
    {
        _countdownEnabled = true;
        _countdownDuration = seconds;
        UpdateCountdownLabel();
    }

    // MARK: - Keyboard

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case Windows.System.VirtualKey.R:
                FinishCapture(CapturePickerMode.Region);
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.S:
                FinishCapture(CapturePickerMode.Screen);
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.W:
                FinishCapture(CapturePickerMode.Window);
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.Escape:
                FinishCancel();
                e.Handled = true;
                break;
        }
    }

    // MARK: - Completion

    private void SavePosition()
    {
        var pos = AppWindow.Position;
        var settings = CaptureSettings.Instance;
        settings.PickerPositionX = pos.X;
        settings.PickerPositionY = pos.Y;
        settings.Save();
    }

    private void OnWindowClosed(object sender, WindowEventArgs e)
    {
        SavePosition();
    }

    private void FinishCapture(CapturePickerMode mode)
    {
        if (_didComplete) return;
        _didComplete = true;
        this.Close();
        OnCapture?.Invoke(mode, _countdownEnabled, _countdownDuration);
        OnCapture = null;
        OnCancelled = null;
    }

    private void FinishCancel()
    {
        if (_didComplete) return;
        _didComplete = true;
        this.Close();
        OnCancelled?.Invoke();
        OnCapture = null;
        OnCancelled = null;
    }
}
