# TinyClips Windows — Release Plan

## Now (v0.1.0 quality)

- [x] Video recording produces a usable MP4 (Media Foundation H.264 SinkWriter — no external deps)
- [x] Window capture mode actually works (WindowSelectorWindow overlay with click-to-select)
- [ ] Test a clean install from release zip on a machine without the SDK
- [ ] Handle multi-monitor mixed-DPI (floating panels position/size correctly across displays)

## Soon (before public sharing)

- [ ] Code signing certificate (required for winget, avoids SmartScreen warnings)
- [ ] Auto-update check (toast notification linking to GitHub Releases when new version available)
- [ ] Error handling polish (capture failures clean up temp files, always reset state)
- [ ] Settings validation (save path exists, GIF framerate reasonable, etc.)

## Later (nice to have)

- [ ] winget submission
- [ ] Inno Setup installer
- [ ] Annotation/editing after screenshot
- [ ] Sound recording with video
- [ ] Cursor capture toggle
- [ ] Custom output resolution for video/GIF
- [ ] Localization
