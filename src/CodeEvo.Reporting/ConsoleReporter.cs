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
}
