using Microsoft.Win32;
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

internal static class Program
{
    private const string Marker = "ALPHA_BRANDING_PAYLOAD_V1";
    private const string ProductName = "Alpha Premier Realty Branding Studio";
    private static readonly string InstallDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Alpha Premier Realty", "Branding Studio");
    private static readonly string ShortcutDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "Alpha Premier Realty");
    private static readonly string ShortcutPath = Path.Combine(ShortcutDirectory, ProductName + ".lnk");
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Alpha Premier Realty Branding Studio";

    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 1 && string.Equals(args[0], "--uninstall", StringComparison.OrdinalIgnoreCase))
                return Uninstall();
            if (args.Length != 0) return Fail("Unknown argument. Use --uninstall to remove the application.", 2);
            return Install();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Installer error: {ex.Message}");
            return 1;
        }
    }

    private static int Install()
    {
        string setupPath = Environment.ProcessPath ?? throw new InvalidOperationException("Unable to locate the running setup executable.");
        string stage = Path.Combine(Path.GetTempPath(), "Alpha.Branding-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(stage);
            string payload = Path.Combine(stage, "payload.zip");
            ExtractPayload(payload);
            string versionFile = Path.Combine(stage, "InstallerVersion.txt");
            using (var archive = ZipFile.OpenRead(payload))
            {
                var entry = archive.GetEntry("InstallerVersion.txt") ?? throw new InvalidDataException("Payload is missing InstallerVersion.txt.");
                entry.ExtractToFile(versionFile, true);
            }
            string version = File.ReadAllText(versionFile).Trim();
            if (!Version.TryParse(version, out _)) throw new InvalidDataException("InstallerVersion.txt contains an invalid version.");

            if (Directory.Exists(InstallDirectory)) Directory.Delete(InstallDirectory, true);
            Directory.CreateDirectory(InstallDirectory);
            ZipFile.ExtractToDirectory(payload, InstallDirectory, true);
            File.Copy(setupPath, Path.Combine(InstallDirectory, "Alpha.Branding.Setup.exe"), true);
            string appPath = Path.Combine(InstallDirectory, "Alpha.Branding.exe");
            if (!File.Exists(appPath)) throw new InvalidDataException("Payload is missing Alpha.Branding.exe.");
            CreateShortcut(appPath);
            using (var key = Registry.CurrentUser.CreateSubKey(UninstallKey)!)
            {
                key.SetValue("DisplayName", ProductName);
                key.SetValue("DisplayVersion", version);
                key.SetValue("Publisher", "Alpha Premier Realty");
                key.SetValue("InstallLocation", InstallDirectory);
                key.SetValue("DisplayIcon", appPath);
                key.SetValue("UninstallString", $"\"{Path.Combine(InstallDirectory, "Alpha.Branding.Setup.exe")}\" --uninstall");
            }
            Console.WriteLine($"Installed {ProductName} to {InstallDirectory}");
            return 0;
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
    }

    private static void ExtractPayload(string destination)
    {
        using var stream = new FileStream(Environment.ProcessPath!, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length < sizeof(long) + Encoding.UTF8.GetByteCount(Marker)) throw new InvalidDataException("Installer payload trailer is missing.");
        stream.Seek(-sizeof(long), SeekOrigin.End);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        long length = reader.ReadInt64();
        byte[] marker = Encoding.UTF8.GetBytes(Marker);
        if (length <= 0 || length > stream.Length - marker.Length - sizeof(long)) throw new InvalidDataException("Installer payload length is invalid.");
        stream.Seek(-(sizeof(long) + marker.Length), SeekOrigin.End);
        if (!reader.ReadBytes(marker.Length).SequenceEqual(marker)) throw new InvalidDataException("Installer payload marker is invalid.");
        stream.Seek(-(sizeof(long) + marker.Length + length), SeekOrigin.End);
        using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
        stream.CopyTo(output, length);
    }

    private static int Uninstall()
    {
        if (Process.GetProcessesByName("Alpha.Branding").Any()) return Fail("Alpha.Branding.exe is running. Close it and try again.", 3);
        try { if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath); if (Directory.Exists(ShortcutDirectory) && !Directory.EnumerateFileSystemEntries(ShortcutDirectory).Any()) Directory.Delete(ShortcutDirectory); } catch (Exception ex) { return Fail("Unable to remove Start Menu shortcut: " + ex.Message, 1); }
        Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);
        string setup = Environment.ProcessPath!;
        string dir = InstallDirectory;
        string command = $"ping 127.0.0.1 -n 2 >nul & rmdir /s /q \"{dir}\"";
        var psi = new ProcessStartInfo("cmd.exe", "/c " + command) { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden };
        Process.Start(psi);
        Console.WriteLine("Uninstall started.");
        return 0;
    }

    private static void CreateShortcut(string appPath)
    {
        Directory.CreateDirectory(ShortcutDirectory);
        Type shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Windows shortcut support is unavailable.");
        object shell = Activator.CreateInstance(shellType)!;
        object shortcut = shellType.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { ShortcutPath })!;
        Type shortcutType = shortcut.GetType();
        shortcutType.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { appPath });
        shortcutType.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { InstallDirectory });
        shortcutType.InvokeMember("Description", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { ProductName });
        shortcutType.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, shortcut, null);
        Marshal.FinalReleaseComObject(shortcut); Marshal.FinalReleaseComObject(shell);
    }

    private static int Fail(string message, int code) { Console.Error.WriteLine("Installer error: " + message); return code; }
}
internal static class StreamExtensions
{
    public static void CopyTo(this Stream source, Stream destination, long bytes)
    {
        byte[] buffer = new byte[81920];
        while (bytes > 0) { int read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, bytes)); if (read == 0) throw new EndOfStreamException(); destination.Write(buffer, 0, read); bytes -= read; }
    }
}
