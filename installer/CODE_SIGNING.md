# Free Code Signing Guide (Self-Signed & Enterprise Trust)

This document describes how to generate, configure, deploy, and verify code signing for Alpha Premier Realty Branding Studio builds.

---

## Table of Contents

1. [Overview](#overview)
2. [Step 1: One-Time Certificate Generation](#step-1-one-time-certificate-generation)
3. [Step 2: Trusting the Certificate on Client Workstations](#step-2-trusting-the-certificate-on-client-workstations)
   - [Option A: Active Directory Group Policy (GPO) Deployment (Recommended for Enterprise)](#option-a-active-directory-group-policy-gpo-deployment-recommended-for-enterprise)
   - [Option B: Local Machine Import (For Testing and Standalone Machines)](#option-b-local-machine-import-for-testing-and-standalone-machines)
   - [Option C: Microsoft Intune Deployment](#option-c-microsoft-intune-deployment)
4. [Step 3: Building Signed Installers](#step-3-building-signed-installers)
5. [Step 4: Verifying the Digital Signature](#step-4-verifying-the-digital-signature)
6. [Security Best Practices](#security-best-practices)
7. [Future Migration Path: Upgrading to Commercial OV/EV Certificates](#future-migration-path-upgrading-to-commercial-ovev-certificates)

---

## Overview

Windows SmartScreen and UAC check downloaded executables and installers for a valid digital signature from a trusted Certificate Authority. For internal and commercial enterprise deployments where public CA certificates are not yet purchased, a **self-signed code signing certificate** can be distributed to enterprise endpoints via Group Policy (GPO) or MDM (Intune).

Once the public root certificate (`.cer`) is installed in the client machines' **Trusted Root Certification Authorities** and **Trusted Publishers** stores:
- Windows SmartScreen recognizes the executable as trusted.
- The UAC elevation prompt displays the verified publisher name ("Alpha Premier Group").
- Unknown publisher warnings are completely eliminated.

---

## Step 1: One-Time Certificate Generation

Use the provided PowerShell script `Generate-SelfSignedCert.ps1` to create a 2048-bit RSA code signing certificate:

```powershell
.\installer\Generate-SelfSignedCert.ps1 -Password "YourStrongPasswordHere123!"
```

### Parameters:
- `-Subject`: Certificate subject (default: `CN=Alpha Premier Group`).
- `-FriendlyName`: Friendly description (default: `Alpha Premier Group Code Signing`).
- `-Password`: Password for the exported PFX file (prompts securely if omitted).
- `-ValidYears`: Certificate validity in years (default: `5`).
- `-OutputDir`: Target folder for certificates (default: `installer/certs`).
- `-KeepInStore`: Optional switch to keep the certificate in `Cert:\CurrentUser\My` store.

### Generated Files:
1. `installer/certs/CodeSigningCert.pfx`: **Private key + certificate** used to sign binaries during the build. **Never share or commit to Git.**
2. `installer/certs/CodeSigningCert.cer`: **Public certificate** containing no private keys. Distribute this file to client computers and domain controllers.

---

## Step 2: Trusting the Certificate on Client Workstations

### Option A: Active Directory Group Policy (GPO) Deployment (Recommended for Enterprise)

Domain administrators can automatically distribute the `.cer` file to all domain-joined Windows machines using Group Policy:

1. Open **Group Policy Management Console** (`gpmc.msc`) on a Domain Controller or management workstation.
2. Right-click the target Organizational Unit (OU) or Domain, and select **Create a GPO in this domain, and Link it here...** (e.g., Name: `Deploy Alpha Premier Code Signing Certificate`).
3. Right-click the newly created GPO and click **Edit**.
4. In the Group Policy Management Editor, navigate to:
   ```text
   Computer Configuration
     └── Policies
           └── Windows Settings
                 └── Security Settings
                       └── Public Key Policies
   ```
5. Import to **Trusted Root Certification Authorities**:
   - Right-click **Trusted Root Certification Authorities** -> **Import...**.
   - Click **Next**, browse to `CodeSigningCert.cer`.
   - Place all certificates in the **Trusted Root Certification Authorities** store.
   - Complete the wizard.
6. Import to **Trusted Publishers**:
   - Right-click **Trusted Publishers** -> **Import...**.
   - Click **Next**, browse to `CodeSigningCert.cer`.
   - Place all certificates in the **Trusted Publishers** store.
   - Complete the wizard.
7. Force a policy update on client workstations to test:
   ```powershell
   gpupdate /force
   ```

### Option B: Local Machine Import (For Testing and Standalone Machines)

Run PowerShell as **Administrator**:

```powershell
# Import public certificate to Trusted Root Certification Authorities
Import-Certificate -FilePath ".\installer\certs\CodeSigningCert.cer" -CertStoreLocation "Cert:\LocalMachine\Root"

# Import public certificate to Trusted Publishers
Import-Certificate -FilePath ".\installer\certs\CodeSigningCert.cer" -CertStoreLocation "Cert:\LocalMachine\TrustedPublisher"
```

Or using `certutil.exe`:

```cmd
certutil -addstore -f "Root" "installer\certs\CodeSigningCert.cer"
certutil -addstore -f "TrustedPublisher" "installer\certs\CodeSigningCert.cer"
```

### Option C: Microsoft Intune Deployment

1. Navigate to the **Microsoft Intune admin center** (`https://intune.microsoft.com`).
2. Go to **Devices** > **Manage devices** > **Configuration** > **Create** > **New Policy**.
3. Platform: **Windows 10 and later**, Profile type: **Templates** > **Trusted certificate**.
4. Under **Certificate file**, upload `CodeSigningCert.cer` and set Destination store to **Computer certificate store - Root**.
5. Create a second profile targeting **Computer certificate store - Trusted Publisher**.
6. Assign both profiles to the target device group.

---

## Step 3: Building Signed Installers

To produce signed executables and packages, supply `-Sign`, `-CertPath`, and `-CertPassword` to `Build-Installer.ps1`:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\installer\Build-Installer.ps1 `
    -Version "1.3.0.0" `
    -Sign `
    -CertPath ".\installer\certs\CodeSigningCert.pfx" `
    -CertPassword "YourStrongPasswordHere123!"
```

### Automated / CI Environment Variables:
You can also supply the password via environment variables without placing it on the command line:

```powershell
$env:CERT_PASSWORD = "YourStrongPasswordHere123!"

powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\installer\Build-Installer.ps1 `
    -Version "1.3.0.0" `
    -Sign `
    -CertPath ".\installer\certs\CodeSigningCert.pfx"
```

### What Happens During a Signed Build:
1. Validates the existence and expiration date of `CodeSigningCert.pfx` (warns if expiring in <30 days).
2. Locates `signtool.exe` from installed Windows 10/11 SDKs.
3. Publishes `Alpha.Branding.exe` and signs it using SHA-256 and RFC-3161 timestamping (`http://timestamp.digicert.com`).
4. Bundles the signed application payload into the bootstrapper.
5. Builds and signs `Alpha.Branding.Setup.exe` (and any MSI artifacts).
6. Embeds timestamp countersignatures ensuring the binaries remain valid even after the signing certificate reaches its end-of-life date.

---

## Step 4: Verifying the Digital Signature

### Command-Line Verification (SignTool):

```powershell
signtool verify /pa /v .\artifacts\Alpha.Branding.Setup.exe
```

When verified on a machine where the `.cer` has been imported into the root store, you will see:
```text
Successfully verified: .\artifacts\Alpha.Branding.Setup.exe
Number of files successfully Verified: 1
Number of warnings: 0
Number of errors: 0
```

### Graphical Verification (Windows Explorer):

1. Right-click `artifacts\Alpha.Branding.Setup.exe` -> **Properties**.
2. Navigate to the **Digital Signatures** tab.
3. Select the signature in the list and click **Details**.
4. The dialog will display: `"This digital signature is OK."` and show the timestamp.

---

## Security Best Practices

1. **Never Commit PFX or Private Keys to Git:**
   - The `.gitignore` file is configured to exclude `installer/certs/`, `*.pfx`, `*.cer`, `*.p12`, and `*.snk`.
   - Never push certificates with private keys to GitHub or public repositories.
2. **Store PFX Passwords in Secret Managers:**
   - In automated build systems (GitHub Actions, Azure DevOps), store the certificate password in encrypted repository secrets (e.g. `CERT_PASSWORD`).
3. **Use Strong Passwords:**
   - Use a minimum 16-character alphanumeric password with symbols for PFX export.
4. **RFC-3161 Timestamping:**
   - Always sign with a timestamp server (`/tr http://timestamp.digicert.com /td SHA256`). This ensures Windows recognizes the signature as valid indefinitely, even after the certificate itself expires.

---

## Future Migration Path: Upgrading to Commercial OV/EV Certificates

When moving to a publicly trusted commercial certificate:

1. **Purchase an OV/EV Code Signing Certificate:**
   - Providers: DigiCert, Sectigo, SSL.com, Certum, or **Azure Trusted Signing** (cloud-based HSM signing).
2. **Obtain the Certificate / Token:**
   - Export the certificate to `.pfx` or connect the hardware token/cloud HSM.
3. **Zero Code Changes Required:**
   - Run the build script with the path to the commercial certificate:
     ```powershell
     .\installer\Build-Installer.ps1 -Sign -CertPath "C:\path\to\CommercialCert.pfx" -CertPassword "TokenPinOrPassword"
     ```
4. **Immediate Public Trust:**
   - Public CA certificates are automatically trusted by all Windows machines worldwide without requiring GPO or manual root certificate deployment.
