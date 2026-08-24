## Alpha Premier Property Branding Studio v1.6.3

Native .NET 8 WPF desktop application with a self-contained win-x64 installer.

### Changes
- **Drag-and-Drop & File Selection Protection**: Dropping a new batch of files onto the upload section or selecting new media while active session results exist now immediately prompts the user with the session confirmation dialog (Cancel, Discard & Continue, or Save & Continue), streamlining the UI/UX.
- **Fixed Confirmation Dialog Sizing**: Increased dialog width to 640px and adjusted action button layout to ensure no button text or actions are cut off across all display resolutions and DPI scales.
- **Safe Application Exit & Modal Collision Guard**: Fixed window closing handling to prevent re-entrant closing exceptions when exiting while a modal popup is on screen or active.

### Installation
Download `Alpha.Branding.Setup.exe` to install per-user without administrator access.
Run with `--uninstall` to remove the application and its shortcuts.
