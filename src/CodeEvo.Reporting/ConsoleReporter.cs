using CodeEvo.Core;
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

    public void ReportScanSummary(int fileCount, int totalSloc, double entropy)
    {
        AnsiConsole.MarkupLine($"\n[bold]Total files:[/] [green]{fileCount}[/]  [bold]Total SLOC:[/] [green]{totalSloc}[/]  [bold]Entropy:[/] [magenta]{entropy:F4}[/]");
    }

    public void ReportScanChart(IReadOnlyList<FileMetrics> files, int topN = 10)
    {
        bool hasCc = files.Any(f => f.CyclomaticComplexity > 0);

        var top = hasCc
            ? files.Where(f => f.CyclomaticComplexity > 0)
                   .OrderByDescending(f => f.CyclomaticComplexity)
                   .Take(topN)
                   .ToList()
            : files.Where(f => f.Sloc > 0)
                   .OrderByDescending(f => f.Sloc)
                   .Take(topN)
                   .ToList();

        if (top.Count == 0)
            return;

        string label = hasCc ? "[bold]Top files by Cyclomatic Complexity[/]" : "[bold]Top files by SLOC[/]";

        var chart = new BarChart()
            .Width(80)
            .Label(label)
            .CenterLabel();

        foreach (var f in top)
        {
            var barLabel = Path.GetFileName(f.Path);
            double value = hasCc ? Math.Round(f.CyclomaticComplexity, 1) : f.Sloc;
            chart.AddItem(barLabel, value, Color.Yellow);
        }

        AnsiConsole.WriteLine();
        AnsiConsole.Write(chart);
    }

    public void ReportRepoMetrics(RepoMetrics repoMetrics)
    {
        AnsiConsole.MarkupLine($"[bold cyan]Commit:[/] [yellow]{repoMetrics.CommitHash[..Math.Min(8, repoMetrics.CommitHash.Length)]}[/]  " +
                               $"Files: [green]{repoMetrics.TotalFiles}[/]  " +
                               $"SLOC: [green]{repoMetrics.TotalSloc}[/]  " +
                               $"Entropy: [magenta]{repoMetrics.EntropyScore:F4}[/]");
    }

    public void ReportEntropyTrend(IReadOnlyList<RepoMetrics> metrics)
    {
        if (metrics.Count == 0)
            return;

        var chart = new BarChart()
            .Width(80)
            .Label("[bold]Entropy score per commit[/]")
            .CenterLabel();

        foreach (var m in metrics)
        {
            var label = m.CommitHash[..Math.Min(7, m.CommitHash.Length)];
            chart.AddItem(label, Math.Round(m.EntropyScore, 4), Color.Magenta1);
        }

        AnsiConsole.WriteLine();
        AnsiConsole.Write(chart);
    }
}
