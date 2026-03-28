using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using TinyClips.Helpers;
using WinRT.Interop;

namespace TinyClips.Views;

/// <summary>
/// Floating countdown overlay: shows 3…2…1 before capture starts.
/// Matches macOS CountdownWindow: 120×120 circle with large number.
/// </summary>
public sealed partial class CountdownWindow : Window
{
    private readonly int _seconds;
    private int _remaining;
    private DispatcherTimer? _timer;
    private readonly TaskCompletionSource _tcs = new();

    public CountdownWindow(int seconds)
    {
        InitializeComponent();
        _seconds = Math.Max(1, seconds);
        _remaining = _seconds;
        CountdownText.Text = _remaining.ToString();
        ConfigureWindow();
    }

    /// <summary>
    /// Show countdown and wait for it to finish. Returns when done.
    /// </summary>
    public static async Task RunCountdownAsync(int seconds)
    {
        var window = new CountdownWindow(seconds);
        window.Activate();
        await window._tcs.Task;
    }

    private void ConfigureWindow()
    {
        var hwnd = WindowNative.GetWindowHandle(this);

        // Remove title bar, make tool window
        var exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE,
            exStyle | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_TOPMOST);

        // Use OverlappedPresenter for borderless look
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
        int size = (int)(160 * scale);
        AppWindow.Resize(new Windows.Graphics.SizeInt32(size, size));

        // Center on primary monitor
        var area = DisplayArea.Primary;
        if (area != null)
        {
            int x = area.WorkArea.X + (area.WorkArea.Width - size) / 2;
            int y = area.WorkArea.Y + (area.WorkArea.Height - size) / 2;
            AppWindow.Move(new Windows.Graphics.PointInt32(x, y));
        }

        // Start ticking
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private void OnTick(object? sender, object e)
    {
        _remaining--;

        if (_remaining <= 0)
        {
            _timer?.Stop();
            Close();
            _tcs.TrySetResult();
            return;
        }

        CountdownText.Text = _remaining.ToString();
    }
}
