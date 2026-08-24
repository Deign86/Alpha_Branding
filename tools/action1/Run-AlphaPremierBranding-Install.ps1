$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$InstallerScriptUrl = 'https://raw.githubusercontent.com/Deign86/Alpha_Branding/chore/action1-deployment-scripts/tools/action1/Install-AlphaPremierBranding.ps1'
$InstallerPath = Join-Path $env:TEMP 'Install-AlphaPremierBranding.ps1'
try {
    Invoke-WebRequest -Uri $InstallerScriptUrl -OutFile $InstallerPath -UseBasicParsing
    if (-not (Test-Path $InstallerPath)) { throw 'Installer script download failed.' }
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File $InstallerPath *> $null
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    exit 0
}
catch {
    exit 1
}
finally {
    Remove-Item $InstallerPath -Force -ErrorAction SilentlyContinue
}
