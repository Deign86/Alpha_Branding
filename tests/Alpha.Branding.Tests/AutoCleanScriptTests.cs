using System.Diagnostics;
using System.IO;
using Xunit;

namespace Alpha.Branding.Tests;

public class AutoCleanScriptTests
{
    private static string FindRepositoryRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "Alpha_Branding.sln")))
            {
                return current;
            }
            var parent = Directory.GetParent(current);
            current = parent?.FullName;
        }
        throw new InvalidOperationException("Could not locate repository root containing Alpha_Branding.sln.");
    }

    [Fact]
    public void AutoCleanScript_ExistsInRepositoryRoot()
    {
        var repoRoot = FindRepositoryRoot();
        var scriptPath = Path.Combine(repoRoot, "Auto-Clean.ps1");

        Assert.True(File.Exists(scriptPath), $"Auto-Clean.ps1 should exist at {scriptPath}");
    }

    [Fact]
    public void AutoCleanScript_DryRun_ExecutesWithZeroExitCode()
    {
        var repoRoot = FindRepositoryRoot();
        var scriptPath = Path.Combine(repoRoot, "Auto-Clean.ps1");

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" -DryRun -ThresholdMB 99999",
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit(10000);

        Assert.Equal(0, process.ExitCode);
        Assert.Contains("Alpha Branding Artifact Auto-Clean", output);
        Assert.Contains("[DRY RUN]", output);
        Assert.Empty(error);
    }

    [Fact]
    public void AutoCleanScript_ThresholdTrigger_DryRunReportsExceeded()
    {
        var repoRoot = FindRepositoryRoot();
        var scriptPath = Path.Combine(repoRoot, "Auto-Clean.ps1");

        // Use a near-zero threshold (0.000001 MB) so even 1 byte triggers threshold
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" -DryRun -Force",
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit(10000);

        Assert.Equal(0, process.ExitCode);
        Assert.Contains("Force flag active", output);
        Assert.Contains("Cleanup WOULD execute", output);
    }
}
