## Alpha Premier Property Branding Studio v1.6.1

Native .NET 8 WPF desktop application with a self-contained win-x64 installer.

### Fixes & Improvements
- **Preserve Video Aspect Ratio & Resolution**: Fixed portrait video smushing and landscape distortion by tailoring the output encoding profile to the source dimensions and orientation metadata.
- **Dynamic Aspect Ratio Watermarking**: Dynamically adapts watermark overlays across landscape (16:9 / 4:3) and portrait (9:16) video canvases to prevent element distortion or stretching.
- **Fixed Preview Ghosting / Double Cropping**: Fixed `PreviewWindow` viewport visibility where the static thumbnail image was rendering behind the active `MediaElement` player.
- **Added Automated Test Coverage**: Added tests for adapted overlay creation and aspect-ratio validation.

### Installation
Download `Alpha.Branding.Setup.exe` to install per-user without administrator access.
Run with `--uninstall` to remove the application and its shortcuts.
