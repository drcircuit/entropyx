using System.Diagnostics;

namespace CodeEvo.Adapters;

public class ToolRunner
{
    public async Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(
        string executable, string args, string? workingDir = null)
    {
        var psi = new ProcessStartInfo(executable, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workingDir ?? Directory.GetCurrentDirectory()
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return (process.ExitCode, stdout, stderr);
    }
}
