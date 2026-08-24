## Alpha Premier Property Branding Studio v1.6.3

Native .NET 8 WPF desktop application with a self-contained win-x64 installer.

### Changes
- **Destructive Action Confirmation Popups**: Protected all destructive workflows with popup confirmation dialogs. Starting a new branding session when active items exist in the current session now always prompts the user to either save (ZIP export), discard, or cancel.
- **Application Exit Guard**: Closing the application with unsaved edits or active branded results now prompts the user to save before exiting.
- **Visual Session Warnings**: Dynamic session indicators warn the user when applying branding will overwrite or replace active session media.

### Installation
Download `Alpha.Branding.Setup.exe` to install per-user without administrator access.
Run with `--uninstall` to remove the application and its shortcuts.
