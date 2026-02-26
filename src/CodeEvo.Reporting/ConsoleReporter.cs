using CodeEvo.Core.Models;
using Spectre.Console;

namespace CodeEvo.Reporting;

public class ConsoleReporter
{
    public void ReportCommit(CommitInfo commit, RepoMetrics repoMetrics)
    {
        AnsiConsole.MarkupLine($"[bold cyan]Commit:[/] [yellow]{commit.Hash[..Math.Min(8, commit.Hash.Length)]}[/]  " +
                               $"[grey]{commit.Timestamp:yyyy-MM-dd HH:mm:ss zzz}[/]");
        AnsiConsole.MarkupLine($"  Files: [green]{repoMetrics.TotalFiles}[/]  " +
                               $"SLOC: [green]{repoMetrics.TotalSloc}[/]  " +
                               $"Entropy: [magenta]{repoMetrics.EntropyScore:F4}[/]");
    }

    public void ReportFileMetrics(IReadOnlyList<FileMetrics> files)
    {
        if (files.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No file metrics to display.[/]");
            return;
        }

        var table = new Table();
        table.AddColumn("Path");
        table.AddColumn("Language");
        table.AddColumn(new TableColumn("SLOC").RightAligned());
        table.AddColumn(new TableColumn("CC").RightAligned());
        table.AddColumn(new TableColumn("MI").RightAligned());
        table.AddColumn(new TableColumn("SmellsH/M/L").RightAligned());

        foreach (var f in files)
        {
            table.AddRow(
                Markup.Escape(f.Path),
                f.Language.Length > 0 ? f.Language : "[grey]unknown[/]",
                f.Sloc.ToString(),
                f.CyclomaticComplexity.ToString("F1"),
                f.MaintainabilityIndex.ToString("F1"),
                $"{f.SmellsHigh}/{f.SmellsMedium}/{f.SmellsLow}");
        }

        AnsiConsole.Write(table);
    }

    public void ReportToolAvailable(string toolName)
    {
        AnsiConsole.MarkupLine($"[green]✓[/] {Markup.Escape(toolName)} is available");
    }

    public void ReportToolMissing(string toolName, string installInstructions)
    {
        AnsiConsole.MarkupLine($"[bold yellow]WARNING:[/] Tool [red]{Markup.Escape(toolName)}[/] is not available.");
        AnsiConsole.MarkupLine($"  Install with: [cyan]{Markup.Escape(installInstructions)}[/]");
    }

    public void ReportDetectedLanguages(IEnumerable<string> languages)
    {
        var langList = languages.ToList();
        if (langList.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No recognized source languages detected.[/]");
            return;
        }
        AnsiConsole.MarkupLine($"[bold]Detected languages:[/] [green]{string.Join(", ", langList.Select(Markup.Escape))}[/]");
    }

    public void ReportLanguageScan(IEnumerable<(string Path, string Language)> results)
    {
        var table = new Table();
        table.AddColumn("File");
        table.AddColumn("Language");
        foreach (var (path, language) in results)
            table.AddRow(Markup.Escape(path), language);
        AnsiConsole.Write(table);
    }

    public void ReportScanSummary(int fileCount, int totalSloc)
    {
        AnsiConsole.MarkupLine($"\n[bold]Total files:[/] [green]{fileCount}[/]  [bold]Total SLOC:[/] [green]{totalSloc}[/]");
    }

    /// <summary>
    /// Renders a per-file temperature graph in the console.
    /// Each file gets a coloured heat bar that runs from green (cool) through
    /// yellow (warm) to red (hot) based on its normalised badness score.
    /// Files are sorted hottest-first.
    /// </summary>
    /// <param name="files">File metrics in any order.</param>
    /// <param name="badness">Parallel badness array from EntropyCalculator.ComputeBadness.</param>
    public void ReportHeatmap(IReadOnlyList<FileMetrics> files, double[] badness)
    {
        if (files.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No files to display.[/]");
            return;
        }

        double max = badness.Length > 0 ? badness.Max() : 1.0;
        if (max == 0) max = 1.0;

        var sorted = files.Zip(badness)
            .OrderByDescending(x => x.Second)
            .ToList();

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Heat");
        table.AddColumn("File");
        table.AddColumn(new TableColumn("SLOC").RightAligned());
        table.AddColumn(new TableColumn("CC").RightAligned());
        table.AddColumn(new TableColumn("Coupling").RightAligned());
        table.AddColumn(new TableColumn("Badness").RightAligned());

        foreach (var (file, b) in sorted)
        {
            double normalized = b / max;
            string colorHex   = TrafficLightHex(normalized);
            string bar        = HeatBar(normalized);

            table.AddRow(
                new Markup($"[{colorHex}]{bar}[/]"),
                new Markup(Markup.Escape(file.Path)),
                new Markup(file.Sloc.ToString()),
                new Markup(file.CyclomaticComplexity.ToString("F1")),
                new Markup(file.CouplingProxy.ToString("F1")),
                new Markup($"[{colorHex}]{b:F3}[/]"));
        }

        AnsiConsole.Write(table);
    }

    // ── heatmap helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Returns a hex colour string interpolating green → yellow → red
    /// for <paramref name="t"/> ∈ [0, 1] (traffic-light gradient).
    /// </summary>
    public static string TrafficLightHex(double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        byte r, g;
        if (t <= 0.5)
        {
            // Green (#00C800) → Yellow (#C8C800)
            r = (byte)(t * 2.0 * 200);
            g = 200;
        }
        else
        {
            // Yellow (#C8C800) → Red (#C80000)
            r = 200;
            g = (byte)((1.0 - t) * 2.0 * 200);
        }
        return $"#{r:X2}{g:X2}00";
    }

    private static string HeatBar(double normalized)
    {
        int filled = (int)Math.Round(normalized * 10);
        return new string('█', filled) + new string('░', 10 - filled);
    }
}
