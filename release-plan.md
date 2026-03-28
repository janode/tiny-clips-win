# TinyClips Windows — Release Plan

## Now (v0.1.0 quality)

- [ ] Video recording produces a usable MP4 (currently depends on ffmpeg or falls back to raw PNGs)
- [ ] Window capture mode actually works (picker shows Region/Screen/Window but capture is region-only)
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
