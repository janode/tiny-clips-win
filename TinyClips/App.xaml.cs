using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using H.NotifyIcon;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Runtime.InteropServices;
using System.Windows.Input;
using TinyClips.Helpers;
using TinyClips.Models;
using TinyClips.Services;
using WinRT.Interop;

namespace TinyClips;

public partial class App : Application
{
    private TaskbarIcon? _trayIcon;
    private CaptureManager? _captureManager;
    private static Mutex? _singleInstanceMutex;
    private Uri? _normalIconUri;
    private Uri? _recordingIconUri;

    // Hidden main window required by WinUI 3 — never shown.
    private Window? _hiddenWindow;

    public static new App Current => (App)Application.Current;
    public CaptureManager CaptureManager => _captureManager!;
    public nint HiddenWindowHandle { get; private set; }
    public Microsoft.UI.Dispatching.DispatcherQueue? MainDispatcherQueue { get; private set; }

    public App()
    {
        this.InitializeComponent();
        this.UnhandledException += OnUnhandledException;
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        var msg = $"UNHANDLED: {e.Exception?.GetType().Name}: {e.Exception?.Message}\n{e.Exception?.StackTrace}";
        System.IO.File.WriteAllText(
            System.IO.Path.Combine(AppContext.BaseDirectory, "crash.log"), msg);
        e.Handled = false;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            OnLaunchedCore(args);
        }
        catch (Exception ex)
        {
            var msg = $"LAUNCH CRASH: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}";
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(AppContext.BaseDirectory, "crash.log"), msg);
            throw;
        }
    }

    private void OnLaunchedCore(LaunchActivatedEventArgs args)
    {
        // Single-instance enforcement
        _singleInstanceMutex = new Mutex(true, "TinyClips_SingleInstance_Mutex", out bool createdNew);
        if (!createdNew)
        {
            Environment.Exit(0);
            return;
        }

        // Create the hidden window (WinUI 3 requires at least one window)
        _hiddenWindow = new Window
        {
            Title = "TinyClips"
        };

        // Get the HWND before Activate() so we can hide it pre-emptively
        HiddenWindowHandle = WindowNative.GetWindowHandle(_hiddenWindow);
        MainDispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

        // Make invisible BEFORE Activate(): apply WS_EX_TOOLWINDOW (hides from
        // taskbar/Alt+Tab) and move to 0×0 size off-screen so nothing flashes.
        var exStyle = NativeMethods.GetWindowLongPtr(HiddenWindowHandle, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLongPtr(HiddenWindowHandle, NativeMethods.GWL_EXSTYLE,
            exStyle | NativeMethods.WS_EX_TOOLWINDOW);
        NativeMethods.SetWindowPos(HiddenWindowHandle, nint.Zero,
            -32000, -32000, 0, 0, NativeMethods.SWP_NOACTIVATE);

        // Activate so WinUI considers the app "alive", then immediately hide
        _hiddenWindow.Activate();
        NativeMethods.ShowWindow(HiddenWindowHandle, NativeMethods.SW_HIDE);

        // Startup diagnostics
        LogStartupInfo();
        CheckDeployment();

        // Initialize services
        _captureManager = new CaptureManager();

        // Create the system tray icon
        SetupTrayIcon();

        // Show onboarding if first run
        if (!CaptureSettings.Instance.HasCompletedOnboarding)
        {
            _captureManager.ShowOnboarding();
        }

        // Check for updates in the background (fire-and-forget)
        _ = UpdateChecker.CheckForUpdateAsync();
    }

    private void SetupTrayIcon()
    {
        var contextMenu = new MenuFlyout();
        contextMenu.AreOpenCloseAnimationsEnabled = false;
        var presenterStyle = new Style(typeof(MenuFlyoutPresenter));
        presenterStyle.Setters.Add(new Setter(MenuFlyoutPresenter.MaxHeightProperty, 800.0));
        presenterStyle.Setters.Add(new Setter(MenuFlyoutPresenter.PaddingProperty, new Thickness(0, 4, 0, 4)));
        contextMenu.MenuFlyoutPresenterStyle = presenterStyle;

        // H.NotifyIcon PopupMenu mode runs Command handlers on a background thread
        // (the tray icon's internal message loop). WinUI windows must be created on
        // the UI thread, so dispatch all actions through MainDispatcherQueue.
        void DispatchToUI(Action action) => MainDispatcherQueue!.TryEnqueue(() => action());

        var screenshotItem = new MenuFlyoutItem
        {
            Text = "Screenshot…",
            Icon = new FontIcon { Glyph = "\uE722" },
            Command = new RelayCommand(() => DispatchToUI(() => _captureManager?.TakeScreenshot())),
            MinWidth = 200
        };

        var videoItem = new MenuFlyoutItem
        {
            Text = "Record Video…",
            Icon = new FontIcon { Glyph = "\uE714" },
            Command = new RelayCommand(() => DispatchToUI(() => _captureManager?.StartVideoRecording())),
            MinWidth = 200
        };

        var gifItem = new MenuFlyoutItem
        {
            Text = "Record GIF…",
            Icon = new FontIcon { Glyph = "\uEB9F" },
            Command = new RelayCommand(() => DispatchToUI(() => _captureManager?.StartGifRecording())),
            MinWidth = 200
        };

        var separator1 = new MenuFlyoutSeparator();

        var settingsItem = new MenuFlyoutItem
        {
            Text = "Settings…",
            Icon = new FontIcon { Glyph = "\uE713" },
            Command = new RelayCommand(() => DispatchToUI(() => _captureManager?.ShowSettings())),
            MinWidth = 200
        };

        var separator2 = new MenuFlyoutSeparator();

        var quitItem = new MenuFlyoutItem
        {
            Text = "Quit TinyClips",
            Icon = new FontIcon { Glyph = "\uE7E8" },
            Command = new RelayCommand(() => DispatchToUI(Quit)),
            MinWidth = 200
        };

        contextMenu.Items.Add(screenshotItem);
        contextMenu.Items.Add(videoItem);
        contextMenu.Items.Add(gifItem);
        contextMenu.Items.Add(separator1);
        contextMenu.Items.Add(settingsItem);
        contextMenu.Items.Add(separator2);
        contextMenu.Items.Add(quitItem);

        _normalIconUri = new Uri(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "TinyClips.ico"));
        _recordingIconUri = CreateRecordingIcon();
        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "TinyClips — Screen Capture",
            ContextMenuMode = ContextMenuMode.PopupMenu,
            NoLeftClickDelay = true,
            IconSource = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(_normalIconUri)
        };
        _trayIcon.ContextFlyout = contextMenu;
        _trayIcon.ForceCreate();
    }

    public void UpdateTrayIconForRecording(bool isRecording)
    {
        if (_trayIcon == null) return;

        _trayIcon.ToolTipText = isRecording
            ? "TinyClips — Recording…"
            : "TinyClips — Screen Capture";

        var uri = isRecording ? _recordingIconUri : _normalIconUri;
        if (uri != null)
            _trayIcon.IconSource = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(uri);
    }

    /// <summary>
    /// Generates a recording icon by overlaying a red dot on the normal tray icon.
    /// Returns a file:// URI pointing to the generated icon in a temp location.
    /// </summary>
    private Uri? CreateRecordingIcon()
    {
        try
        {
            var icoPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "TinyClips.ico");
            using var icon = new Icon(icoPath, 32, 32);
            using var bmp = icon.ToBitmap();
            using var g = Graphics.FromImage(bmp);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            int dotSize = bmp.Width / 3;
            int x = bmp.Width - dotSize - 1;
            int y = bmp.Height - dotSize - 1;

            // White outline for visibility
            using var outline = new SolidBrush(Color.White);
            g.FillEllipse(outline, x - 1, y - 1, dotSize + 2, dotSize + 2);

            // Red dot
            using var red = new SolidBrush(Color.FromArgb(255, 59, 48));
            g.FillEllipse(red, x, y, dotSize, dotSize);

            var tempPath = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "TinyClips_recording.ico");

            // Save as .ico via converting back from bitmap
            using var stream = new System.IO.FileStream(tempPath, System.IO.FileMode.Create);
            using var recordingIcon = System.Drawing.Icon.FromHandle(bmp.GetHicon());
            recordingIcon.Save(stream);

            return new Uri(tempPath);
        }
        catch
        {
            return _normalIconUri;
        }
    }

    private static void LogStartupInfo()
    {
        var asm = typeof(App).Assembly.GetName();
        var ver = asm.Version?.ToString(3) ?? "?";
        var os = Environment.OSVersion.Version;
        var arch = RuntimeInformation.ProcessArchitecture;
        var dotnet = Environment.Version;
        AppLog.Info($"TinyClips {ver} | .NET {dotnet} | {arch} | Windows {os}");
    }

    private static void CheckDeployment()
    {
        var baseDir = AppContext.BaseDirectory;
        var criticalFiles = new[]
        {
            System.IO.Path.Combine(baseDir, "TinyClips.pri"),
            System.IO.Path.Combine(baseDir, "Microsoft.UI.Xaml.Controls.pri"),
            System.IO.Path.Combine(baseDir, "Microsoft.WindowsAppRuntime.pri"),
        };

        foreach (var file in criticalFiles)
        {
            if (!System.IO.File.Exists(file))
                AppLog.Error($"MISSING deployment file: {file} — XAML windows will fail to load");
        }

        AppLog.Info($"BaseDirectory: {baseDir}");
    }

    public void Quit()
    {
        _captureManager?.Dispose();
        _trayIcon?.Dispose();
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        Environment.Exit(0);
    }
}

file class RelayCommand(Action execute) : ICommand
{
#pragma warning disable CS0067
    public event EventHandler? CanExecuteChanged;
#pragma warning restore CS0067
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => execute();
}
