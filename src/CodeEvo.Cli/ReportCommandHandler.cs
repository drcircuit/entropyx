using CodeEvo.Core;
using CodeEvo.Core.Models;
using CodeEvo.Reporting;
using CodeEvo.Storage;
using Spectre.Console;

namespace CodeEvo.Cli;

internal static class ReportCommandHandler
{
    internal static void Handle(
        string repoPath, string dbPath, string? commitHash, string? htmlPath, string kind, string? exportFiguresDir)
    {
        var repositoryName = GetRepositoryName(repoPath);
        var reporter = new ConsoleReporter();
        var db = new DatabaseContext();
        db.Initialize(dbPath);
        var repoMetricsRepo = new RepoMetricsRepository(db);
        var commitRepo = new CommitRepository(db);
        var fileMetricsRepo = new FileMetricsRepository(db);

        var allMetrics = repoMetricsRepo.GetAll();
        if (allMetrics.Count == 0)
        {
            Console.WriteLine("No metrics found. Run 'scan full', 'scan head', 'scan from', or 'scan chk' first.");
            return;
        }

        var filtered = allMetrics
            .Where(rm => commitHash is null || rm.CommitHash.StartsWith(commitHash, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var rm in filtered)
            reporter.ReportRepoMetrics(rm, repositoryName);

        reporter.ReportEntropyTrend(filtered);

        if (htmlPath is not null)
        {
            AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .Start("Generating HTML report...", _ =>
                    WriteHtmlReport(allMetrics, commitRepo, fileMetricsRepo, htmlPath, kind, exportFiguresDir, repositoryName));

            AnsiConsole.MarkupLine($"[green]✓[/] HTML report written to [cyan]{Markup.Escape(htmlPath)}[/]");
            var jsonOutputPath = Path.ChangeExtension(htmlPath, ".json");
            AnsiConsole.MarkupLine($"[green]✓[/] Data JSON written to [cyan]{Markup.Escape(jsonOutputPath)}[/]");
            if (exportFiguresDir is not null)
                AnsiConsole.MarkupLine($"[green]✓[/] SVG/PNG figures exported to [cyan]{Markup.Escape(exportFiguresDir)}[/]");
        }
        else if (exportFiguresDir is not null)
        {
            // Export figures even without --html
            AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .Start($"Exporting SVG/PNG figures to {Markup.Escape(exportFiguresDir)}...", _ =>
                    ExportFiguresOnly(allMetrics, commitRepo, fileMetricsRepo, kind, exportFiguresDir, repositoryName));
            AnsiConsole.MarkupLine($"[green]✓[/] SVG/PNG figures exported to [cyan]{Markup.Escape(exportFiguresDir)}[/]");
        }
    }

    private static void ExportFiguresOnly(
        IReadOnlyList<RepoMetrics> allMetrics,
        CommitRepository commitRepo,
        FileMetricsRepository fileMetricsRepo,
        string kind,
        string exportFiguresDir,
        string repositoryName)
    {
        var (history, commitStats) = BuildHistoryAndStats(allMetrics, commitRepo, fileMetricsRepo, kind);
        HtmlReporter.ExportSvgFigures(exportFiguresDir, history, commitStats, repositoryName);
    }

    private static void WriteHtmlReport(
        IReadOnlyList<RepoMetrics> allMetrics,
        CommitRepository commitRepo,
        FileMetricsRepository fileMetricsRepo,
        string htmlPath,
        string kind,
        string? exportFiguresDir,
        string repositoryName)
    {
        var (history, commitStats) = BuildHistoryAndStats(allMetrics, commitRepo, fileMetricsRepo, kind);

        // Get file metrics for the latest (most recent) commit, filtered by kind
        IReadOnlyList<FileMetrics> latestFiles = [];
        if (history.Count > 0)
            latestFiles = CliHelpers.FilterByKind(fileMetricsRepo.GetByCommit(history[^1].Item1.Hash), kind);

        // Get file metrics for the previous commit (for delta contributor analysis)
        IReadOnlyList<FileMetrics>? prevFiles = null;
        if (history.Count >= 2)
            prevFiles = CliHelpers.FilterByKind(fileMetricsRepo.GetByCommit(history[^2].Item1.Hash), kind);

        var htmlReporter = new HtmlReporter();
        var html = htmlReporter.Generate(history, latestFiles, commitStats, prevFiles, repositoryName);
        File.WriteAllText(htmlPath, html);

        // Write data.json alongside the HTML for later comparison
        var jsonPath = Path.ChangeExtension(htmlPath, ".json");
        var json = HtmlReporter.GenerateDataJson(history, latestFiles);
        File.WriteAllText(jsonPath, json);

        if (exportFiguresDir is not null)
            HtmlReporter.ExportSvgFigures(exportFiguresDir, history, commitStats, repositoryName);
    }

    private static string GetRepositoryName(string repoPath)
    {
        var trimmed = repoPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Path.GetFileName(trimmed);
    }

    /// <summary>
    /// Builds the ordered commit history and computes per-commit avg CC / smell / SLOC-per-file stats.
    /// </summary>
    private static (List<(CommitInfo, RepoMetrics)> History, List<HtmlReporter.CommitFileStats> CommitStats)
        BuildHistoryAndStats(
            IReadOnlyList<RepoMetrics> allMetrics,
            CommitRepository commitRepo,
            FileMetricsRepository fileMetricsRepo,
            string kind)
    {
        var allCommits = commitRepo.GetAll();
        var commitByHash = allCommits.ToDictionary(c => c.Hash);
        var metricsByHash = allMetrics.ToDictionary(m => m.CommitHash);

        var history = allCommits
            .Where(c => metricsByHash.ContainsKey(c.Hash))
            .OrderBy(c => c.Timestamp)
            .Select(c => (c, metricsByHash[c.Hash]))
            .ToList();

        // Fall back: use metrics in stored order when commit info is unavailable
        if (history.Count == 0)
        {
            history = allMetrics
                .Select(m =>
                {
                    commitByHash.TryGetValue(m.CommitHash, out var ci);
                    ci ??= new CommitInfo(m.CommitHash, DateTimeOffset.MinValue, []);
                    return (ci, m);
                })
                .ToList();
        }

        // When --kind is specified, rebuild per-commit entropy from stored file metrics
        if (!string.Equals(kind, "all", StringComparison.OrdinalIgnoreCase))
        {
            history = history.Select(h =>
            {
                var commitFiles = CliHelpers.FilterByKind(fileMetricsRepo.GetByCommit(h.Item1.Hash), kind);
                var kindEntropy = EntropyCalculator.ComputeEntropy(commitFiles);
                var kindMetrics = new RepoMetrics(
                    h.Item2.CommitHash,
                    commitFiles.Count,
                    commitFiles.Sum(f => f.Sloc),
                    kindEntropy);
                return (h.Item1, kindMetrics);
            }).ToList();
        }

        // Compute per-commit CC/smell stats (sampled for performance on large repos)
        const int maxStatPoints = 200;
        var commitStats = SampleList(history, maxStatPoints)
            .Select(h =>
            {
                var files = CliHelpers.FilterByKind(fileMetricsRepo.GetByCommit(h.Item1.Hash), kind);
                double avgCc = files.Count > 0 ? files.Average(f => f.CyclomaticComplexity) : 0;
                double avgSmell = files.Count > 0 ? files.Average(f => f.SmellsHigh * 3.0 + f.SmellsMedium * 2.0 + f.SmellsLow) : 0;
                double slocPerFile = files.Count > 0 ? (double)files.Sum(f => f.Sloc) / files.Count : 0;
                return new HtmlReporter.CommitFileStats(h.Item1, avgCc, avgSmell, slocPerFile);
            })
            .ToList();

        return (history, commitStats);
    }

    private static IReadOnlyList<T> SampleList<T>(IReadOnlyList<T> source, int maxPoints)
    {
        if (source.Count <= maxPoints) return source;
        var result = new List<T>(maxPoints);
        double step = (double)(source.Count - 1) / (maxPoints - 1);
        for (int i = 0; i < maxPoints; i++)
            result.Add(source[(int)Math.Round(i * step)]);
        return result;
    }
}
