using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Animation;
using TinyClips.Helpers;
using WinRT.Interop;

namespace TinyClips.Views;

/// <summary>
/// Floating panel during recording with a pulsing red dot, timer, and Stop button.
/// Matches macOS StopRecordingPanel design.
/// </summary>
public sealed partial class StopRecordingWindow : Window
{
    private bool _didComplete;
    private DispatcherTimer? _timer;
    private DateTime _startTime;
    private Storyboard? _pulseAnimation;

    public Action? OnStop { get; set; }

    public StopRecordingWindow()
    {
        InitializeComponent();
        ConfigureWindow();
        CreatePulseAnimation();
        Closed += OnWindowClosed;
    }

    private void CreatePulseAnimation()
    {
        var animation = new DoubleAnimation
        {
            From = 1.0,
            To = 0.3,
            Duration = new Duration(TimeSpan.FromSeconds(0.8)),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        };
        Storyboard.SetTarget(animation, RecordingDot);
        Storyboard.SetTargetProperty(animation, "Opacity");

        _pulseAnimation = new Storyboard();
        _pulseAnimation.Children.Add(animation);
    }

    public void StartTimer()
    {
        _startTime = DateTime.Now;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += OnTimerTick;
        _timer.Start();

        _pulseAnimation?.Begin();
    }

    public void UpdateElapsed(TimeSpan elapsed)
    {
        TimerText.Text = elapsed.ToString(@"mm\:ss");
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
        int w = (int)(280 * scale);
        int h = (int)(52 * scale);
        AppWindow.Resize(new Windows.Graphics.SizeInt32(w, h));

        // Position near top-right of primary display
        var area = DisplayArea.Primary;
        if (area != null)
        {
            int px = area.WorkArea.X + area.WorkArea.Width - w - (int)(20 * scale);
            int py = area.WorkArea.Y + (int)(20 * scale);
            AppWindow.Move(new Windows.Graphics.PointInt32(px, py));
        }
    }

    private void OnTimerTick(object? sender, object e)
    {
        var elapsed = DateTime.Now - _startTime;
        UpdateElapsed(elapsed);
    }

    private void OnStopClick(object sender, RoutedEventArgs e) => FinishStop();

    public void FinishStop()
    {
        if (_didComplete) return;
        _didComplete = true;
        _timer?.Stop();
        _pulseAnimation?.Stop();
        OnStop?.Invoke();
        Close();
    }

    private void OnWindowClosed(object sender, WindowEventArgs e)
    {
        _timer?.Stop();
        if (!_didComplete)
        {
            OnStop?.Invoke();
        }
        OnStop = null;
    }
}
