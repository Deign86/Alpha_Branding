<#
.SYNOPSIS
    Monitors and automatically cleans build artifacts when repository or artifact size exceeds a specified threshold.

.DESCRIPTION
    Scans the repository for build outputs, intermediate compilation files, and publish artifacts:
      - **/bin/
      - **/obj/
      - artifacts/
      - **/TestResults/
      - .vs/ (optional with -IncludeVs)

    Calculates current disk usage and compares it against the configured threshold (default: 500 MB).
    If target size exceeds the threshold, or if -Force is specified, it cleans the artifacts.
    Supports -DryRun, -Watch (continuous background monitor), and -InstallGitHook for automated post-commit cleanup.

.PARAMETER ThresholdMB
    Size threshold in megabytes. If measured size exceeds this value, cleanup is triggered.
    Default: 500 MB.

.PARAMETER Target
    What to measure against the threshold:
      - 'Artifacts' (default): Measures combined size of bin/, obj/, artifacts/, TestResults/
      - 'Repository': Measures total size of the entire repository (excluding .git).

.PARAMETER Force
    Cleans build artifacts immediately regardless of current size.

.PARAMETER DryRun
    Calculates and displays sizes, reporting whether cleanup would trigger, without deleting any files.

.PARAMETER NoDotNetClean
    Skips running 'dotnet clean' before deleting directories.

.PARAMETER IncludeVs
    Also includes Visual Studio cache directories (.vs/) in size calculation and cleanup.

.PARAMETER Watch
    Runs as a persistent background loop, checking size at specified intervals and auto-cleaning whenever threshold is exceeded.

.PARAMETER IntervalSeconds
    Interval in seconds between checks when running with -Watch. Default: 60.

.PARAMETER InstallGitHook
    Installs a Git post-commit hook so this script runs automatically after git commits.

.PARAMETER RemoveGitHook
    Removes the Git post-commit hook.

.PARAMETER Quiet
    Suppresses detailed per-folder output, printing only summary actions.

.EXAMPLE
    # Check if build artifacts exceed 500 MB, clean if so:
    .\Auto-Clean.ps1

.EXAMPLE
    # Clean if build artifacts exceed 200 MB:
    .\Auto-Clean.ps1 -ThresholdMB 200

.EXAMPLE
    # Check size without deleting anything:
    .\Auto-Clean.ps1 -DryRun

.EXAMPLE
    # Force clean immediately:
    .\Auto-Clean.ps1 -Force

.EXAMPLE
    # Clean if entire repo exceeds 1 GB:
    .\Auto-Clean.ps1 -Target Repository -ThresholdMB 1024

.EXAMPLE
    # Install git hook so repo automatically cleans itself after commits:
    .\Auto-Clean.ps1 -InstallGitHook -ThresholdMB 300
#>

[CmdletBinding()]
param(
    [double]$ThresholdMB = 500,
    [ValidateSet('Artifacts', 'Repository')]
    [string]$Target = 'Artifacts',
    [switch]$Force,
    [switch]$DryRun,
    [switch]$NoDotNetClean,
    [switch]$IncludeVs,
    [switch]$Watch,
    [int]$IntervalSeconds = 60,
    [switch]$InstallGitHook,
    [switch]$RemoveGitHook,
    [switch]$Quiet
)

$ErrorActionPreference = 'Stop'

# Determine repository root
$scriptDir = if ($MyInvocation.MyCommand.Path) {
    Split-Path -Parent $MyInvocation.MyCommand.Path
} else {
    $PWD.Path
}

$repoRoot = if ($scriptDir -and (Test-Path -LiteralPath (Join-Path $scriptDir 'Alpha_Branding.sln'))) {
    $scriptDir
} elseif ($scriptDir -and (Test-Path -LiteralPath (Join-Path $scriptDir '..\Alpha_Branding.sln'))) {
    (Resolve-Path (Join-Path $scriptDir '..')).Path
} else {
    $PWD.Path
}

# Protected paths that must NEVER be deleted
$protectedDirectories = @(
    (Join-Path $repoRoot 'installer\certs'),
    (Join-Path $repoRoot '.git'),
    (Join-Path $repoRoot 'src\Alpha.Branding\Assets'),
    (Join-Path $repoRoot 'installer\Bootstrapper\Assets'),
    (Join-Path $repoRoot 'assets'),
    (Join-Path $repoRoot 'img')
)

function Format-ByteSize([int64]$Bytes) {
    if ($Bytes -ge 1GB) {
        return ("{0:N2} GB" -f ($Bytes / 1GB))
    }
    if ($Bytes -ge 1MB) {
        return ("{0:N2} MB" -f ($Bytes / 1MB))
    }
    if ($Bytes -ge 1KB) {
        return ("{0:N2} KB" -f ($Bytes / 1KB))
    }
    return ("{0} B" -f $Bytes)
}

function Get-DirectorySize([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return [int64]0 }
    $measure = Get-ChildItem -LiteralPath $Path -Recurse -File -Force -ErrorAction SilentlyContinue |
        Measure-Object -Property Length -Sum
    if ($measure -and $measure.Sum) { return [int64]$measure.Sum }
    return [int64]0
}

function Get-ArtifactDirectories {
    $list = [System.Collections.Generic.List[System.IO.DirectoryInfo]]::new()

    $rootArtifacts = Join-Path $repoRoot 'artifacts'
    if (Test-Path -LiteralPath $rootArtifacts) {
        $list.Add((Get-Item -LiteralPath $rootArtifacts))
    }

    $rootTestResults = Join-Path $repoRoot 'TestResults'
    if (Test-Path -LiteralPath $rootTestResults) {
        $list.Add((Get-Item -LiteralPath $rootTestResults))
    }

    if ($IncludeVs) {
        $rootVs = Join-Path $repoRoot '.vs'
        if (Test-Path -LiteralPath $rootVs) {
            $list.Add((Get-Item -LiteralPath $rootVs))
        }
    }

    $subdirs = Get-ChildItem -LiteralPath $repoRoot -Directory -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -ne '.git' -and $_.Name -ne 'artifacts' -and $_.Name -ne 'TestResults' -and $_.Name -ne '.vs' }

    foreach ($sub in $subdirs) {
        if ($sub.Name -in @('bin', 'obj', 'TestResults')) {
            $list.Add($sub)
            continue
        }

        $found = Get-ChildItem -LiteralPath $sub.FullName -Recurse -Directory -Force -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -in @('bin', 'obj', 'TestResults') }

        foreach ($d in $found) {
            $isNested = $false
            foreach ($existing in $list) {
                if ($d.FullName.StartsWith($existing.FullName + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
                    $isNested = $true
                    break
                }
            }
            if (-not $isNested) {
                # Safety check against protected directories
                $isProtected = $false
                foreach ($prot in $protectedDirectories) {
                    if ($d.FullName.StartsWith($prot, [StringComparison]::OrdinalIgnoreCase)) {
                        $isProtected = $true
                        break
                    }
                }
                if (-not $isProtected) {
                    $list.Add($d)
                }
            }
        }
    }

    return $list
}

function Get-RepositorySize {
    $measure = Get-ChildItem -LiteralPath $repoRoot -Recurse -File -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\\.git\\' } |
        Measure-Object -Property Length -Sum
    if ($measure -and $measure.Sum) { return [int64]$measure.Sum }
    return [int64]0
}

function Remove-DirectorySafely([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return }
    try {
        Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop
    } catch {
        # Fallback to file-by-file deletion if locked files exist
        $files = Get-ChildItem -LiteralPath $Path -Recurse -File -Force -ErrorAction SilentlyContinue
        foreach ($f in $files) {
            try {
                Remove-Item -LiteralPath $f.FullName -Force -ErrorAction SilentlyContinue
            } catch {}
        }
        # Retry removing directory
        try {
            Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction SilentlyContinue
        } catch {
            Write-Warning "Could not fully remove '$Path': $($_.Exception.Message)"
        }
    }
}

# --- Handle Git Hook Management ---
$gitHooksDir = Join-Path $repoRoot '.git\hooks'
$hookFile = Join-Path $gitHooksDir 'post-commit'

if ($InstallGitHook) {
    if (-not (Test-Path -LiteralPath $gitHooksDir)) {
        throw "Git hooks directory not found at '$gitHooksDir'. Ensure this is a git repository."
    }
    $hookContent = @"
#!/bin/sh
# Alpha Branding auto-clean hook
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "./Auto-Clean.ps1" -ThresholdMB $ThresholdMB -Target $Target -Quiet
"@
    # Write with LF line endings for git shell compatibility
    [System.IO.File]::WriteAllText($hookFile, ($hookContent.Replace("`r`n", "`n") + "`n"), [System.Text.Encoding]::ASCII)
    Write-Host "Auto-clean Git post-commit hook successfully installed at: $hookFile" -ForegroundColor Green
    Write-Host "The repository will now automatically check size and clean build artifacts after commits if threshold ($ThresholdMB MB) is exceeded." -ForegroundColor Cyan
    exit 0
}

if ($RemoveGitHook) {
    if (Test-Path -LiteralPath $hookFile) {
        Remove-Item -LiteralPath $hookFile -Force
        Write-Host "Auto-clean Git post-commit hook removed." -ForegroundColor Green
    } else {
        Write-Host "No post-commit hook found to remove." -ForegroundColor Yellow
    }
    exit 0
}

# --- Main Clean Check Function ---
function Invoke-CleanCheck {
    $thresholdBytes = [int64]($ThresholdMB * 1MB)
    $artifactDirs = Get-ArtifactDirectories

    $details = [System.Collections.Generic.List[PSCustomObject]]::new()
    $totalArtifactBytes = [int64]0

    foreach ($dir in $artifactDirs) {
        $size = Get-DirectorySize -Path $dir.FullName
        $totalArtifactBytes += $size
        $relPath = if ($dir.FullName.StartsWith($repoRoot, [StringComparison]::OrdinalIgnoreCase)) {
            $dir.FullName.Substring($repoRoot.Length).TrimStart('\', '/')
        } else {
            $dir.FullName
        }
        $details.Add([PSCustomObject]@{
            Directory = $relPath
            SizeBytes = $size
            Formatted = Format-ByteSize $size
        })
    }

    $measuredBytes = if ($Target -eq 'Repository') {
        Get-RepositorySize
    } else {
        $totalArtifactBytes
    }

    $thresholdExceeded = $measuredBytes -gt $thresholdBytes

    if (-not $Quiet) {
        Write-Host "==========================================================" -ForegroundColor Cyan
        Write-Host "Alpha Branding Artifact Auto-Clean" -ForegroundColor Cyan
        Write-Host "==========================================================" -ForegroundColor Cyan
        Write-Host ("Repository Root:    {0}" -f $repoRoot)
        Write-Host ("Measurement Target: {0}" -f $Target)
        Write-Host ("Target Size:        {0} ({1:N0} bytes)" -f (Format-ByteSize $measuredBytes), $measuredBytes)
        Write-Host ("Threshold:          {0} ({1:N0} bytes)" -f (Format-ByteSize $thresholdBytes), $thresholdBytes)
        Write-Host ("Artifact Folders:   {0}" -f $artifactDirs.Count)
        Write-Host "----------------------------------------------------------"

        if ($details.Count -gt 0) {
            $details | Sort-Object -Property SizeBytes -Descending | Format-Table -Property @(
                @{ Label = "Artifact Directory"; Expression = { $_.Directory } },
                @{ Label = "Size"; Expression = { $_.Formatted }; Alignment = "Right" }
            ) | Out-Host
        } else {
            Write-Host "No build artifact directories currently exist." -ForegroundColor Gray
        }
    }

    if ($DryRun) {
        Write-Host "[DRY RUN] No files will be deleted." -ForegroundColor Yellow
        if ($Force) {
            Write-Host "[DRY RUN] Force flag active. Cleanup WOULD execute." -ForegroundColor Yellow
        } elseif ($thresholdExceeded) {
            Write-Host ("[DRY RUN] Threshold exceeded ({0} > {1}). Cleanup WOULD execute." -f (Format-ByteSize $measuredBytes), (Format-ByteSize $thresholdBytes)) -ForegroundColor Yellow
        } else {
            Write-Host ("[DRY RUN] Target size is within threshold ({0} <= {1}). No cleanup needed." -f (Format-ByteSize $measuredBytes), (Format-ByteSize $thresholdBytes)) -ForegroundColor Green
        }
        return
    }

    if (-not $thresholdExceeded -and -not $Force) {
        if (-not $Quiet) {
            Write-Host ("Status: UNDER THRESHOLD ({0} <= {1}). No cleanup required." -f (Format-ByteSize $measuredBytes), (Format-ByteSize $thresholdBytes)) -ForegroundColor Green
        }
        return
    }

    $reason = if ($Force) { "Force flag set" } else { "Size threshold exceeded ({0} > {1})" -f (Format-ByteSize $measuredBytes), (Format-ByteSize $thresholdBytes) }
    if ($Quiet) {
        Write-Host ("Auto-clean triggered: {0}. Cleaning build artifacts..." -f $reason) -ForegroundColor Yellow
    } else {
        Write-Host ("Status: TRIGGERED ({0}). Cleaning build artifacts..." -f $reason) -ForegroundColor Yellow
    }

    # 1. dotnet clean (if solution file exists and not suppressed)
    $slnPath = Join-Path $repoRoot 'Alpha_Branding.sln'
    if (-not $NoDotNetClean -and (Test-Path -LiteralPath $slnPath)) {
        $dotnet = Get-Command 'dotnet' -ErrorAction SilentlyContinue
        if ($dotnet) {
            if (-not $Quiet) { Write-Host "Running 'dotnet clean'..." -ForegroundColor Gray }
            & dotnet clean $slnPath --nologo -v q | Out-Null
        }
    }

    # 2. Delete artifact directories
    $deletedCount = 0
    foreach ($dir in $artifactDirs) {
        if (Test-Path -LiteralPath $dir.FullName) {
            if (-not $Quiet) {
                Write-Host ("Removing: {0}" -f $dir.FullName) -ForegroundColor Gray
            }
            Remove-DirectorySafely -Path $dir.FullName
            $deletedCount++
        }
    }

    # 3. Report reclaimed space
    $remainingDirs = Get-ArtifactDirectories
    $remainingBytes = [int64]0
    foreach ($dir in $remainingDirs) {
        $remainingBytes += Get-DirectorySize -Path $dir.FullName
    }
    $freedBytes = [Math]::Max([int64]0, $totalArtifactBytes - $remainingBytes)

    if ($Quiet) {
        Write-Host ("Auto-clean complete. Freed {0}." -f (Format-ByteSize $freedBytes)) -ForegroundColor Green
    } else {
        Write-Host "----------------------------------------------------------"
        Write-Host ("Cleanup Complete!" ) -ForegroundColor Green
        Write-Host ("  Artifact directories removed: {0}" -f $deletedCount) -ForegroundColor Green
        Write-Host ("  Space reclaimed:              {0} ({1:N0} bytes)" -f (Format-ByteSize $freedBytes), $freedBytes) -ForegroundColor Green
        Write-Host ("  Remaining artifact size:      {0}" -f (Format-ByteSize $remainingBytes)) -ForegroundColor Green
        Write-Host "==========================================================" -ForegroundColor Cyan
    }
}

# --- Execution ---
if ($Watch) {
    Write-Host "Starting Auto-Clean watch monitor (Interval: $IntervalSeconds s, Threshold: $ThresholdMB MB)... Press Ctrl+C to stop." -ForegroundColor Cyan
    while ($true) {
        Invoke-CleanCheck
        Start-Sleep -Seconds $IntervalSeconds
    }
} else {
    Invoke-CleanCheck
}
