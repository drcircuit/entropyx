using CodeEvo.Core;
using CodeEvo.Core.Models;
using CodeEvo.Reporting;
using CodeEvo.Storage;
using Spectre.Console;

namespace CodeEvo.Cli;

internal static class ReportCommandHandler
{
    internal static void Handle(
        string repoPath, string dbPath, string? commitHash, string? htmlPath, string kind)
    {
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
            reporter.ReportRepoMetrics(rm);

        reporter.ReportEntropyTrend(filtered);

        if (htmlPath is not null)
        {
            AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .Start("Generating HTML report...", _ =>
                    WriteHtmlReport(allMetrics, commitRepo, fileMetricsRepo, htmlPath, kind));

            AnsiConsole.MarkupLine($"[green]✓[/] HTML report written to [cyan]{Markup.Escape(htmlPath)}[/]");
            var jsonOutputPath = Path.ChangeExtension(htmlPath, ".json");
            AnsiConsole.MarkupLine($"[green]✓[/] Data JSON written to [cyan]{Markup.Escape(jsonOutputPath)}[/]");
        }
    }

    private static void WriteHtmlReport(
        IReadOnlyList<RepoMetrics> allMetrics,
        CommitRepository commitRepo,
        FileMetricsRepository fileMetricsRepo,
        string htmlPath,
        string kind)
    {
        // Build ordered commit history
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

        // Get file metrics for the latest (most recent) commit, filtered by kind
        IReadOnlyList<FileMetrics> latestFiles = [];
        if (history.Count > 0)
            latestFiles = CliHelpers.FilterByKind(fileMetricsRepo.GetByCommit(history[^1].Item1.Hash), kind);

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

        var htmlReporter = new HtmlReporter();
        var html = htmlReporter.Generate(history, latestFiles);
        File.WriteAllText(htmlPath, html);

        // Write data.json alongside the HTML for later comparison
        var jsonPath = Path.ChangeExtension(htmlPath, ".json");
        var json = HtmlReporter.GenerateDataJson(history, latestFiles);
        File.WriteAllText(jsonPath, json);
    }
}
