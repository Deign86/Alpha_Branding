<#
.SYNOPSIS
    Generates a self-signed code signing certificate and exports PFX and CER files.

.DESCRIPTION
    Creates an RSA 2048-bit code signing certificate using New-SelfSignedCertificate,
    exports a password-protected PFX file for signing and a public CER file for
    Group Policy (GPO) or client trust deployment.

.PARAMETER Subject
    The certificate subject name (default: 'CN=Alpha Premier Group').

.PARAMETER FriendlyName
    The friendly name for the certificate (default: 'Alpha Premier Group Code Signing').

.PARAMETER OutputDir
    The output directory for exported files (default: 'installer/certs').

.PARAMETER CertName
    Base file name for the exported files without extension (default: 'CodeSigningCert').

.PARAMETER Password
    The password protecting the exported PFX file. If omitted, prompts for a password.

.PARAMETER ValidYears
    Number of years the certificate is valid for (default: 5).

.PARAMETER KeepInStore
    If set, keeps the generated certificate in the CurrentUser\My certificate store.
    By default, it is removed after exporting to keep the certificate store clean.

.EXAMPLE
    .\Generate-SelfSignedCert.ps1 -Password "SecurePass123!"

.EXAMPLE
    .\Generate-SelfSignedCert.ps1 -Subject "CN=Alpha Premier Realty" -ValidYears 3
#>

[CmdletBinding()]
param(
    [string]$Subject = 'CN=Alpha Premier Group',
    [string]$FriendlyName = 'Alpha Premier Group Code Signing',
    [string]$OutputDir = '',
    [string]$CertName = 'CodeSigningCert',
    [string]$Password = '',
    [int]$ValidYears = 5,
    [switch]$KeepInStore = $false
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
    $OutputDir = Join-Path $scriptRoot 'certs'
}

if (-not (Test-Path -LiteralPath $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

$pfxPath = Join-Path $OutputDir "$CertName.pfx"
$cerPath = Join-Path $OutputDir "$CertName.cer"

# Secure password handling
$securePassword = $null
if (-not [string]::IsNullOrEmpty($Password)) {
    $securePassword = ConvertTo-SecureString -String $Password -Force -AsPlainText
} else {
    Write-Host 'No password supplied via -Password parameter.' -ForegroundColor Cyan
    $securePassword = Read-Host -Prompt 'Enter password to protect the PFX certificate' -AsSecureString
    if ($null -eq $securePassword -or $securePassword.Length -eq 0) {
        throw 'A non-empty password is required to export the PFX certificate.'
    }
}

Write-Host "Generating code signing certificate..." -ForegroundColor Cyan
Write-Host "  Subject:       $Subject"
Write-Host "  Friendly Name: $FriendlyName"
Write-Host "  Key Algorithm: RSA 2048-bit"
Write-Host "  Valid for:     $ValidYears years"

$notAfter = (Get-Date).AddYears($ValidYears)
$cert = New-SelfSignedCertificate `
    -Type CodeSigningCert `
    -Subject $Subject `
    -FriendlyName $FriendlyName `
    -KeyAlgorithm RSA `
    -KeyLength 2048 `
    -CertStoreLocation 'Cert:\CurrentUser\My' `
    -NotAfter $notAfter

try {
    Write-Host "Exporting public certificate (.cer) for client/GPO deployment..." -ForegroundColor Cyan
    Export-Certificate -Cert $cert -FilePath $cerPath -Force | Out-Null
    Write-Host "  Exported CER: $cerPath" -ForegroundColor Green

    Write-Host "Exporting private key (.pfx) for code signing..." -ForegroundColor Cyan
    Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $securePassword -Force | Out-Null
    Write-Host "  Exported PFX: $pfxPath" -ForegroundColor Green

    Write-Host ""
    Write-Host "================================================================" -ForegroundColor Green
    Write-Host "Certificate generated and exported successfully!" -ForegroundColor Green
    Write-Host "================================================================" -ForegroundColor Green
    Write-Host "  Thumbprint:    $($cert.Thumbprint)"
    Write-Host "  Valid From:    $($cert.NotBefore.ToString('yyyy-MM-dd HH:mm:ss'))"
    Write-Host "  Valid Until:   $($cert.NotAfter.ToString('yyyy-MM-dd HH:mm:ss'))"
    Write-Host "  PFX (Signing): $pfxPath"
    Write-Host "  CER (Trust):   $cerPath"
    Write-Host ""
    Write-Host "Next Steps:" -ForegroundColor Yellow
    Write-Host "1. Deploy '$cerPath' to client machines via GPO or import into:"
    Write-Host "   - Trusted Root Certification Authorities (Local Computer)"
    Write-Host "   - Trusted Publishers (Local Computer)"
    Write-Host "2. Build signed installer:"
    Write-Host "   .\installer\Build-Installer.ps1 -Sign -CertPath `"$pfxPath`" -CertPassword `"<password>`""
    Write-Host "================================================================" -ForegroundColor Green
} finally {
    if (-not $KeepInStore) {
        Write-Host "Cleaning up certificate from CurrentUser\My store..." -ForegroundColor DarkGray
        Remove-Item -LiteralPath $cert.PSPath -Force -ErrorAction SilentlyContinue
    }
}
