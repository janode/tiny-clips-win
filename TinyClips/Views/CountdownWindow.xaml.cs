using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Animation;
using TinyClips.Helpers;
using WinRT.Interop;

namespace TinyClips.Views;

/// <summary>
/// Floating countdown overlay: shows 3…2…1 before capture starts.
/// Each number scales in and fades out with smooth animation.
/// </summary>
public sealed partial class CountdownWindow : Window
{
    private readonly int _seconds;
    private int _remaining;
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

        // Start the animated countdown once the content is loaded
        CountdownContainer.Loaded += (_, _) => _ = RunAnimatedCountdown();
    }

    private async Task RunAnimatedCountdown()
    {
        while (_remaining > 0)
        {
            CountdownText.Text = _remaining.ToString();
            await AnimateNumber();
            _remaining--;
        }

        Close();
        _tcs.TrySetResult();
    }

    /// <summary>
    /// Animates a single number: scale from 0.5→1.0 + fade in over the first 250ms,
    /// then hold briefly, then scale 1.0→1.15 + fade out over the last 200ms.
    /// Total: ~900ms per number (leaves 100ms gap at 1s intervals).
    /// </summary>
    private async Task AnimateNumber()
    {
        var transform = (Microsoft.UI.Xaml.Media.CompositeTransform)CountdownContainer.RenderTransform;
        var easeIn = new CircleEase { EasingMode = EasingMode.EaseOut };

        // Phase 1: Scale in + fade in (250ms)
        var appearStoryboard = new Storyboard();

        var fadeIn = new DoubleAnimation { From = 0, To = 1, Duration = new Duration(TimeSpan.FromMilliseconds(250)), EasingFunction = easeIn };
        Storyboard.SetTarget(fadeIn, CountdownContainer);
        Storyboard.SetTargetProperty(fadeIn, "Opacity");
        appearStoryboard.Children.Add(fadeIn);

        var scaleInX = new DoubleAnimation { From = 0.5, To = 1.0, Duration = new Duration(TimeSpan.FromMilliseconds(250)), EasingFunction = easeIn };
        Storyboard.SetTarget(scaleInX, transform);
        Storyboard.SetTargetProperty(scaleInX, "ScaleX");
        appearStoryboard.Children.Add(scaleInX);

        var scaleInY = new DoubleAnimation { From = 0.5, To = 1.0, Duration = new Duration(TimeSpan.FromMilliseconds(250)), EasingFunction = easeIn };
        Storyboard.SetTarget(scaleInY, transform);
        Storyboard.SetTargetProperty(scaleInY, "ScaleY");
        appearStoryboard.Children.Add(scaleInY);

        var tcs1 = new TaskCompletionSource();
        appearStoryboard.Completed += (_, _) => tcs1.TrySetResult();
        appearStoryboard.Begin();
        await tcs1.Task;

        // Phase 2: Hold (450ms)
        await Task.Delay(450);

        // Phase 3: Scale out + fade out (200ms)
        var easeOut = new CircleEase { EasingMode = EasingMode.EaseIn };

        var dismissStoryboard = new Storyboard();

        var fadeOut = new DoubleAnimation { To = 0, Duration = new Duration(TimeSpan.FromMilliseconds(200)), EasingFunction = easeOut };
        Storyboard.SetTarget(fadeOut, CountdownContainer);
        Storyboard.SetTargetProperty(fadeOut, "Opacity");
        dismissStoryboard.Children.Add(fadeOut);

        var scaleOutX = new DoubleAnimation { To = 1.15, Duration = new Duration(TimeSpan.FromMilliseconds(200)), EasingFunction = easeOut };
        Storyboard.SetTarget(scaleOutX, transform);
        Storyboard.SetTargetProperty(scaleOutX, "ScaleX");
        dismissStoryboard.Children.Add(scaleOutX);

        var scaleOutY = new DoubleAnimation { To = 1.15, Duration = new Duration(TimeSpan.FromMilliseconds(200)), EasingFunction = easeOut };
        Storyboard.SetTarget(scaleOutY, transform);
        Storyboard.SetTargetProperty(scaleOutY, "ScaleY");
        dismissStoryboard.Children.Add(scaleOutY);

        var tcs2 = new TaskCompletionSource();
        dismissStoryboard.Completed += (_, _) => tcs2.TrySetResult();
        dismissStoryboard.Begin();
        await tcs2.Task;
    }
}
