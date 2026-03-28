# TinyClips Windows — Project Guidelines

TinyClips Windows is a Windows 11 system-tray app for screen capture (screenshots, video, GIF). It targets **Windows 10 19041+** (optimized for Windows 11), uses **C# / .NET 10** with **WinUI 3** (Windows App SDK 1.8), and ships as an unpackaged self-contained executable for direct distribution.

## Architecture

- **System tray app** using `H.NotifyIcon.WinUI` — no taskbar window by default. A hidden `Window` is created and immediately hidden via `ShowWindow(SW_HIDE)` + `WS_EX_TOOLWINDOW` to keep WinUI 3 alive without a visible window.
- **WinUI 3**: WinUI `Window` subclasses for capture-time panels (picker, countdown, start, stop, region selector, settings). Fluent Design with Mica/Acrylic backdrops where supported.
- **`CaptureManager`** in `CaptureManager.cs` is the central coordinator owning recorders, writers, and capture-time window lifecycles. Mirrors the macOS `CaptureManager` from `TinyClipsApp.swift`.
- **Singleton services**: `CaptureSettings.Instance`, `SaveService.Instance`, `NotificationService.Instance`, `HotKeyManager` (per-CaptureManager instance).
- **Unpackaged deployment** (`WindowsPackageType=None`) — self-contained with `WindowsAppSDKSelfContained=true`. No MSIX packaging required for development.
- **Single-instance** enforcement via named `Mutex("TinyClips_SingleInstance_Mutex")`.

## Code Style

- Use `CaptureSettings` singleton with JSON persistence at `%LocalAppData%\TinyClips\settings.json` — analogous to macOS `@AppStorage`.
- Use `// MARK: -` comments for section organization within files (same convention as macOS codebase).
- Keep P/Invoke declarations centralized in `Helpers/NativeMethods.cs` as `LibraryImport` partial methods. Always specify explicit `EntryPoint` for functions with A/W variants (e.g., `EntryPoint = "DefWindowProcW"`, `EntryPoint = "GetMonitorInfoW"`) — ARM64 Windows may not have the undecorated entry points.
- Use `partial class` for any class containing `[LibraryImport]` source-generated methods.
- All capture classes (`ScreenshotCapture`, `VideoRecorder`, `GifWriter`) use async methods (`Task`-returning) for start/stop operations.
- Use `Action<T>?` properties (not events) for callbacks between capture classes and `CaptureManager` (e.g., `OnElapsedChanged`, `OnStart`, `OnCancelled`).

## Build and Test

```powershell
# Build using the build script (required — works around XAML compiler Unicode path bug)
.\build.ps1 -Platform arm64 -Configuration Debug

# Build and run
.\build.ps1 -Platform arm64 -Configuration Debug -Run

# Build for x64
.\build.ps1 -Platform x64 -Configuration Release
```

The build script copies the project to `C:\BuildTest` before building because the WinUI 3 XAML compiler (`XamlCompiler.exe`, net472) crashes silently when source files are under a path containing non-ASCII characters (e.g., `Ø` in the username). This is a known toolchain workaround, not a project issue.

No test project exists yet.

## Project Conventions

### Window Pattern
Use WinUI 3 `Window` subclasses for all windows. Capture-time windows (picker, countdown, start/stop) are configured as always-on-top, borderless floating panels.

For callback-driven windows, keep `Action?` callback properties (`OnStart`, `OnStop`, `OnCancelled`, `OnCapture`). Guard against double-callbacks with boolean flags where needed. `CaptureManager` nulls out window references after close/dismiss.

### Floating Panel Recipe
Floating capture panels (`StopRecordingWindow`, `StartRecordingWindow`, `CapturePickerWindow`, `CountdownWindow`) use:
- `ExtendsContentIntoTitleBar = true` (hides default title bar)
- Win32 `SetWindowPos(HWND_TOPMOST, ...)` for always-on-top
- `WS_EX_TOOLWINDOW` to hide from taskbar/Alt+Tab
- `presenter.SetBorderAndTitleBar(false, false)` via `OverlappedPresenter` for borderless look

Keyboard-interactive picker panels install keyboard handlers for shortcut keys (`R`/`S`/`W` for mode selection, `Esc` for cancel).

### Window Lifecycle
`CaptureManager` holds strong references to capture-time windows and `Close()`s them on dismiss paths with `try/catch` guards. Window references are set to `null` after closing. Dismissal patterns always call `DismissPicker()` / `DismissStopPanel()` before creating replacements.

Hidden main window lifecycle:
1. Create `Window` → get HWND via `WindowNative.GetWindowHandle()`
2. `Activate()` first (WinUI 3 requires this or it may exit)
3. `ShowWindow(SW_HIDE)` + `WS_EX_TOOLWINDOW` to hide completely

### Capture Flows
1. **Screenshot:** `CapturePickerWindow` (region/screen/window + countdown toggle) → optional `RegionSelectorWindow` for region mode → optional `CountdownWindow` → `ScreenshotCapture.CaptureRegionAsync()` → `SaveService.HandleSavedFile()` → reopen picker.
2. **Video:** `CapturePickerWindow` → region selection → `StartRecordingWindow` → optional countdown → `VideoRecorder.StartAsync()` → `StopRecordingWindow` → `VideoRecorder.StopAsync()` → save.
3. **GIF:** `CapturePickerWindow` → region selection → `StartRecordingWindow` → optional countdown → `GifWriter.StartAsync()` → `StopRecordingWindow` → `GifWriter.StopAsync()` → save.

### Region Selector
- Static async entry: `await RegionSelectorWindow.SelectRegionAsync()` returns `CaptureRegion?`.
- Creates a fullscreen transparent Win32 window (not WinUI) via `RegisterClassExW`/`CreateWindowExW` at screen-saver level, with crosshair cursor.
- Minimum selection: 10×10 pixels. Renders crosshairs, selection rectangle, and dimension text via GDI (`CreatePen`, `SelectObject`, `Rectangle`, `DrawText`).
- `CaptureRegion` is a record with `ScreenRect` property (System.Drawing.Rectangle) and static factory `FullScreenAtCursor()`.

### P/Invoke Safety (ARM64)
- **Always use explicit `EntryPoint` for Win32 functions that have A/W variants**: `DefWindowProcW`, `GetMonitorInfoW`, `FindWindowW`, `RegisterClassExW`, `CreateWindowExW`, `DrawTextW`, etc.
- `LibraryImport` without `StringMarshalling` does **not** auto-append W — the function name is used literally.
- `DllImport` with `CharSet = CharSet.Unicode` **does** auto-append W, but prefer explicit entry points for clarity.
- All P/Invoke struct parameters use `nint` for pointer-sized values (not `int` or `IntPtr`).

### Capture Engines
- **ScreenshotCapture**: GDI+ `Graphics.CopyFromScreen()` (BitBlt) — simple, reliable, no special API permissions needed. Saves as PNG or JPEG based on settings.
- **VideoRecorder**: GDI+ frame capture on a background thread at configured FPS → `System.Drawing.Bitmap` frames → writes H.264 MP4 (placeholder; MediaFoundation encoder planned).
- **GifWriter**: GDI+ frame capture at configured FPS → `SixLabors.ImageSharp` GIF encoder with frame accumulation and quantization.

### Error Handling
Errors are surfaced via `NotificationService.Instance.ShowErrorNotification()` which posts a Windows toast notification. Capture methods use try/catch with user-facing error messages. `CaptureManager` always resets `IsRecording` state on error paths.

### File Naming
Output: `TinyClips {date} at {time}.{ext}` by default. Template tokens: `{app}`, `{type}`, `{date}` (yyyy-MM-dd), `{time}` (HH.mm.ss), `{datetime}` (yyyy-MM-dd_HH.mm.ss). Duplicate files get ` (2)`, ` (3)`, etc. suffixes. Invalid filename characters are replaced with `-`.

### Keyboard Shortcuts
Screenshot `Ctrl+Alt+Shift+5`, Video `Ctrl+Alt+Shift+6`, GIF `Ctrl+Alt+Shift+7`, Stop `Ctrl+.`. Picker shortcuts: Region `R`, Screen `S`, Window `W`, Cancel `Esc`. All global hotkeys use Win32 `RegisterHotKey`/`UnregisterHotKey` via a hidden message-only window.

Custom hotkeys are stored as Win32 virtual key codes (`int Vk`) and modifier flags (`int Mod`) in `CaptureSettings`.

### Notifications & Clipboard
- Post-save notifications via Windows toast notifications (`AppNotificationManager` / `ToastNotificationManager`).
- Clipboard: screenshots as bitmap via `DataPackage`, video/GIF as file paths.
- All inter-component communication uses **Action closures/callbacks**, no event bus or message passing.

### Settings View
- `SettingsWindow` with WinUI 3 `NavigationView` sidebar.
- Tabs: General, Screenshot, Video, GIF, Shortcuts, About.
- `Form`-style layout with `StackPanel` groups, proper spacing.
- Settings changes call `CaptureSettings.Instance.Save()` immediately.
- Shortcut tab uses custom `ShortcutRecorderControl` for capturing key combinations.

### Dispatcher & Threading
- `App.Current.MainDispatcherQueue` stores the UI thread's `DispatcherQueue` for cross-thread UI updates.
- Use `MainDispatcherQueue.TryEnqueue()` to marshal callbacks from capture threads to UI thread.
- Capture engines run on background threads (`Task.Run`) and communicate elapsed time via `Action<TimeSpan>` callbacks.

## Project Structure

```
TinyClips/
├── App.xaml / App.xaml.cs          — Entry point, tray icon, hidden window
├── CaptureManager.cs               — Central capture flow coordinator
├── Assets/
│   └── TinyClips.ico               — App icon
├── Capture/
│   ├── CaptureHelper.cs            — CaptureRegion, CaptureType, CapturePickerMode
│   ├── GifWriter.cs                — GIF recording engine (ImageSharp)
│   ├── ScreenshotCapture.cs        — Screenshot capture (GDI+)
│   └── VideoRecorder.cs            — Video recording engine
├── Helpers/
│   ├── ClipboardHelper.cs          — Clipboard operations
│   └── NativeMethods.cs            — Centralized Win32 P/Invoke declarations
├── Models/
│   └── CaptureSettings.cs          — Settings singleton with JSON persistence
├── Services/
│   ├── HotKeyManager.cs            — Global hotkey registration (Win32)
│   ├── LaunchAtLoginManager.cs     — Startup registration
│   ├── NotificationService.cs      — Windows toast notifications
│   ├── PermissionManager.cs        — Capture capability checks
│   └── SaveService.cs              — File naming, post-save actions
└── Views/
    ├── CapturePickerWindow.xaml/.cs — Mode selection pill (Region/Screen/Window)
    ├── CountdownWindow.xaml/.cs     — 3-2-1 countdown overlay
    ├── RegionSelectorWindow.cs      — Fullscreen region selection (Win32, no XAML)
    ├── SettingsWindow.xaml/.cs       — Settings with NavigationView sidebar
    ├── StartRecordingWindow.xaml/.cs — Pre-recording Start button pill
    └── StopRecordingWindow.xaml/.cs  — Recording timer + Stop button pill
```

## Security

- App runs as standard user, no elevation required.
- Screen capture uses GDI+ `CopyFromScreen` — works without special permissions on Windows.
- Global hotkeys registered per-session via `RegisterHotKey` (no admin needed).
- Settings stored in user-scoped `%LocalAppData%\TinyClips\`.
- No network access, no telemetry, no external service calls.
- P/Invoke declarations use `LibraryImport` source generator (compile-time verified, no runtime delegate marshaling).

## Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| Microsoft.WindowsAppSDK | 1.8.260317003 | WinUI 3 framework |
| H.NotifyIcon.WinUI | 2.4.1 | System tray icon |
| SixLabors.ImageSharp | 3.1.12 | GIF encoding |
