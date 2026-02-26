using System.Diagnostics;
using CodeEvo.Core;

namespace CodeEvo.Adapters;

public class LizardAnalyzer : ILizardAnalyzer
{
    public IReadOnlyDictionary<string, LizardFileResult> AnalyzeDirectory(string dirPath)
    {
        string stdout;
        try
        {
            var psi = new ProcessStartInfo("lizard", $"--csv \"{dirPath}\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            using var process = new Process { StartInfo = psi };
            process.Start();
            // Read stdout and stderr concurrently to prevent buffer deadlocks
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            stdout = stdoutTask.GetAwaiter().GetResult();
            stderrTask.GetAwaiter().GetResult(); // drain stderr
            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(stdout))
                return new Dictionary<string, LizardFileResult>();
        }
        catch
        {
            return new Dictionary<string, LizardFileResult>();
        }

        return ParseCsvOutput(stdout, dirPath);
    }

    public static IReadOnlyDictionary<string, LizardFileResult> ParseCsvOutput(string csv, string dirPath)
    {
        // CSV columns (lizard --csv):
        // 0:NLOC, 1:CCN, 2:tokens, 3:params, 4:length, 5:location, 6:file_path, ...
        var fileData = new Dictionary<string, List<double>>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in csv.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = SplitCsvLine(line);
            if (parts.Length < 7) continue;
            if (!double.TryParse(parts[1], out double ccn)) continue;

            var absPath = parts[6];
            var relPath = Path.GetRelativePath(dirPath, absPath);
            if (!fileData.TryGetValue(relPath, out var list))
                fileData[relPath] = list = [];
            list.Add(ccn);
        }

        return fileData.ToDictionary(
            kvp => kvp.Key,
            kvp => new LizardFileResult(
                AvgCyclomaticComplexity: kvp.Value.Average(),
                SmellsHigh: kvp.Value.Count(cc => cc > 20),
                SmellsMedium: kvp.Value.Count(cc => cc > 15 && cc <= 20),
                SmellsLow: kvp.Value.Count(cc => cc > 10 && cc <= 15)),
            StringComparer.OrdinalIgnoreCase);
    }

    private static string[] SplitCsvLine(string line)
    {
        var fields = new List<string>();
        bool inQuotes = false;
        var current = new System.Text.StringBuilder();

        foreach (char c in line)
        {
            if (c == '"')
                inQuotes = !inQuotes;
            else if (c == ',' && !inQuotes)
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
                current.Append(c);
        }
        fields.Add(current.ToString());
        return fields.ToArray();
    }
}
