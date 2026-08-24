# Alpha Branding Installer

Build the self-contained per-user Windows installer from any directory:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\installer\Build-Installer.ps1 -Version 1.0.0.0
```

The output is `artifacts/Alpha.Branding.Setup.exe`. It is a native WPF self-extracting bootstrapper (`WinExe`) containing the complete self-contained `win-x64` publish output.

## Silent & Action1 / CI PowerShell Installer (`Install.ps1`)

For automated environments, CI runners (GitHub Actions), and remote management platforms (Action1 RMM) where typical GUI setup executables cannot run or are undesirable:

```powershell
# Standard local silent install (from source repo or artifacts):
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install.ps1

# Remote Action1 RMM deployment (from GitHub Release asset):
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install.ps1 `
  -DownloadUrl "https://github.com/Deign86/Alpha_Branding/releases/latest/download/Alpha.Branding.Setup.exe" `
  -AllUsers `
  -CreateDesktopShortcut `
  -LogPath "$env:ProgramData\Alpha Premier Realty\install.log"

# Silent uninstallation:
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install.ps1 -Uninstall
```

### Script Capabilities:
- **Zero GUI popups**: Runs completely headless and unattended with clear exit codes (0 for success, 1 on failure).
- **Flexible payload resolution**: Automatically extracts payload from `Alpha.Branding.Setup.exe`, installs from pre-published folders (`-SourceDir`), unzips archives (`-ZipPath`), downloads release binaries directly (`-DownloadUrl`), or builds via `dotnet publish`.
- **System-wide / Action1 Support**: Supports `-AllUsers` / `-System` to install to `%ProgramFiles%` and register in `HKLM`, or per-user `%LOCALAPPDATA%` and `HKCU`.
- **Shortcut & Registry management**: Configures Start Menu and Desktop shortcuts, plus Windows Add/Remove Programs (`UninstallString` and `QuietUninstallString`).
- **Clean uninstallation**: Generates a standalone `Uninstall.ps1` inside the installation folder and supports `Install.ps1 -Uninstall`.
