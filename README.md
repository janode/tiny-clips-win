# TinyClips for Windows

A polished Windows 11 system-tray app for screen capture — screenshots, video (MP4), and GIF. Built with WinUI 3, .NET 10, and the Windows App SDK.

> Inspired by [TinyClips for Mac](https://github.com/jamesmontemagno/tiny-clips-mac) by James Montemagno.

## Features

- **Screenshot** — capture a region, full screen, or window → PNG/JPEG
- **Video** — record region, screen, or window → H.264 MP4 with optional system audio
- **GIF** — record region, screen, or window → animated GIF with configurable FPS and max width
- **System tray** — runs entirely from the notification area, no taskbar window
- **Global hotkeys** — trigger captures from any app (default: Ctrl+Alt+Shift+5/6/7)
- **Customizable save** — configurable directory, file name template with token presets
- **Clipboard** — screenshots copied as bitmap, video/GIF as file path
- **Toast notifications** — post-save notification, click to open in Explorer
- **Settings** — full WinUI 3 settings window with Mica backdrop, NavigationView sidebar
- **First-run onboarding** — permission check + setup wizard

## Requirements

| Requirement | Version |
|---|---|
| OS | Windows 10 1903+ (targeting Windows 11) |
| .NET SDK | 10.0.201+ |
| Windows App SDK | 1.8+ (bundled via NuGet) |

Install the .NET 10 SDK from https://dotnet.microsoft.com/download/dotnet/10.0

Verify with:

```powershell
dotnet --version
# Should print 10.0.201 or later
```

## Project Structure

```
tiny-clips-windows/
├── build.ps1                  # Build script (workaround for XAML compiler Unicode path bug)
├── global.json                # Pins .NET SDK version to 10.0.x
├── TinyClips.sln              # Solution file
├── TinyClips/
│   ├── TinyClips.csproj       # Project — WinUI 3, .NET 10, unpackaged
│   ├── App.xaml / .cs         # Entry point, tray setup, hidden window
│   ├── CaptureManager.cs      # Central coordinator for all capture flows
│   ├── Assets/                # App icon (.ico)
│   ├── Capture/
│   │   ├── ScreenshotCapture.cs   # GDI+ BitBlt screenshot
│   │   ├── VideoRecorder.cs       # Graphics Capture → MFSinkWriter MP4
│   │   ├── GifWriter.cs           # Graphics Capture → ImageSharp GIF
│   │   └── CaptureHelper.cs       # Shared types (CaptureRegion, CaptureMode)
│   ├── Helpers/
│   │   ├── NativeMethods.cs       # Win32 P/Invoke declarations
│   │   ├── ClipboardHelper.cs     # Clipboard operations
│   │   └── WindowHelper.cs        # WinUI window interop utilities
│   ├── Models/
│   │   └── CaptureSettings.cs     # All user preferences, persisted to JSON
│   ├── Services/
│   │   ├── HotKeyManager.cs       # Global hotkeys via RegisterHotKey
│   │   ├── SaveService.cs         # File naming, save, open-in-Explorer
│   │   ├── PermissionManager.cs   # Screen capture permission check
│   │   ├── NotificationService.cs # Windows toast notifications
│   │   └── LaunchAtLoginManager.cs# Startup registration
│   └── Views/
│       ├── CapturePickerWindow.xaml/.cs    # Mode selection pill (Region/Screen/Window)
│       ├── RegionSelectorWindow.cs         # Full-screen crosshair overlay (Win32)
│       ├── CountdownWindow.xaml/.cs        # 3-2-1 countdown overlay
│       ├── StartRecordingWindow.xaml/.cs   # Pre-recording Start pill
│       ├── StopRecordingWindow.xaml/.cs    # Recording timer + Stop pill
│       └── SettingsWindow.xaml/.cs         # Full settings UI
├── plan.md                    # Implementation roadmap
└── spec.md                    # Full PRD with requirements
```

## Build

### Using the build script (recommended)

The WinUI 3 XAML compiler crashes when source files are under a path containing non-ASCII characters (e.g. `Ø`, `å`, `ü`). The build script works around this by copying sources to `C:\BuildTest` before building.

```powershell
# Build for ARM64 (default)
.\build.ps1

# Build for x64
.\build.ps1 -Platform x64

# Release build
.\build.ps1 -Configuration Release

# Build and run
.\build.ps1 -Run

# Combine flags
.\build.ps1 -Platform x64 -Configuration Release -Run
```

The script copies build output back to `TinyClips\bin\<Configuration>\<rid>\` for convenience.

### Manual build (ASCII-only path required)

If your project path contains only ASCII characters, you can build directly:

```powershell
dotnet build TinyClips\TinyClips.csproj -c Debug -r win-arm64
# or
dotnet build TinyClips\TinyClips.csproj -c Debug -r win-x64
```

### Opening in Visual Studio

```powershell
start TinyClips.sln
```

Then select the `TinyClips` project, choose your platform (arm64/x64), and press F5.

## Run

After building, the executable is at:

```
TinyClips\bin\Debug\win-arm64\TinyClips.exe
# or
TinyClips\bin\Debug\win-x64\TinyClips.exe
```

The app launches to the system tray (notification area) with no visible window. Look for the TinyClips icon in the tray.

### Default Keyboard Shortcuts

**Global** (work from any app):

| Action | Shortcut |
|---|---|
| Screenshot | Ctrl+Alt+Shift+5 |
| Record Video | Ctrl+Alt+Shift+6 |
| Record GIF | Ctrl+Alt+Shift+7 |
| Stop Recording | Ctrl+. |

**Capture Picker** (when the picker pill is open):

| Action | Key |
|---|---|
| Region mode | R |
| Screen mode | S |
| Window mode | W |
| Cancel | Esc |

All global shortcuts are customizable in Settings → Shortcuts.

Right-click the tray icon for the context menu: Screenshot, Record Video, Record GIF, Settings, and Quit.

## Settings

Settings are stored as JSON at:

```
%LocalAppData%\TinyClips\settings.json
```

Delete this file to reset all settings to defaults.

## Dependencies

| Package | Version | Purpose |
|---|---|---|
| [Microsoft.WindowsAppSDK](https://www.nuget.org/packages/Microsoft.WindowsAppSDK) | 1.8.260317003 | WinUI 3 framework |
| [H.NotifyIcon.WinUI](https://www.nuget.org/packages/H.NotifyIcon.WinUI) | 2.4.1 | System tray icon |
| [SixLabors.ImageSharp](https://www.nuget.org/packages/SixLabors.ImageSharp) | 3.1.12 | GIF encoding |

## Known Issues

- **XAML compiler Unicode path bug** — The WinUI 3 XAML compiler (`XamlCompiler.exe`, net472) crashes silently when source files are under a path containing non-ASCII characters. Use `build.ps1` which copies to `C:\BuildTest` as a workaround.
- **ARM64 P/Invoke** — All Win32 functions with A/W variants must use explicit `EntryPoint = "...W"` annotations, as ARM64 Windows may not export the undecorated stub.

## License

See [LICENSE](LICENSE) for details.
