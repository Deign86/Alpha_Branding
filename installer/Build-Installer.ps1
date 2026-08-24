[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')]
    [string]$Version = '1.0.0.0',
    [switch]$Sign = $true,
    [switch]$NoSign = $false,
    [string]$CertPath = '',
    [string]$CertPassword = '',
    [string]$TimestampServer = 'http://timestamp.digicert.com',
    [string]$CertSubject = 'CN=Alpha Premier Group'
)

$ErrorActionPreference = 'Stop'
if ($NoSign) { $Sign = $false }

$installerRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repositoryRoot = Split-Path -Parent $installerRoot
$project = Join-Path $repositoryRoot 'src\Alpha.Branding\Alpha.Branding.csproj'
$bootstrapper = Join-Path $installerRoot 'Bootstrapper\Bootstrapper.csproj'
$artifactDirectory = Join-Path $repositoryRoot 'artifacts'
$artifact = Join-Path $artifactDirectory 'Alpha.Branding.Setup.exe'
$stage = Join-Path ([System.IO.Path]::GetTempPath()) ('Alpha.Branding-installer-' + [guid]::NewGuid().ToString('N'))
$publish = Join-Path $stage 'publish'
$bootstrapPublish = Join-Path $stage 'bootstrapper'
$payload = Join-Path $stage 'payload.zip'
$marker = 'ALPHA_BRANDING_PAYLOAD_V1'
$defaultPassword = 'AlphaPremier_SigningKey_2026!'

function Require-File([string]$Path, [string]$Description) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Required $Description was not found: $Path" }
}

function Find-SignTool {
    $cmd = Get-Command 'signtool.exe' -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    $searchRoots = @(
        "${env:ProgramFiles(x86)}\Windows Kits\10\bin",
        "${env:ProgramFiles}\Windows Kits\10\bin",
        "${env:ProgramFiles(x86)}\Windows Kits\10\App Certification Kit",
        "${env:ProgramFiles}\Windows Kits\10\App Certification Kit",
        "${env:ProgramFiles(x86)}\Windows Kits\8.1\bin"
    )

    $candidates = [System.Collections.Generic.List[string]]::new()
    foreach ($root in $searchRoots) {
        if (Test-Path -LiteralPath $root) {
            Get-ChildItem -Path $root -Filter 'signtool.exe' -Recurse -File -ErrorAction SilentlyContinue |
                ForEach-Object { $candidates.Add($_.FullName) }
        }
    }

    if ($candidates.Count -eq 0) {
        return $null
    }

    $is64 = [Environment]::Is64BitOperatingSystem
    $sorted = $candidates | Sort-Object -Descending {
        $weight = 0
        if ($is64 -and $_ -match '\\x64\\') { $weight += 100 }
        if ($_ -match '10\.0\.(\d+)\.0') { $weight += [int]$Matches[1] }
        $weight
    }

    return $sorted[0]
}

function Ensure-CodeSigningCertificate([string]$PfxFile, [string]$CerFile, [string]$Password, [string]$Subject) {
    if (Test-Path -LiteralPath $PfxFile) {
        return
    }

    $certsDir = Split-Path -Parent $PfxFile
    if (-not (Test-Path -LiteralPath $certsDir)) {
        New-Item -ItemType Directory -Path $certsDir -Force | Out-Null
    }

    Write-Host "No certificate found at '$PfxFile'. Automatically generating code signing certificate..." -ForegroundColor Yellow
    $notAfter = (Get-Date).AddYears(5)
    $cert = New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject $Subject `
        -FriendlyName 'Alpha Premier Group Code Signing' `
        -KeyAlgorithm RSA `
        -KeyLength 2048 `
        -CertStoreLocation 'Cert:\CurrentUser\My' `
        -NotAfter $notAfter

    try {
        Export-Certificate -Cert $cert -FilePath $CerFile -Force | Out-Null
        $secPwd = ConvertTo-SecureString -String $Password -Force -AsPlainText
        Export-PfxCertificate -Cert $cert -FilePath $PfxFile -Password $secPwd -Force | Out-Null
        Write-Host "Auto-generated certificate exported to:" -ForegroundColor Green
        Write-Host "  PFX (for signing): $PfxFile" -ForegroundColor Green
        Write-Host "  CER (for trust):   $CerFile" -ForegroundColor Green
    } finally {
        Remove-Item -LiteralPath $cert.PSPath -Force -ErrorAction SilentlyContinue
    }
}

function Invoke-SignBinary([string]$SigntoolPath, [string]$TargetFile, [string]$PfxFile, [string]$Password, [string]$Timestamp) {
    Require-File $TargetFile 'target binary to sign'
    Require-File $PfxFile 'code signing certificate (PFX)'

    $leaf = Split-Path -Leaf $TargetFile
    Write-Host ("Signing {0} with SignTool..." -f $leaf) -ForegroundColor Cyan
    $signArgs = @('sign', '/fd', 'SHA256')
    if (-not [string]::IsNullOrWhiteSpace($Timestamp)) {
        $signArgs += @('/tr', $Timestamp, '/td', 'SHA256')
    }
    $signArgs += @('/f', $PfxFile)
    if (-not [string]::IsNullOrEmpty($Password)) {
        $signArgs += @('/p', $Password)
    }
    $signArgs += $TargetFile

    & $SigntoolPath @signArgs
    if ($LASTEXITCODE -ne 0) {
        throw ("Code signing failed for '{0}' with exit code {1}." -f $TargetFile, $LASTEXITCODE)
    }
    Write-Host ("Successfully signed {0}." -f $leaf) -ForegroundColor Green
}

try {
    Require-File $project 'WPF project'
    Require-File $bootstrapper 'bootstrapper project'
    New-Item -ItemType Directory -Path $publish, $bootstrapPublish, $artifactDirectory -Force | Out-Null

    $signtoolPath = $null
    if ($Sign) {
        if ([string]::IsNullOrWhiteSpace($CertPath)) {
            $defaultPfx = Join-Path $installerRoot 'certs\CodeSigningCert.pfx'
            $defaultCer = Join-Path $installerRoot 'certs\CodeSigningCert.cer'
            if (-not (Test-Path -LiteralPath $defaultPfx)) {
                $pwdToUse = if (-not [string]::IsNullOrEmpty($CertPassword)) { $CertPassword } else { $defaultPassword }
                Ensure-CodeSigningCertificate -PfxFile $defaultPfx -CerFile $defaultCer -Password $pwdToUse -Subject $CertSubject
            }
            $CertPath = $defaultPfx
        }
        Require-File $CertPath 'code signing certificate (PFX)'

        if ([string]::IsNullOrEmpty($CertPassword)) {
            if (-not [string]::IsNullOrEmpty($env:CERT_PASSWORD)) {
                $CertPassword = $env:CERT_PASSWORD
            } elseif (-not [string]::IsNullOrEmpty($env:CODE_SIGN_PASSWORD)) {
                $CertPassword = $env:CODE_SIGN_PASSWORD
            } else {
                $CertPassword = $defaultPassword
            }
        }

        $signtoolPath = Find-SignTool
        if (-not $signtoolPath -or -not (Test-Path -LiteralPath $signtoolPath)) {
            throw "SignTool (signtool.exe) was not found. Please install the Windows 10/11 SDK or add signtool.exe to your PATH."
        }
        Write-Host ("Found SignTool: {0}" -f $signtoolPath) -ForegroundColor Cyan

        # Validate certificate and check expiration date
        try {
            $certObj = if ($CertPassword) {
                [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($CertPath, $CertPassword, [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::DefaultKeySet)
            } else {
                [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($CertPath)
            }
            $now = Get-Date
            if ($certObj.NotAfter -lt $now) {
                throw ("Code signing certificate '{0}' expired on {1:u}." -f $CertPath, $certObj.NotAfter)
            }
            $daysRemaining = ($certObj.NotAfter - $now).TotalDays
            if ($daysRemaining -lt 30) {
                Write-Warning ("Code signing certificate '{0}' expires in {1:N0} days ({2:u})." -f $CertPath, $daysRemaining, $certObj.NotAfter)
            } else {
                Write-Host ("Code signing certificate '{0}' is valid until {1:u} ({2:N0} days remaining)." -f $certObj.Subject, $certObj.NotAfter, $daysRemaining) -ForegroundColor Cyan
            }
        } catch {
            throw "Failed to open certificate at '$CertPath': $($_.Exception.Message)"
        }
    }

    Write-Host 'Publishing self-contained win-x64 application...'
    & dotnet publish $project --configuration Release --runtime win-x64 --self-contained true --output $publish --nologo
    if ($LASTEXITCODE -ne 0) { throw "Application publish failed with exit code $LASTEXITCODE." }
    $publishedAppExe = Join-Path $publish 'Alpha.Branding.exe'
    Require-File $publishedAppExe 'published executable'
    Require-File (Join-Path $publish 'Assets\logo_phoenix.png') 'published phoenix logo asset'
    Require-File (Join-Path $publish 'Assets\logo w name.png') 'published full logo asset'
    Require-File (Join-Path $publish 'Assets\alpha_branding.png') 'published branding asset'
    Set-Content -LiteralPath (Join-Path $publish 'InstallerVersion.txt') -Value $Version -Encoding ASCII

    if ($Sign) {
        Invoke-SignBinary -SigntoolPath $signtoolPath -TargetFile $publishedAppExe -PfxFile $CertPath -Password $CertPassword -Timestamp $TimestampServer
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory($publish, $payload, [System.IO.Compression.CompressionLevel]::Optimal, $false)
    Require-File $payload 'publish payload archive'

    Write-Host 'Publishing self-contained single-file bootstrapper...'
    & dotnet publish $bootstrapper --configuration Release --runtime win-x64 --self-contained true --output $bootstrapPublish --nologo -p:PublishSingleFile=true
    if ($LASTEXITCODE -ne 0) { throw "Bootstrapper publish failed with exit code $LASTEXITCODE." }
    $bootstrapExe = Join-Path $bootstrapPublish 'Bootstrapper.exe'
    Require-File $bootstrapExe 'published bootstrapper executable'

    if (Test-Path -LiteralPath $artifact) { Remove-Item -LiteralPath $artifact -Force }
    Copy-Item -LiteralPath $bootstrapExe -Destination $artifact
    $payloadBytes = [System.IO.File]::ReadAllBytes($payload)
    $markerBytes = [System.Text.Encoding]::UTF8.GetBytes($marker)
    $lengthBytes = [BitConverter]::GetBytes([int64]$payloadBytes.Length)
    $stream = [System.IO.File]::Open($artifact, [System.IO.FileMode]::Append, [System.IO.FileAccess]::Write, [System.IO.FileShare]::Read)
    try { $stream.Write($payloadBytes, 0, $payloadBytes.Length); $stream.Write($markerBytes, 0, $markerBytes.Length); $stream.Write($lengthBytes, 0, $lengthBytes.Length) } finally { $stream.Dispose() }
    Require-File $artifact 'installer executable'
    if ((Get-Item -LiteralPath $artifact).Length -le 0) { throw 'Installer executable is empty.' }
    $bytes = [System.IO.File]::ReadAllBytes($artifact)
    $length = [BitConverter]::ToInt64($bytes, $bytes.Length - 8)
    $markerAt = $bytes.Length - 8 - $markerBytes.Length
    $actualMarker = [System.Text.Encoding]::UTF8.GetString($bytes, $markerAt, $markerBytes.Length)
    if ($length -ne $payloadBytes.Length -or $actualMarker -cne $marker) { throw 'Installer trailer validation failed.' }

    if ($Sign) {
        Invoke-SignBinary -SigntoolPath $signtoolPath -TargetFile $artifact -PfxFile $CertPath -Password $CertPassword -Timestamp $TimestampServer
    }

    # Also sign any MSI files if present in output/installer directories
    $msiFiles = Get-ChildItem -Path $installerRoot, $artifactDirectory -Filter '*.msi' -File -ErrorAction SilentlyContinue
    if ($Sign -and $msiFiles) {
        foreach ($msi in $msiFiles) {
            Invoke-SignBinary -SigntoolPath $signtoolPath -TargetFile $msi.FullName -PfxFile $CertPath -Password $CertPassword -Timestamp $TimestampServer
        }
    }

    Write-Host ("Created {0} ({1} bytes){2}." -f $artifact, (Get-Item -LiteralPath $artifact).Length, $(if ($Sign) { ' [SIGNED]' } else { ' [UNSIGNED]' })) -ForegroundColor Green

    if ($Sign) {
        $sigInfo = Get-AuthenticodeSignature -FilePath $artifact
        Write-Host "Signature Details:" -ForegroundColor Cyan
        Write-Host ("  Signer:    {0}" -f $sigInfo.SignerCertificate.Subject)
        Write-Host ("  Issuer:    {0}" -f $sigInfo.SignerCertificate.Issuer)
        Write-Host ("  Timestamp: {0} ({1})" -f $sigInfo.TimeStamperCertificate.NotBefore, $sigInfo.TimeStamperCertificate.Subject)
    }
} finally {
    if (Test-Path -LiteralPath $stage) {
        try { Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue } catch { }
    }
}
