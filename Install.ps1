<#
.SYNOPSIS
    Root wrapper for Alpha Premier Realty Branding Studio PowerShell installer.
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
$installerScript = Join-Path $PSScriptRoot 'installer\Install.ps1'

if (-not (Test-Path -LiteralPath $installerScript)) {
    throw "Installer script not found at '$installerScript'."
}

if ($Uninstall) {
    $uninstallParams = @{
        Uninstall = $true
        AllUsers = $AllUsers
        PerUser = $PerUser
        Force = $Force
    }
    if ($InstallDir) { $uninstallParams['InstallDir'] = $InstallDir }
    if ($LogPath) { $uninstallParams['LogPath'] = $LogPath }
    & $installerScript @uninstallParams
} else {
    $params = @{
        AllUsers = $AllUsers
        PerUser = $PerUser
        Version = $Version
        SelfContained = $SelfContained
        CreateDesktopShortcut = $CreateDesktopShortcut
        NoShortcuts = $NoShortcuts
        NoRegistry = $NoRegistry
        Launch = $Launch
        Force = $Force
    }
    if ($InstallDir) { $params['InstallDir'] = $InstallDir }
    if ($SourceDir) { $params['SourceDir'] = $SourceDir }
    if ($SetupExe) { $params['SetupExe'] = $SetupExe }
    if ($ZipPath) { $params['ZipPath'] = $ZipPath }
    if ($DownloadUrl) { $params['DownloadUrl'] = $DownloadUrl }
    if ($LogPath) { $params['LogPath'] = $LogPath }

    & $installerScript @params
}
