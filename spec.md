# TinyClips Windows — Product Requirements

> Complete PRD for TinyClips Windows 11 screen capture app.
> Features are organized by area. Each requirement has a status checkbox.
> Items marked ~~strikethrough~~ are implemented.

---

## 1. App Shell & Lifecycle

### 1.1 System Tray
- [x] ~~App runs as system tray icon with no taskbar window~~
- [x] ~~Context menu: Screenshot…, Record Video…, Record GIF…, separator, Settings…, separator, Quit~~
- [x] ~~Tray tooltip changes to "Recording…" during active capture~~
- [x] ~~Tray icon changes color/style to red during active recording~~
- [x] ~~Custom app icon (`.ico` with 16/32/48/256 px layers)~~

### 1.2 Single Instance
- [x] ~~Only one instance of TinyClips can run at a time (named Mutex)~~
- [x] ~~Second launch exits silently~~

### 1.3 Launch at Login
- [x] ~~`LaunchAtLoginManager` sets/clears `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` registry key~~
- [x] ~~Toggle in Settings → General~~

### 1.4 Persistence
- [x] ~~All settings persisted as JSON in `%LocalAppData%\TinyClips\settings.json`~~
- [x] ~~Settings loaded on launch, saved on every change~~

---

## 2. Screenshot Capture

### 2.1 Capture Modes
- [x] ~~**Region:** user draws a rectangle on screen to capture~~
- [x] ~~**Screen:** captures entire display where cursor is located~~
- [x] ~~**Window:** captures target window via `WindowSelectorWindow` overlay~~

### 2.2 Capture Flow
- [x] ~~User triggers via tray menu or global hotkey~~
- [x] ~~Capture Picker panel appears with Region / Screen / Window buttons~~
- [x] ~~Picker supports keyboard shortcuts: R (Region), S (Screen), W (Window), Esc (Cancel)~~
- [x] ~~Optional countdown before capture (configurable 1–10 seconds)~~
- [x] ~~After capture, file is saved to configured directory~~
- [x] ~~Picker reopens after capture for additional screenshots~~

### 2.3 Region Selector
- [x] ~~Fullscreen transparent overlay window per monitor~~
- [x] ~~Crosshair cursor while selecting~~
- [x] ~~Drag to draw selection rectangle with visible border~~
- [x] ~~Minimum selection: 10×10 pixels~~
- [x] ~~Esc cancels and returns to picker~~
- [x] ~~Selection rectangle shows live dimensions (W×H)~~
- [x] ~~Semi-transparent dark overlay outside selected region~~

### 2.4 Output
- [x] ~~Save as PNG (default) or JPEG~~
- [x] ~~Configurable JPEG quality (0–100, default 85)~~
- [x] ~~Configurable scale factor (%)~~
- [x] ~~Copy to clipboard as bitmap (configurable, default on)~~

### 2.5 Post-Capture
- [x] ~~Toast notification with file name~~
- [x] ~~Click notification to reveal file in Explorer~~
- [x] ~~"Show in Explorer" option (configurable)~~
- [x] ~~Optional screenshot editor (crop, draw, arrow, blur, text) before save~~

---

## 3. Video Recording

### 3.1 Capture Modes
- [x] ~~**Region:** record a screen rectangle~~
- [x] ~~**Screen:** record entire display~~
- [x] ~~**Window:** record target window via `WindowSelectorWindow`~~

### 3.2 Recording Flow
- [x] ~~User triggers via tray menu or global hotkey~~
- [x] ~~Capture Picker panel appears~~
- [x] ~~After mode selection + optional region draw: Start Recording panel appears~~
- [x] ~~Optional countdown before recording begins~~
- [x] ~~Stop Recording panel appears during recording with red pulse dot + elapsed timer + Stop button~~
- [x] ~~Stop via panel button or global hotkey~~

### 3.3 Encoding
- [x] ~~H.264 MP4 via MFSinkWriter~~
- [x] ~~Configurable frame rate: 24, 30, or 60 FPS~~
- [x] ~~System audio capture via WASAPI loopback (`SystemAudioCapture` + NAudio)~~
- [ ] Microphone audio capture (deferred to v1.1)

### 3.4 Output
- [x] ~~Save as `.mp4` to configured directory~~
- [x] ~~Copy file path to clipboard (configurable, default off)~~
- [x] ~~Toast notification with file name~~
- [x] ~~Optional video trimmer (trim start/end) before save~~

---

## 4. GIF Recording

### 4.1 Capture Modes
- [x] ~~**Region:** record a screen rectangle as GIF~~
- [x] ~~**Screen:** record entire display as GIF~~
- [x] ~~**Window:** record target window as GIF via `WindowSelectorWindow`~~

### 4.2 Recording Flow
- [x] ~~Same picker → start → stop flow as video recording~~
- [x] ~~Elapsed timer shown during recording~~

### 4.3 Encoding
- [x] ~~Animated GIF via SixLabors.ImageSharp~~
- [x] ~~Configurable frame rate (1–30 FPS, default 10)~~
- [x] ~~Configurable max width (default 640 px, proportional scaling)~~

### 4.4 Output
- [x] ~~Save as `.gif` to configured directory~~
- [x] ~~Copy file path to clipboard (configurable, default off)~~
- [x] ~~Toast notification with file name~~
- [x] ~~Optional GIF trimmer (trim frames + resize) before save~~

---

## 5. Floating Capture Panels

### 5.1 Capture Picker Panel
- [x] ~~Compact floating always-on-top pill~~
- [x] ~~Three mode buttons: Region, Screen, Window~~
- [x] ~~Countdown toggle with duration selector~~
- [x] ~~Keyboard shortcuts: R, S, W, Esc~~
- [x] ~~Mica/Acrylic backdrop~~
- [x] ~~Remembers last position across sessions~~
- [x] ~~Smooth appear/dismiss animation~~

### 5.2 Start Recording Panel
- [x] ~~Floating pill with Start button~~
- [x] ~~Shows capture type label (Video / GIF)~~
- [x] ~~Cancel button returns to picker~~
- [x] ~~Remembers last position~~

### 5.3 Stop Recording Panel
- [x] ~~Floating always-on-top pill~~
- [x] ~~Red pulse dot animation indicating active recording~~
- [x] ~~Monospaced elapsed timer (MM:SS.f with smooth tenths)~~
- [x] ~~Stop button~~
- [x] ~~Draggable~~
- [x] ~~Remembers last position~~

### 5.4 Countdown Window
- [x] ~~Centered overlay with large countdown number~~
- [x] ~~Counts down from configured duration to 1, then auto-dismisses~~
- [x] ~~Smooth scale/fade animation per number~~

---

## 6. Global Hotkeys

### 6.1 Default Shortcuts
- [x] ~~Screenshot: `Ctrl+Alt+Shift+5`~~
- [x] ~~Record Video: `Ctrl+Alt+Shift+6`~~
- [x] ~~Record GIF: `Ctrl+Alt+Shift+7`~~
- [x] ~~Stop Recording: registered dynamically during active recording~~

### 6.2 Customization
- [x] ~~Custom shortcut recorder field in Settings → Shortcuts~~
- [x] ~~Conflict detection (warns if shortcut is already in use)~~
- [x] ~~Reset to defaults button~~

### 6.3 Implementation
- [x] ~~Win32 `RegisterHotKey` / `UnregisterHotKey` via P/Invoke~~
- [x] ~~Message-only HWND for hotkey message processing~~
- [x] ~~Hotkeys work from any foreground application~~

---

## 7. Settings Window

### 7.1 Layout
- [x] ~~Standalone WinUI 3 Window with `NavigationView` sidebar~~
- [x] ~~Tabs: General, Screenshot, Video, GIF, Shortcuts, About~~
- [x] ~~Mica backdrop~~
- [x] ~~Minimum window size enforced (720×460)~~

### 7.2 General Tab
- [x] ~~Save directory path display~~
- [x] ~~Browse button with `FolderPicker` dialog~~
- [x] ~~File name template with `{date}`, `{time}`, `{type}` tokens~~
- [x] ~~Live preview of generated file name~~
- [x] ~~Show save notifications toggle~~
- [x] ~~Launch at startup toggle~~
- [x] ~~Always capture main display toggle~~

### 7.3 Screenshot Tab
- [x] ~~Format selector: PNG / JPEG~~
- [x] ~~JPEG quality slider (0–100)~~
- [x] ~~Scale factor selector~~
- [x] ~~Countdown toggle + duration~~
- [x] ~~Copy to clipboard toggle~~
- [x] ~~Show screenshot editor toggle~~

### 7.4 Video Tab
- [x] ~~Frame rate selector: 24 / 30 / 60 FPS~~
- [x] ~~System audio toggle~~
- [x] ~~Countdown toggle + duration~~
- [x] ~~Region indicator toggle~~
- [x] ~~Copy to clipboard toggle~~
- [x] ~~Show video trimmer toggle~~

### 7.5 GIF Tab
- [x] ~~Frame rate slider~~
- [x] ~~Max width setting~~
- [x] ~~Countdown toggle + duration~~
- [x] ~~Copy to clipboard toggle~~
- [x] ~~Show GIF trimmer toggle~~

### 7.6 Shortcuts Tab
- [x] ~~Display current hotkey assignments~~
- [x] ~~Editable shortcut recorder fields~~
- [x] ~~Conflict detection~~

### 7.7 About Tab
- [x] ~~App name + version~~
- [x] ~~GitHub / Issues / Privacy links~~
- [x] ~~Custom app icon display~~
- [x] ~~Copy Diagnostic Log button~~
- [x] ~~Open Log Folder button~~

---

## 8. Notifications & Clipboard

### 8.1 Toast Notifications
- [x] ~~Post-save notification with file name~~
- [x] ~~Error notifications for capture failures~~
- [x] ~~Click notification to reveal file in Explorer~~
- [ ] Notification includes thumbnail preview for screenshots

### 8.2 Clipboard
- [x] ~~Screenshot: copy as bitmap (`DataPackage`)~~
- [x] ~~Video / GIF: copy file path as text~~
- [ ] Video / GIF: copy as file reference (`IStorageItem`)

---

## 9. File Management

### 9.1 Naming
- [x] ~~Template-based: `TinyClips {date} at {time}.{ext}`~~
- [x] ~~Token substitution: `{app}`, `{type}`, `{date}`, `{time}`, `{datetime}`~~
- [x] ~~Deduplication: appends ` (2)`, ` (3)`, etc. if file exists~~

### 9.2 Formats
- [x] ~~Screenshot: `.png` or `.jpg`~~
- [x] ~~Video: `.mp4`~~
- [x] ~~GIF: `.gif`~~

### 9.3 Save Location
- [x] ~~Default: user's Desktop~~
- [x] ~~Configurable in Settings~~
- [x] ~~Folder picker dialog~~

---

## 10. Onboarding

- [ ] First-run wizard window appears on initial launch
- [ ] Page 1: Welcome — app name, icon, tagline
- [ ] Page 2: Permissions — verify screen capture access
- [ ] Page 3: Key Settings — choose save location, review hotkeys
- [ ] Page 4: Ready — shortcut reference card, "Get Started" button
- [x] ~~`HasCompletedOnboarding` flag persisted — wizard shows once~~
- [ ] Smooth page transitions with Fluent Design animations

---

## 11. Accessibility

- [x] ~~All buttons and controls have `AutomationProperties.Name`~~
- [ ] Keyboard navigation through all panels and settings
- [ ] High contrast mode support
- [ ] Screen reader (Narrator) support for capture picker, countdown, timer
- [ ] Focus indicators visible on all interactive elements

---

## 12. Multi-Monitor & DPI

- [x] ~~Region selector works on cursor's current display~~
- [ ] Region selector spans all connected displays
- [ ] Correct DPI-aware coordinate mapping
- [ ] Crisp UI at 100%, 125%, 150%, 200% scale factors
- [ ] Test: multi-monitor with mixed DPI settings

---

## 13. Distribution

### 13.1 Direct Distribution
- [x] ~~Unpackaged self-contained `.exe` build~~
- [x] ~~`build.ps1` script builds + publishes~~
- [x] ~~GitHub Actions CI pipeline (build matrix: x64+arm64, Debug+Release)~~
- [x] ~~GitHub Actions Release pipeline (tag-triggered, creates GitHub Release with zips)~~
- [x] ~~GitHub Pages site with install guide, features, and keyboard shortcuts~~
- [x] ~~`UpdateChecker` — checks GitHub Releases API for newer version on launch~~
- [ ] Code signing

### 13.2 Microsoft Store
- [ ] MSIX packaging
- [ ] Store listing metadata (description, screenshots, icon)
- [ ] Store submission

---

## 13.5 Architecture & Diagnostics
- [x] ~~`SettingsViewModel` with `INotifyPropertyChanged` for x:Bind compiled bindings~~
- [x] ~~`ScreenshotEditorViewModel` for tool state and undo tracking~~
- [x] ~~`ISaveService` / `INotificationService` interfaces for testability~~
- [x] ~~`AppLog` file logger (`%LocalAppData%\TinyClips\tinyclips.log`, 1 MB rotation)~~
- [x] ~~All catch blocks log via `AppLog.Error`; lifecycle events via `AppLog.Info`~~
- [x] ~~Copy Diagnostic Log + Open Log Folder buttons in Settings → About~~

---

## 14. Deferred Features (v1.1+)

| # | Feature | Priority | Notes |
|---|---|---|---|
| ~~14.1~~ | ~~Screenshot editor (crop, annotate, blur)~~ | ~~High~~ | ~~Done — crop, draw, arrow, blur, text with undo~~ |
| ~~14.2~~ | ~~Video trimmer (trim start/end)~~ | ~~High~~ | ~~Done — trim with preview before save~~ |
| ~~14.3~~ | ~~GIF trimmer (trim + resize)~~ | ~~Medium~~ | ~~Done — trim frames + resize before save~~ |
| 14.4 | Clips Manager (browse, tag, search) | Medium | Pro feature on macOS |
| ~~14.5~~ | ~~System audio in video (WASAPI loopback)~~ | ~~High~~ | ~~Done — `SystemAudioCapture` via NAudio~~ |
| 14.6 | Microphone audio in video | Medium | Separate `AudioGraph` / WASAPI input |
| ~~14.7~~ | ~~True window capture~~ | ~~Medium~~ | ~~Done — `WindowSelectorWindow` overlay~~ |
| 14.8 | Subscriptions / Pro tier | Low | Monetization gating |
| 14.9 | Uploadcare cloud upload | Low | User brings own API keys |
| 14.10 | Region indicator overlay during recording | Medium | Dashed border around captured area |
| ~~14.11~~ | ~~Auto-update for direct builds~~ | ~~Medium~~ | ~~Done — `UpdateChecker` via GitHub Releases API~~ |

---

## Summary

| Area | Total | Done | Remaining |
|---|---|---|---|
| App Shell & Lifecycle | 9 | 9 | 0 |
| Screenshot Capture | 17 | 17 | 0 |
| Video Recording | 14 | 13 | 1 |
| GIF Recording | 11 | 11 | 0 |
| Floating Panels | 18 | 18 | 0 |
| Global Hotkeys | 7 | 7 | 0 |
| Settings Window | 24 | 24 | 0 |
| Notifications & Clipboard | 7 | 5 | 2 |
| File Management | 9 | 9 | 0 |
| Onboarding | 7 | 1 | 6 |
| Accessibility | 5 | 1 | 4 |
| Multi-Monitor & DPI | 5 | 1 | 4 |
| Distribution | 10 | 6 | 4 |
| Architecture & Diagnostics | 6 | 6 | 0 |
| **Total** | **148** | **127** | **21** |
