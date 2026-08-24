using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Alpha.Branding.Bootstrapper;

public static class InstallerService
{
    public const string Marker = "ALPHA_BRANDING_PAYLOAD_V1";
    public const string ProductName = "Alpha Premier Realty Branding Studio";
    public const string Publisher = "Alpha Premier Realty";
    public const string DefaultVersion = "1.5.0.0";

    public static string InstallDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Alpha Premier Realty", "Branding Studio");

    public static string ShortcutDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
        "Programs", "Alpha Premier Realty");

    public static string ShortcutPath => Path.Combine(ShortcutDirectory, ProductName + ".lnk");

    public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Alpha Premier Realty Branding Studio";

    public static string AppExecutablePath => Path.Combine(InstallDirectory, "Alpha.Branding.exe");
    public static string SetupExecutablePath => Path.Combine(InstallDirectory, "Alpha.Branding.Setup.exe");

    public static bool TryGetPayloadLocation(Stream stream, out long payloadStart, out long payloadLength)
    {
        payloadStart = 0;
        payloadLength = 0;
        byte[] markerBytes = Encoding.UTF8.GetBytes(Marker);
        int trailerOverhead = markerBytes.Length + sizeof(long);
        if (stream.Length < trailerOverhead) return false;

        // When unsigned, the trailer is at the very end of the file.
        // When Authenticode-signed, the Authenticode certificate table is appended at the end,
        // so the trailer precedes the signature. We scan backwards in the last 1MB of the file.
        const int maxScanWindow = 1024 * 1024;
        int scanSize = (int)Math.Min(stream.Length, maxScanWindow);
        long scanStart = stream.Length - scanSize;
        stream.Seek(scanStart, SeekOrigin.Begin);

        byte[] scanBuffer = new byte[scanSize];
        int bytesRead = 0;
        while (bytesRead < scanSize)
        {
            int r = stream.Read(scanBuffer, bytesRead, scanSize - bytesRead);
            if (r == 0) break;
            bytesRead += r;
        }

        for (int i = bytesRead - trailerOverhead; i >= 0; i--)
        {
            bool match = true;
            for (int j = 0; j < markerBytes.Length; j++)
            {
                if (scanBuffer[i + j] != markerBytes[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                long length = BitConverter.ToInt64(scanBuffer, i + markerBytes.Length);
                long markerAbsolutePos = scanStart + i;
                long pStart = markerAbsolutePos - length;
                if (length > 0 && pStart >= 0 && pStart <= stream.Length - trailerOverhead)
                {
                    payloadStart = pStart;
                    payloadLength = length;
                    return true;
                }
            }
        }

        return false;
    }

    public static bool HasPayload()
    {
        try
        {
            string setupPath = Environment.ProcessPath ?? "";
            if (!File.Exists(setupPath)) return false;
            using var stream = new FileStream(setupPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return TryGetPayloadLocation(stream, out _, out _);
        }
        catch
        {
            return false;
        }
    }

    public static string GetPayloadVersion()
    {
        try
        {
            string setupPath = Environment.ProcessPath ?? "";
            if (!File.Exists(setupPath)) return DefaultVersion;
            using var stream = new FileStream(setupPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (!TryGetPayloadLocation(stream, out long payloadStart, out _)) return DefaultVersion;

            stream.Seek(payloadStart, SeekOrigin.Begin);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            var entry = archive.GetEntry("InstallerVersion.txt");
            if (entry != null)
            {
                using var entryStream = entry.Open();
                using var readerText = new StreamReader(entryStream, Encoding.ASCII);
                string v = readerText.ReadToEnd().Trim();
                if (!string.IsNullOrEmpty(v)) return v;
            }
        }
        catch
        {
            // fallback
        }
        return DefaultVersion;
    }

    public static async Task InstallAsync(string targetDirectory, IProgress<(double Progress, string Status)>? progress = null)
    {
        string setupPath = Environment.ProcessPath ?? throw new InvalidOperationException("Unable to locate the running setup executable.");
        string stage = Path.Combine(Path.GetTempPath(), "Alpha.Branding-Install-" + Guid.NewGuid().ToString("N"));

        try
        {
            progress?.Report((10, "Preparing setup files..."));
            await Task.Run(() => Directory.CreateDirectory(stage));

            string payload = Path.Combine(stage, "payload.zip");
            progress?.Report((25, "Extracting payload archive..."));
            await Task.Run(() => ExtractPayload(payload));

            string versionFile = Path.Combine(stage, "InstallerVersion.txt");
            string version = DefaultVersion;
            await Task.Run(() =>
            {
                using (var archive = ZipFile.OpenRead(payload))
                {
                    var entry = archive.GetEntry("InstallerVersion.txt");
                    if (entry != null)
                    {
                        entry.ExtractToFile(versionFile, true);
                        version = File.ReadAllText(versionFile).Trim();
                    }
                }
            });

            progress?.Report((50, "Installing application files..."));
            await Task.Run(() =>
            {
                if (Process.GetProcessesByName("Alpha.Branding").Length > 0)
                {
                    throw new InvalidOperationException("Alpha Premier Realty Branding Studio is currently running. Please close it before installing.");
                }

                if (Directory.Exists(targetDirectory))
                {
                    try { Directory.Delete(targetDirectory, true); } catch { }
                }
                Directory.CreateDirectory(targetDirectory);
                ZipFile.ExtractToDirectory(payload, targetDirectory, true);
                File.Copy(setupPath, Path.Combine(targetDirectory, "Alpha.Branding.Setup.exe"), true);
            });

            progress?.Report((75, "Creating Start Menu shortcuts..."));
            string appPath = Path.Combine(targetDirectory, "Alpha.Branding.exe");
            if (!File.Exists(appPath)) throw new FileNotFoundException("Payload does not contain Alpha.Branding.exe.", appPath);

            await Task.Run(() => CreateShortcut(appPath, targetDirectory));

            progress?.Report((90, "Registering application with Windows..."));
            await Task.Run(() =>
            {
                using var key = Registry.CurrentUser.CreateSubKey(UninstallKey)
                    ?? throw new InvalidOperationException("Unable to create registry uninstall registration.");
                key.SetValue("DisplayName", ProductName);
                key.SetValue("DisplayVersion", version);
                key.SetValue("Publisher", Publisher);
                key.SetValue("InstallLocation", targetDirectory);
                key.SetValue("DisplayIcon", appPath);
                key.SetValue("UninstallString", $"\"{Path.Combine(targetDirectory, "Alpha.Branding.Setup.exe")}\" --uninstall");
            });

            progress?.Report((100, "Installation complete!"));
        }
        finally
        {
            if (Directory.Exists(stage))
            {
                try { Directory.Delete(stage, true); } catch { }
            }
        }
    }

    public static void ExtractPayload(string destination)
    {
        string setupPath = Environment.ProcessPath ?? throw new InvalidOperationException("Process path not found.");
        using var stream = new FileStream(setupPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (!TryGetPayloadLocation(stream, out long payloadStart, out long payloadLength))
            throw new InvalidDataException("Installer payload trailer is missing or invalid.");

        stream.Seek(payloadStart, SeekOrigin.Begin);
        using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
        stream.CopyTo(output, payloadLength);
    }

    public static async Task UninstallAsync(IProgress<(double Progress, string Status)>? progress = null)
    {
        if (Process.GetProcessesByName("Alpha.Branding").Length > 0)
        {
            throw new InvalidOperationException("Alpha Premier Realty Branding Studio is currently running. Please close it and try again.");
        }

        progress?.Report((20, "Removing Start Menu shortcuts..."));
        await Task.Run(() =>
        {
            try
            {
                if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath);
                if (Directory.Exists(ShortcutDirectory) && !Directory.EnumerateFileSystemEntries(ShortcutDirectory).Any())
                {
                    Directory.Delete(ShortcutDirectory);
                }
            }
            catch { }
        });

        progress?.Report((50, "Removing registry registration..."));
        await Task.Run(() =>
        {
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);
            }
            catch { }
        });

        progress?.Report((80, "Scheduling file cleanup..."));
        await Task.Run(() =>
        {
            string dir = InstallDirectory;
            string command = $"choice /C Y /N /D Y /T 2 >nul & if exist \"{dir}\" rmdir /s /q \"{dir}\"";
            var psi = new ProcessStartInfo("cmd.exe", "/c " + command)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            Process.Start(psi);
        });

        progress?.Report((100, "Uninstallation complete."));
    }

    public static void CreateShortcut(string appPath, string installDir)
    {
        Directory.CreateDirectory(ShortcutDirectory);
        Type shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Windows shortcut support is unavailable.");
        object? shell = Activator.CreateInstance(shellType) ?? throw new InvalidOperationException("Failed to initialize Windows shell automation object.");
        try
        {
            object? shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { ShortcutPath })
                ?? throw new InvalidOperationException("Failed to create shortcut object.");
            try
            {
                Type shortcutType = shortcut.GetType();
                shortcutType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { appPath });
                shortcutType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { installDir });
                shortcutType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { ProductName });
                shortcutType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
            }
            finally
            {
                Marshal.FinalReleaseComObject(shortcut);
            }
        }
        finally
        {
            Marshal.FinalReleaseComObject(shell);
        }
    }
}

internal static class StreamExtensions
{
    public static void CopyTo(this Stream source, Stream destination, long bytes)
    {
        byte[] buffer = new byte[81920];
        while (bytes > 0)
        {
            int read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, bytes));
            if (read == 0) throw new EndOfStreamException();
            destination.Write(buffer, 0, read);
            bytes -= read;
        }
    }
}
