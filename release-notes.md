## Alpha Premier Realty Branding Studio v1.4.0

Native .NET 8 WPF desktop application with a self-contained win-x64 installer.

### Changes
- Added dirty-state tracking to protect edited property photo batches from accidental loss.
- Added modal confirmation workflow when applying branding over an active session with unsaved edits ("Save & Continue", "Discard Edits & Continue", "Cancel").
- Added dynamic pre-apply session status hint and amber warning indicator.
- Added session protection when closing the application.
- Added comprehensive unit tests and FlaUI UI automation coverage.

### Installation
Download `Alpha.Branding.Setup.exe` to install per-user without administrator access.
Run with `--uninstall` to remove the application and its shortcuts.
