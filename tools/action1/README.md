# Action1 deployment scripts

The Action1 wrapper and installer are designed for unattended deployment. They do not display installer windows, prompts, or application UI, and they return an exit code for Action1.

The installer downloads the public GitHub Release asset:

`https://github.com/Deign86/Alpha_Branding/releases/latest/download/AlphaBranding-1.6.3.zip`

The release asset must exist and contain the published Windows application output. It may contain a `publish`, `app-files`, or `Alpha.Branding` folder, or published files at the archive root. The archive must include the application EXE and supporting files.

Use `Run-AlphaPremierBranding-Install.ps1` in Action1's PowerShell Run Script action. Do not add a top-level `param()` block because Action1 prepends preference assignments. The script installs to `C:\Program Files\Alpha Premier Branding`, creates common Desktop and Start Menu shortcuts, registers Add/Remove Programs, and creates a silent uninstaller. Logs are written to `C:\ProgramData\Alpha Premier Branding\action1-install.log`.

The application itself is not launched during installation. The application and bootstrapper are not used by this deployment path, avoiding interactive WPF UI.
