## Alpha Premier Property Branding Studio v1.7.0

Native .NET 8 WPF desktop application with an Authenticode code-signed self-contained win-x64 installer.

### Changes
- **Automated Authenticode Code Signing**: Added full SHA-256 Authenticode code signing with RFC-3161 DigiCert timestamping to the application executable and the single-file bootstrapper installer.
- **Self-Signed Enterprise Certificate Provisioning**: Added automated certificate generation and management (`installer/Generate-SelfSignedCert.ps1`) for internal enterprise deployments.
- **Enterprise Trust Deployment Documentation**: Added comprehensive GPO, Intune, and local trust deployment instructions in `installer/CODE_SIGNING.md` to eliminate Windows SmartScreen and Unknown Publisher warnings on enterprise workstations.
- **Resilient Payload Scanning**: Updated bootstrapper payload discovery to dynamically support Authenticode-signed executables with PKCS#7 security directory tables.

### Installation
Download `Alpha.Branding.Setup.exe` to install per-user without administrator access.
Run with `--uninstall` to remove the application and its shortcuts.
