# Alpha Branding Installer

Build the self-contained per-user Windows installer from any directory:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\installer\Build-Installer.ps1 -Version 1.0.0.0
```

The exact output is `artifacts/Alpha.Branding.Setup.exe`. It is a native self-extracting C# bootstrapper containing the complete self-contained `win-x64` publish output. It installs per-user under `%LOCALAPPDATA%\Alpha Premier Realty\Branding Studio`, requires no administrator access, certificates, or MSIX, and creates a Start Menu shortcut plus an Apps & Features uninstall entry in HKCU.

Uninstall from Apps & Features, or run the installed `Alpha.Branding.Setup.exe --uninstall`. Uninstall refuses while the application is running, then removes the shortcut, registry entry, and installed files after the setup process exits.
