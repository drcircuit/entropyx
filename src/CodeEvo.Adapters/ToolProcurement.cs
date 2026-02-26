using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CodeEvo.Adapters;

public class ToolProcurement
{
    public bool CheckTool(string toolName)
    {
        try
        {
            var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            var psi = new ProcessStartInfo(isWindows ? "where" : "which", toolName)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            using var process = Process.Start(psi);
            process?.WaitForExit();
            return process?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public string GetInstallInstructions(string toolName, string platform)
    {
        var instructions = (toolName.ToLowerInvariant(), platform.ToLowerInvariant()) switch
        {
            ("git", "linux") => "sudo apt-get install git",
            ("git", "macos") => "brew install git",
            ("git", "windows") => "winget install --id Git.Git",
            ("cloc", "linux") => "sudo apt-get install cloc",
            ("cloc", "macos") => "brew install cloc",
            ("cloc", "windows") => "winget install cloc",
            _ => $"Please install '{toolName}' for platform '{platform}' manually."
        };
        return instructions;
    }
}
