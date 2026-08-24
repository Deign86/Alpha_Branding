$ErrorActionPreference = 'Stop'

$AppName = 'Alpha Premier Branding'
$Publisher = 'Deign'
$DisplayVersion = '1.6.3'
$AppId = 'AlphaPremierBranding'
$DownloadUrl = 'https://YOUR-DIRECT-HOST/AlphaBranding-1.6.3.zip'

$InstallRoot = Join-Path $env:ProgramFiles 'Alpha Premier Branding'
$UninstallRoot = Join-Path $InstallRoot 'Uninstall'
$UninstallScript = Join-Path $UninstallRoot 'Uninstall.ps1'
$UninstallKey = "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$AppId"
$StartMenuRoot = Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\Alpha Premier Branding'
$DesktopShortcut = Join-Path ([Environment]::GetFolderPath('CommonDesktopDirectory')) "$AppName.lnk"
$StartShortcut = Join-Path $StartMenuRoot "$AppName.lnk"
$LogDirectory = Join-Path $env:ProgramData 'Alpha Premier Branding'
$LogPath = Join-Path $LogDirectory 'action1-install.log'
$TempRoot = Join-Path $env:TEMP 'AlphaBranding-Action1'
$TempZip = Join-Path $env:TEMP 'AlphaBranding-1.6.3.zip'
$ExtractRoot = Join-Path $TempRoot 'Extracted'

function Write-Log([string]$Message) {
    New-Item -Path $LogDirectory -ItemType Directory -Force | Out-Null
    Add-Content -Path $LogPath -Value "$(Get-Date -Format s) $Message"
}

function New-Shortcut([string]$ShortcutPath, [string]$TargetPath, [string]$WorkingDirectory) {
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($ShortcutPath)
    $shortcut.TargetPath = $TargetPath
    $shortcut.WorkingDirectory = $WorkingDirectory
    $shortcut.Description = $AppName
    $shortcut.Save()
}

try {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'The script must run as Administrator or LocalSystem.'
    }
    if ($DownloadUrl -like '*YOUR-DIRECT-HOST*') {
        throw 'Set $DownloadUrl to a real direct HTTPS URL before running this script.'
    }

    Write-Log "Installing $AppName $DisplayVersion"
    if (Test-Path $TempRoot) { Remove-Item $TempRoot -Recurse -Force }
    if (Test-Path $TempZip) { Remove-Item $TempZip -Force }
    New-Item $TempRoot -ItemType Directory -Force | Out-Null
    Invoke-WebRequest -Uri $DownloadUrl -OutFile $TempZip -UseBasicParsing
    Expand-Archive -Path $TempZip -DestinationPath $ExtractRoot -Force

    $PayloadRoot = $ExtractRoot
    foreach ($folder in @('publish', 'app-files', 'Alpha.Branding')) {
        $candidate = Join-Path $ExtractRoot $folder
        if (Test-Path $candidate -PathType Container) { $PayloadRoot = $candidate; break }
    }

    $AppExe = Get-ChildItem -Path $PayloadRoot -Filter '*.exe' -File -Recurse |
        Where-Object { $_.Name -notmatch 'Setup|Installer|Uninstall|Bootstrapper' } |
        Select-Object -First 1
    if (-not $AppExe) { throw 'No application EXE was found in the downloaded ZIP.' }

    New-Item $InstallRoot -ItemType Directory -Force | Out-Null
    Copy-Item (Join-Path $PayloadRoot '*') $InstallRoot -Recurse -Force
    $InstalledExe = Join-Path $InstallRoot $AppExe.Name
    if (-not (Test-Path $InstalledExe)) { throw "Application EXE was not copied: $InstalledExe" }

    New-Item $StartMenuRoot -ItemType Directory -Force | Out-Null
    New-Item $UninstallRoot -ItemType Directory -Force | Out-Null
    New-Shortcut $StartShortcut $InstalledExe $InstallRoot
    New-Shortcut $DesktopShortcut $InstalledExe $InstallRoot

    $uninstaller = @'
$ErrorActionPreference = 'SilentlyContinue'
$InstallRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$AppName = 'Alpha Premier Branding'
$UninstallKey = 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\AlphaPremierBranding'
$StartMenuRoot = Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\Alpha Premier Branding'
$DesktopShortcut = Join-Path ([Environment]::GetFolderPath('CommonDesktopDirectory')) "$AppName.lnk"
$StartShortcut = Join-Path $StartMenuRoot "$AppName.lnk"
Remove-Item $DesktopShortcut -Force
Remove-Item $StartShortcut -Force
Remove-Item $StartMenuRoot -Recurse -Force
Remove-Item $UninstallKey -Recurse -Force
$CleanupFile = Join-Path $env:TEMP "AlphaBranding-Cleanup-$([guid]::NewGuid().ToString('N')).cmd"
@"
@echo off
ping 127.0.0.1 -n 3 >nul
rmdir /s /q "$InstallRoot"
del /f /q "%~f0"
"@ | Set-Content $CleanupFile -Encoding ASCII
Start-Process $env:ComSpec -ArgumentList "/c `"$CleanupFile`"" -WindowStyle Hidden
'@
    Set-Content $UninstallScript $uninstaller -Encoding UTF8

    New-Item $UninstallKey -Force | Out-Null
    New-ItemProperty $UninstallKey DisplayName -Value $AppName -PropertyType String -Force | Out-Null
    New-ItemProperty $UninstallKey DisplayVersion -Value $DisplayVersion -PropertyType String -Force | Out-Null
    New-ItemProperty $UninstallKey Publisher -Value $Publisher -PropertyType String -Force | Out-Null
    New-ItemProperty $UninstallKey InstallLocation -Value $InstallRoot -PropertyType String -Force | Out-Null
    New-ItemProperty $UninstallKey DisplayIcon -Value $InstalledExe -PropertyType String -Force | Out-Null
    $UninstallCommand = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$UninstallScript`""
    New-ItemProperty $UninstallKey UninstallString -Value $UninstallCommand -PropertyType String -Force | Out-Null
    New-ItemProperty $UninstallKey QuietUninstallString -Value $UninstallCommand -PropertyType String -Force | Out-Null
    New-ItemProperty $UninstallKey NoModify -Value 1 -PropertyType DWord -Force | Out-Null
    New-ItemProperty $UninstallKey NoRepair -Value 1 -PropertyType DWord -Force | Out-Null

    Write-Log "Installation completed: $InstalledExe"
    Write-Output "$AppName $DisplayVersion installed successfully."
    exit 0
}
catch {
    Write-Log "INSTALLATION ERROR: $($_.Exception.Message)"
    Write-Error $_.Exception.Message
    exit 1
}
finally {
    Remove-Item $TempZip -Force -ErrorAction SilentlyContinue
    Remove-Item $TempRoot -Recurse -Force -ErrorAction SilentlyContinue
}
