$ErrorActionPreference = 'Stop'

# Action1 prepends preference variables, so this wrapper intentionally has no top-level param() block.
# Set this URL to the raw GitHub URL of Install-AlphaPremierBranding.ps1 on this branch,
# or host the script on another trusted HTTPS endpoint.
$InstallerScriptUrl = 'https://raw.githubusercontent.com/Deign86/Alpha_Branding/chore/action1-deployment-scripts/tools/action1/Install-AlphaPremierBranding.ps1'
$InstallerPath = Join-Path $env:TEMP 'Install-AlphaPremierBranding.ps1'

try {
    Invoke-WebRequest -Uri $InstallerScriptUrl -OutFile $InstallerPath -UseBasicParsing
    if (-not (Test-Path $InstallerPath)) { throw 'Installer script download failed.' }
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $InstallerPath
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    exit 0
}
catch {
    Write-Error $_.Exception.Message
    exit 1
}
finally {
    Remove-Item $InstallerPath -Force -ErrorAction SilentlyContinue
}
