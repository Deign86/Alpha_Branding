# Alpha Premier Realty Branding Studio

Native .NET 8 WPF application for preparing local listing photography. It stretches each selected image to exactly 1200x1000, composites the local Alpha branding frame, encodes WebP at quality 80, and creates sequential sanitized filenames. Results can be previewed, saved individually, or exported as a ZIP.

## Compatibility decisions

This remains a native Windows WPF desktop app with no browser shell, server, cloud integration, or persistence. The native **Apply branding** button is intentional: the original visible Apply control was disabled/unused while processing happened automatically on file selection, so this migration makes the operation explicit and reviewable. Output remains stretched to 1200x1000 to preserve the original behavior. Files are local-only and are never uploaded.

## Development

```powershell
dotnet restore Alpha_Branding.sln
dotnet build Alpha_Branding.sln --configuration Release
dotnet test Alpha_Branding.sln --configuration Release
dotnet restore Alpha_Branding.sln -r win-x64
dotnet publish src/Alpha.Branding/Alpha.Branding.csproj -c Release -r win-x64 --self-contained false
```

ImageSharp is used only for local composition and WebP encoding because WPF/WIC does not provide a supported built-in WebP encoder. ZIP creation uses `System.IO.Compression`.
