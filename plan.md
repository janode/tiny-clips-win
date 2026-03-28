# TinyClips Windows — Implementation Plan

> Port TinyClips to Windows 11 as a polished system-tray screen capture app.
> **Stack:** WinUI 3 / C# / .NET 10 / Windows App SDK 1.8

---

## Phase 1: Project Scaffold & System Tray *(Foundation)*

- [x] ~~Create WinUI 3 project targeting .NET 10 with packaged + unpackaged build configs~~
- [x] ~~Remove default MainWindow — launch headless to tray only, no taskbar entry~~
- [x] ~~Add `H.NotifyIcon.WinUI` — context menu: Screenshot…, Record Video…, Record GIF…, divider, Settings…, Quit~~
- [x] ~~Tray icon toggles tooltip to "Recording…" during capture~~
- [x] ~~Single-instance enforcement via named `Mutex`~~
- [x] ~~`CaptureSettings` singleton — all macOS `@AppStorage` keys mirrored, backed by JSON in `%LocalAppData%\TinyClips\`~~
- [x] ~~`build.ps1` build script with clean/restore/publish and ASCII-path workaround for XAML compiler bug~~
- [x] ~~`global.json` pinning .NET 10 SDK~~
- [x] ~~ARM64 P/Invoke audit — all Win32 functions use explicit `W` entry points~~

## Phase 2: Screen Capture Engine

- [x] ~~`ScreenshotCapture` — GDI+ `Graphics.CopyFromScreen` BitBlt, region crop, PNG/JPEG save~~
- [x] ~~`CaptureHelper` — shared `CaptureRegion` model, `FullScreenAtCursor()`, multi-monitor `MONITORINFO` rect lookup~~
- [x] ~~`VideoRecorder` — `Graphics.CopyFromScreen` frame loop → `MFSinkWriter` H.264 MP4 pipeline with start/stop~~
- [x] ~~`GifWriter` — capture frames at configured FPS → accumulate bitmaps → `SixLabors.ImageSharp` GIF encoder~~
- [ ] System audio — WASAPI loopback capture into MFSinkWriter audio input *(defer to v1.1 if it blocks polish)*
- [ ] Functional test: screenshot produces correct file at configured location
- [ ] Functional test: video records MP4 with timer, plays in Media Player
- [ ] Functional test: GIF records and plays in browser

## Phase 3: Capture UI — Picker, Region, Floating Panels

- [x] ~~`CapturePickerWindow` — floating always-on-top pill with Region / Screen / Window + countdown toggle. Keyboard: R / S / W / Esc~~
- [x] ~~`RegionSelectorWindow` — fullscreen transparent borderless Win32 window per monitor, crosshair cursor, drag selection (min 10×10 px), Esc cancels~~
- [ ] Screen Picker — multi-monitor selection UI (skip if single monitor; currently falls back to cursor's display)
- [x] ~~`CountdownWindow` — centered overlay with animated 3…2…1 countdown~~
- [x] ~~`StartRecordingWindow` — compact floating pill with Start + countdown toggle~~
- [x] ~~`StopRecordingWindow` — floating pill: red pulse dot + monospaced timer + Stop button, movable, always-on-top~~
- [ ] Region indicator overlay — dashed border shown during region-mode recording
- [ ] Polish: all panels respect DPI scaling at 100%, 125%, 150%, 200%
- [ ] Polish: panels remember last position across sessions
- [ ] Polish: smooth show/hide animations using Storyboard or Composition API

## Phase 4: Save, Clipboard, Notifications

- [x] ~~`SaveService` — configurable directory, file name template with `{date}`, `{time}`, `{type}` tokens, dedup~~
- [x] ~~`ClipboardHelper` — screenshot as bitmap via `DataPackage`, video/GIF as file path~~
- [x] ~~`NotificationService` — toast notifications (post-save), error notifications~~
- [ ] Show in File Explorer — `explorer.exe /select,"path"`
- [ ] Functional test: clipboard paste works (bitmap for screenshots, file for video/GIF)
- [ ] Functional test: toast notifications appear and click opens containing folder

## Phase 5: Global Hotkeys

- [x] ~~`HotKeyManager` — Win32 `RegisterHotKey` / `UnregisterHotKey` P/Invoke with message-only HWND~~
- [x] ~~Default hotkeys: Ctrl+Alt+Shift+5/6/7 (screenshot/video/GIF), stop hotkey during recording~~
- [ ] Custom shortcut recorder field in settings with conflict detection
- [ ] Functional test: hotkeys trigger from any foreground app

## Phase 6: Settings Window

- [x] ~~`SettingsWindow` with WinUI 3 `NavigationView` sidebar~~
- [x] ~~General tab: save dir, file name template, show in Explorer, toast, launch at startup~~
- [x] ~~Screenshot tab: PNG/JPEG, quality slider, scale, countdown~~
- [x] ~~Video tab: FPS (24/30/60), system audio toggle, countdown~~
- [x] ~~GIF tab: FPS slider, max width, countdown~~
- [x] ~~Shortcuts tab: current hotkey display~~
- [x] ~~About tab: version, links~~
- [x] ~~`LaunchAtLoginManager` — registry key `CurrentUser\Run`~~
- [ ] Mica backdrop on Settings window
- [ ] File name template live preview
- [ ] Save directory folder picker dialog
- [ ] Shortcut recorder — editable hotkey fields with conflict detection
- [ ] Polish: all form controls have proper spacing, grouping, and pixel-perfect alignment

## Phase 7: Onboarding

- [ ] `OnboardingWindow` — first-run wizard: Welcome → Permissions → Key Settings → Ready
- [ ] Shortcut reference card on final page
- [ ] `HasCompletedOnboarding` setting — show once on first launch only
- [ ] Polish: Fluent Design illustrations + smooth page transitions

## Phase 8: Distribution & Packaging

- [ ] MSIX packaging for Microsoft Store submission
- [x] ~~Unpackaged self-contained build for GitHub Releases / direct download~~
- [x] ~~GitHub Actions CI: build x64+arm64, Debug+Release matrix on push/PR to main~~
- [x] ~~GitHub Actions Release: tag-triggered workflow creates GitHub Release with x64+arm64 zips~~
- [x] ~~GitHub Pages site with install guide, features, and keyboard shortcuts~~
- [ ] Auto-update checker for direct builds (GitHub Releases version compare)
- [x] ~~App icon — `.ico` with 16/24/32/48/64/256 px layers~~
- [ ] Installer / MSIX signing

---

## Deferred to v1.1+

| Feature | Notes |
|---|---|
| Screenshot editor | Crop, annotate, blur — post-capture editing |
| Video trimmer | Trim start/end before save |
| GIF trimmer | Trim + resize before save |
| Clips Manager | Browse, tag, organize, search past captures (Pro feature on macOS) |
| Subscriptions / Pro tier | Monetization, feature gating |
| Uploadcare integration | Cloud upload with user's own API keys |
| Microphone recording | Separate audio input mixed into video |
| Window capture | True window-only capture via `GraphicsCapturePicker` (currently falls back to fullscreen) |
| System audio in video | WASAPI loopback capture |
| ~~Custom tray icon~~ | ~~Done — multi-size `.ico` with 16–256 px layers~~ |
| Sparkle-equivalent auto-update | For direct-distribution builds |

---

## Key Decisions

| Decision | Rationale |
|---|---|
| .NET 10 RTM + WinUI 3 | Current LTS-track, Fluent Design built-in, Mica/Acrylic native |
| SixLabors.ImageSharp for GIF | Pure .NET, ~100 KB dependency vs ~80 MB for bundled ffmpeg |
| GDI+ BitBlt for screenshots | Simple, reliable, no COM interop needed |
| MFSinkWriter for video | Performant native encoder, no external binaries |
| H.NotifyIcon.WinUI for tray | Most maintained WinUI 3 tray icon library |
| JSON settings in LocalAppData | Simple, human-readable, no registry pollution |
| ASCII build path workaround | WindowsAppSDK XAML compiler crashes on non-ASCII paths |
