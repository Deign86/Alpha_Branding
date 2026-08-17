## Alpha Premier Realty Branding Studio v1.2.0

Native .NET 8 WPF desktop application with a self-contained win-x64 installer.

### Changes
- Improved image processing throughput by generating previews directly from encoded JPEG bytes.
- Fixed cancellation token handling so batch operations cancel immediately when requested.
- Hardened crash logging to use local application data directory with fallback support.
- Hardened installer registry registration, process guards, and shortcut management.
- Refined portrait photo detection and side-by-side pairing logic.

### Installation
Download `Alpha.Branding.Setup.exe` to install per-user without administrator access.
Run with `--uninstall` to remove the application and its shortcuts.
