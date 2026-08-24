# Action1 deployment scripts

These scripts provide an unattended deployment path for Alpha Premier Branding without launching the interactive WPF bootstrapper.

## Required change

Edit `Install-AlphaPremierBranding.ps1` and replace `$DownloadUrl` with a direct HTTPS URL to a ZIP containing the published Windows application output.

The ZIP should contain either a `publish`, `app-files`, or `Alpha.Branding` directory, or contain the published files at its root. It must include the application EXE and its accompanying DLL, JSON, and asset files.

## Action1 Run Script

Use `Run-AlphaPremierBranding-Install.ps1` as the Action1 PowerShell Run Script. It downloads the installer script from this branch, executes it, propagates the exit code, and removes its temporary copy.

The installer script itself then downloads the application ZIP, copies it to `C:\Program Files\Alpha Premier Branding`, creates common Desktop and Start Menu shortcuts, registers the app under the machine-wide uninstall registry key, and creates an uninstaller.

Do not add a `param()` block to the wrapper because Action1 prepends preference assignments before executing Run Script content. Do not place secrets in these files. For production, pin the raw script URL to a reviewed commit SHA or host it on a controlled HTTPS endpoint.
