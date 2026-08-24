<#
.SYNOPSIS
    Installs or uninstalls Alpha Premier Realty Branding Studio without GUI prompts.

.DESCRIPTION
    Non-interactive PowerShell installer for Alpha Premier Realty Branding Studio.
    Designed for Action1 RMM, CI/CD pipelines (GitHub Actions), automated deployment tools,
    and headless environments where GUI setup executables cannot run.

.PARAMETER InstallDir
    Target directory for installation.
    Default (Per-User): %LOCALAPPDATA%\Alpha Premier Realty\Branding Studio
    Default (All-Users / SYSTEM / Action1): %ProgramFiles%\Alpha Premier Realty\Branding Studio

.PARAMETER AllUsers
    Installs system-wide for all users (Program Files, HKLM, Common Start Menu/Desktop).
    Automatically enabled when executing under LocalSystem (Action1 default).

.PARAMETER PerUser
    Forces per-user installation even when running as Administrator.

.PARAMETER SourceDir
    Path to pre-published application folder.

.PARAMETER SetupExe
    Path to Alpha.Branding.Setup.exe bootstrapper to extract the payload from.

.PARAMETER ZipPath
    Path to a zip archive containing application files.

.PARAMETER DownloadUrl
    URL to download Alpha.Branding.Setup.exe or zip release asset from before installing.

.PARAMETER Version
    Version string to register in Windows Add/Remove Programs.
    Default: 1.5.0.0 (or auto-detected from payload)

.PARAMETER SelfContained
    Whether to build self-contained win-x64 if publishing from source.
    Default: $true

.PARAMETER CreateDesktopShortcut
    Creates a desktop shortcut in addition to the Start Menu shortcut.

.PARAMETER NoShortcuts
    Skips shortcut creation. Useful for headless CI runners.

.PARAMETER NoRegistry
    Skips Windows Add/Remove Programs registry registration.

.PARAMETER LogPath
    Path to write installation log entries.

.PARAMETER Launch
    Launches the application immediately after successful installation.

.PARAMETER Force
    Terminates running instances of Alpha.Branding before installing or uninstalling.

.PARAMETER Uninstall
    Uninstalls the application, removes shortcuts and registry registration.

.EXAMPLE
    .\Install.ps1
    Installs the application silently from repository files or built artifacts.

.EXAMPLE
    # Action1 / RMM silent system deployment from release URL
    .\Install.ps1 -DownloadUrl "https://github.com/Deign86/Alpha_Branding/releases/latest/download/Alpha.Branding.Setup.exe" -AllUsers -CreateDesktopShortcut

.EXAMPLE
    .\Install.ps1 -Uninstall
    Silently uninstalls the application, removing shortcuts and registry entries.
#>

[CmdletBinding(DefaultParameterSetName = 'Install')]
param(
    [Parameter(ParameterSetName = 'Install')]
    [Parameter(ParameterSetName = 'Uninstall')]
    [string]$InstallDir = '',

    [Parameter(ParameterSetName = 'Install')]
    [Parameter(ParameterSetName = 'Uninstall')]
    [switch]$AllUsers,

    [Parameter(ParameterSetName = 'Install')]
    [Parameter(ParameterSetName = 'Uninstall')]
    [switch]$PerUser,

    [Parameter(ParameterSetName = 'Install')]
    [string]$SourceDir = '',

    [Parameter(ParameterSetName = 'Install')]
    [string]$SetupExe = '',

    [Parameter(ParameterSetName = 'Install')]
    [string]$ZipPath = '',

    [Parameter(ParameterSetName = 'Install')]
    [string]$DownloadUrl = '',

    [Parameter(ParameterSetName = 'Install')]
    [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')]
    [string]$Version = '1.5.0.0',

    [Parameter(ParameterSetName = 'Install')]
    [switch]$SelfContained = $true,

    [Parameter(ParameterSetName = 'Install')]
    [switch]$CreateDesktopShortcut,

    [Parameter(ParameterSetName = 'Install')]
    [switch]$NoShortcuts,

    [Parameter(ParameterSetName = 'Install')]
    [switch]$NoRegistry,

    [Parameter(ParameterSetName = 'Install')]
    [Parameter(ParameterSetName = 'Uninstall')]
    [string]$LogPath = '',

    [Parameter(ParameterSetName = 'Install')]
    [switch]$Launch,

    [Parameter(ParameterSetName = 'Install')]
    [Parameter(ParameterSetName = 'Uninstall')]
    [switch]$Force,

    [Parameter(ParameterSetName = 'Uninstall', Mandatory = $true)]
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'

$productName = 'Alpha Premier Realty Branding Studio'
$publisher = 'Alpha Premier Realty'
$marker = 'ALPHA_BRANDING_PAYLOAD_V1'

$isSystem = [System.Security.Principal.WindowsIdentity]::GetCurrent().IsSystem
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
$isMachineInstall = $AllUsers -or ($isSystem -and (-not $PerUser))

if ([string]::IsNullOrWhiteSpace($InstallDir)) {
    if ($isMachineInstall) {
        $InstallDir = Join-Path ${env:ProgramFiles} 'Alpha Premier Realty\Branding Studio'
    } else {
        $InstallDir = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'Alpha Premier Realty\Branding Studio'
    }
}

if ($isMachineInstall) {
    $startMenuFolder = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::CommonStartMenu)) 'Programs\Alpha Premier Realty'
    $desktopShortcut = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::CommonDesktopDirectory)) ($productName + '.lnk')
    $uninstallKey = 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Alpha Premier Realty Branding Studio'
} else {
    $startMenuFolder = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::StartMenu)) 'Programs\Alpha Premier Realty'
    $desktopShortcut = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::Desktop)) ($productName + '.lnk')
    $uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Alpha Premier Realty Branding Studio'
}
$startMenuShortcut = Join-Path $startMenuFolder ($productName + '.lnk')

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = if (Test-Path -LiteralPath (Join-Path $scriptDir '..\Alpha_Branding.sln')) {
    (Resolve-Path (Join-Path $scriptDir '..')).Path
} elseif (Test-Path -LiteralPath (Join-Path $scriptDir 'Alpha_Branding.sln')) {
    $scriptDir
} else {
    $scriptDir
}

function Write-InstallLog([string]$Message, [string]$Color = 'Gray') {
    $timestamp = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")
    $line = "[$timestamp] $Message"
    if ($Color -eq 'Cyan') {
        Write-Host $Message -ForegroundColor Cyan
    } elseif ($Color -eq 'Green') {
        Write-Host $Message -ForegroundColor Green
    } elseif ($Color -eq 'Yellow') {
        Write-Host $Message -ForegroundColor Yellow
    } elseif ($Color -eq 'Red') {
        Write-Host $Message -ForegroundColor Red
    } else {
        Write-Host $Message -ForegroundColor Gray
    }
    if ($LogPath) {
        try {
            $logDir = Split-Path -Parent $LogPath
            if ($logDir -and -not (Test-Path -LiteralPath $logDir)) {
                New-Item -ItemType Directory -Path $logDir -Force | Out-Null
            }
            Add-Content -Path $LogPath -Value $line -Encoding UTF8
        } catch {}
    }
}

function Create-Shortcut([string]$Path, [string]$TargetPath, [string]$WorkingDirectory, [string]$Description, [string]$IconLocation) {
    $dir = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }
    $wshShell = New-Object -ComObject WScript.Shell
    try {
        $shortcut = $wshShell.CreateShortcut($Path)
        $shortcut.TargetPath = $TargetPath
        $shortcut.WorkingDirectory = $WorkingDirectory
        $shortcut.Description = $Description
        if ($IconLocation -and (Test-Path -LiteralPath $IconLocation)) {
            $shortcut.IconLocation = "$IconLocation,0"
        } else {
            $shortcut.IconLocation = "$TargetPath,0"
        }
        $shortcut.Save()
        [System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut) | Out-Null
    } finally {
        [System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($wshShell) | Out-Null
    }
}

function Extract-PayloadFromSetupExe([string]$ExePath, [string]$DestinationZip) {
    $markerBytes = [System.Text.Encoding]::UTF8.GetBytes($marker)
    $overhead = $markerBytes.Length + 8

    $fileStream = [System.IO.File]::OpenRead($ExePath)
    try {
        $fileLen = $fileStream.Length
        if ($fileLen -lt $overhead) {
            throw "File '$ExePath' is too small to contain a setup payload."
        }

        $scanWindow = [Math]::Min($fileLen, 1048576) # Scan last 1MB
        $scanStart = $fileLen - $scanWindow
        $fileStream.Seek($scanStart, [System.IO.SeekOrigin]::Begin) | Out-Null

        $buffer = New-Object byte[] $scanWindow
        $read = 0
        while ($read -lt $scanWindow) {
            $r = $fileStream.Read($buffer, $read, $scanWindow - $read)
            if ($r -le 0) { break }
            $read += $r
        }

        $found = $false
        $payloadStart = 0
        $payloadLength = 0

        for ($i = $read - $overhead; $i -ge 0; $i--) {
            $matched = $true
            for ($j = 0; $j -lt $markerBytes.Length; $j++) {
                if ($buffer[$i + $j] -ne $markerBytes[$j]) {
                    $matched = $false
                    break
                }
            }
            if ($matched) {
                $payloadLength = [BitConverter]::ToInt64($buffer, $i + $markerBytes.Length)
                $markerAbsolutePos = $scanStart + $i
                $payloadStart = $markerAbsolutePos - $payloadLength
                if ($payloadLength -gt 0 -and $payloadStart -ge 0 -and $payloadStart -le ($fileLen - $overhead)) {
                    $found = $true
                    break
                }
            }
        }

        if (-not $found) {
            throw "Installer payload trailer is missing or invalid in '$ExePath'."
        }

        $fileStream.Seek($payloadStart, [System.IO.SeekOrigin]::Begin) | Out-Null
        $outStream = [System.IO.File]::Create($DestinationZip)
        try {
            $copyBuf = New-Object byte[] 81920
            $remaining = $payloadLength
            while ($remaining -gt 0) {
                $toRead = [Math]::Min($copyBuf.Length, $remaining)
                $r = $fileStream.Read($copyBuf, 0, $toRead)
                if ($r -le 0) { throw "Unexpected end of stream while extracting payload." }
                $outStream.Write($copyBuf, 0, $r)
                $remaining -= $r
            }
        } finally {
            $outStream.Dispose()
        }
    } finally {
        $fileStream.Dispose()
    }
}

function Ensure-NoRunningProcesses([switch]$ForceKill) {
    $processes = Get-Process -Name 'Alpha.Branding' -ErrorAction SilentlyContinue
    if ($processes) {
        if ($ForceKill) {
            Write-InstallLog 'Stopping running instances of Alpha.Branding...' -Color 'Yellow'
            $processes | Stop-Process -Force
            Start-Sleep -Milliseconds 500
        } else {
            throw 'Alpha Premier Realty Branding Studio is currently running. Close it or use -Force to proceed.'
        }
    }
}

if ($Uninstall) {
    Write-InstallLog "Uninstalling $productName..." -Color 'Cyan'
    Ensure-NoRunningProcesses -ForceKill:$Force

    # 1. Remove Shortcuts
    Write-InstallLog 'Removing Start Menu shortcuts...' -Color 'Gray'
    if (Test-Path -LiteralPath $startMenuShortcut) {
        Remove-Item -LiteralPath $startMenuShortcut -Force -ErrorAction SilentlyContinue
    }
    if (Test-Path -LiteralPath $desktopShortcut) {
        Remove-Item -LiteralPath $desktopShortcut -Force -ErrorAction SilentlyContinue
    }
    if ((Test-Path -LiteralPath $startMenuFolder) -and ((Get-ChildItem -LiteralPath $startMenuFolder -Force | Measure-Object).Count -eq 0)) {
        Remove-Item -LiteralPath $startMenuFolder -Force -Recurse -ErrorAction SilentlyContinue
    }

    # 2. Remove Registry Entry
    Write-InstallLog 'Removing Windows Add/Remove Programs registration...' -Color 'Gray'
    if (Test-Path -LiteralPath $uninstallKey) {
        Remove-Item -LiteralPath $uninstallKey -Recurse -Force -ErrorAction SilentlyContinue
    }

    # 3. Remove Installed Files
    if (Test-Path -LiteralPath $InstallDir) {
        Write-InstallLog "Removing installation files from '$InstallDir'..." -Color 'Gray'
        try {
            Remove-Item -LiteralPath $InstallDir -Recurse -Force -ErrorAction Stop
        } catch {
            $cleanupCommand = "Start-Sleep -Milliseconds 1500; if (Test-Path -LiteralPath '$InstallDir') { Remove-Item -LiteralPath '$InstallDir' -Recurse -Force -ErrorAction SilentlyContinue }"
            $psi = [System.Diagnostics.ProcessStartInfo]::new('powershell.exe', "-NoProfile -ExecutionPolicy Bypass -Command `"$cleanupCommand`"")
            $psi.CreateNoWindow = $true
            $psi.WindowStyle = [System.Diagnostics.ProcessWindowStyle]::Hidden
            [System.Diagnostics.Process]::Start($psi) | Out-Null
        }
    }

    Write-InstallLog "$productName was successfully uninstalled." -Color 'Green'
    exit 0
}

# --- INSTALLATION WORKFLOW ---
Write-InstallLog "==========================================================" -Color 'Cyan'
Write-InstallLog "Installing $productName" -Color 'Cyan'
Write-InstallLog "==========================================================" -Color 'Cyan'

Ensure-NoRunningProcesses -ForceKill:$Force

$tempStage = Join-Path ([System.IO.Path]::GetTempPath()) ("AlphaBranding_InstallStage_" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tempStage -Force | Out-Null

try {
    # 0. Download if DownloadUrl provided
    if ($DownloadUrl) {
        Write-InstallLog "Downloading installer asset from: $DownloadUrl" -Color 'Cyan'
        $downloadFile = Join-Path $tempStage ("AlphaBranding_Download_" + [Guid]::NewGuid().ToString('N') + $(if ($DownloadUrl -match '\.exe($|\?)') { '.exe' } else { '.zip' }))
        $oldProgress = $ProgressPreference
        $ProgressPreference = 'SilentlyContinue'
        try {
            [System.Net.ServicePointManager]::SecurityProtocol = [System.Net.SecurityProtocolType]::Tls12 -bor [System.Net.SecurityProtocolType]::Tls13
            Invoke-WebRequest -Uri $DownloadUrl -OutFile $downloadFile -UseBasicParsing
        } finally {
            $ProgressPreference = $oldProgress
        }
        if ($downloadFile.EndsWith('.exe', [StringComparison]::OrdinalIgnoreCase)) {
            $SetupExe = $downloadFile
        } else {
            $ZipPath = $downloadFile
        }
    }

    $sourceFilesDir = $null

    # 1. Resolve source files
    if ($SourceDir -and (Test-Path -LiteralPath $SourceDir)) {
        Write-InstallLog "Using specified source directory: $SourceDir" -Color 'Gray'
        $sourceFilesDir = (Resolve-Path $SourceDir).Path
    } elseif ($ZipPath -and (Test-Path -LiteralPath $ZipPath)) {
        Write-InstallLog "Extracting archive: $ZipPath" -Color 'Gray'
        $unzipTarget = Join-Path $tempStage 'unzipped'
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [System.IO.Compression.ZipFile]::ExtractToDirectory($ZipPath, $unzipTarget)
        $sourceFilesDir = $unzipTarget
    } elseif ($SetupExe -and (Test-Path -LiteralPath $SetupExe)) {
        Write-InstallLog "Extracting payload from setup executable: $SetupExe" -Color 'Gray'
        $extractedZip = Join-Path $tempStage 'payload.zip'
        Extract-PayloadFromSetupExe -ExePath $SetupExe -DestinationZip $extractedZip
        $unzipTarget = Join-Path $tempStage 'unzipped'
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [System.IO.Compression.ZipFile]::ExtractToDirectory($extractedZip, $unzipTarget)
        $sourceFilesDir = $unzipTarget
    } else {
        # Auto-discover from artifacts or source
        $defaultSetupExe = Join-Path $repoRoot 'artifacts\Alpha.Branding.Setup.exe'
        $defaultPublishDir = Join-Path $repoRoot 'artifacts\publish'
        $csprojPath = Join-Path $repoRoot 'src\Alpha.Branding\Alpha.Branding.csproj'

        if (Test-Path -LiteralPath $defaultSetupExe) {
            Write-InstallLog "Found artifact setup executable: $defaultSetupExe" -Color 'Gray'
            $extractedZip = Join-Path $tempStage 'payload.zip'
            Extract-PayloadFromSetupExe -ExePath $defaultSetupExe -DestinationZip $extractedZip
            $unzipTarget = Join-Path $tempStage 'unzipped'
            Add-Type -AssemblyName System.IO.Compression.FileSystem
            [System.IO.Compression.ZipFile]::ExtractToDirectory($extractedZip, $unzipTarget)
            $sourceFilesDir = $unzipTarget
        } elseif (Test-Path -LiteralPath (Join-Path $defaultPublishDir 'Alpha.Branding.exe')) {
            Write-InstallLog "Found pre-published files at: $defaultPublishDir" -Color 'Gray'
            $sourceFilesDir = $defaultPublishDir
        } elseif (Test-Path -LiteralPath $csprojPath) {
            Write-InstallLog "Publishing application from source project: $csprojPath" -Color 'Cyan'
            $buildPublishDir = Join-Path $tempStage 'dotnet_publish'
            $publishArgs = @(
                'publish', $csprojPath,
                '--configuration', 'Release',
                '--runtime', 'win-x64',
                '--self-contained', $SelfContained.ToString().ToLower(),
                '--output', $buildPublishDir,
                '--nologo'
            )
            & dotnet @publishArgs
            if ($LASTEXITCODE -ne 0) {
                throw "dotnet publish failed with exit code $LASTEXITCODE"
            }
            $sourceFilesDir = $buildPublishDir
        } else {
            throw "Unable to find application source files or setup executable. Specify -SourceDir, -SetupExe, -DownloadUrl, or run from the repository root."
        }
    }

    $appExeSource = Join-Path $sourceFilesDir 'Alpha.Branding.exe'
    if (-not (Test-Path -LiteralPath $appExeSource)) {
        throw "Source directory does not contain 'Alpha.Branding.exe': $sourceFilesDir"
    }

    # Detect version from InstallerVersion.txt if present
    $versionFile = Join-Path $sourceFilesDir 'InstallerVersion.txt'
    if (Test-Path -LiteralPath $versionFile) {
        $detectedVersion = (Get-Content -LiteralPath $versionFile -Raw).Trim()
        if ($detectedVersion -match '^\d+\.\d+\.\d+\.\d+$') {
            $Version = $detectedVersion
        }
    }

    # 2. Deploy files to target installation directory
    Write-InstallLog "Deploying files to: $InstallDir" -Color 'Cyan'
    if (-not (Test-Path -LiteralPath $InstallDir)) {
        New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
    }

    # Copy all files and subdirectories
    Get-ChildItem -Path $sourceFilesDir | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $InstallDir -Recurse -Force
    }

    # Generate standalone uninstaller script in installation folder
    $uninstallScriptPath = Join-Path $InstallDir 'Uninstall.ps1'
    $isMachineBoolStr = if ($isMachineInstall) { '$true' } else { '$false' }
    $uninstallScriptContent = @"
<#
.SYNOPSIS
    Uninstaller for Alpha Premier Realty Branding Studio
#>
[CmdletBinding()]
param(
    [switch]`$Force
)

`$ErrorActionPreference = 'Stop'
`$productName = 'Alpha Premier Realty Branding Studio'
`$installDir = `$PSScriptRoot
`$isMachine = $isMachineBoolStr

if (`$isMachine) {
    `$shortcutDir = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::CommonStartMenu)) 'Programs\Alpha Premier Realty'
    `$desktopShortcut = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::CommonDesktopDirectory)) (`$productName + '.lnk')
    `$registryKey = 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Alpha Premier Realty Branding Studio'
} else {
    `$shortcutDir = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::StartMenu)) 'Programs\Alpha Premier Realty'
    `$desktopShortcut = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::Desktop)) (`$productName + '.lnk')
    `$registryKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Alpha Premier Realty Branding Studio'
}
`$shortcutPath = Join-Path `$shortcutDir (`$productName + '.lnk')

Write-Host "Uninstalling `$productName..." -ForegroundColor Cyan

`$running = Get-Process -Name 'Alpha.Branding' -ErrorAction SilentlyContinue
if (`$running) {
    if (`$Force) {
        `$running | Stop-Process -Force
    } else {
        throw "Alpha Premier Realty Branding Studio is currently running. Close it or pass -Force."
    }
}

if (Test-Path -LiteralPath `$shortcutPath) {
    Remove-Item -LiteralPath `$shortcutPath -Force -ErrorAction SilentlyContinue
}
if (Test-Path -LiteralPath `$desktopShortcut) {
    Remove-Item -LiteralPath `$desktopShortcut -Force -ErrorAction SilentlyContinue
}
if ((Test-Path -LiteralPath `$shortcutDir) -and ((Get-ChildItem -LiteralPath `$shortcutDir -Force | Measure-Object).Count -eq 0)) {
    Remove-Item -LiteralPath `$shortcutDir -Force -Recurse -ErrorAction SilentlyContinue
}

if (Test-Path -LiteralPath `$registryKey) {
    Remove-Item -LiteralPath `$registryKey -Recurse -Force -ErrorAction SilentlyContinue
}

`$cleanupCommand = "Start-Sleep -Milliseconds 1500; if (Test-Path -LiteralPath '`$installDir') { Remove-Item -LiteralPath '`$installDir' -Recurse -Force -ErrorAction SilentlyContinue }"
`$psi = [System.Diagnostics.ProcessStartInfo]::new('powershell.exe', ('-NoProfile -ExecutionPolicy Bypass -Command "' + `$cleanupCommand + '"'))
`$psi.CreateNoWindow = `$true
`$psi.WindowStyle = [System.Diagnostics.ProcessWindowStyle]::Hidden
[System.Diagnostics.Process]::Start(`$psi) | Out-Null

Write-Host "`$productName uninstalled successfully." -ForegroundColor Green
"@
    Set-Content -LiteralPath $uninstallScriptPath -Value $uninstallScriptContent -Encoding UTF8

    $targetAppExe = Join-Path $InstallDir 'Alpha.Branding.exe'
    $targetIcon = Join-Path $InstallDir 'Assets\app.ico'

    # 3. Create Shortcuts
    if (-not $NoShortcuts) {
        Write-InstallLog 'Creating Start Menu shortcut...' -Color 'Gray'
        Create-Shortcut -Path $startMenuShortcut -TargetPath $targetAppExe -WorkingDirectory $InstallDir -Description $productName -IconLocation $targetIcon

        if ($CreateDesktopShortcut) {
            Write-InstallLog 'Creating Desktop shortcut...' -Color 'Gray'
            Create-Shortcut -Path $desktopShortcut -TargetPath $targetAppExe -WorkingDirectory $InstallDir -Description $productName -IconLocation $targetIcon
        }
    }

    # 4. Register in Windows Add/Remove Programs (Registry)
    if (-not $NoRegistry) {
        Write-InstallLog 'Registering with Windows Add/Remove Programs...' -Color 'Gray'
        if (-not (Test-Path -LiteralPath $uninstallKey)) {
            New-Item -Path $uninstallKey -Force | Out-Null
        }
        Set-ItemProperty -Path $uninstallKey -Name 'DisplayName' -Value $productName -Force
        Set-ItemProperty -Path $uninstallKey -Name 'DisplayVersion' -Value $Version -Force
        Set-ItemProperty -Path $uninstallKey -Name 'Publisher' -Value $publisher -Force
        Set-ItemProperty -Path $uninstallKey -Name 'InstallLocation' -Value $InstallDir -Force
        Set-ItemProperty -Path $uninstallKey -Name 'DisplayIcon' -Value $targetAppExe -Force
        Set-ItemProperty -Path $uninstallKey -Name 'UninstallString' -Value "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$uninstallScriptPath`"" -Force
        Set-ItemProperty -Path $uninstallKey -Name 'QuietUninstallString' -Value "powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$uninstallScriptPath`" -Force" -Force
        Set-ItemProperty -Path $uninstallKey -Name 'NoModify' -Value 1 -Type DWord -Force
        Set-ItemProperty -Path $uninstallKey -Name 'NoRepair' -Value 1 -Type DWord -Force
        Set-ItemProperty -Path $uninstallKey -Name 'URLInfoAbout' -Value 'https://github.com/Deign86/Alpha_Branding' -Force

        try {
            $totalSize = (Get-ChildItem -Path $InstallDir -Recurse -File | Measure-Object -Property Length -Sum).Sum
            $sizeInKB = [int]($totalSize / 1024)
            Set-ItemProperty -Path $uninstallKey -Name 'EstimatedSize' -Value $sizeInKB -Type DWord -Force
        } catch {}
    }

    Write-InstallLog "==========================================================" -Color 'Green'
    Write-InstallLog "Installation Succeeded!" -Color 'Green'
    Write-InstallLog "  Location: $InstallDir" -Color 'Green'
    Write-InstallLog "  Version:  $Version" -Color 'Green'
    Write-InstallLog "==========================================================" -Color 'Green'

    # 5. Launch if requested
    if ($Launch) {
        Write-InstallLog "Launching $productName..." -Color 'Cyan'
        Start-Process -FilePath $targetAppExe -WorkingDirectory $InstallDir
    }

    exit 0
} finally {
    if (Test-Path -LiteralPath $tempStage) {
        try { Remove-Item -LiteralPath $tempStage -Recurse -Force -ErrorAction SilentlyContinue } catch {}
    }
}
